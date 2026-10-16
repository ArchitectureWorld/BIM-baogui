using System;

namespace Hifc.Web.Contracts
{
    public sealed class RequestContext
    {
        public int ProtocolMajor { get; set; } = 1;
        public string ClientVersion { get; set; } = "0.5.0";
        public string RuleVersion { get; set; }
        public string RequestId { get; set; }
        public string DocumentSessionId { get; set; }
        public string ScanId { get; set; }
        public string SnapshotHash { get; set; }
    }
    public sealed class ElementRecord
    {
        public int Id { get; set; }
        public string UniqueId { get; set; }
        public int TypeId { get; set; }
        public string TypeUniqueId { get; set; }
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string Category { get; set; }
        public string Entity { get; set; }
        public string DeclaredSlot { get; set; }
        public string AssignedSlot { get; set; }
        public string Level { get; set; }
        public string Building { get; set; }
        public string AreaScheme { get; set; }
        public string CoverageNote { get; set; }
        public double? NativeAreaM2 { get; set; }
        public double? ProjectedAreaM2 { get; set; }
    }
    public sealed class InventoryPage
    {
        public RequestContext Context { get; set; }
        public string Stage { get; set; }
        public string ModelKind { get; set; }
        public int PageIndex { get; set; }
        public int PageCount { get; set; }
        public int TotalElements { get; set; }
        public string PageHash { get; set; }
        public ElementRecord[] Elements { get; set; } = new ElementRecord[0];
    }
    public sealed class CaptureTarget
    {
        public string ElementUniqueId { get; set; }
        public int OwnerId { get; set; }
        public string OwnerUniqueId { get; set; }
        public string Source { get; set; }
        public string Origin { get; set; }
    }
    public sealed class CapturePlan
    {
        public RequestContext Context { get; set; }
        public bool InventoryComplete { get; set; }
        public string ModelKind { get; set; }
        public string[] DetectionEvidence { get; set; } = new string[0];
        public CaptureTarget[] Targets { get; set; } = new CaptureTarget[0];
    }
    public sealed class ParameterValue
    {
        public string OwnerUniqueId { get; set; }
        public int OwnerId { get; set; }
        public string Source { get; set; }
        public string Origin { get; set; }
        public bool Exists { get; set; }
        public bool HasValue { get; set; }
        public string RawValue { get; set; }
        public string DisplayValue { get; set; }
        public string StorageType { get; set; }
        public string Unit { get; set; }
        public bool ReadOnly { get; set; }
        public string SharedGuid { get; set; }
    }
    public sealed class CapturePage
    {
        public RequestContext Context { get; set; }
        public int PageIndex { get; set; }
        public int PageCount { get; set; }
        public int TotalValues { get; set; }
        public string PageHash { get; set; }
        public ParameterValue[] Values { get; set; } = new ParameterValue[0];
    }
    public sealed class FieldResult
    {
        public string Key { get; set; }
        public int ElementId { get; set; }
        public string ElementUniqueId { get; set; }
        public string ElementName { get; set; }
        public int OwnerId { get; set; }
        public string OwnerUniqueId { get; set; }
        public string Slot { get; set; }
        public string PropertySet { get; set; }
        public string Property { get; set; }
        public string Binding { get; set; }
        public string Source { get; set; }
        public string SourceGuid { get; set; }
        public string RawType { get; set; }
        public string State { get; set; }
        public string Value { get; set; }
        public string DisplayValue { get; set; }
        public string SuggestedValue { get; set; }
        public string SuggestionUnit { get; set; }
        public string Detail { get; set; }
        public bool Editable { get; set; }
        public bool AllowedEmpty { get; set; }
        public bool ManualInputRequired { get; set; }
        public string SourceSignature { get; set; }
        public int AffectedInstances { get; set; }
    }
    public sealed class ElementResult
    {
        public ElementRecord Element { get; set; }
        public string[] Slots { get; set; } = new string[0];
        public string[] CandidateSlots { get; set; } = new string[0];
        public string[] CandidateEvidence { get; set; } = new string[0];
        public string Status { get; set; }
    }
    public sealed class CheckResult
    {
        public RequestContext Context { get; set; }
        public string Stage { get; set; }
        public string ModelKind { get; set; }
        public bool Complete { get; set; }
        public string CaptureHash { get; set; }
        public DateTime ServerTimeUtc { get; set; }
        public ElementResult[] Elements { get; set; } = new ElementResult[0];
        public FieldResult[] Fields { get; set; } = new FieldResult[0];
        public string[] AvailableSlots { get; set; } = new string[0];
        public int ReviewCount { get; set; }
    }
}
