using System;
using System.Linq;

namespace Hifc.Reference
{
    public static class ControlLinePolicy
    {
        public const string PropertySet = "Pset_规划控制线信息属性集";
        public const string NetLandLabel = "净用地控制红线";
        static bool NetLandName(string value) => new[] { "用地红线", "净用地规划控制红线", NetLandLabel }
            .Contains(value?.Trim(), StringComparer.Ordinal);

        // A net-land name is evidence only after the carrier has been assigned
        // the planning-control property group. Do not reclassify net-land slabs
        // or apply this label to road/building control lines.
        public static void Populate(ModelElementCheck row)
        {
            var fields = row.Fields.Where(f => f.PropertySet == PropertySet &&
                (f.Property == "名称" || f.Property == "红线类型")).ToArray();
            if (fields.Length == 0 || !(NetLandName(row.Name) || NetLandName(row.TypeName) ||
                fields.Any(f => NetLandName(f.Value) || f.RequiredValue == NetLandLabel))) return;
            foreach (var field in fields)
            {
                field.RequiredValue = NetLandLabel;
                field.AllowedEmpty = false;
                if (!field.RequiresRuleWrite || !(field.State == "缺参数" || field.State == "未填写" ||
                    field.State == "有效" || field.State == "值非法")) continue;
                field.SuggestedValue = NetLandLabel;
                field.SuggestionReason = "已确认净用地控制红线规则：名称和红线类型统一填写“" + NetLandLabel + "”"
                    + (string.IsNullOrWhiteSpace(field.Value) ? "" : "；当前值“" + field.Value + "”将在写入时修正");
            }
        }
    }
}
