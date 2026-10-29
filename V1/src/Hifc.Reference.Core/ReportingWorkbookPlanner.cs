using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hifc.Reference
{
    public static class ReportingWorkbookPlanner
    {
        public static ReportingWorkbookPlan Build(ReportingWorkbookTable table, ModelElementCheck[] elements, string kind, string targetBuildingUniqueId = null)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            elements = elements ?? new ModelElementCheck[0];
            var issues = new List<string>(table.Issues ?? new string[0]);
            var references = (table.Items ?? new ReportingWorkbookItem[0]).Select(i => new ReportingWorkbookReference {
                TableId = table.Id, Kind = table.Kind, BuildingName = table.BuildingName, Field = i.Field, Value = i.Value,
                LevelName = i.LevelName, Unit = i.Unit, Note = i.Note, Status = i.Issue ?? "", Provenance = i.Provenance }).ToArray();
            var result = new ReportingWorkbookPlan { TableId = table.Id, References = references,
                SourceFiles = references.Where(r => r.Provenance != null).GroupBy(r => r.Provenance.Path + "|" + r.Provenance.Sha256)
                    .Select(g => new ReportingWorkbookFile { Path = g.First().Provenance.Path, Sha256 = g.First().Provenance.Sha256 }).ToArray() };
            if (kind != table.Kind || kind != "单体建筑" && kind != "总图")
            {
                issues.Add("所选表的模型类型与当前模型不符；仅保留资料。");
                foreach (var reference in references) reference.Status = ReportingWorkbookReader.Join(reference.Status, "模型类型不匹配；仅资料");
                result.Issues = issues.ToArray(); return result;
            }
            var buildings = elements.Where(e => e.Entity == "IfcBuilding").ToArray();
            ModelElementCheck target = null;
            if (kind == "单体建筑")
            {
                // null requests exact-name auto-match; empty is an explicit cleared selection.
                var matching = targetBuildingUniqueId == null
                    ? buildings.Where(b => !string.IsNullOrWhiteSpace(table.BuildingName) && Names(b).Contains(table.BuildingName.Trim(), StringComparer.Ordinal)).ToArray()
                    : buildings.Where(b => b.UniqueId == targetBuildingUniqueId).ToArray();
                if (matching.Length == 1) target = matching[0];
                else issues.Add("无法唯一确认目标楼栋；请在预览中明确选择模型楼栋。");
                result.TargetBuildingUniqueId = target?.UniqueId;
            }
            var changes = new List<ReportingWorkbookChange>();
            for (int i = 0; i < references.Length; i++)
            {
                var item = table.Items[i]; var reference = references[i];
                ModelElementCheck[] rows;
                if (kind == "单体建筑")
                {
                    if (target == null) { reference.Status = ReportingWorkbookReader.Join(reference.Status, "目标楼栋待确认；仅资料"); continue; }
                    rows = string.IsNullOrWhiteSpace(item.LevelName) ? new[] { target } : elements.Where(e => e.Entity == "IfcBuildingStorey" &&
                        (e.Name == item.LevelName || (e.Fields ?? new FieldCheck[0]).Any(f => f.PropertySet == FloorAreaPolicy.FloorSet && f.Property == "楼层名称" && f.Value == item.LevelName)) &&
                        InBuilding(e, target, buildings)).ToArray();
                }
                else rows = elements;
                var matches = rows.SelectMany(row => (row.Fields ?? new FieldCheck[0])
                    .Where(f => Name(f.Property) == Name(item.Field) || Name(f.Source) == Name(item.Field)).Select(f => new { Row = row, Field = f })).ToArray();
                if (matches.Length != 1 || !string.IsNullOrWhiteSpace(item.LevelName) && rows.Length != 1)
                {
                    reference.Status = ReportingWorkbookReader.Join(reference.Status, matches.Length > 1 || rows.Length > 1 && !string.IsNullOrWhiteSpace(item.LevelName)
                        ? "存在多个同名属性或楼层，目标不唯一；仅资料" : "未找到唯一现有映射；仅资料，不新增 IFC 属性");
                    continue;
                }
                var model = matches[0].Row; var field = matches[0].Field;
                var binding = string.IsNullOrWhiteSpace(field.Binding) ? field.Origin == "type" ? "类型" : "实例" : field.Binding;
                var change = new ReportingWorkbookChange { ElementId = model.Id, ElementUniqueId = model.UniqueId, ElementName = model.Name,
                    OwnerUniqueId = field.OwnerUniqueId ?? model.UniqueId, PropertySet = field.PropertySet, Property = field.Property,
                    Source = field.Source, Binding = binding, RawType = field.RawType, CurrentValue = field.Value, CurrentDisplayValue=field.DisplayValue??field.Value, CurrentState = field.State,
                    SourceSignature = field.SourceSignature, Value = item.Value, Provenance = item.Provenance, ValidationField = field,
                    ParentValue = (model.Fields ?? new FieldCheck[0]).FirstOrDefault(f => f.PropertySet == field.PropertySet && IsParent(f.Property))?.Value };
                string issue;
                var valid = Normalize(item, field, out var value, out issue); change.Value = value;
                if (string.IsNullOrWhiteSpace(model.UniqueId) || string.IsNullOrWhiteSpace(field.Source) || string.IsNullOrWhiteSpace(field.PropertySet))
                { valid = false; issue = ReportingWorkbookReader.Join(issue, "模型映射或稳定标识缺失"); }
                if (binding == "类型" || field.Origin == "type") { valid = false; issue = ReportingWorkbookReader.Join(issue, "类型来源会影响其他实例，请使用现有属性预览核对"); }
                if (!field.Editable && field.State != "缺参数" || field.State == "有歧义" || field.State == "类型待核验")
                { valid = false; issue = ReportingWorkbookReader.Join(issue, "现有参数只读或来源有歧义"); }
                if (field.PropertySet == FloorAreaPolicy.FloorSet && field.Property == "楼层面积" && string.IsNullOrWhiteSpace(item.LevelName))
                { valid = false; issue = ReportingWorkbookReader.Join(issue, "楼层面积必须有唯一楼层名称"); }
                if (!string.IsNullOrEmpty(field.RequiredValue) && value != field.RequiredValue)
                { valid = false; issue = ReportingWorkbookReader.Join(issue, "与当前强制规则值不符"); }
                bool same = EqualValue(field, value);
                change.IsConflict = valid && ((!string.IsNullOrWhiteSpace(field.Value) && !same) || field.State == "人工清空");
                if (same && valid) { valid = false; issue = ReportingWorkbookReader.Join(issue, "与当前值一致，无需写入"); }
                bool duplicate = table.Items.Count(other => Name(other.Field) == Name(item.Field) && other.LevelName == item.LevelName) > 1;
                if (duplicate) issue = ReportingWorkbookReader.Join(issue, "重复属性，默认不勾选；只能选择一个来源");
                if (change.IsConflict) issue = ReportingWorkbookReader.Join(issue, "Excel 与现值冲突，需明确勾选后才覆盖");
                if (table.AmbiguousBuilding) issue = ReportingWorkbookReader.Join(issue, "原表含多栋组合信息，需逐项确认适用楼栋");
                change.CanSelect = valid; change.Selected = valid && !change.IsConflict && !duplicate && !table.AmbiguousBuilding;
                change.Issue = issue ?? "";
                reference.Status = valid ? change.Selected ? "拟补空值；待暂存和写入" : "可选，需人工确认：" + change.Issue : "仅资料：" + change.Issue;
                changes.Add(change);
            }
            result.Changes = changes.ToArray(); result.Issues = issues.ToArray(); return result;
        }
        public static ReportingWorkbookDecision Decide(ReportingWorkbookPlan plan, bool retainReferences = true)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.Changes.Any(c => c.Selected && !c.CanSelect)) throw new InvalidOperationException("不能选择缺缓存、空值、只读或无效的来源。");
            var selected = plan.Changes.Where(c => c.Selected && c.CanSelect).ToArray();
            if (selected.GroupBy(c => c.OwnerUniqueId + "|" + c.PropertySet + "|" + c.Source + "|" + c.Binding).Any(g => g.Count() > 1))
                throw new InvalidOperationException("同一目标属性选择了多个来源；请只勾选一个有依据的来源。");
            foreach (var change in selected)
            {
                var field = change.ValidationField;
                if (field == null) throw new InvalidOperationException("导入预览已失效，请重新读取报规表。");
                var parent = selected.FirstOrDefault(c => c.ElementUniqueId == change.ElementUniqueId && c.PropertySet == change.PropertySet && IsParent(c.Property))?.Value ?? change.ParentValue;
                var error = ModelChecks.InputError(field, change.Value, parent);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(change.ElementName + " / " + change.Property + "：" + error);
            }
            if (selected.Length == 0 && (!retainReferences || plan.References.Length == 0)) throw new InvalidOperationException("尚未选择可暂存属性或保留资料。");
            return new ReportingWorkbookDecision { TableId = plan.TableId, TargetBuildingUniqueId = plan.TargetBuildingUniqueId, Changes = selected,
                References = retainReferences ? plan.References.ToArray() : new ReportingWorkbookReference[0], SourceFiles = plan.SourceFiles.ToArray() };
        }
        static bool IsParent(string property) => property == "主功能类别" || property == "公建大类";
        static IEnumerable<string> Names(ModelElementCheck building) => new[] { building.Name, building.Building }
            .Concat((building.Fields ?? new FieldCheck[0]).Where(f => f.PropertySet == "Pset_建筑技术信息属性集" && f.Property == "建筑名称").Select(f => f.Value))
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.Ordinal);
        static bool InBuilding(ModelElementCheck floor, ModelElementCheck target, ModelElementCheck[] buildings)
        {
            if (buildings.Length == 1) return string.IsNullOrWhiteSpace(floor.Building) || Names(target).Contains(floor.Building.Trim(), StringComparer.Ordinal);
            return !string.IsNullOrWhiteSpace(floor.Building) && Names(target).Any(n => n == floor.Building.Trim() && buildings.Count(b => Names(b).Contains(n, StringComparer.Ordinal)) == 1);
        }
        static string Name(string value)
        {
            var result = (value ?? "").Trim();
            result = Regex.Replace(result, @"[（(]\s*(?:m²|m2|㎡|平方米|m|米|层|人|户|个)\s*[）)]$", "", RegexOptions.IgnoreCase).Trim();
            return result == "临时/永久" ? "临时永久" : result;
        }
        static bool Normalize(ReportingWorkbookItem item, FieldCheck field, out string value, out string issue)
        {
            value = (item.Value ?? "").Trim(); issue = item.Issue ?? "";
            if (!string.IsNullOrEmpty(issue)) return false;
            if (string.IsNullOrWhiteSpace(value)) { issue = "Excel 为空，保留现值"; return false; }
            if ((item.Provenance?.NumberFormat ?? "").Contains("%") || IsDateFormat(item.Provenance?.NumberFormat))
            { issue = "百分比或日期格式不自动换算成业务值，请核对来源和单位"; return false; }
            var unit = (item.Unit ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            if (field.RawType == "Label" || field.RawType == "Text")
            {
                if (unit != "") { issue = "文字属性带数值单位，请核对源表"; return false; }
                return true;
            }
            if (field.RawType == "Boolean")
            {
                if (unit != "" && unit != "是/否") { issue = "布尔值单位待核对"; return false; }
                var text = value.ToLowerInvariant();
                if (text == "是" || text == "true" || text == "1") { value = "1"; return true; }
                if (text == "否" || text == "false" || text == "0") { value = "0"; return true; }
                issue = "布尔值仅接受是/否、true/false 或 1/0"; return false;
            }
            if (field.RawType != "Real" && field.RawType != "Integer") { issue = "映射类型尚不支持导入"; return false; }
            var canonicalUnit = PropertyUnits.Canonical(field.Property,field.RawType);
            var allowed = canonicalUnit=="m²" ? new[] { "", "m²", "m2", "m^2", "㎡", "平方米" } : canonicalUnit=="m" ? new[] { "", "m", "米" } : field.RawType == "Integer" ? CountUnits(field.Property) : new[] { "" };
            if (!allowed.Contains(unit)) { issue = "单位不匹配；不自动换算 " + item.Unit; return false; }
            if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            { issue = "不是明确的单一数值；不拆分组合值或推算公式"; return false; }
            if (field.RawType == "Integer" && (number != decimal.Truncate(number) || number < int.MinValue || number > int.MaxValue))
            { issue = "该属性要求整数"; return false; }
            value = number.ToString("G29", CultureInfo.InvariantCulture); return true;
        }
        static bool EqualValue(FieldCheck field, string value)
        {
            if (string.IsNullOrWhiteSpace(field.Value)) return false;
            if (field.RawType == "Real" || field.RawType == "Integer")
                return decimal.TryParse(field.MetricValue?.ToString("R",CultureInfo.InvariantCulture)??field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var old) && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var next) && Math.Abs(old-next)<.000001m;
            return field.Value == value;
        }
        static string[] CountUnits(string property)
        {
            property=property??"";
            if(property.Contains("人数"))return new[]{"","人"};
            if(property.Contains("层数"))return new[]{"","层"};
            if(property.Contains("户数"))return new[]{"","户"};
            if(property.Contains("车位"))return new[]{"","个","位"};
            if(property.Contains("栋数")||property.Contains("幢数"))return new[]{"","栋","幢"};
            if(property.EndsWith("个数",StringComparison.Ordinal)||property.EndsWith("数量",StringComparison.Ordinal))return new[]{"","个"};
            return new[]{""};
        }
        static bool IsDateFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format)) return false;
            if (format == "date/time") return true;
            var code = Regex.Replace(format, "\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", "");
            return Regex.IsMatch(code, "[ydhs]|m{2,}", RegexOptions.IgnoreCase);
        }
    }
}
