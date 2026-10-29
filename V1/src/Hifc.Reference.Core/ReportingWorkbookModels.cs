using System;

namespace Hifc.Reference
{
    public sealed class ReportingWorkbookFile
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
    }
    public sealed class ReportingWorkbookProvenance
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
        public string SheetName { get; set; }
        public string LabelCell { get; set; }
        public string ValueCell { get; set; }
        public string LevelCell { get; set; }
        public string UnitCell { get; set; }
        public string NoteCell { get; set; }
        public string RawValue { get; set; }
        public string CellType { get; set; }
        public string NumberFormat { get; set; }
        public string Formula { get; set; }
        public bool HasCachedValue { get; set; }
    }
    public sealed class ReportingWorkbookItem
    {
        public string Field { get; set; }
        public string Value { get; set; }
        public string LevelName { get; set; }
        public string Unit { get; set; }
        public string Note { get; set; }
        public string Issue { get; set; }
        public ReportingWorkbookProvenance Provenance { get; set; }
    }
    public sealed class ReportingWorkbookTable
    {
        public string Id { get; set; }
        public string SheetName { get; set; }
        public string Kind { get; set; }
        public string BuildingName { get; set; }
        public bool AmbiguousBuilding { get; set; }
        public ReportingWorkbookItem[] Items { get; set; } = new ReportingWorkbookItem[0];
        public string[] Issues { get; set; } = new string[0];
        public string Title => SheetName + " · " + Kind + (string.IsNullOrWhiteSpace(BuildingName) ? "" : " · " + BuildingName);
    }
    public sealed class ReportingWorkbookDocument
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
        public ReportingWorkbookTable[] Tables { get; set; } = new ReportingWorkbookTable[0];
        public string[] Issues { get; set; } = new string[0];
    }
    public sealed class ReportingWorkbookChange
    {
        public int ElementId { get; set; }
        public string ElementUniqueId { get; set; }
        public string ElementName { get; set; }
        public string OwnerUniqueId { get; set; }
        public string PropertySet { get; set; }
        public string Property { get; set; }
        public string Source { get; set; }
        public string Binding { get; set; }
        public string RawType { get; set; }
        public string CurrentValue { get; set; }
        public string CurrentDisplayValue { get; set; }
        public string CurrentState { get; set; }
        public string SourceSignature { get; set; }
        public string Value { get; set; }
        public bool CanSelect { get; set; }
        public string ValueUnit => PropertyUnits.Canonical(Property,RawType);
        public bool Selected { get; set; }
        public bool IsConflict { get; set; }
        public string Issue { get; set; }
        public ReportingWorkbookProvenance Provenance { get; set; }
        public string SourceLocation => Provenance == null ? "" : Provenance.SheetName + "!" + Provenance.ValueCell;
        public string PreviewValue => Value+(string.IsNullOrEmpty(ValueUnit)?"":" "+ValueUnit);
        internal FieldCheck ValidationField { get; set; }
        internal string ParentValue { get; set; }
    }
    public sealed class ReportingWorkbookReference
    {
        public string TableId { get; set; }
        public string Kind { get; set; }
        public string BuildingName { get; set; }
        public string Field { get; set; }
        public string Value { get; set; }
        public string LevelName { get; set; }
        public string Unit { get; set; }
        public string Note { get; set; }
        public string Status { get; set; }
        public ReportingWorkbookProvenance Provenance { get; set; }
        public string SourceLocation => Provenance == null ? "" : Provenance.SheetName + "!" + Provenance.ValueCell;
    }
    public sealed class ReportingWorkbookPlan
    {
        public string TableId { get; set; }
        public string TargetBuildingUniqueId { get; set; }
        public ReportingWorkbookChange[] Changes { get; set; } = new ReportingWorkbookChange[0];
        public ReportingWorkbookReference[] References { get; set; } = new ReportingWorkbookReference[0];
        public ReportingWorkbookFile[] SourceFiles { get; set; } = new ReportingWorkbookFile[0];
        public string[] Issues { get; set; } = new string[0];
    }
    public sealed class ReportingWorkbookDecision
    {
        public string TableId { get; set; }
        public string TargetBuildingUniqueId { get; set; }
        public ReportingWorkbookChange[] Changes { get; set; } = new ReportingWorkbookChange[0];
        public ReportingWorkbookReference[] References { get; set; } = new ReportingWorkbookReference[0];
        public ReportingWorkbookFile[] SourceFiles { get; set; } = new ReportingWorkbookFile[0];
    }
}
