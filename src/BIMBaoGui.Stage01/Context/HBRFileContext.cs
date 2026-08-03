using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.Context
{
  public sealed class HBRFileContext
  {
    public HBRFileContext(
      string schemaVersion,
      string workflowVersion,
      string fileGuid,
      string revitDocumentFingerprint,
      string revitDocumentTitle,
      string projectNumber,
      string projectName,
      string subitemCode,
      string subitemName,
      string modelFileType,
      string modelScope,
      HBRSpatialReference spatialReference,
      IDictionary<string, PlanningTargetValue> planningTargets,
      IDictionary<string, bool> projectConditions,
      IEnumerable<string> activatedRuleIds,
      IEnumerable<string> notApplicableRuleIds,
      bool initializationPassed,
      bool officialProtocolCompatible,
      string rulePackVersion,
      string sourcePayloadHash,
      string fileContextHash)
      : this(
        schemaVersion,
        workflowVersion,
        fileGuid,
        revitDocumentFingerprint,
        revitDocumentTitle,
        projectNumber,
        projectName,
        subitemCode,
        subitemName,
        modelFileType,
        modelScope,
        spatialReference,
        planningTargets,
        projectConditions,
        activatedRuleIds,
        notApplicableRuleIds,
        initializationPassed,
        officialProtocolCompatible,
        rulePackVersion,
        string.Empty,
        string.Empty,
        string.Empty,
        sourcePayloadHash,
        fileContextHash)
    {
    }

    public HBRFileContext(
      string schemaVersion,
      string workflowVersion,
      string fileGuid,
      string revitDocumentFingerprint,
      string revitDocumentTitle,
      string projectNumber,
      string projectName,
      string subitemCode,
      string subitemName,
      string modelFileType,
      string modelScope,
      HBRSpatialReference spatialReference,
      IDictionary<string, PlanningTargetValue> planningTargets,
      IDictionary<string, bool> projectConditions,
      IEnumerable<string> activatedRuleIds,
      IEnumerable<string> notApplicableRuleIds,
      bool initializationPassed,
      bool officialProtocolCompatible,
      string rulePackVersion,
      string rulePackageId,
      string rulePackageVersion,
      string rulePackageSha256,
      string sourcePayloadHash,
      string fileContextHash)
    {
      SchemaVersion = schemaVersion ?? string.Empty;
      WorkflowVersion = workflowVersion ?? string.Empty;
      FileGuid = fileGuid ?? string.Empty;
      RevitDocumentFingerprint = revitDocumentFingerprint ?? string.Empty;
      RevitDocumentTitle = revitDocumentTitle ?? string.Empty;
      ProjectNumber = projectNumber ?? string.Empty;
      ProjectName = projectName ?? string.Empty;
      SubitemCode = subitemCode ?? string.Empty;
      SubitemName = subitemName ?? string.Empty;
      ModelFileType = modelFileType ?? string.Empty;
      ModelScope = modelScope ?? string.Empty;
      SpatialReference = spatialReference;
      PlanningTargets = new Dictionary<string, PlanningTargetValue>(planningTargets ?? new Dictionary<string, PlanningTargetValue>(), StringComparer.Ordinal);
      ProjectConditions = new Dictionary<string, bool>(projectConditions ?? new Dictionary<string, bool>(), StringComparer.Ordinal);
      ActivatedRuleIds = (activatedRuleIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
      NotApplicableRuleIds = (notApplicableRuleIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
      InitializationPassed = initializationPassed;
      OfficialProtocolCompatible = officialProtocolCompatible;
      RulePackVersion = rulePackVersion ?? string.Empty;
      RulePackageId = rulePackageId ?? string.Empty;
      RulePackageVersion = rulePackageVersion ?? string.Empty;
      RulePackageSha256 = rulePackageSha256 ?? string.Empty;
      SourcePayloadHash = sourcePayloadHash ?? string.Empty;
      FileContextHash = fileContextHash ?? string.Empty;
    }

    public string SchemaVersion { get; }
    public string WorkflowVersion { get; }
    public string FileGuid { get; }
    public string RevitDocumentFingerprint { get; }
    public string RevitDocumentTitle { get; }
    public string ProjectNumber { get; }
    public string ProjectName { get; }
    public string SubitemCode { get; }
    public string SubitemName { get; }
    public string ModelFileType { get; }
    public string ModelScope { get; }
    public HBRSpatialReference SpatialReference { get; }
    public IReadOnlyDictionary<string, PlanningTargetValue> PlanningTargets { get; }
    public IReadOnlyDictionary<string, bool> ProjectConditions { get; }
    public IReadOnlyList<string> ActivatedRuleIds { get; }
    public IReadOnlyList<string> NotApplicableRuleIds { get; }
    public bool InitializationPassed { get; }
    public bool OfficialProtocolCompatible { get; }
    public string RulePackVersion { get; }
    public string RulePackageId { get; }
    public string RulePackageVersion { get; }
    public string RulePackageSha256 { get; }
    public string SourcePayloadHash { get; }
    public string FileContextHash { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(FileGuid)
      && !string.IsNullOrWhiteSpace(RevitDocumentFingerprint)
      && !string.IsNullOrWhiteSpace(RulePackageId)
      && !string.IsNullOrWhiteSpace(RulePackageVersion)
      && !string.IsNullOrWhiteSpace(RulePackageSha256)
      && !string.IsNullOrWhiteSpace(FileContextHash);

    public bool IsReady => IsValid
      && InitializationPassed
      && OfficialProtocolCompatible;

    internal HBRFileContext WithHash(string hash)
    {
      return new HBRFileContext(
        SchemaVersion,
        WorkflowVersion,
        FileGuid,
        RevitDocumentFingerprint,
        RevitDocumentTitle,
        ProjectNumber,
        ProjectName,
        SubitemCode,
        SubitemName,
        ModelFileType,
        ModelScope,
        SpatialReference,
        PlanningTargets.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
        ProjectConditions.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
        ActivatedRuleIds,
        NotApplicableRuleIds,
        InitializationPassed,
        OfficialProtocolCompatible,
        RulePackVersion,
        RulePackageId,
        RulePackageVersion,
        RulePackageSha256,
        SourcePayloadHash,
        hash);
    }

    public override string ToString()
    {
      string state = InitializationPassed ? "初始化通过" : "初始化未通过";
      return "HBR_FileContext / " + ModelFileType + " / " + ProjectName + " / " + SubitemName + " / " + state;
    }
  }
}
