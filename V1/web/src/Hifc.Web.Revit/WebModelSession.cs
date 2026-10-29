using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public sealed class WebModelSession : IDisposable, INativeMaterialSession
    {
        private readonly NativeModelAccess model;
        private readonly ProfileStore profiles;
        private readonly WorkspaceStore workspaces;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly WebApiClient api;
        private Compatibility compatibility;
        private CapturePlan capture;
        private AccountSession identity;
        private readonly HashSet<string> conflicts = new HashSet<string>(StringComparer.Ordinal);
        public SavedWorkspace Workspace { get; }
        public CheckResult Result { get; private set; }
        public EditPlan Plan { get; private set; }
        public ExportPlan ExportPlan { get; private set; }
        public ExportReceipt LastExport => Workspace.ExportReceiptRecorded ? Workspace.LastExport : null;
        public PublicSigningKey SigningKey => api.SigningKey;
        public DateTime ServerNowUtc => api.ServerNowUtc;
        public AccountInfo Account => identity?.Account;
        public string Title => model.Title;
        public string ActivePhone => profiles.Current?.Phone ?? profiles.LegacyPhone;
        public string ActiveName => profiles.Current?.RealName ?? "";
        public string Status { get; private set; } = "连接服务器后开始全面检查；已有草稿可离线编辑。";
        public bool LiveCheck { get; private set; }
        public bool Busy { get; private set; }
        public bool IncludeSuggestions { get; set; } = true;
        public bool SkipInvalidDrafts { get; set; }
        public int ConflictCount => conflicts.Count;
        public int InvalidDraftCount => Result == null ? 0 : Workspace.Drafts.Count(d => !Result.Fields.Any(f => f.Key == d.FieldKey));
        public string SettingsStatus => Workspace.SettingsConflict ? "模型设置已变化 · 本机草稿待确认" : Workspace.SettingsDirty ? "计算设置待写入模型" : Workspace.LastModelSettingsHash == null ? "模型设置待读取" : Workspace.LastModelSettingsHash == Canonical.Hash("") ? "模型尚无 Web 设置记录" : "计算设置已写入模型";
        public event Action Changed;
        public WebModelSession(NativeModelAccess model, Uri server, string stateRoot)
        {
            this.model = model; api = new WebApiClient(server);
            profiles = new ProfileStore(Path.Combine(stateRoot, "accounts")); workspaces = new WorkspaceStore(Path.Combine(stateRoot, "drafts"));
            Workspace = workspaces.Load(model.DocumentKey); Result = Workspace.LastCheck;
            if (Result != null) Status = "已恢复上次检查与人工草稿；写入前会重新全面检查。";
        }
        private void Save() { workspaces.Save(Workspace); Changed?.Invoke(); }
        public void Flush() => workspaces.Save(Workspace);
        private void Say(string status) { Status = status; Changed?.Invoke(); }
        public async Task RunAsync(Func<Task> action)
        {
            if (Busy) return; Busy = true; Changed?.Invoke();
            try { await action(); }
            catch (Exception ex) { Say(ex is WebApiException ? ex.Message : "操作未完成：" + ex.Message); }
            finally { Busy = false; Save(); }
        }
        public async Task ConnectAsync(string phone, string name, bool register)
        {
            compatibility = await api.CompatibilityAsync(cancellation.Token); phone = ProfileStore.NormalizePhone(phone);
            name = (name ?? "").Trim();
            identity = register ? await api.RegisterAsync(new Registration { Phone = phone, RealName = name }, cancellation.Token)
                : await api.LoginAsync(new LoginRequest { Phone = phone, RealName = name }, cancellation.Token);
            profiles.Save(identity.Account.Phone, identity.Account.RealName);
            Say(identity.Account.IsActive ? "已连接，可以开始全面检查。" : "账号已到期；已有草稿仍可编辑，请联系管理员延期。");
        }
        private async Task EnsureIdentity()
        {
            if (compatibility == null || identity == null) throw new InvalidOperationException("请先填写手机号和姓名并连接。");
            if (api.ServerNowUtc >= identity.SessionExpiresAtUtc.AddMinutes(-1)) {
                var person = profiles.Current; if (person == null) throw new InvalidOperationException("请先填写手机号和姓名并连接。");
                identity = await api.LoginAsync(new LoginRequest { Phone = person.Phone, RealName = person.RealName }, cancellation.Token);
            }
            identity.Account = await api.MeAsync(cancellation.Token);
            if (!identity.Account.IsActive) throw new InvalidOperationException("账号已到期，请联系管理员延期；已有草稿已保留。");
        }
        public void SetDraft(FieldResult field, string value)
        {
            var previous = Workspace.Drafts.SingleOrDefault(d => d.FieldKey == field.Key);
            var draft = new EditDraft { FieldKey = field.Key, OriginalValue = previous != null ? previous.OriginalValue : field.DisplayValue ?? field.Value, OriginalSignature = previous?.OriginalSignature ?? field.SourceSignature, Value = value };
            Workspace.Drafts = Workspace.Drafts.Where(d => d.FieldKey != field.Key).Append(draft).ToArray(); Plan = null; ExportPlan = null; Save();
        }
        public void ResolveDraft(FieldResult field, bool keep)
        {
            var draft = Workspace.Drafts.SingleOrDefault(d => d.FieldKey == field.Key); if (draft == null) return;
            if (keep) { draft.OriginalSignature = field.SourceSignature; draft.OriginalValue = field.DisplayValue ?? field.Value; }
            else Workspace.Drafts = Workspace.Drafts.Where(d => d.FieldKey != field.Key).ToArray();
            conflicts.Remove(field.Key); Plan = null; ExportPlan = null; Save();
        }
        public void Assign(string elementUniqueId, string slot) { Workspace.AssignedSlots[elementUniqueId] = slot; Plan = null; ExportPlan = null; LiveCheck = false; Save(); }
        public void SelectModelKind(string kind) { Workspace.ModelKind = kind; LiveCheck = false; Plan = null; ExportPlan = null; Save(); }
        public void InvalidatePlan() { Plan = null; ExportPlan = null; Save(); }
        public void SetSettings(ModelSettings settings) { NativeSettingsState.Edit(Workspace, settings); LiveCheck = false; InvalidatePlan(); }
        public async Task RestoreModelSettingsAsync()
        {
            NativeSettingsState.Observe(Workspace, NativeSettingsState.Record(await model.ProjectDataAsync())); LiveCheck = false; Plan = null; ExportPlan = null; Save(); Say(SettingsStatus);
        }
        public async Task ResolveModelSettingsAsync(bool keepLocal)
        {
            NativeSettingsState.Resolve(Workspace, NativeSettingsState.Record(await model.ProjectDataAsync()), keepLocal); LiveCheck = false; InvalidatePlan(); Say(SettingsStatus);
        }
        public async Task ScanAsync()
        {
            await EnsureIdentity(); Say("正在全面盘点模型……"); Plan = null; ExportPlan = null; LiveCheck = false;
            var rows = await model.InventoryAsync(Workspace.AssignedSlots);
            var data = await model.ProjectDataAsync();
            NativeSettingsState.Observe(Workspace, NativeSettingsState.Record(data));
            var context = new RequestContext { RuleVersion = compatibility.RuleVersion, RequestId = Guid.NewGuid().ToString("N"), ScanId = Guid.NewGuid().ToString("N"), DocumentSessionId = model.SessionId,
                SnapshotHash = Canonical.InventoryHash(rows), ConfigurationHash = Canonical.ConfigurationHash(Workspace.Settings, data) };
            var pages = Math.Max(1, (rows.Length + 255) / 256);
            for (var index = 0; index < pages; index++)
            {
                var part = rows.Skip(index * 256).Take(256).ToArray();
                capture = await api.StartCheckAsync(new InventoryPage { Context = context, Stage = "报规", ModelKind = Workspace.ModelKind, Settings = Workspace.Settings, ProjectData = data, PageIndex = index, PageCount = pages, TotalElements = rows.Length, Elements = part, PageHash = Canonical.InventoryHash(part) }, cancellation.Token);
                Say("模型盘点 " + (index + 1) + " / " + pages);
            }
            if (capture?.InventoryComplete != true) throw new InvalidOperationException("盘点分页未齐备，请重新检查。");
            Say("正在读取本轮所需字段……"); var values = await model.ValuesAsync(capture.Targets);
            var definitions = await model.DefinitionsAsync(capture.DefinitionSources);
            var valuePages = NativeCapturePages.Create(context, values, definitions);
            foreach (var page in valuePages)
            {
                await api.CaptureAsync(page, cancellation.Token);
                Say("字段取数 " + (page.PageIndex + 1) + " / " + page.PageCount);
            }
            var result = await api.CompleteAsync(context, cancellation.Token);
            WriteGuard.Capture(result, await model.InventoryAsync(Workspace.AssignedSlots), await model.ValuesAsync(capture.Targets), await model.DefinitionsAsync(capture.DefinitionSources));
            if (context.ConfigurationHash != Canonical.ConfigurationHash(Workspace.Settings, await model.ProjectDataAsync())) throw new InvalidOperationException("检查期间模型资料已变化，请重新全面检查。");
            var rebase = DraftRebaser.Rebase(Workspace.Drafts, result.Fields); conflicts.Clear(); foreach (var conflict in rebase.Conflicts) conflicts.Add(conflict.Draft.FieldKey);
            Result = Workspace.LastCheck = result; LiveCheck = true; Save();
            Say("全面检查完成：" + result.Elements.Length + " 个模型记录，" + result.Fields.Length + " 项字段。" + (conflicts.Count > 0 ? "存在原值变化，请确认草稿。" : "可继续编辑并预览写入。") + SettingsStatus + "。");
        }
        public async Task PreviewAsync()
        {
            await EnsureIdentity(); if (!LiveCheck) await ScanAsync();
            if (Workspace.SettingsConflict) throw new InvalidOperationException("模型内设置已变化。请打开计算设置，确认采用模型设置或保留本机草稿后再预览。");
            if (conflicts.Count > 0) throw new InvalidOperationException("请先核对原值变化，选择保留本行草稿或采用模型现值。");
            if (InvalidDraftCount > 0 && !SkipInvalidDrafts) throw new InvalidOperationException("部分草稿目标已删除或分类已改变；确认本轮跳过后仍会保留在本机。");
            var drafts = Workspace.Drafts.Where(d => Result.Fields.Any(f => f.Key == d.FieldKey)).ToArray();
            var normalized = await model.NormalizeDraftsAsync(drafts, Result.Fields);
            ValidateMaterials();
            await SyncMaterialsAsync();
            Plan = await api.PreviewAsync(new PreviewRequest { Context = Result.Context, Drafts = normalized, IncludeSuggestions = IncludeSuggestions, Materials = Workspace.Materials.Select(m => m.Decision).ToArray() }, cancellation.Token);
            Say("写入预览已生成：" + Plan.Changes.Length + " 项修改。请核对原值、拟写值与类型影响范围。");
        }
        public async Task WriteAsync()
        {
            await EnsureIdentity(); if (Plan == null) throw new InvalidOperationException("请先生成并核对写入预览。");
            if (Workspace.SettingsConflict) throw new InvalidOperationException("计算设置存在冲突，请先确认。");
            ValidateMaterials();
            if (api.ServerNowUtc >= Plan.ExpiresAtUtc) { await ScanAsync(); await PreviewAsync(); Say("预览已刷新，请核对后再次点击写入。"); return; }
            var plan = Plan; var permit = await api.AuthorizeAsync(plan, Guid.NewGuid().ToString("N"), cancellation.Token);
            var receipt = await model.ApplyAsync(new NativeWriteInput { DocumentSessionId = model.SessionId, Basis = Result, Capture = capture, Plan = plan, Permit = permit, AssignedSlots = Workspace.AssignedSlots, Settings = Workspace.Settings }, api.SigningKey, () => api.ServerNowUtc);
            Workspace.LastReceipt = Workspace.PendingReceipt = receipt; var applied = plan.Changes.Select(c => c.FieldKey).ToHashSet(StringComparer.Ordinal);
            Save(); NativeSettingsState.Committed(Workspace, plan.Native.DataWrites.SingleOrDefault(w => w.SchemaId == ModelStorageSchemas.WebSettingsId));
            Workspace.Drafts = Workspace.Drafts.Where(d => !applied.Contains(d.FieldKey)).ToArray(); Workspace.AssignedSlots.Clear(); Workspace.Materials = new PendingMaterial[0]; LiveCheck = false; Plan = null; Save();
            Say(receipt.Detail);
            try { await api.ReceiptAsync(receipt, cancellation.Token); await ScanAsync(); receipt.RecheckCompleted = true; await api.ReceiptAsync(receipt, cancellation.Token); Workspace.PendingReceipt = null; Save(); Say("写入与回读成功，全面复检已完成。请查看剩余问题，并保存模型副本。"); }
            catch (Exception ex) { Say("模型写入与本机回读成功；全面复检或回执尚未完成。" + ex.Message + " 请恢复连接后重新检查；不要重复写入。"); }
        }
        public Task LocateAsync(int[] ids) => model.LocateAsync(ids);
        public async Task PreviewExportAsync(string fileName)
        {
            await EnsureIdentity(); if (!LiveCheck) await ScanAsync();
            if (Workspace.Drafts.Length != 0 || Workspace.Materials.Length != 0 || Workspace.SettingsDirty || Workspace.SettingsConflict || Plan != null) throw new InvalidOperationException("仍有未写入的属性、资料或计算设置，请先核对、写入并复检后再导出。");
            await SyncMaterialsAsync();
            ExportPlan = await api.PreviewExportAsync(new ExportRequest { Context = Result.Context, FileName = fileName }, cancellation.Token);
            Say("导出前核对已完成，确认后按服务器签名方案执行原生 IFC 导出。");
        }
        public async Task ExportAsync(string directory)
        {
            await EnsureIdentity(); if (ExportPlan == null) throw new InvalidOperationException("请先完成导出前核对。");
            var receipt = await model.ExportAsync(new NativeExportInput { DocumentSessionId = model.SessionId, Basis = Result, Capture = capture, Plan = ExportPlan,
                OutputDirectory = directory, Settings = Workspace.Settings, AssignedSlots = Workspace.AssignedSlots }, api.SigningKey, () => api.ServerNowUtc);
            Workspace.LastExport = receipt; Workspace.ExportReceiptRecorded = false; ExportPlan = null; Save();
            try { await RecordExportReceiptAsync(); Say("原生 IFC 已导出，模型恢复核验通过：" + receipt.IfcPath); }
            catch (Exception ex) { Say("IFC 已导出且模型已恢复；服务器回执待补传：" + ex.Message); }
        }
        public async Task RecordExportReceiptAsync()
        {
            await EnsureIdentity(); var receipt = Workspace.LastExport;
            if (receipt == null || !receipt.Exported || !receipt.SourceRestored || receipt.SnapshotBefore != receipt.SnapshotAfter) throw new InvalidOperationException("没有可提交的完整导出回执。");
            await api.ExportReceiptAsync(receipt, cancellation.Token); Workspace.ExportReceiptRecorded = true; Save();
        }
        public async Task<PlatformSync.PlatformSyncSource> ReadPlatformSyncSourceAsync()
        {
            await EnsureIdentity(); var identity = await model.DocumentIdentityAsync();
            return new PlatformSync.PlatformSyncSource { DocumentPath = identity.Item1, ProjectUniqueId = identity.Item2, DocumentSessionId = model.SessionId, Receipt = LastExport };
        }
        public async Task<PlatformLaunchPermit> AuthorizePlatformLaunchAsync(PlatformLaunchRequest request)
        {
            await EnsureIdentity(); return await api.AuthorizePlatformLaunchAsync(request, cancellation.Token);
        }
        public async Task<MaterialPreview> PreviewMaterialAsync(MaterialPreviewRequest request)
        {
            await EnsureIdentity(); if (!LiveCheck) await ScanAsync(); request.Context = Result.Context;
            if (request.Kind == "redline") request.Evidence = await model.RedlineEvidenceAsync(Result.Elements.Where(e => e.Slots.Concat(e.CandidateSlots).Any(s => s == "规划净用地" || s == "规划控制线" || s == "净用地控制红线")).Select(e => e.Element.UniqueId).Distinct().ToArray());
            return await api.MaterialPreviewAsync(request, cancellation.Token);
        }
        public async Task AcceptMaterialAsync(MaterialPreview preview, MaterialDecision decision, string sourcePath)
        {
            Workspace.Materials = Workspace.Materials.Where(m => !(m.Preview.Kind == preview.Kind && m.SourcePath == sourcePath))
                .Append(new PendingMaterial { Preview = preview, Decision = decision, SourcePath = sourcePath, ScanId = Result.Context.ScanId }).ToArray();
            Plan = null; ExportPlan = null; Save(); await SyncMaterialsAsync(); Say("资料选择已暂存。请生成写入预览，与属性在同一事务中提交。");
        }
        public async Task RemoveMaterialAsync(string previewId) { Workspace.Materials = Workspace.Materials.Where(m => m.Preview.Id != previewId).ToArray(); InvalidatePlan(); if (LiveCheck) await SyncMaterialsAsync(); }
        private async Task SyncMaterialsAsync() => await api.MaterialSelectionsAsync(new MaterialSelectionRequest { Context = Result.Context,
            Decisions = Workspace.Materials.Where(m => m.ScanId == Result.Context.ScanId).Select(m => m.Decision).ToArray() }, cancellation.Token);
        private void ValidateMaterials()
        {
            foreach (var material in Workspace.Materials)
            {
                if (material.ScanId != Result.Context.ScanId) throw new InvalidOperationException("资料预览属于旧检查，请重新导入预览；原选择仍保留。");
                if (string.IsNullOrEmpty(material.SourcePath)) continue;
                if (!File.Exists(material.SourcePath)) throw new InvalidOperationException("资料来源文件已移动，请重新导入。");
                using (var stream = File.OpenRead(material.SourcePath)) using (var sha = SHA256.Create())
                    if (!string.Equals(string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2"))), material.Preview.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("资料来源文件已变化，请重新导入预览。");
            }
        }
        public async Task SaveModelAsync()
        {
            await model.SaveAsync();
            if (Workspace.LastReceipt?.DocumentSessionId == model.SessionId) {
                Workspace.LastReceipt.RvtSaved = true; Workspace.PendingReceipt = Workspace.LastReceipt; Save();
                try { await api.ReceiptAsync(Workspace.LastReceipt, cancellation.Token); Workspace.PendingReceipt = null; Save(); }
                catch (WebApiException) { Say("当前模型已保存；保存回执待恢复连接后发送。"); return; }
            }
            Say(Workspace.SettingsDirty ? "当前 RVT 已保存；本机计算设置尚未提交，请生成预览并写入模型。" : "当前模型及已提交的 Web 设置已保存。");
        }
        public bool IsConflict(string key) => conflicts.Contains(key);
        public void Dispose() { cancellation.Cancel(); api.Dispose(); model.Dispose(); cancellation.Dispose(); }
    }
}
