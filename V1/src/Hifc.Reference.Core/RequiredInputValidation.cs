using System;
using System.Collections.Generic;
using System.Linq;

namespace Hifc.Reference
{
    public sealed class RequiredInputValidation
    {
        public const string Schema="hifc.required-input-review/1";
        public string schema { get; set; }=Schema;
        public string rule_profile_id { get; set; }
        public string rule_revision { get; set; }=PlatformInputOverrides.Revision;
        public string rule_evidence_sha256 { get; set; }=PlatformInputOverrides.Sha256;
        public bool complete { get; set; }
        public string model_kind { get; set; }
        public int checked_objects { get; set; }
        public int checked_fields { get; set; }
        public int blocking_count { get; set; }
        public static RequiredInputValidation Create(ModelElementCheck[] rows,string profile,string kind,bool complete,InputReviewItem[] inputs)
            =>new RequiredInputValidation {rule_profile_id=profile,model_kind=kind,complete=complete,
                checked_objects=rows.Count(r=>r.Fields.Length>0),checked_fields=rows.Sum(r=>r.Fields.Length),blocking_count=inputs.Count(i=>i.BlocksExport)};
        public static void RequireReceipt(object value)
        {
            var d=value as Dictionary<string,object>;
            object Get(string key)=>d!=null&&d.TryGetValue(key,out var found)?found:null;
            bool Positive(string key)=>Get(key) is int count&&count>0;
            if(d==null||(Get("schema") as string)!=Schema||(Get("rule_revision") as string)!=PlatformInputOverrides.Revision||
                (Get("rule_evidence_sha256") as string)!=PlatformInputOverrides.Sha256||!PlatformRules.IsActive(Get("rule_profile_id") as string)||
                !(Get("complete") is bool done&&done)||!new[]{"总图","单体建筑"}.Contains(Get("model_kind") as string)||
                !Positive("checked_objects")||!Positive("checked_fields")||!(Get("blocking_count") is int blockers&&blockers==0))
                throw new InvalidOperationException("本次导出缺少当前规则的完整必填检查通过记录。请在插件中重新全面检查、补齐缺项并导出后再同步。");
        }
    }
}
