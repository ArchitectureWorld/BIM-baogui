using System;
using System.Collections.Generic;

namespace Hifc.Web.Contracts
{
    public static class ModelStorageSchemas
    {
        public const string WebSettingsId = "4ca6adf6-957d-4d80-a982-87ea6f46c809";
        public const string WebSettingsName = "HifcWebModelSettingsV2";
        public const string JsonField = "Json";
    }
    public sealed class ModelRoleChoice
    {
        public string UniqueId { get; set; }
        public string Slot { get; set; }
        public string Identity { get; set; }
    }
    public sealed class UndergroundSelection
    {
        public bool Confirmed { get; set; }
        public double? GroundElevationM { get; set; }
        public string[] LevelUniqueIds { get; set; } = new string[0];
        public string BaseLevelUniqueId { get; set; }
    }
    public sealed class ModelSettings
    {
        public string RuleProfileId { get; set; } = "";
        public ModelRoleChoice[] Roles { get; set; } = new ModelRoleChoice[0];
        public Dictionary<string, string> CommonValues { get; set; } = new Dictionary<string, string>();
        public bool LevelsConfirmed { get; set; }
        public string[] BuildingLevelUniqueIds { get; set; } = new string[0];
        public string TopBoundaryLevelUniqueId { get; set; }
        public UndergroundSelection Underground { get; set; } = new UndergroundSelection();
    }
    public sealed class RuleProfileInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Scope { get; set; }
    }
    public sealed class ProjectDataRecord
    {
        public string SchemaId { get; set; }
        public string SchemaName { get; set; }
        public string Json { get; set; }
        public string Hash { get; set; }
    }
    public sealed class MaterialPreviewRequest
    {
        public RequestContext Context { get; set; }
        public string Kind { get; set; }
        public string FileName { get; set; }
        public string SourcePath { get; set; }
        public string ContentBase64 { get; set; }
        public string TableId { get; set; }
        public string TargetBuildingUniqueId { get; set; }
        public string DataJson { get; set; }
        public NativeRedlineEvidence Evidence { get; set; }
        public double ToleranceM { get; set; } = .01;
        public double MinimumGapM { get; set; } = .001;
    }
    public sealed class MaterialTableInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Kind { get; set; }
        public string BuildingName { get; set; }
    }
    public sealed class MaterialChange
    {
        public string Key { get; set; }
        public string FieldKey { get; set; }
        public string ElementUniqueId { get; set; }
        public string ElementName { get; set; }
        public string Property { get; set; }
        public string CurrentValue { get; set; }
        public string Value { get; set; }
        public string Unit { get; set; }
        public string SourceLocation { get; set; }
        public string Issue { get; set; }
        public bool Selected { get; set; }
        public bool CanSelect { get; set; }
        public bool IsConflict { get; set; }
    }
    public sealed class MaterialReference
    {
        public string Field { get; set; }
        public string Value { get; set; }
        public string LevelName { get; set; }
        public string Unit { get; set; }
        public string Status { get; set; }
        public string SourceLocation { get; set; }
    }
    public sealed class MaterialPreview
    {
        public RequestContext Context { get; set; }
        public string Id { get; set; }
        public string Kind { get; set; }
        public string FileName { get; set; }
        public string Sha256 { get; set; }
        public MaterialTableInfo[] Tables { get; set; } = new MaterialTableInfo[0];
        public MaterialChange[] Changes { get; set; } = new MaterialChange[0];
        public MaterialReference[] References { get; set; } = new MaterialReference[0];
        public string[] Issues { get; set; } = new string[0];
        public string DataJson { get; set; }
    }
    public sealed class MaterialDecision
    {
        public string PreviewId { get; set; }
        public string[] SelectedKeys { get; set; } = new string[0];
        public bool RetainReferences { get; set; } = true;
        public bool Accepted { get; set; }
    }
    public sealed class MaterialSelectionRequest
    {
        public RequestContext Context { get; set; }
        public MaterialDecision[] Decisions { get; set; } = new MaterialDecision[0];
    }
}
