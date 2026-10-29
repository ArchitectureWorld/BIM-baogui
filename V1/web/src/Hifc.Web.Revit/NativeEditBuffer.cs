using System;
using System.Linq;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    // Loading a mixed selection is read-only. Only Set represents explicit intent.
    public sealed class NativeEditBuffer
    {
        private readonly FieldResult[] fields;
        private readonly Action<FieldResult, string> changed;
        public bool Mixed { get; private set; }
        public string Input { get; private set; }
        public NativeEditBuffer(FieldResult[] fields, EditDraft[] drafts, Action<FieldResult, string> changed)
        {
            if (fields == null || fields.Length == 0 || drafts == null || changed == null) throw new ArgumentException("属性编辑输入不完整。");
            this.fields = fields; this.changed = changed;
            var values = fields.Select(item => Display(item, drafts.SingleOrDefault(d => d.FieldKey == item.Key)?.Value
                ?? NativeUnitInput.EditorValue(item) ?? "")).Distinct(StringComparer.Ordinal).ToArray();
            Mixed = values.Length > 1; Input = Mixed ? null : values[0];
        }
        public void Set(string value)
        {
            if (fields.Any(item => !item.Editable)) throw new InvalidOperationException("所选属性中有不可编辑项，未修改任何草稿。");
            value = Display(fields[0], value);
            if (!Mixed && Input == value) return;
            foreach (var item in fields) changed(item, value);
            Input = value; Mixed = false;
        }
        private static string Display(FieldResult item, string value)
        {
            if (item.RawType != "Boolean") return value;
            if (value == "是" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return "1";
            if (value == "否" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return "0";
            return value;
        }
    }
}
