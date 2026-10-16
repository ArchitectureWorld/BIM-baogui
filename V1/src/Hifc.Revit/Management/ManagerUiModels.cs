using System;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal sealed class ModelSnapshot
    {
        public string Scope { get; set; } = "全项目";
        public string DocumentKey { get; set; }
        public string Title { get; set; }
        public string Stage { get; set; }
        public string RequestedKind { get; set; }
        public string ModelKind { get; set; }
        public string[] DetectionEvidence { get; set; } = new string[0];
        public bool Complete { get; set; }
        public string ScanId { get; set; }
        public string CompletedUtc { get; set; }
        public ModelElementCheck[] Elements { get; set; } = new ModelElementCheck[0];
    }
    internal sealed class EditRequest
    {
        public string Kind { get; set; }
        public int[] Ids { get; set; }
        public string Slot { get; set; }
        public string Source { get; set; }
        public string Value { get; set; }
        public string PropertySet { get; set; }
        public string Binding { get; set; }
        public EditBaseline[] Baselines { get; set; } = new EditBaseline[0];
    }
    internal sealed class EditBaseline
    {
        public int ElementId { get; set; }
        public string ElementUniqueId { get; set; }
        public string OwnerUniqueId { get; set; }
        public string Value { get; set; }
        public string State { get; set; }
        public string SourceSignature { get; set; }
    }
    internal sealed class EditChange
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string ExpectedRaw { get; set; }
        public string OwnerUniqueId { get; set; }
        public string Action { get; set; }
        public string Binding { get; set; }
        public string Detail { get; set; }
    }
    internal sealed class EditPreview
    {
        public string DocumentKey { get; set; }
        public string SnapshotHash { get; set; }
        public EditRequest Request { get; set; }
        public EditChange[] Changes { get; set; }
        public string[] Conflicts { get; set; } = new string[0];
        public string ScanId { get; set; }
        public ModelElementCheck[] ProposedElements { get; set; } = new ModelElementCheck[0];
    }
    internal interface IManagerSession
    {
        void Scan();
        void PrepareAll();
        void Cancel();
        void Locate(int[] ids);
        void Preview(EditRequest request);
        void Apply(EditPreview preview);
        void Write(EditRequest[] requests);
        void Export(int[] ids, string path);
        void Close();
    }
}
