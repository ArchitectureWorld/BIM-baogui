using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal static class CaseEdits
    {
        private static Element Owner(Document doc, EditChange change)
        {
            var owner = doc.GetElement(new ElementId(change.Id));
            if (owner == null || owner.UniqueId != change.OwnerUniqueId) throw new InvalidOperationException("参数载体已删除或被替换，请重新检查。");
            return owner;
        }
        private static string Fingerprint(Document doc, IEnumerable<EditChange> changes) => Evidence.Hash(PlatformContext.ProfileId(doc) + "|" + Evidence.Json(changes.Select(c => {
            var owner = Owner(doc, c);
            return new { owner.UniqueId, owner.Name, type = owner.GetTypeId().IntegerValue, c.Source,
                reads = RevitMappingService.ReadOwner(owner, c.Source, owner is ElementType ? "type" : "instance").ToArray() };
        }).ToArray()));
        private static EditChange Change(Document doc, Element owner, string source, string action, string value, string reason)
        {
            var reads = RevitMappingService.ReadOwner(owner, source, owner is ElementType ? "type" : "instance").ToArray();
            var old = ReferencePolicy.Resolve(reads, source);
            var affected = owner is ElementType ? new FilteredElementCollector(doc).WhereElementIsNotElementType().Count(e => e.GetTypeId() == owner.Id) : 1;
            return new EditChange { Id = owner.Id.IntegerValue, OwnerUniqueId = owner.UniqueId, Name = owner.Name, Source = source, Action = action,
                Binding = owner is ElementType ? "类型" : "实例", OldValue = reads.Length == 0 ? "缺参数" : old?.DisplayValue ?? ReferencePolicy.ToBridgeValue(old) ?? "未填写",
                NewValue = action == "新增参数" ? "创建参数；不虚构业务值" : value, ExpectedRaw = value,
                Detail = reason + "；影响当前文档 " + affected + " 个实例" + (action == "新增参数" ? "；共享参数绑定覆盖该类别" : "") };
        }
        private static void ValidateCreate(Document doc, Element owner, string source)
        {
            if (owner.Category == null || !owner.Category.AllowsBoundParameters) throw new InvalidOperationException("类别不支持参数绑定");
            var existing = SharedParameterElement.Lookup(doc, ReferencePolicy.SourceGuid(source));
            if (existing != null)
            {
                if (existing.Name != source || existing.GetDefinition().ParameterType != ParameterType.Text) throw new InvalidOperationException("来源 GUID 定义冲突");
                var binding = doc.ParameterBindings.get_Item(existing.GetDefinition());
                if (binding != null && (binding is TypeBinding) != (owner is ElementType)) throw new InvalidOperationException("来源已有另一种实例/类型绑定");
            }
            // Reject another family/binding definition in the same category before offering a write.
            var collector = new FilteredElementCollector(doc).OfCategoryId(owner.Category.Id);
            if (owner is ElementType) collector.WhereElementIsElementType(); else collector.WhereElementIsNotElementType();
            foreach (var other in collector)
                if (other.GetParameters(source).Any(p => !p.IsShared || p.GUID != ReferencePolicy.SourceGuid(source)))
                    throw new InvalidOperationException("同类别存在其他同名参数定义，需先统一来源（" + other.Id.IntegerValue + "）");
        }
        private static void AddPreparation(Document doc, Element owner, FieldCheck field, List<EditChange> changes, List<string> conflicts, string parentFunction = null)
        {
            if (owner == null) { conflicts.Add(field.Source + "：找不到可写载体"); return; }
            var key = owner.UniqueId + "|" + field.Source;
            if (changes.Any(c => c.OwnerUniqueId + "|" + c.Source == key)) return;
            var ruleCorrection = field.RequiresRuleWrite && field.SuggestedValue == field.RequiredValue;
            try
            {
                if (ruleCorrection && owner is ElementType) throw new InvalidOperationException("净用地控制红线规则需要实例参数；当前来源在共享类型上，请先统一来源，避免影响其他构件");
                if (field.State == "缺参数")
                {
                    ValidateCreate(doc, owner, field.Source);
                    changes.Add(Change(doc, owner, field.Source, "新增参数", null, "案例字段：" + field.PropertySet));
                }
                if (!string.IsNullOrWhiteSpace(field.SuggestedValue))
                {
                    var error = ModelChecks.InputError(field, field.SuggestedValue, parentFunction);
                    if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                    if (field.State == "缺参数" || field.State == "未填写" || (field.FloorAreaOverride || ruleCorrection) && field.State == "有效" || ruleCorrection && field.State == "值非法")
                    {
                        if (field.State != "缺参数" && !field.Editable) throw new InvalidOperationException("参数只读或有歧义");
                        var parameter = field.State == "缺参数" ? null : owner.GetParameters(field.Source).Single();
                        var raw = field.SuggestedValue;
                        if (parameter?.StorageType == StorageType.Double && field.SuggestionUnit == "m²" && parameter.Definition.UnitType == UnitType.UT_Area)
                            raw = UnitUtils.ConvertToInternalUnits(double.Parse(raw, CultureInfo.InvariantCulture), DisplayUnitType.DUT_SQUARE_METERS).ToString("R", CultureInfo.InvariantCulture);
                        else if(parameter?.StorageType==StorageType.Double && field.SuggestionUnit=="m" && parameter.Definition.UnitType==UnitType.UT_Length)
                            raw=UnitUtils.ConvertToInternalUnits(double.Parse(raw,CultureInfo.InvariantCulture),DisplayUnitType.DUT_METERS).ToString("R",CultureInfo.InvariantCulture);
                        else raw = ModelEdits.Normalize(doc, parameter, new ReferenceProperty { RawType = field.RawType }, raw);
                        changes.Add(Change(doc, owner, field.Source, "赋值", raw, field.SuggestionReason));
                    }
                    else if (field.Value != field.SuggestedValue) conflicts.Add(owner.Id.IntegerValue + " / " + field.Source + "：现值与 " + field.SuggestionReason + " 不同，保留现值并请人工复核");
                }
                if (field.State == "有歧义" || field.State == "值非法" && !ruleCorrection || field.State == "类型待核验") conflicts.Add(owner.Id.IntegerValue + " / " + field.Source + "：" + field.State);
            }
            catch (Exception ex) { conflicts.Add(owner.Id.IntegerValue + " / " + field.Source + "：" + ex.Message); }
        }
        private static void AddClassification(Document doc, Element element, string slot, List<EditChange> changes, List<string> conflicts, string evidence = "人工选择业务分类")
        {
            try
            {
                var parameters = element.GetParameters("类目");
                if (parameters.Count == 0)
                {
                    if (RevitMappingService.Reads(doc, element, "类目").Length > 0) throw new InvalidOperationException("类目在类型上，需人工核对同类型影响范围");
                    ValidateCreate(doc, element, "类目"); changes.Add(Change(doc, element, "类目", "新增参数", null, "构件业务分类"));
                }
                else if (parameters.Count != 1 || parameters[0].IsReadOnly || parameters[0].StorageType != StorageType.String)
                    throw new InvalidOperationException("类目参数有歧义或不可写");
                if (parameters.Count == 0 || parameters[0].AsString() != slot)
                    changes.Add(Change(doc, element, "类目", "分类", slot, evidence + "；核对本预览后写入才确认分类"));
            }
            catch (Exception ex) { conflicts.Add(element.Id.IntegerValue + " / 类目：" + ex.Message); }
        }
        private static void ValidateClear(Document doc, Parameter parameter)
        {
            if (parameter.IsReadOnly || parameter.StorageType == StorageType.ElementId) throw new InvalidOperationException("旧属性只读或不支持清空");
            if (parameter.StorageType == StorageType.String) return;
            if (parameter.IsShared && SharedParameterElement.Lookup(doc, parameter.GUID)?.ShouldHideWhenNoValue() == true) return;
            throw new InvalidOperationException("该数值参数不支持恢复未填写，请先在 Revit 中调整参数定义；不会用 0 或 否 代替空值");
        }
        private static ModelElementCheck Proposed(Document doc, ModelSnapshot snapshot, ModelElementCheck row, string slot)
        {
            var profileId = snapshot.Planning?.RuleProfileId;
            slot = PlatformContext.CanonicalSlot(slot, profileId);
            var rules = CaseWorkflow.ForModel(snapshot.Stage, snapshot.ModelKind, profileId);
            var rule = rules.SingleOrDefault(r => r.Slot == slot && CaseWorkflow.CanClassify(r, row));
            var element = doc.GetElement(new ElementId(row.Id));
            if (rule == null || element is Level || element is ProjectInfo) throw new InvalidOperationException("当前案例没有适用的实体业务分类。");
            if(CaseWorkflow.AreaSlots.Contains(slot) && !(element is Area))throw new InvalidOperationException("该区域类目请选择 Revit 面积（Area）对象。");
            var proposed = CaseModelReader.Inventory(doc, element); proposed.Slot = slot; proposed.SuggestedSlots = new string[0];
            if (!CaseWorkflow.CanClassify(rule, proposed)) throw new InvalidOperationException("构件载体已变化，请全面复检后重新分类。");
            proposed.Entity = CaseWorkflow.ClassificationEntity(proposed, slot);
            CaseModelReader.ReadFieldsWithPlanning(doc, element, proposed, rules, snapshot.ModelKind,null,snapshot.Planning);
            PlanningInputs.Populate(new[]{proposed},snapshot.Planning,snapshot.Underground);
            return proposed;
        }
        private static void AddOldClassCleanup(Document doc, Element element, ModelElementCheck original, ModelElementCheck proposed, List<EditChange> changes, List<string> conflicts)
        {
            if (string.IsNullOrEmpty(original.Slot) || original.Slot == proposed.Slot || original.Slot == "总平构件") return;
            var keep = proposed.Fields.Select(f => f.Source).Concat(original.Fields.Where(f => f.Binding == "类型").Select(f => f.Source)).ToArray();
            // Clear physical instance values only. Definitions and values owned by a
            // shared type remain available to the other instances using that type.
            var profileId = PlatformContext.ProfileId(doc);
            var sources = CaseExportPolicy.SourceProperties(profileId).Where(p => !p.NativeGroup && ReferenceCatalog.LegacySlot(p.PropertySet) == PlatformContext.CanonicalSlot(original.Slot, profileId) && p.Binding == "实例")
                .Select(p => p.SourceParameter).Except(keep).Where(s => s != "类目").Distinct();
            foreach (var source in sources)
            {
                try
                {
                    var reads = RevitMappingService.ReadOwner(element, source, "instance").ToArray();
                    var value = ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(reads, source));
                    if (value == null) continue;
                    var parameter = element.GetParameters(source).Single(); ValidateClear(doc, parameter);
                    changes.Add(Change(doc, element, source, "清空", null, "从“" + original.Slot + "”改为“" + proposed.Slot + "”：清空原类目专属实例值，保留参数定义及共用值"));
                }
                catch (Exception ex) { conflicts.Add(element.Id.IntegerValue + " / 清理 " + source + "：" + ex.Message); }
            }
        }
        internal static EditPreview Preview(Document doc, ModelSnapshot snapshot, EditRequest request = null)
            => PreviewCore(doc, snapshot, request, null);
        private static EditPreview PreviewCore(Document doc, ModelSnapshot snapshot, EditRequest request, ModelElementCheck[] mergedRows)
        {
            var profileId = snapshot?.Planning?.RuleProfileId ?? "";
            if (snapshot == null || !snapshot.Complete || snapshot.Stage != "报规" || CaseWorkflow.ForModel(snapshot.Stage, snapshot.ModelKind,profileId).Length == 0)
                throw new InvalidOperationException("请先完成报规阶段及模型类型明确的全面检查。");
            if (profileId != PlatformContext.ProfileId(doc)) throw new InvalidOperationException("规则方案已变化，请重新全面检查后再生成写入预览。");
            var changes = new List<EditChange>(); var conflicts = new List<string>();
            var proposedRows = new List<ModelElementCheck>();
            var rows = request == null ? snapshot.Elements : snapshot.Elements.Where(r => request.Ids.Contains(r.Id)).ToArray();
            if (request != null && rows.Length != request.Ids.Distinct().Count()) throw new InvalidOperationException("部分目标不属于本轮完整扫描。");
            foreach (var row in rows)
            {
                var element = doc.GetElement(new ElementId(row.Id));
                if (element == null || element.UniqueId != row.UniqueId) throw new InvalidOperationException("构件已替换，请全面复检。");
                if (request?.Kind == "赋值")
                {
                    var field = row.Fields.SingleOrDefault(f => f.PropertySet == request.PropertySet && f.Source == request.Source && f.Binding == request.Binding);
                    if (field == null || !field.Editable && field.State != "缺参数") throw new InvalidOperationException("所选构件不共享可写的该属性，请缩小面板选择范围。");
                    var owner = doc.GetElement(new ElementId(field.OwnerId));
                    if (owner == null) throw new InvalidOperationException("属性载体已不存在，请全面检查。");
                    var parameter = field.State == "缺参数" ? null : owner.GetParameters(field.Source).Single();
                    if (parameter == null) { ValidateCreate(doc, owner, field.Source); changes.Add(Change(doc, owner, field.Source, "新增参数", null, "手工填写缺失属性")); }
                    var property = new ReferenceProperty { RawType = field.RawType };
                    var clear = string.IsNullOrWhiteSpace(request.Value);
                    if (clear && parameter != null) ValidateClear(doc, parameter);
                    var raw = clear ? null : ModelEdits.Normalize(doc, parameter, property, request.Value);
                    ValidateAssignment(mergedRows?.Single(r => r.Id == row.Id) ?? row, field, raw, profileId);
                    if (!changes.Any(c => c.Id == field.OwnerId && c.Source == field.Source && c.Action == (clear ? "清空" : "赋值"))) changes.Add(Change(doc, owner, field.Source, clear ? "清空" : "赋值", raw, "人工属性修改"));
                    continue;
                }
                if (request?.Kind == "分类")
                {
                    var proposed = Proposed(doc, snapshot, row, request.Slot); proposedRows.Add(proposed);
                    AddClassification(doc, element, proposed.Slot, changes, conflicts);
                    AddOldClassCleanup(doc, element, row, proposed, changes, conflicts);
                    var prepared = mergedRows?.Single(r => r.Id == row.Id) ?? proposed;
                    foreach (var field in prepared.Fields.Where(f => snapshot.Scope != "所选构件" || f.Binding == "实例"))
                        AddPreparation(doc, doc.GetElement(new ElementId(field.OwnerId)), field, changes, conflicts,
                            PlanningInputs.ParentValue(prepared.Fields, field.PropertySet, true));
                    continue;
                }
                if (request != null) throw new InvalidOperationException("未知修改操作。");
                if (!string.IsNullOrEmpty(row.CoverageNote) || !string.IsNullOrEmpty(row.Issue)) { conflicts.Add(row.Id + "：" + (row.CoverageNote ?? row.Issue) + "\n" + row.Recognition); continue; }
                if (row.Candidates.Length == 1 && row.Candidates[0].CanPrepare && string.IsNullOrWhiteSpace(row.Slot))
                    AddClassification(doc, element, row.Candidates[0].Slot, changes, conflicts, row.Candidates[0].Evidence);
                else if (row.SuggestedSlots.Length > 0)
                    conflicts.Add(row.Id + "：待确认分类；请在管理清单选择对象并预览所选分类。\n" + row.Recognition);
                foreach (var field in row.Fields) AddPreparation(doc, doc.GetElement(new ElementId(field.OwnerId)), field, changes, conflicts,
                    PlanningInputs.ParentValue(row.Fields, field.PropertySet, true));
            }
            var array = changes.ToArray();
            return new EditPreview { DocumentKey = snapshot.DocumentKey, ScanId = snapshot.ScanId, SnapshotHash = Fingerprint(doc, array), Changes = array,
                AreaSyncFingerprint=request==null&&array.Length>0&&rows.Any(r=>r.Fields.Any(f=>f.FloorAreaOverride))?FloorAreaSynchronization.Fingerprint(doc):null,
                ProposedElements = proposedRows.ToArray(), Conflicts = conflicts.Distinct().ToArray(), Request = request ?? new EditRequest { Kind = "案例补齐", Ids = rows.Select(r => r.Id).ToArray() } };
        }
        private static void ValidateAssignment(ModelElementCheck row, FieldCheck field, string value, string profileId)
        {
            var isParent = field.Property == "主功能类别" || field.Property == "公建大类";
            var parent = isParent ? value : PlanningInputs.ParentValue(row.Fields, field.PropertySet);
            var error = ModelChecks.InputError(field, value, parent, profileId);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error + " 原参数值保留。");
            if (isParent)
                foreach (var child in row.Fields.Where(f => f.PropertySet == field.PropertySet && (f.Property == "子功能类别" || f.Property == "公建小类")))
                {
                    error = ModelChecks.InputError(child, child.Value, parent, profileId);
                    if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error + " 请同时确认该对象的主、子功能，原值保留。");
                }
        }
        internal static EditPreview PreviewBatch(Document doc, ModelSnapshot snapshot, EditRequest[] requests)
            => PreviewBatchCore(doc, snapshot, requests, true);
        internal static EditPreview PreviewSelection(Document doc, ModelSnapshot snapshot, EditRequest[] requests)
        {
            if (snapshot?.Scope != "所选构件") throw new InvalidOperationException("请重新读取当前选择。");
            if ((requests ?? new EditRequest[0]).Any(r => r.Kind == "赋值" && r.Binding != "实例")) throw new InvalidOperationException("快捷赋属性只支持所选实例，类型属性请在 Revit 类型属性中编辑。");
            return PreviewBatchCore(doc, snapshot, requests, false);
        }
        private static EditPreview PreviewBatchCore(Document doc, ModelSnapshot snapshot, EditRequest[] requests, bool includeAutomatic)
        {
            requests = requests ?? new EditRequest[0];
            var effective = new Dictionary<int, ModelElementCheck>();
            foreach (var classification in requests.Where(r => r.Kind == "分类"))
                foreach (var id in classification.Ids)
                {
                    if (effective.ContainsKey(id)) throw new InvalidOperationException("同一构件有多个分类修改，请重新确认。");
                    var row = snapshot.Elements.Single(r => r.Id == id); effective[id] = Proposed(doc, snapshot, row, classification.Slot);
                }
            foreach (var request in requests)
            {
                if (request.Baselines == null || request.Baselines.Length != request.Ids.Length) throw new InvalidOperationException("修改缺少原值记录，请重新填写该属性。");
                foreach (var baseline in request.Baselines)
                {
                    var row = snapshot.Elements.SingleOrDefault(r => r.Id == baseline.ElementId && r.UniqueId == baseline.ElementUniqueId);
                    if (row == null) throw new InvalidOperationException("暂存构件已被替换，请重新检查并确认修改。");
                    if (request.Kind == "分类")
                    {
                        if (row.Slot != baseline.Value) throw new InvalidOperationException("暂存分类与模型现状有冲突，请重新确认分类。");
                        continue;
                    }
                    if (!string.IsNullOrEmpty(request.Slot) && (!effective.TryGetValue(row.Id, out var target) || target.Slot != request.Slot)) throw new InvalidOperationException("暂存属性不属于本轮确认的新分类。");
                    var field = (effective.TryGetValue(row.Id, out var proposed) ? proposed : row).Fields.SingleOrDefault(f => f.PropertySet == request.PropertySet && f.Source == request.Source && f.Binding == request.Binding);
                    if (field == null || field.OwnerUniqueId != baseline.OwnerUniqueId || field.Value != baseline.Value || field.State != baseline.State || field.SourceSignature != baseline.SourceSignature)
                        throw new InvalidOperationException("暂存属性与模型现状有冲突，请重新确认：" + request.Source);
                }
            }
            var valueSnapshot = new ModelSnapshot { Scope = snapshot.Scope, DocumentKey = snapshot.DocumentKey, ScanId = snapshot.ScanId, Stage = snapshot.Stage, ModelKind = snapshot.ModelKind, Complete = true,
                Planning=snapshot.Planning,Underground=snapshot.Underground,
                Elements = snapshot.Elements.Select(r => effective.TryGetValue(r.Id, out var proposed) ? proposed : r).ToArray() };
            ValidateNetLand(doc,valueSnapshot.Elements);
            var calculationSnapshot=new ModelSnapshot {Scope=snapshot.Scope,DocumentKey=snapshot.DocumentKey,ScanId=snapshot.ScanId,
                Stage=snapshot.Stage,ModelKind=snapshot.ModelKind,Complete=true,Planning=snapshot.Planning,Underground=snapshot.Underground,
                Elements=PlanningInputs.WithDraftValues(valueSnapshot.Elements,requests.Where(r=>r.Kind=="赋值").SelectMany(r=>r.Baselines.Select(b=>{
                    var field=valueSnapshot.Elements.Single(e=>e.UniqueId==b.ElementUniqueId).Fields
                        .Single(f=>f.PropertySet==r.PropertySet&&f.Source==r.Source&&f.Binding==r.Binding);
                    var owner=doc.GetElement(new ElementId(field.OwnerId));
                    var parameter=field.State=="缺参数"?null:owner.GetParameters(field.Source).Single();
                    var raw=string.IsNullOrWhiteSpace(r.Value)?null:ModelEdits.Normalize(doc,parameter,new ReferenceProperty{RawType=field.RawType},r.Value);
                    double? metric=null;
                    if(raw!=null&&parameter?.StorageType==StorageType.Double) {
                        var number=double.Parse(raw,CultureInfo.InvariantCulture);
                        if(parameter.Definition.UnitType==UnitType.UT_Area)metric=number*.3048*.3048;
                        if(parameter.Definition.UnitType==UnitType.UT_Length)metric=number*.3048;
                    }
                    return new PlanningValueOverride{OwnerUniqueId=b.OwnerUniqueId,PropertySet=r.PropertySet,Source=r.Source,Binding=r.Binding,Value=raw,MetricValue=metric};
                })).ToArray())};
            if(includeAutomatic) {
                ModelGeometry.CheckAggregation(doc,calculationSnapshot.Elements);
                PlanningInputs.Populate(calculationSnapshot.Elements,snapshot.Planning,snapshot.Underground);
                foreach(var request in requests.Where(r=>r.Kind=="赋值"&&r.PropertySet==FloorAreaPolicy.RegionSet))
                    if(request.Ids.Any(id=>calculationSnapshot.Elements.Single(r=>r.Id==id).Fields.Any(f=>f.Source==request.Source&&f.FloorAreaOverride)))
                        throw new InvalidOperationException("建筑区域面积修改与已填写的楼层面积不一致，请先统一整层总值或各区域分配后再写入。");
            }
            var automatic = includeAutomatic ? Preview(doc, calculationSnapshot) : new EditPreview { Changes = new EditChange[0] };
            var changes = automatic.Changes.ToList(); var conflicts = automatic.Conflicts.ToList();
            var manualValues = new Dictionary<string, string>();
            foreach (var request in requests.OrderBy(r => r.Kind == "分类" ? 0 : 1))
            {
                var proposed = PreviewCore(doc, request.Kind == "分类" ? snapshot : valueSnapshot, request, calculationSnapshot.Elements);
                if (proposed.Conflicts.Length > 0) throw new InvalidOperationException(string.Join("\n", proposed.Conflicts));
                if (request.Kind == "分类") changes.RemoveAll(c => request.Ids.Contains(c.Id));
                foreach (var change in proposed.Changes)
                {
                    if (request.Kind == "赋值" && (change.Action == "赋值" || change.Action == "清空"))
                    {
                        var key = change.OwnerUniqueId + "|" + change.Source;
                        if (manualValues.TryGetValue(key, out var previous) && previous != change.ExpectedRaw) throw new InvalidOperationException("同一来源参数在不同属性组中输入了不同的值，请统一后写入：" + change.Source);
                        manualValues[key] = change.ExpectedRaw;
                    }
                    changes.RemoveAll(c => c.OwnerUniqueId == change.OwnerUniqueId && c.Source == change.Source && (c.Action == change.Action || change.Action != "新增参数" && c.Action != "新增参数"));
                    changes.Add(change);
                }
            }
            // Quick assignment is strictly instance scoped. Preparing a business
            // class must not create or overwrite values on a shared family type.
            var array = changes.Where(c => includeAutomatic || c.Binding == "实例").ToArray();
            return new EditPreview { DocumentKey = snapshot.DocumentKey, ScanId = snapshot.ScanId, SnapshotHash = Fingerprint(doc, array),
                AreaSyncFingerprint=automatic.AreaSyncFingerprint,
                ProposedElements = effective.Values.ToArray(), Changes = array, Conflicts = conflicts.Distinct().ToArray(), Request = new EditRequest { Kind = "统一写入", Ids = snapshot.Elements.Select(r => r.Id).ToArray() } };
        }
        internal static void ValidateNetLand(Document doc,ModelElementCheck[] projectedRows=null)
        {
            var overrides=(projectedRows??new ModelElementCheck[0]).ToDictionary(r=>r.Id,r=>PlanningInputs.EffectiveSlot(r));
            var rows=new List<ModelElementCheck>();
            foreach(var floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()) {
                var slot=overrides.TryGetValue(floor.Id.IntegerValue,out var proposed)?proposed:RevitMappingService.Slot(doc,floor);
                if(slot!="规划净用地")continue;
                rows.Add(new ModelElementCheck{Id=floor.Id.IntegerValue,Name=floor.Name,Slot=slot,ProjectionKey=ModelGeometry.ProjectionKey(floor)});
            }
            var issues=PlanningInputs.DuplicateLandIssues(rows.ToArray());
            if(issues.Length>0)throw new InvalidOperationException(string.Join("\n",issues));
        }
        internal static void Apply(Document doc, EditPreview preview)
        {
            EditGuard.Check(preview.DocumentKey, ModelReader.Key(doc), preview.SnapshotHash, Fingerprint(doc, preview.Changes));
            if(preview.AreaSyncFingerprint!=null&&preview.AreaSyncFingerprint!=FloorAreaSynchronization.Fingerprint(doc))
                throw new InvalidOperationException("楼层面积或区域范围已变化，请全面复检后重新预览；未写入旧分摊结果。");
            ValidateNetLand(doc,preview.Changes.Where(c=>c.Action=="分类").Select(c=>new ModelElementCheck{Id=c.Id,Slot=c.ExpectedRaw}).ToArray());
            if (preview.Changes.Length == 0) throw new InvalidOperationException("没有可写入的补齐项。");
            var directory = RuntimePaths.NewRunDirectory();
            using (var group = new TransactionGroup(doc, "H-IFC 全面检查属性补齐"))
            {
                group.Start();
                try
                {
                    using (var tx = new Transaction(doc, "按本轮完整预览补齐属性"))
                    {
                        tx.Start();
                        foreach (var change in preview.Changes)
                        {
                            var owner = Owner(doc, change);
                            if (change.Action == "新增参数")
                            {
                                if (owner.GetParameters(change.Source).Count == 0)
                                    RevitMappingService.EnsureDefinitions(doc, new[] { owner }, new[] { new ParameterDefinition { Name = change.Source, Guid = ReferencePolicy.SourceGuid(change.Source) } }, directory, owner is ElementType);
                                doc.Regenerate();
                            }
                            else
                            {
                                var parameters = owner.GetParameters(change.Source);
                                if (parameters.Count != 1 || parameters[0].IsReadOnly) throw new InvalidOperationException("写入来源有歧义或只读：" + change.Source);
                                var parameter = parameters[0]; var raw = change.ExpectedRaw;
                                if (change.Action == "清空") ValidateClear(doc, parameter);
                                bool ok = change.Action == "清空" ? parameter.StorageType == StorageType.String ? parameter.Set("") : parameter.ClearValue()
                                    : parameter.StorageType == StorageType.String ? parameter.Set(raw) : parameter.StorageType == StorageType.Integer
                                    ? parameter.Set(int.Parse(raw, CultureInfo.InvariantCulture)) : parameter.StorageType == StorageType.Double && parameter.Set(double.Parse(raw, CultureInfo.InvariantCulture));
                                if (!ok) throw new InvalidOperationException("参数写入失败：" + change.Source);
                            }
                        }
                        doc.Regenerate();
                        foreach (var change in preview.Changes)
                        {
                            var owner = Owner(doc, change); var reads = RevitMappingService.ReadOwner(owner, change.Source, owner is ElementType ? "type" : "instance").ToArray();
                            if (reads.Length != 1) throw new InvalidOperationException("参数回读有歧义或缺失：" + change.Source);
                            if (change.Action != "新增参数" && ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(reads, change.Source)) != change.ExpectedRaw)
                                throw new InvalidOperationException("参数回读不一致：" + change.Source);
                        }
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("写入事务未完成。");
                    }
                    Evidence.Write(Path.Combine(directory, "case-management-write.json"), new { state = "VERIFIED_IN_TRANSACTION_GROUP", preview, after = Fingerprint(doc, preview.Changes) });
                    if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("事务组未提交。");
                }
                catch { if (group.GetStatus() == TransactionStatus.Started) group.RollBack(); throw; }
            }
        }
    }
}
