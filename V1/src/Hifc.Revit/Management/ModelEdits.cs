using System;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal static class ModelEdits
    {
        private static Element[] Targets(Document doc, EditRequest request)
        {
            if (request?.Ids == null || request.Ids.Length == 0) throw new InvalidOperationException("请在管理清单中选择要处理的构件。");
            if (request.Ids.Distinct().Count() != request.Ids.Length) throw new InvalidOperationException("目标构件重复。");
            return request.Ids.Select(id => doc.GetElement(new ElementId(id)) ?? throw new InvalidOperationException("目标构件已删除，请复检。")).ToArray();
        }
        private static string Hash(Document doc, EditRequest request)
        {
            var names = request.Kind == "分类" ? RevitMappingService.Catalog.ForSlot(request.Slot).Select(p => p.SourceParameter).Append("类目").Distinct().ToArray() : new[] { "类目", request.Source };
            return Evidence.Hash(Evidence.Json(Targets(doc, request).OrderBy(e => e.Id.IntegerValue).Select(e => new {
                e.UniqueId, e.Name, entity = RevitMappingService.Entity(doc, e), level = e.LevelId.IntegerValue,
                observations = names.Select(n => new { name = n, reads = RevitMappingService.Reads(doc, e, n) }).ToArray()
            }).ToArray()));
        }
        private static ReferenceProperty Property(Document doc, Element element, string source)
        {
            var properties = RevitMappingService.Properties(doc, element).Where(p => p.SourceParameter == source).ToArray();
            if (properties.Length != 1) throw new InvalidOperationException("构件 " + element.Id.IntegerValue + " 的来源属性不唯一或不属于当前类目：" + source);
            return properties[0];
        }
        private static Parameter Writable(Element element, string source)
        {
            var parameters = element.GetParameters(source);
            if (parameters.Count != 1 || parameters[0].IsReadOnly || parameters[0].StorageType == StorageType.ElementId)
                throw new InvalidOperationException("构件 " + element.Id.IntegerValue + " 的实例参数不可写或有歧义：" + source + "。类型来源在本版仅查看。");
            return parameters[0];
        }
        internal static string Normalize(Document doc, Parameter parameter, ReferenceProperty property, string input)
        {
            if (property.RawType == "Boolean")
            {
                if (string.Equals(input, "false", StringComparison.OrdinalIgnoreCase) || input == "否") input = "0";
                if (string.Equals(input, "true", StringComparison.OrdinalIgnoreCase) || input == "是") input = "1";
            }
            if (parameter?.StorageType == StorageType.Double)
            {
                if (!UnitFormatUtils.TryParse(doc.GetUnits(), parameter.Definition.UnitType, input, out var raw))
                    throw new InvalidOperationException("数值或单位无法按项目单位解析：" + input);
                input = raw.ToString("R", CultureInfo.InvariantCulture);
            }
            if (!ModelChecks.ValueIsValid(property.RawType, input)) throw new InvalidOperationException("新值不符合参考类型 " + property.RawType + "：" + input);
            return input;
        }
        internal static EditPreview Preview(Document doc, EditRequest request)
        {
            var targets = Targets(doc, request);
            var changes = targets.SelectMany(element => {
                if (request.Kind == "分类")
                {
                    var entity = RevitMappingService.Entity(doc, element);
                    if (!RevitMappingService.Catalog.ForSlot(request.Slot).Any(p => p.Entity.Split(',').Contains(entity)))
                        throw new InvalidOperationException("构件 " + element.Id.IntegerValue + " 不支持类目 " + request.Slot);
                    var old = ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(RevitMappingService.Reads(doc, element, "类目"), "类目"));
                    var classification = new EditChange { Id = element.Id.IntegerValue, Name = element.Name, Source = "类目（并补齐缺失参数）", OldValue = old ?? "未填写", NewValue = request.Slot, ExpectedRaw = request.Slot };
                    var additions = RevitMappingService.Catalog.ForSlot(request.Slot).Select(p => p.SourceParameter).Distinct()
                        .Where(n => n != "IfcExportAs" && n != "IfcExportType" && n != "IfcObjectType" && RevitMappingService.Reads(doc, element, n).Length == 0)
                        .Select(n => new EditChange { Id = element.Id.IntegerValue, Name = element.Name, Source = n, OldValue = "缺参数", NewValue = "新增参数（值保持未填写）" });
                    return new[] { classification }.Concat(additions);
                }
                if (request.Kind != "赋值") throw new InvalidOperationException("修改操作无效。");
                var property = Property(doc, element, request.Source);
                var parameter = Writable(element, request.Source);
                var read = ReferencePolicy.Resolve(RevitMappingService.Reads(doc, element, request.Source), request.Source);
                if (read?.Origin == "type") throw new InvalidOperationException("当前值来自类型参数，请在 Revit 类型属性中编辑，避免影响范围外实例。");
                var raw = Normalize(doc, parameter, property, request.Value);
                return new[] { new EditChange { Id = element.Id.IntegerValue, Name = element.Name, Source = request.Source,
                    OldValue = read?.DisplayValue ?? ReferencePolicy.ToBridgeValue(read) ?? "未填写", NewValue = request.Value, ExpectedRaw = raw } };
            }).ToArray();
            return new EditPreview { DocumentKey = ModelReader.Key(doc), SnapshotHash = Hash(doc, request), Request = request, Changes = changes };
        }
        internal static void Apply(Document doc, EditPreview preview)
        {
            EditGuard.Check(preview.DocumentKey, ModelReader.Key(doc), preview.SnapshotHash, Hash(doc, preview.Request));
            var request = preview.Request;
            var targets = Targets(doc, request);
            var directory = RuntimePaths.NewRunDirectory();
            using (var group = new TransactionGroup(doc, "H-IFC 模型属性管理"))
            {
                group.Start();
                try
                {
                    using (var tx = new Transaction(doc, "按管理窗口预览写入"))
                    {
                        tx.Start();
                        if (request.Kind == "分类") RevitMappingService.Prepare(doc, targets, request.Slot, directory);
                        else foreach (var element in targets)
                        {
                            var parameter = Writable(element, request.Source);
                            var raw = preview.Changes.Single(c => c.Id == element.Id.IntegerValue).ExpectedRaw;
                            bool ok = parameter.StorageType == StorageType.String ? parameter.Set(raw)
                                : parameter.StorageType == StorageType.Integer ? parameter.Set(int.Parse(raw, CultureInfo.InvariantCulture))
                                : parameter.Set(double.Parse(raw, CultureInfo.InvariantCulture));
                            if (!ok) throw new InvalidOperationException("参数写入失败：" + request.Source);
                        }
                        doc.Regenerate();
                        foreach (var element in targets)
                        {
                            var source = request.Kind == "分类" ? "类目" : request.Source;
                            var actual = ReferencePolicy.ToBridgeValue(ReferencePolicy.Resolve(RevitMappingService.Reads(doc, element, source), source));
                            var expected = preview.Changes.Single(c => c.Id == element.Id.IntegerValue && c.ExpectedRaw != null).ExpectedRaw;
                            if (actual != expected) throw new InvalidOperationException("构件 " + element.Id.IntegerValue + " 的参数回读不一致。");
                        }
                        if (request.Kind == "分类") foreach (var addition in preview.Changes.Where(c => c.ExpectedRaw == null))
                            if (RevitMappingService.Reads(doc, doc.GetElement(new ElementId(addition.Id)), addition.Source).Length == 0)
                                throw new InvalidOperationException("新增参数未回读到：" + addition.Source);
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("写入事务未提交。");
                    }
                    Evidence.Write(System.IO.Path.Combine(directory, "management-write.json"), new { status = "VERIFIED_IN_TRANSACTION_GROUP", document = ModelReader.Key(doc), preview, after = Hash(doc, request) });
                    if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("写入事务组未提交。");
                }
                catch { if (group.GetStatus() == TransactionStatus.Started) group.RollBack(); throw; }
            }
        }
    }
}
