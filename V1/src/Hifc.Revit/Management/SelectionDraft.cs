using System;
using System.ComponentModel;
using System.Linq;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class SelectionReadResult
    {
        public ModelSnapshot Snapshot { get; set; }
        public string[] Slots { get; set; } = new string[0];
        public string[] Excluded { get; set; } = new string[0];
        public int SelectedCount { get; set; }
    }
    internal interface ISelectionSession
    {
        void Read();
        void Classify(string slot);
        void Write(EditRequest[] requests);
        void Close();
    }
    internal sealed class SelectionDraft
    {
        internal ModelSnapshot Snapshot { get; }
        internal string Slot { get; private set; }
        internal SelectionField[] Fields { get; private set; } = new SelectionField[0];
        internal bool Dirty => Fields.Any(f => f.Pending) || Slot != null && Snapshot.Elements.Any(e => e.Slot != Slot);
        internal event Action Changed;
        internal SelectionDraft(ModelSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Complete || snapshot.Scope != "所选构件") throw new InvalidOperationException("请先读取所选构件。");
            Snapshot = snapshot;
        }
        internal void SetClassification(string slot, ModelElementCheck[] proposed)
        {
            if (proposed.Length != Snapshot.Elements.Length || proposed.Any(r => !Snapshot.Elements.Any(o => o.Id == r.Id && o.UniqueId == r.UniqueId)))
                throw new InvalidOperationException("分类结果与当前处理对象不一致。");
            Slot = slot;
            Fields = proposed.SelectMany(r => r.Fields).GroupBy(f => f.PropertySet + "|" + f.Binding + "|" + f.Source)
                .Where(g => proposed.All(r => r.Fields.Any(f => Key(f) == Key(g.First()))))
                .Select(g => new SelectionField(this, proposed.Select(r => r.Fields.Single(f => Key(f) == Key(g.First()))).ToArray())).ToArray();
            Changed?.Invoke();
        }
        internal EditRequest[] BuildRequests()
        {
            if (string.IsNullOrEmpty(Slot)) throw new InvalidOperationException("请先指定业务类目。");
            var classification = new EditRequest { Kind = "分类", Slot = Slot, Ids = Snapshot.Elements.Select(r => r.Id).ToArray(),
                Baselines = Snapshot.Elements.Select(r => new EditBaseline {ElementId=r.Id,ElementUniqueId=r.UniqueId,OwnerUniqueId=r.UniqueId,Value=r.Slot}).ToArray() };
            return new[] { classification }.Concat(Fields.Where(f => f.Pending).Select(f => new EditRequest {
                Kind="赋值",Slot=Slot,Ids=Snapshot.Elements.Select(r=>r.Id).ToArray(),PropertySet=f.Field.PropertySet,Source=f.Field.Source,Binding=f.Field.Binding,Value=f.RawValue,
                Baselines=Snapshot.Elements.Select((r,i)=>new EditBaseline {ElementId=r.Id,ElementUniqueId=r.UniqueId,OwnerUniqueId=f.Targets[i].OwnerUniqueId,
                    Value=f.Targets[i].Value,State=f.Targets[i].State,SourceSignature=f.Targets[i].SourceSignature}).ToArray()
            })).ToArray();
        }
        internal void UpdateHistory(ManualWrite[] history)
        {
            foreach (var f in Fields) { f.Written = f.Targets.All(t => (history ?? new ManualWrite[0]).Any(h => h.Key == HistoryKey(t) && h.Value == t.Value)); f.Notify(); }
        }
        internal static string HistoryKey(FieldCheck f) => f.OwnerUniqueId + "|" + f.PropertySet + "|" + f.Binding + "|" + f.Source;
        static string Key(FieldCheck f) => f.PropertySet + "|" + f.Binding + "|" + f.Source;
        internal void Notify() => Changed?.Invoke();
    }
    internal sealed class SelectionField : INotifyPropertyChanged
    {
        readonly SelectionDraft draft;
        internal FieldCheck[] Targets { get; }
        internal FieldCheck Field => Targets[0];
        internal bool Pending { get; private set; }
        internal bool Written { get; set; }
        internal string RawValue { get; private set; }
        public string Property => Field.Property;
        public string Detail => Field.Source + " · " + Field.Binding + (Editable ? "" : " · 只读，避免影响未选中的同类型构件")
            + (string.IsNullOrEmpty(Field.SuggestionReason) ? "" : " · " + Field.SuggestionReason);
        public string RawType => Field.RawType;
        public bool Editable => Targets.All(f => f.Binding == "实例" && f.Origin != "type" && (f.Editable || f.State == "缺参数"));
        public string State => Pending ? "待写入" : Written ? "已手工写入" : Targets.Any(f => !string.IsNullOrEmpty(f.SuggestedValue)) ? "自动填写" : Targets.Any(f => !f.Valid) ? "待补充" : "已有值";
        public string Background => Pending ? "#FFFFE9E7" : Written ? "#FFFFE7C2" : "#FFFFFFFF";
        public string[] Choices => new[] { "多个值", "未填写", "是", "否" };
        static string BooleanLabel(string v) => string.IsNullOrWhiteSpace(v) ? "未填写" : v == "1" ? "是" : v == "0" ? "否" : v;
        public string Value
        {
            get
            {
                if (Pending) return RawType == "Boolean" ? BooleanLabel(RawValue) : RawValue;
                var values = Targets.Select(f => !string.IsNullOrEmpty(f.Value) ? f.Value : f.SuggestedValue ?? "").Distinct().ToArray();
                return values.Length != 1 ? "多个值" : RawType == "Boolean" ? BooleanLabel(values[0]) : values[0];
            }
            set
            {
                if (!Editable) throw new InvalidOperationException("该属性只读或来自共享类型。");
                if (value == "多个值" || !Pending && value == Value) return;
                RawValue = RawType == "Boolean" ? value == "是" || value == "true" ? "1" : value == "否" || value == "false" ? "0" : value == "未填写" ? "" : value : value ?? "";
                Pending = true; Notify(); draft.Notify();
            }
        }
        internal SelectionField(SelectionDraft draft, FieldCheck[] targets) { this.draft=draft; Targets=targets; }
        public event PropertyChangedEventHandler PropertyChanged;
        internal void Notify() { foreach (var p in new[]{"Value","State","Background"}) PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(p)); }
    }
}
