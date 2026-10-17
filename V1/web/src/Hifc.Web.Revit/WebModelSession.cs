using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public sealed class WebModelSession : IDisposable
    {
        private readonly NativeModelAccess model;
        private readonly DeviceStore devices;
        private readonly WorkspaceStore workspaces;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly WebApiClient api;
        private Compatibility compatibility;
        private CapturePlan capture;
        private AccountSession identity;
        private DeviceCredential credential;
        private readonly HashSet<string> conflicts = new HashSet<string>(StringComparer.Ordinal);
        public SavedWorkspace Workspace { get; }
        public CheckResult Result { get; private set; }
        public EditPlan Plan { get; private set; }
        public AccountInfo Account => identity?.Account;
        public string Title => model.Title;
        public string ActivePhone => devices.ActivePhone;
        public string Status { get; private set; } = "连接服务器后开始全面检查；已有草稿可离线编辑。";
        public bool LiveCheck { get; private set; }
        public bool Busy { get; private set; }
        public bool IncludeSuggestions { get; set; } = true;
        public bool SkipInvalidDrafts { get; set; }
        public int ConflictCount => conflicts.Count;
        public int InvalidDraftCount => Result == null ? 0 : Workspace.Drafts.Count(d => !Result.Fields.Any(f => f.Key == d.FieldKey));
        public event Action Changed;
        public WebModelSession(NativeModelAccess model, Uri server, string stateRoot)
        {
            this.model = model; api = new WebApiClient(server);
            devices = new DeviceStore(Path.Combine(stateRoot, "accounts")); workspaces = new WorkspaceStore(Path.Combine(stateRoot, "drafts"));
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
        public async Task ConnectAsync(string phone, string name, bool register, string rebindCode = null)
        {
            compatibility = await api.CompatibilityAsync(cancellation.Token); phone = DeviceStore.NormalizePhone(phone);
            if (!string.IsNullOrWhiteSpace(rebindCode))
            {
                credential = devices.Stage(phone);
                try { identity = await api.RebindAsync(new RebindRequest { Phone = phone, Code = rebindCode.Trim(), Credential = credential.Credential }, cancellation.Token); }
                catch (WebApiException ex) when (ex.Code == "NETWORK_UNAVAILABLE" || ex.Code == "NETWORK_TIMEOUT") { identity = await api.LoginAsync(new LoginRequest { Phone = phone, Credential = credential.Credential }, cancellation.Token); }
            }
            else
            {
                identity = null;
                foreach (var candidate in devices.Candidates(phone))
                {
                    try { identity = await api.LoginAsync(new LoginRequest { Phone = phone, Credential = candidate.Credential }, cancellation.Token); credential = candidate; break; }
                    catch (WebApiException ex) when (ex.Code == "IDENTITY_REJECTED") { }
                }
                if (identity == null)
                {
                    if (!register) throw new InvalidOperationException("本机没有可用凭证，请先登记；已有账号换机请向管理员领取一次性绑定码。");
                    credential = devices.Stage(phone); identity = await api.RegisterAsync(new Registration { Phone = phone, RealName = name, Credential = credential.Credential }, cancellation.Token);
                }
            }
            devices.Accept(credential); Say(identity.Account.IsActive ? "已连接，可以开始全面检查。" : "账号已到期；已有草稿仍可编辑，请联系管理员延期。");
        }
        private async Task EnsureIdentity()
        {
            if (compatibility == null || identity == null || credential == null) throw new InvalidOperationException("请先连接并登记登录。");
            if (api.ServerNowUtc >= identity.SessionExpiresAtUtc.AddMinutes(-1)) identity = await api.LoginAsync(new LoginRequest { Phone = credential.Phone, Credential = credential.Credential }, cancellation.Token);
            identity.Account = await api.MeAsync(cancellation.Token);
            if (!identity.Account.IsActive) throw new InvalidOperationException("账号已到期，请联系管理员延期；已有草稿已保留。");
        }
        public void SetDraft(FieldResult field, string value)
        {
            var previous = Workspace.Drafts.SingleOrDefault(d => d.FieldKey == field.Key);
            var draft = new EditDraft { FieldKey = field.Key, OriginalValue = previous != null ? previous.OriginalValue : field.DisplayValue ?? field.Value, OriginalSignature = previous?.OriginalSignature ?? field.SourceSignature, Value = value };
            Workspace.Drafts = Workspace.Drafts.Where(d => d.FieldKey != field.Key).Append(draft).ToArray(); Plan = null; Save();
        }
        public void ResolveDraft(FieldResult field, bool keep)
        {
            var draft = Workspace.Drafts.SingleOrDefault(d => d.FieldKey == field.Key); if (draft == null) return;
            if (keep) { draft.OriginalSignature = field.SourceSignature; draft.OriginalValue = field.DisplayValue ?? field.Value; }
            else Workspace.Drafts = Workspace.Drafts.Where(d => d.FieldKey != field.Key).ToArray();
            conflicts.Remove(field.Key); Plan = null; Save();
        }
        public void Assign(string elementUniqueId, string slot) { Workspace.AssignedSlots[elementUniqueId] = slot; Plan = null; LiveCheck = false; Save(); }
        public void SelectModelKind(string kind) { Workspace.ModelKind = kind; LiveCheck = false; Plan = null; Save(); }
        public async Task ScanAsync()
        {
            await EnsureIdentity(); Say("正在全面盘点模型……"); Plan = null; LiveCheck = false;
            var rows = await model.InventoryAsync(Workspace.AssignedSlots);
            var context = new RequestContext { RuleVersion = compatibility.RuleVersion, RequestId = Guid.NewGuid().ToString("N"), ScanId = Guid.NewGuid().ToString("N"), DocumentSessionId = model.SessionId, SnapshotHash = Canonical.InventoryHash(rows) };
            var pages = Math.Max(1, (rows.Length + 255) / 256);
            for (var index = 0; index < pages; index++)
            {
                var part = rows.Skip(index * 256).Take(256).ToArray();
                capture = await api.StartCheckAsync(new InventoryPage { Context = context, Stage = "报规", ModelKind = Workspace.ModelKind, PageIndex = index, PageCount = pages, TotalElements = rows.Length, Elements = part, PageHash = Canonical.InventoryHash(part) }, cancellation.Token);
                Say("模型盘点 " + (index + 1) + " / " + pages);
            }
            if (capture?.InventoryComplete != true) throw new InvalidOperationException("盘点分页未齐备，请重新检查。");
            Say("正在读取本轮所需字段……"); var values = await model.ValuesAsync(capture.Targets); pages = Math.Max(1, (values.Length + 1023) / 1024);
            for (var index = 0; index < pages; index++)
            {
                var part = values.Skip(index * 1024).Take(1024).ToArray();
                await api.CaptureAsync(new CapturePage { Context = context, PageIndex = index, PageCount = pages, TotalValues = values.Length, Values = part, PageHash = Canonical.ValuesHash(part) }, cancellation.Token);
                Say("字段取数 " + (index + 1) + " / " + pages);
            }
            var result = await api.CompleteAsync(context, cancellation.Token);
            WriteGuard.Capture(result, await model.InventoryAsync(Workspace.AssignedSlots), await model.ValuesAsync(capture.Targets));
            var rebase = DraftRebaser.Rebase(Workspace.Drafts, result.Fields); conflicts.Clear(); foreach (var conflict in rebase.Conflicts) conflicts.Add(conflict.Draft.FieldKey);
            Result = Workspace.LastCheck = result; LiveCheck = true; Save();
            Say("全面检查完成：" + result.Elements.Length + " 个模型记录，" + result.Fields.Length + " 项字段。" + (conflicts.Count > 0 ? "存在原值变化，请确认草稿。" : "可继续编辑并预览写入。"));
        }
        public async Task PreviewAsync()
        {
            await EnsureIdentity(); if (!LiveCheck) await ScanAsync();
            if (conflicts.Count > 0) throw new InvalidOperationException("请先核对原值变化，选择保留本行草稿或采用模型现值。");
            if (InvalidDraftCount > 0 && !SkipInvalidDrafts) throw new InvalidOperationException("部分草稿目标已删除或分类已改变；确认本轮跳过后仍会保留在本机。");
            var drafts = Workspace.Drafts.Where(d => Result.Fields.Any(f => f.Key == d.FieldKey)).ToArray();
            var normalized = await model.NormalizeDraftsAsync(drafts, Result.Fields);
            Plan = await api.PreviewAsync(new PreviewRequest { Context = Result.Context, Drafts = normalized, IncludeSuggestions = IncludeSuggestions }, cancellation.Token);
            Say("写入预览已生成：" + Plan.Changes.Length + " 项修改。请核对原值、拟写值与类型影响范围。");
        }
        public async Task WriteAsync()
        {
            await EnsureIdentity(); if (Plan == null) throw new InvalidOperationException("请先生成并核对写入预览。");
            if (api.ServerNowUtc >= Plan.ExpiresAtUtc) { await ScanAsync(); await PreviewAsync(); Say("预览已刷新，请核对后再次点击写入。"); return; }
            var plan = Plan; var permit = await api.AuthorizeAsync(plan, Guid.NewGuid().ToString("N"), cancellation.Token);
            var receipt = await model.ApplyAsync(new NativeWriteInput { DocumentSessionId = model.SessionId, Basis = Result, Capture = capture, Plan = plan, Permit = permit, AssignedSlots = Workspace.AssignedSlots }, api.SigningKey, () => api.ServerNowUtc);
            Workspace.LastReceipt = Workspace.PendingReceipt = receipt; var applied = plan.Changes.Select(c => c.FieldKey).ToHashSet(StringComparer.Ordinal);
            Workspace.Drafts = Workspace.Drafts.Where(d => !applied.Contains(d.FieldKey)).ToArray(); Workspace.AssignedSlots.Clear(); LiveCheck = false; Plan = null; Save();
            Say(receipt.Detail);
            try { await api.ReceiptAsync(receipt, cancellation.Token); await ScanAsync(); receipt.RecheckCompleted = true; await api.ReceiptAsync(receipt, cancellation.Token); Workspace.PendingReceipt = null; Save(); Say("写入与回读成功，全面复检已完成。请查看剩余问题，并保存模型副本。"); }
            catch (Exception ex) { Say("模型写入与本机回读成功；全面复检或回执尚未完成。" + ex.Message + " 请恢复连接后重新检查；不要重复写入。"); }
        }
        public Task LocateAsync(int[] ids) => model.LocateAsync(ids);
        public async Task SaveModelAsync()
        {
            await model.SaveAsync();
            if (Workspace.LastReceipt?.DocumentSessionId == model.SessionId) {
                Workspace.LastReceipt.RvtSaved = true; Workspace.PendingReceipt = Workspace.LastReceipt; Save();
                try { await api.ReceiptAsync(Workspace.LastReceipt, cancellation.Token); Workspace.PendingReceipt = null; Save(); }
                catch (WebApiException) { Say("当前模型已保存；保存回执待恢复连接后发送。"); return; }
            }
            Say("当前模型已保存。");
        }
        public bool IsConflict(string key) => conflicts.Contains(key);
        public void Dispose() { cancellation.Cancel(); api.Dispose(); model.Dispose(); cancellation.Dispose(); }
    }
}
