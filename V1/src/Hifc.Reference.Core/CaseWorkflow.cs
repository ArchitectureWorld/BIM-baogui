using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace Hifc.Reference
{
    public sealed class CaseRule
    {
        public string Slot { get; set; }
        public string Binding { get; set; }
        public string[] Entities { get; set; }
        public ReferenceProperty[] Properties { get; set; }
        public string[] EmptyAllowed { get; set; }
        public string[] Sources { get; set; }
        public bool Required { get; set; }
        public bool ReferenceSupplement { get; set; }
    }
    public sealed class ModelDetection
    {
        public string Kind { get; set; }
        public string[] Evidence { get; set; }
    }
    public static class CaseWorkflow
    {
        public static CaseRule[] ForModel(string stage, string kind, string ruleProfileId)
        {
            if (!PlatformRules.IsActive(ruleProfileId)) return ForModel(stage, kind);
            if (stage != "报规" || kind != "总图" && kind != "单体建筑") return new CaseRule[0];
            var result = new List<CaseRule>();
            foreach (var group in PlatformRules.Rules().GroupBy(r => r.PropertySet))
            {
                var slot = ReferenceCatalog.LegacySlot(group.Key);
                var area = AreaSlots.Contains(slot);
                var building = group.Any(r => new[] { "102", "103", "104", "204" }.Contains(r.EntityCode));
                if (!area && slot != "总平构件" && (kind == "单体建筑") != building) continue;
                var properties = CaseExportPolicy.PropertiesForSlot(slot, ruleProfileId)
                    .Where(p => !p.NativeGroup && p.PropertySet == group.Key)
                    .GroupBy(p => p.Property).Select(g => CaseExportPolicy.WithBinding(
                        g.FirstOrDefault(p => p.Binding == "实例") ?? g.First(), "实例",
                        string.Join(",", g.SelectMany(p => p.Entity.Split(',')).Distinct()))).ToArray();
                if (properties.Length == 0) continue;
                var requiredFields = PlatformRules.RequiredFields(group.Key);
                result.Add(new CaseRule { Slot = slot, Binding = "实例", Properties = properties,
                    Entities = properties.SelectMany(p => p.Entity.Split(',')).Distinct().ToArray(),
                    EmptyAllowed = properties.Where(p => !requiredFields.Contains(p.Property)).Select(p => p.Property).ToArray(),
                    Required = kind == "总图" ? slot == "规划净用地" : new[] { "建筑技术", "建筑楼层", "建筑区域" }.Contains(slot),
                    ReferenceSupplement = true, Sources = new[] { PlatformRules.Title } });
            }
            return result.OrderBy(r => r.Slot, StringComparer.Ordinal).ToArray();
        }
        public const string BaselineHash = "2118bcea847210f562a6b40d286fae71517db9b087639f0025f658cf31e700e2";
        private static readonly ReferenceCatalog Catalog = ReferenceCatalog.FromEmbeddedBaseline();
        private static readonly Dictionary<string, object>[] Models = Load();
        private static Dictionary<string, object>[] Load()
        {
            using (var stream = typeof(CaseWorkflow).Assembly.GetManifestResourceStream("Hifc.Reference.SuccessCase.json"))
            using (var buffer = new MemoryStream())
            {
                if (stream == null) throw new InvalidOperationException("成功案例基线缺失。");
                stream.CopyTo(buffer); var bytes = buffer.ToArray();
                using (var hash = SHA256.Create())
                    if (BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != BaselineHash)
                        throw new InvalidOperationException("成功案例基线哈希不匹配。");
                var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                if ((string)root["workflow_stage"] != "报规") throw new InvalidOperationException("案例阶段不匹配。");
                return ((object[])root["models"]).Cast<Dictionary<string, object>>().ToArray();
            }
        }
        private static string[] Strings(Dictionary<string, object> item, string key) => ((object[])item[key]).Cast<string>().ToArray();
        private static string[] Keys(Dictionary<string, object> item, string key) => ((Dictionary<string, object>)item[key]).Keys.ToArray();
        public static CaseRule[] ForModel(string stage, string kind)
        {
            if (stage != "报规" || kind != "总图" && kind != "单体建筑") return new CaseRule[0];
            var models = Models.Where(m => (string)m["scope"] == kind).ToArray();
            var rules = models.SelectMany(m => ((object[])m["items"]).Cast<Dictionary<string, object>>().Select(i => new { Model = m, Item = i }))
                .GroupBy(x => (string)x.Item["pset"]).Select(group => {
                    var first = group.First().Item;
                    var binding = group.Any(x => Convert.ToInt32(x.Item["type_definitions"]) > 0) ? "类型" : "实例";
                    var observed = group.SelectMany(x => Strings(x.Item, "observed_properties")).Distinct().ToArray();
                    var pset = group.Key;
                    var properties = Catalog.Properties.Where(p => p.PropertySet == pset && observed.Contains(p.Property))
                        .GroupBy(p => p.Property).Select(g => g.FirstOrDefault(p => p.Binding == binding) ?? g.First()).ToArray();
                    var entities = group.SelectMany(x => Keys(x.Item, "direct_owner_entities").Concat(Keys(x.Item, "inherited_owner_entities"))).Distinct().ToArray();
                    properties = properties.Select(p => CaseExportPolicy.WithBinding(p, binding, string.Join(",", entities))).ToArray();
                    return new CaseRule { Slot = ReferenceCatalog.LegacySlot(pset), Binding = binding,
                        Entities = entities,
                        Properties = properties, EmptyAllowed = group.SelectMany(x => Strings(x.Item, "observed_empty_properties")).Distinct().ToArray(),
                        Sources = group.Select(x => (string)x.Model["file"]).Distinct().ToArray(), Required = group.Select(x => x.Model).Distinct().Count() == models.Length };
                }).ToList();
            // Reference configuration supplies optional capabilities, never proof that
            // a project must contain a civil-defence or parking zone.
            foreach (var slot in AreaSlots.Where(s => s != "室内停车场" && rules.All(r => r.Slot != s)))
            {
                var properties = Catalog.ForSlot(slot).Where(p => p.Entity == "IfcSpace" && p.Binding == "实例").ToArray();
                rules.Add(new CaseRule { Slot=slot, Binding="实例", Entities=new[]{"IfcSpace"}, Properties=properties,
                    EmptyAllowed=properties.Select(p=>p.Property).ToArray(), Required=false, ReferenceSupplement=true,
                    Sources=new[]{"参考软件工规 config_backup1.tsv；载体约定：Revit 面积（Area），按实际适用"} });
            }
            var technical=rules.SingleOrDefault(r=>r.Slot=="建筑技术");
            if(technical!=null)
            {
                var supplements=Catalog.ForSlot("建筑技术").Where(p=>p.Property=="地下建筑层数" || p.Property=="建筑面积" || p.Property=="建筑用途名称");
                technical.Properties=technical.Properties.Concat(supplements).GroupBy(p=>p.Property).Select(g=>g.First()).ToArray();
                technical.EmptyAllowed=technical.EmptyAllowed.Concat(new[]{"地下建筑层数","建筑面积"}).Where(p=>p!="建筑用途名称").Distinct().ToArray();
            }
            foreach (var general in rules.Where(r => r.Slot == "总平构件")) {
                general.Required = false;
                general.Binding = "实例";
                general.Properties = CaseExportPolicy.PropertiesForSlot("总平构件", null);
                general.Entities = general.Properties.SelectMany(p=>p.Entity.Split(',')).Distinct().ToArray();
                general.EmptyAllowed = general.Properties.Select(p=>p.Property).Distinct().ToArray();
            }
            return rules.OrderBy(r=>r.Slot,StringComparer.Ordinal).ToArray();
        }
        public static readonly string[] AreaSlots = { "人防区域", "消防分区", "停车场", "停车位", "室内停车场" };
        public static string ClassificationEntity(ModelElementCheck row, string slot)
        {
            var source = string.IsNullOrEmpty(row.SourceEntity) ? row.Entity : row.SourceEntity;
            return slot == "规划控制线" && source == "IfcSlab" ? "IfcBuildingElementProxy" : source;
        }
        public static bool CanClassify(CaseRule rule, ModelElementCheck row)
            => (rule.Binding == "实例" || rule.Slot == "窗") && rule.Entities.Contains(ClassificationEntity(row, rule.Slot))
                && (!AreaSlots.Contains(rule.Slot) || row.AreaScheme != null);
        private static string[] Roles(ModelElementCheck row) => row.Slots.Length > 0 ? row.Slots : string.IsNullOrWhiteSpace(row.Slot) ? new string[0] : new[] { ReferenceCatalog.LegacySlot(row.Slot) };
        public static string[] SuggestSlots(ModelElementCheck row, string kind) => SuggestSlots(row, kind, "");
        public static string[] SuggestSlots(ModelElementCheck row, string kind, string profileId)
        {
            var rules = BusinessRules(kind, profileId);
            if (!string.IsNullOrWhiteSpace(row.Slot) && row.Slot != "总平构件")
                return rules.Where(r => r.Slot == row.Slot && r.Entities.Contains(row.Entity)).Select(r => r.Slot).ToArray();
            return GetCandidates(row, kind, profileId).Select(c => c.Slot).ToArray();
        }
        private static CaseRule[] BusinessRules(string kind, string profileId) => ForModel("报规", kind, profileId).Where(r => (r.Binding == "实例" || r.Slot == "窗") && r.Slot != "申报" && r.Slot != "申报信息" && r.Slot != "建筑技术" && r.Slot != "建筑楼层").ToArray();
        public static ClassificationCandidate[] GetCandidates(ModelElementCheck row, string kind) => GetCandidates(row, kind, "");
        public static ClassificationCandidate[] GetCandidates(ModelElementCheck row, string kind, string profileId)
        {
            var rules = BusinessRules(kind, profileId);
            if (!string.IsNullOrWhiteSpace(row.Slot) && row.Slot != "总平构件") return new ClassificationCandidate[0];
            var candidates = new List<ClassificationCandidate>();
            var areaText=(row.AreaScheme??"")+" "+(row.Name??"");
            var dedicatedAreas = row.SuggestedSlots.Where(s => AreaSlots.Contains(s) && rules.Any(r => r.Slot == s)).ToArray();
            foreach(var slot in AreaSlots.Where(s=>rules.Any(r=>r.Slot==s)))
            {
                var named=(areaText.Contains(slot) && !(slot=="停车场" && areaText.Contains("室内停车场") && PlatformRules.IsActive(profileId))) || slot=="人防区域"&&areaText.Contains("人防") || slot=="消防分区"&&areaText.Contains("防火分区")
                    || slot=="停车位"&&areaText.Contains("车位") || slot=="停车位"&&areaText.Contains("停车")&&!areaText.Contains("停车场");
                if(row.AreaScheme==null || !named || dedicatedAreas.Length > 0)continue;
                candidates.Add(new ClassificationCandidate {Slot=slot,CanPrepare=row.Entity=="IfcSpace"&&string.IsNullOrEmpty(row.CoverageNote),
                    RequiresReview=!string.IsNullOrEmpty(row.CoverageNote),Evidence="Revit 面积对象；面积方案：“"+row.AreaScheme+"”；名称：“"+row.Name+"”"+(row.CoverageNote??"")});
            }
            foreach (var slot in row.SuggestedSlots.Where(s => rules.Any(r => r.Slot == s)))
            {
                var supported=rules.Single(r=>r.Slot==slot).Entities.Contains(row.Entity)&&(!AreaSlots.Contains(slot)||row.AreaScheme!=null);
                candidates.Add(new ClassificationCandidate { Slot=slot, Evidence="本轮读取到该业务的非空专用来源参数"+(supported?"":"；该报规区域类目需核对面积（Area）载体"), CanPrepare=supported, RequiresReview=!supported });
            }
            foreach (var source in RecognitionEvidence.Match(row, kind).Where(e => rules.Any(r => r.Slot == e.Slot)))
            {
                var same = row.Name == source.Name && row.TypeName == source.TypeName && row.Category == source.Category && row.Entity == source.Entity;
                var supported = rules.Single(r => r.Slot == source.Slot).Entities.Contains(row.Entity) && (!AreaSlots.Contains(source.Slot)||row.AreaScheme!=null);
                var blocked = !same || !supported || !string.IsNullOrEmpty(source.Caution);
                candidates.Add(new ClassificationCandidate { Slot = source.Slot, CanPrepare = !blocked, RequiresReview = blocked,
                    Evidence = source.SourceIfc + " / #" + source.StepId + " / Revit Tag " + source.RevitTag + "；UniqueId 对应；"
                        + (same ? "当前名称、类型及类别一致" : "当前名称、类型或类别已变化，需重新核对")
                        + (string.IsNullOrEmpty(source.Caution) ? "" : "；" + source.Caution) });
            }
            var text = (row.Name ?? "") + " " + (row.TypeName ?? "");
            foreach (var rule in rules)
            {
                if (rule.Slot == "窗" || rule.Slot == "墙") continue; // Human classification or dedicated evidence, never generic carrier names.
                if(AreaSlots.Contains(rule.Slot) && row.AreaScheme!=null)continue;
                var exact = text.Contains(rule.Slot);
                var alias = rule.Slot == "规划净用地" && (text.Contains("净用地") || text.Contains("场地"))
                    || rule.Slot == "绿地" && text.Contains("绿化")
                    || rule.Slot == "消防回车场" && text.Contains("回车场")
                    || rule.Slot == "规划控制线" && text.Contains("规划红线");
                if (!exact && !alias) continue;
                var supported = rule.Entities.Contains(row.Entity) && (!AreaSlots.Contains(rule.Slot) || row.AreaScheme!=null);
                var greenDefault = alias && rule.Slot == "绿地" && supported;
                candidates.Add(new ClassificationCandidate { Slot = rule.Slot, CanPrepare = (exact || greenDefault) && supported, RequiresReview = !supported,
                    Evidence = (exact ? "名称含业务名称" : greenDefault ? "绿化楼板默认按绿地补齐，可人工修改" : "名称别名候选，需确认业务含义") + "：“" + row.Name + " / " + row.TypeName + "” → " + rule.Slot
                        + (supported ? "" : "；当前载体 " + row.Entity + "，成功案例载体 " + string.Join("、", rule.Entities) + "，需核对载体/导出关系") });
            }
            // These are possible calculation carriers, not proof of planning use.
            // Keep them in the human review list even when their family names are generic.
            if (row.SuggestedSlots.Length == 0 && candidates.Count == 0)
            {
                string suggested = kind == "单体建筑" && row.Entity == "IfcSlab" && row.Category == "楼板" && !new[]{"道路","用地","场地","绿化","绿地","停车"}.Any(text.Contains) ? "建筑区域"
                    : kind == "总图" && text.Contains("用地红线") ? "规划净用地"
                    : kind == "总图" && ((row.Name??"").Trim()=="道路" || (row.TypeName??"").Trim()=="道路") ? "道路边界线" : null;
                if (suggested != null && rules.Any(r => r.Slot == suggested && r.Entities.Contains(row.Entity)))
                    candidates.Add(new ClassificationCandidate { Slot=suggested, CanPrepare=false, RequiresReview=true,
                        Evidence="模型载体候选："+row.Name+"；请确认业务范围；楼板、道路或用地红线名称本身不等于报规分类" });
            }
            return candidates.GroupBy(c => c.Slot).Select(g => new ClassificationCandidate { Slot = g.Key,
                Evidence = string.Join("；", g.Select(c => c.Evidence).Distinct()), RequiresReview = g.Any(c => c.RequiresReview),
                CanPrepare = g.Any(c => c.CanPrepare) && !g.Any(c => c.RequiresReview) }).OrderBy(c => c.Slot, StringComparer.Ordinal).ToArray();
        }
        public static ModelDetection Detect(ModelElementCheck[] rows)
        {
            var evidence = new List<string>();
            var siteRoles = ForModel("报规", "总图").Where(r => r.Binding == "实例" && !AreaSlots.Contains(r.Slot)).Select(r => r.Slot).ToArray();
            var site = rows.Where(r => Roles(r).Concat(r.SuggestedSlots).Any(siteRoles.Contains) || SuggestSlots(r, "总图").Any(siteRoles.Contains) || r.Entity == "IfcSite").ToArray();
            var building = rows.Where(r => Roles(r).Concat(r.SuggestedSlots).Contains("建筑区域") ||
                GetCandidates(r, "单体建筑").Any(c=>c.Slot=="建筑区域" && c.CanPrepare)).ToArray();
            var namedSite = rows.Where(r => new[] { "IfcSlab", "IfcSpace", "IfcBuildingElementProxy", "IfcSite" }.Contains(r.Entity)).ToArray();
            var siteTerms = new[] { "场地", "道路", "绿化", "回车场", "规划红线", "停车位" }
                .Where(term => namedSite.Any(r => ((r.Name ?? "") + " " + (r.TypeName ?? "")).Contains(term))).ToArray();
            bool siteEvidence = site.Length > 0 || siteTerms.Length >= 2;
            var walls = rows.Count(r => r.Entity == "IfcWall"); var openings = rows.Count(r => r.Entity == "IfcWindow" || r.Entity == "IfcDoor");
            var floors=rows.Count(r=>r.Entity=="IfcSlab" && r.Category=="楼板");
            var levels=rows.Count(r=>r.Entity=="IfcBuildingStorey");
            bool buildingComposition=walls>=3 && (openings>=2 || floors>=2 && levels>=2);
            if (site.Length > 0) evidence.Add("场地/用地/道路业务证据 " + site.Length + " 项，例如：" + site[0].Id + " " + site[0].Name + " " + site[0].SlotText);
            if (siteTerms.Length >= 2) evidence.Add("场地内容组合：" + string.Join("、", siteTerms) + "；用于模型类型识别，具体业务分类仍需核对");
            if (building.Length > 0) evidence.Add("建筑区域业务证据 " + building.Length + " 项");
            bool buildingEvidence = building.Length > 0 || buildingComposition;
            if (buildingComposition) evidence.Add("建筑组合：墙 " + walls + "、门窗 " + openings + "、楼板 " + floors + "、标高 " + levels + "；用于识别模型类型，楼板的建筑区域用途仍需确认");
            if (evidence.Count == 0) evidence.Add("缺少足够的场地或建筑组合证据，请人工指定类型。");
            return new ModelDetection { Kind = siteEvidence ? buildingEvidence ? "混合模型" : "总图" : buildingEvidence ? "单体建筑" : "待确认", Evidence = evidence.ToArray() };
        }
        public static ModelSummary Summarize(string stage, string kind, ModelElementCheck[] elements) => Summarize(stage, kind, elements, "");
        public static ModelSummary Summarize(string stage, string kind, ModelElementCheck[] elements, string profileId)
        {
            PlatformInputReview.ApplyManualPolicy(elements);
            var rules = ForModel(stage, kind, profileId);
            var suggestions = new Dictionary<ModelElementCheck, string[]>();
            Func<ModelElementCheck, string[]> suggestionsFor = row => {
                string[] values;
                if (!suggestions.TryGetValue(row, out values))
                    suggestions[row] = values = SuggestSlots(row, kind, profileId);
                return values;
            };
            var roles = rules.Select(rule => {
                var rows = elements.Where(e => Roles(e).Contains(rule.Slot)).ToArray();
                var count = rule.Binding == "类型" ? rows.Select(e => e.TypeId > 0 ? "type:" + e.TypeId : e.UniqueId).Distinct().Count() : rows.Length;
                var candidates = elements.Count(e => !Roles(e).Contains(rule.Slot) && (e.SuggestedSlots.Contains(rule.Slot) || string.IsNullOrEmpty(e.Slot) && suggestionsFor(e).Contains(rule.Slot)));
                var fields = rows.SelectMany(e => e.Fields.Where(f => f.PropertySet == rule.Properties.FirstOrDefault()?.PropertySet)
                    .Select(f => new { Row = e, Field = f }))
                    .GroupBy(x => (x.Field.OwnerUniqueId ?? x.Row.UniqueId) + "|" + x.Field.PropertySet + "|" + x.Field.Binding + "|" + x.Field.Property)
                    .Select(g => g.First()).ToArray();
                var missing = fields.Count(x => !PlanningInputs.AcceptableForRules(x.Field,x.Row.Fields,profileId));
                var manualMissing = elements.SelectMany(e => e.Fields.Where(f => f.PropertySet == rule.Properties.FirstOrDefault()?.PropertySet && f.ManualInputRequired && !f.Valid)
                    .Select(f => new { Row=e, Field=f })).GroupBy(x => (x.Field.OwnerUniqueId ?? x.Row.UniqueId) + "|" + x.Field.Source).Count();
                var incomplete = rows.Count(e => !string.IsNullOrEmpty(e.CoverageNote) || !string.IsNullOrEmpty(e.Issue));
                return new RoleCoverage { Slot = rule.Slot, Mode = rule.Required ? "必要" : rule.ReferenceSupplement ? "参考补充" : "案例补充", Count = count, Candidates = candidates,
                    MissingFields = missing, MissingManualFields = manualMissing, IncompleteObjects = incomplete,
                    State = manualMissing > 0 ? (rule.Slot=="建筑技术"?"建筑用途名称待填写 ":"消防道路宽度待手填 ") + manualMissing + " 项" : count > 0 ? incomplete > 0 ? "模型待核对 " + incomplete + " 项" : missing > 0 ? "已识别 · 属性可查看" : "本轮属性已齐" : candidates > 0 ? "待确认分类" : rule.Required ? "参考项未找到" : "案例差异项 · 按实际适用" };
            }).ToArray();
            var fields = elements.SelectMany(e => e.Fields.Select(f => new { Row = e, Field = f }))
                .GroupBy(x => (x.Field.OwnerUniqueId ?? x.Row.UniqueId) + "|" + x.Field.PropertySet + "|" + x.Field.Binding + "|" + x.Field.Property).Select(g => g.First()).ToArray();
            var result = new ModelSummary { Stage = stage, ModelKind = kind, Roles = roles, ElementCount = elements.Length,
                ClassifiedCount = elements.Count(e => Roles(e).Length > 0), FieldCount = fields.Length, ValidFields = fields.Count(x => x.Field.Valid),
                PresentFields = fields.Count(x => x.Field.State != "缺参数"), ReviewCount = elements.Count(e => !string.IsNullOrEmpty(e.Issue) || e.SuggestedSlots.Length > 0),
                CoverageGaps = elements.Count(e => !string.IsNullOrEmpty(e.CoverageNote)), MissingRequired = roles.Count(r => r.Mode == "必要" && r.Count == 0 && r.Candidates == 0),
                PendingClassificationRoles = roles.Count(r => r.Mode == "必要" && r.Count == 0 && r.Candidates > 0),
                PendingRequirements = rules.Length == 0 ? 1 : 0,
                Basis = stage == "施工图" ? "施工图检查规则待建立" : rules.Length == 0 ? "请先人工确认阶段与模型类型" : "报规成功案例 01 + 参考软件工规区域配置 · " + kind + " · 区域补充项按实际适用",
                FieldPercent = fields.Length == 0 ? (double?)null : 100.0 * fields.Count(x => x.Field.Valid) / fields.Length };
            result.Ready = rules.Length > 0 && elements.Length > 0 && result.MissingRequired == 0 && result.PendingClassificationRoles == 0 && result.ReviewCount == 0 && result.CoverageGaps == 0 && fields.Length > 0 && fields.All(x => PlanningInputs.AcceptableForRules(x.Field,x.Row.Fields,profileId));
            if (PlatformRules.IsActive(profileId)) {
                result.Basis = PlatformRules.Title + " · " + kind + "；按实际业务适用，不等于平台验收";
                result.Ready = result.Ready && PlanningInputs.Issues(elements, kind, profileId).Length == 0;
            }
            return result;
        }
    }
}
