using System;
using System.Collections.Generic;

namespace Hifc.Web.Contracts
{
    public sealed class NativePoint3
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
    public sealed class NativeRedlineCarrier
    {
        public int ElementId { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public NativePoint3[] TopInternal { get; set; } = new NativePoint3[0];
        public NativePoint3[] BottomInternal { get; set; } = new NativePoint3[0];
        public NativePoint3[] TopShared { get; set; } = new NativePoint3[0];
        public NativePoint3[] BottomShared { get; set; } = new NativePoint3[0];
        public double BottomM { get; set; }
        public double TopM { get; set; }
        public bool HorizontalLinearFaces { get; set; }
        public string UnsupportedReason { get; set; }
        public string GeometryFingerprint { get; set; }
    }
    public sealed class NativeRedlinePair
    {
        public string LeftUniqueId { get; set; }
        public string RightUniqueId { get; set; }
        public double? IntersectionVolumeM3 { get; set; }
        public string Error { get; set; }
    }
    public sealed class NativeRedlineEvidence
    {
        public NativeRedlineCarrier[] Carriers { get; set; } = new NativeRedlineCarrier[0];
        public NativeRedlinePair[] Pairs { get; set; } = new NativeRedlinePair[0];
        public string ProjectLocationName { get; set; }
        public string Fingerprint { get; set; }
    }
    public sealed class NativeProjectionOverlap
    {
        public string OtherUniqueId { get; set; }
        public double? OverlapM2 { get; set; }
        public string Error { get; set; }
    }
    public sealed class NativeDefinitionSnapshot
    {
        public string Source { get; set; }
        public string Guid { get; set; }
        public int ParameterId { get; set; }
        public string DefinitionType { get; set; }
        public string Origin { get; set; }
        public int[] CategoryIds { get; set; } = new int[0];
        public ParameterValue[] Values { get; set; } = new ParameterValue[0];
        public string[] Dependencies { get; set; } = new string[0];
        public string Hash { get; set; }
    }
    public sealed class NativeDefinitionOperation
    {
        public string Source { get; set; }
        public string Guid { get; set; }
        public string DefinitionType { get; set; }
        public string Origin { get; set; }
        public int[] CategoryIds { get; set; } = new int[0];
        public string ExpectedBindingHash { get; set; }
        // Existing definition identity is required before deletion/recreation.
        public int? ReplaceParameterId { get; set; }
        public NativeMigrationValue[] MigrationValues { get; set; } = new NativeMigrationValue[0];
    }
    public sealed class NativeMigrationValue
    {
        public string Source { get; set; }
        public int OwnerId { get; set; }
        public string OwnerUniqueId { get; set; }
        public string ExpectedSignature { get; set; }
        public string Value { get; set; }
        public bool HasValue { get; set; }
    }
    public sealed class NativeDataWrite
    {
        public string SchemaId { get; set; }
        public string SchemaName { get; set; }
        public string ExpectedHash { get; set; }
        public string Json { get; set; }
        public string Hash { get; set; }
        public NativeSourceFile[] SourceFiles { get; set; } = new NativeSourceFile[0];
    }
    public sealed class NativeSourceFile
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
    }
    public sealed class NativeOperations
    {
        public NativeDefinitionOperation[] Definitions { get; set; } = new NativeDefinitionOperation[0];
        public NativeDataWrite[] DataWrites { get; set; } = new NativeDataWrite[0];
    }
    public sealed class ExportRequest
    {
        public RequestContext Context { get; set; }
        public string FileName { get; set; }
        public bool HasPendingMaterials { get; set; }
    }
    public sealed class NativeExportProperty
    {
        public string Property { get; set; }
        public string BridgeName { get; set; }
        public string BridgeGuid { get; set; }
        public string ExportType { get; set; }
    }
    public sealed class NativeExportRoute
    {
        public string PropertySet { get; set; }
        public string Entity { get; set; }
        public string Origin { get; set; }
        public string[] OwnerUniqueIds { get; set; } = new string[0];
        public bool ExactOwnerFilter { get; set; }
        public NativeExportProperty[] Properties { get; set; } = new NativeExportProperty[0];
    }
    public sealed class ExportPlan
    {
        public RequestContext Context { get; set; }
        public string Id { get; set; }
        public string CaptureHash { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public string Hash { get; set; }
        public string Signature { get; set; }
        public string FileName { get; set; }
        public string MappingText { get; set; }
        public WriteChange[] TemporaryChanges { get; set; } = new WriteChange[0];
        public NativeOperations Native { get; set; } = new NativeOperations();
        public NativeExportRoute[] Routes { get; set; } = new NativeExportRoute[0];
        public string[] SuppressParameterNames { get; set; } = new string[0];
        public string ExpectedDataHash { get; set; }
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    }
    public sealed class ExportReceipt
    {
        public string PlanId { get; set; }
        public string DocumentSessionId { get; set; }
        public string IfcPath { get; set; }
        public string IfcHash { get; set; }
        public string ReceiptPath { get; set; }
        public string SnapshotBefore { get; set; }
        public string SnapshotAfter { get; set; }
        public bool SourceRestored { get; set; }
        public bool Exported { get; set; }
        public DateTime CompletedUtc { get; set; }
    }
}
