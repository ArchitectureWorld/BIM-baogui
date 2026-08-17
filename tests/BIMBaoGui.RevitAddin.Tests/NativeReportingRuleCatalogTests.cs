using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using BIMBaoGui.RevitAddin.Rules;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeReportingRuleCatalogTests
  {
    [Fact]
    public void TotalPlanCatalogExposesEveryTaskMappingAndAcceptanceProperty()
    {
      NativeReportingRuleCatalog reporting = NativeReportingRuleCatalog.Current;

      Assert.Equal(15, reporting.GetTaskIds("总平模型").Count);
      Assert.Equal(13, reporting.GetSemanticRoles("总平模型").Count);
      Assert.Equal(10, reporting.InternalProperties.Count);
      Assert.All(reporting.InternalProperties, value =>
      {
        Assert.Equal(value.PropertyId, value.ParameterGuid.ToString("D"));
        Assert.Equal(
          NativeOfficialCarrierEvidenceStatus.InternalOnly,
          value.EvidenceStatus);
        Assert.False(value.OfficialExportVerified);
      });

      IReadOnlyList<NativeReportingSemanticRole> roles =
        reporting.GetSemanticRoles("总平模型");
      Assert.Equal(37, roles.SelectMany(value => value.AttributeMappings).Count());
      Assert.All(roles, role => Assert.Equal(
        NativeRuleCatalog.Current.TasksById[role.TaskId].AttributeRequirements,
        role.AttributeMappings.Select(value => value.AttributeRequirement)));

      IReadOnlyList<string> acceptanceIds =
        reporting.OfficialAcceptancePropertyIds;
      IReadOnlyList<NativeOfficialAcceptancePropertyDefinition> acceptance =
        reporting.OfficialAcceptanceProperties;
      Assert.Equal(62, acceptanceIds.Count);
      Assert.Equal(
        acceptanceIds.OrderBy(value => value, StringComparer.Ordinal),
        acceptanceIds);
      Assert.Equal(
        acceptanceIds.Count,
        acceptanceIds.Distinct(StringComparer.Ordinal).Count());
      Assert.Equal(acceptanceIds, acceptance.Select(value => value.PropertyId));
      Assert.All(acceptance, value =>
      {
        NativeStage02PropertyDefinition property =
          NativeStage02RuleCatalog.Current.PropertiesById[value.PropertyId];
        Assert.Equal(
          string.Join(
            "|",
            property.IfcEntity,
            property.IfcPropertySet,
            property.IfcProperty),
          value.Identity);
        Assert.Equal(property.DeclaredIfcType, value.DeclaredIfcType);
        Assert.Equal(property.CanonicalUnit, value.CanonicalUnit);
        Assert.Equal(property.ParameterGuid, value.ParameterGuid);
        Assert.Equal("INSTANCE", value.BindingScope);
        Assert.Contains(value.SourceStage, new[]
        {
          NativeReportingSourceStage.Stage01,
          NativeReportingSourceStage.Stage02A,
          NativeReportingSourceStage.Stage02B
        });
      });
    }

    [Fact]
    public void TotalPlanChecksProjectEverySourceRuleExactlyOnce()
    {
      NativeReportingRuleCatalog reporting = NativeReportingRuleCatalog.Current;
      IReadOnlyList<NativeReportingCheckDefinition> checks =
        reporting.GetChecks("总平模型");

      Assert.Equal(140, checks.Count);
      Assert.Equal(
        checks.OrderBy(value => value.Sequence)
          .ThenBy(value => value.CheckId, StringComparer.Ordinal),
        checks);
      Assert.Equal(
        checks.Count,
        checks.Select(value => value.CheckId).Distinct(StringComparer.Ordinal).Count());
      Assert.Contains(checks, value =>
        value.CheckId ==
          "STAGE02B.METRIC.ca21e324-046b-5bfd-84c8-0d3470082303"
        && value.PropertyId == "ca21e324-046b-5bfd-84c8-0d3470082303");
      Assert.Contains(checks, value => value.CheckId == "CROSS.DOCUMENT_IDENTITY");
      Assert.Contains(checks, value => value.CheckId == "EXPORT.REPORT_WRITER");
      Assert.All(
        checks.Where(value => value.TaskId == "SITE.SKELETON"
          && value.CheckKind == NativeReportingCheckKind.Geometry),
        value => Assert.Equal(NativeReportingSourceStage.Stage01, value.SourceStage));

      foreach (string taskId in reporting.GetTaskIds("总平模型"))
      {
        NativeTaskDefinition task = NativeRuleCatalog.Current.TasksById[taskId];
        Assert.Equal(
          task.AttributeRequirements,
          checks.Where(value => value.TaskId == taskId
              && value.CheckKind == NativeReportingCheckKind.AttributeRequirement)
            .Select(value => value.RuleText));
        Assert.Equal(
          task.GeometryChecks,
          checks.Where(value => value.TaskId == taskId
              && value.CheckKind == NativeReportingCheckKind.Geometry)
            .Select(value => value.RuleText));
        Assert.Equal(
          task.PropertyChecks,
          checks.Where(value => value.TaskId == taskId
              && value.CheckKind == NativeReportingCheckKind.PropertyConsistency)
            .Select(value => value.RuleText));
        Assert.Equal(
          task.TargetComparisons,
          checks.Where(value => value.TaskId == taskId
              && value.CheckKind == NativeReportingCheckKind.TargetComparison)
            .Select(value => value.RuleText));
      }
    }

    [Fact]
    public void RuntimeRejectsBrokenAttributeMapping()
    {
      AssertRejects(reporting =>
      {
        Dictionary<string, object> role = Objects(reporting, "semanticRoles")
          .Select(AsObject).First();
        role["attributeMappings"] = Objects(role, "attributeMappings")
          .Skip(1).ToArray();
      });
    }

    [Fact]
    public void RuntimeRejectsMissingGeometryEvaluationPolicy()
    {
      AssertRejects(reporting =>
      {
        reporting["geometryEvaluationPolicies"] =
          Objects(reporting, "geometryEvaluationPolicies").Skip(1).ToArray();
      });
    }

    [Fact]
    public void RuntimeRejectsCrossStageAcceptanceOwnershipConflict()
    {
      AssertRejects(reporting =>
      {
        string metricPropertyId = (string)AsObject(
          Objects(reporting, "stage02BMetrics").First())["propertyId"];
        Dictionary<string, object> role = Objects(reporting, "semanticRoles")
          .Select(AsObject).First();
        Dictionary<string, object> mapping = Objects(role, "attributeMappings")
          .Select(AsObject).First();
        mapping["internalPropertyId"] = metricPropertyId;
        mapping["definitionSource"] = "RULE_PROPERTY";
      });
    }

    [Fact]
    public void RuntimeRejectsOfficialPolicyThatOutrunsPendingMetrics()
    {
      AssertRejects(reporting =>
      {
        Dictionary<string, object> policy =
          AsObject(Objects(reporting, "officialCarrierPolicies").First());
        policy["evidenceStatus"] = "VERIFIED";
      });
    }

    [Fact]
    public void RuntimeRejectsCrossPropertyCarrierAndProbeReferences()
    {
      AssertRejects(reporting =>
      {
        object[] metricValues = Objects(reporting, "stage02BMetrics");
        Dictionary<string, object> metric = AsObject(metricValues[0]);
        string otherPropertyId = (string)AsObject(metricValues[1])["propertyId"];
        metric["officialCarrierStatus"] = "VERIFIED";
        metric["officialProjectionCarrierId"] = "CARRIER.CROSS.PROPERTY";
        metric["officialCarrierProbeRef"] = "PROBE.CROSS.PROPERTY";
        reporting["officialProjectionCarriers"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["carrierId"] = "CARRIER.CROSS.PROPERTY",
            ["propertyId"] = otherPropertyId,
            ["selectorKind"] = "PROJECT_INFORMATION",
            ["roleId"] = string.Empty,
            ["categoryBuiltInId"] = string.Empty,
            ["elementClass"] = "Autodesk.Revit.DB.ProjectInfo",
            ["bindingScope"] = "INSTANCE",
            ["parameterGuid"] = otherPropertyId
          }
        };
        reporting["officialCarrierProbeRecords"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["probeId"] = "PROBE.CROSS.PROPERTY",
            ["propertyId"] = otherPropertyId,
            ["sourceGoldenRvtSha256"] = new string('a', 64),
            ["probeSeedManifestSha256"] = new string('b', 64),
            ["probeRvtSha256"] = new string('c', 64),
            ["probeIfcSha256"] = new string('d', 64),
            ["hifcToolManifestSha256"] = new string('e', 64),
            ["hifcToolDllSha256"] = new string('f', 64),
            ["hifcToolProductVersion"] = "1.0.0",
            ["observedRevitUniqueId"] = "revit-cross-property",
            ["observedIfcGlobalId"] = "ifc-cross-property",
            ["observedBindingScope"] = "INSTANCE",
            ["observedParameterGuid"] = otherPropertyId,
            ["observedSentinel"] = "700002.000002"
          }
        };
      }, "verified metric carrier/probe 外键无效");
    }

    [Fact]
    public void RuntimeRejectsCrossPropertyOfficialEvidenceReference()
    {
      AssertRejects(reporting =>
      {
        object[] metricValues = Objects(reporting, "stage02BMetrics");
        Dictionary<string, object> metric = AsObject(metricValues[0]);
        string propertyId = (string)metric["propertyId"];
        string otherPropertyId = (string)AsObject(metricValues[1])["propertyId"];
        metric["officialCarrierStatus"] = "VERIFIED";
        metric["officialProjectionCarrierId"] = "CARRIER.EVIDENCE.CROSS";
        metric["officialCarrierProbeRef"] = "PROBE.EVIDENCE.CROSS";
        metric["officialExportVerified"] = true;
        metric["officialEvidenceRef"] = "EVIDENCE.CROSS.PROPERTY";
        reporting["officialProjectionCarriers"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["carrierId"] = "CARRIER.EVIDENCE.CROSS",
            ["propertyId"] = propertyId,
            ["selectorKind"] = "PROJECT_INFORMATION",
            ["roleId"] = string.Empty,
            ["categoryBuiltInId"] = string.Empty,
            ["elementClass"] = "Autodesk.Revit.DB.ProjectInfo",
            ["bindingScope"] = "INSTANCE",
            ["parameterGuid"] = propertyId
          }
        };
        reporting["officialCarrierProbeRecords"] = new object[]
        {
          ValidProbe("PROBE.EVIDENCE.CROSS", propertyId)
        };
        string sha = new string('a', 64);
        reporting["officialEvidenceRecords"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["evidenceId"] = "EVIDENCE.CROSS.PROPERTY",
            ["propertyId"] = otherPropertyId,
            ["goldenRvtSha256"] = sha,
            ["hifctoolManifestSha256"] = sha,
            ["hifctoolDllSha256"] = sha,
            ["hifctoolProductVersion"] = "1.0.0",
            ["officialIfcSha256"] = sha,
            ["ifcFluxProductVersion"] = "0.1.0",
            ["ifcFluxReportSha256"] = sha,
            ["observedRevitUniqueId"] = "revit-evidence-cross",
            ["observedIfcGlobalId"] = "ifc-evidence-cross",
            ["observedBindingScope"] = "INSTANCE",
            ["observedParameterGuid"] = otherPropertyId
          }
        };
      }, "verified metric evidence 外键无效");
    }

    [Fact]
    public void RuntimeRejectsIncompleteCarrierProbeEvidence()
    {
      AssertRejects(reporting =>
      {
        Dictionary<string, object> metric =
          AsObject(Objects(reporting, "stage02BMetrics").First());
        string propertyId = (string)metric["propertyId"];
        metric["officialCarrierStatus"] = "VERIFIED";
        metric["officialProjectionCarrierId"] = "CARRIER.INCOMPLETE.PROBE";
        metric["officialCarrierProbeRef"] = "PROBE.INCOMPLETE";
        reporting["officialProjectionCarriers"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["carrierId"] = "CARRIER.INCOMPLETE.PROBE",
            ["propertyId"] = propertyId,
            ["selectorKind"] = "PROJECT_INFORMATION",
            ["roleId"] = string.Empty,
            ["categoryBuiltInId"] = string.Empty,
            ["elementClass"] = "Autodesk.Revit.DB.ProjectInfo",
            ["bindingScope"] = "INSTANCE",
            ["parameterGuid"] = propertyId
          }
        };
        reporting["officialCarrierProbeRecords"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["probeId"] = "PROBE.INCOMPLETE",
            ["propertyId"] = propertyId,
            ["observedBindingScope"] = "INSTANCE",
            ["observedParameterGuid"] = propertyId
          }
        };
      });
    }

    [Fact]
    public void RuntimeRejectsVerifiedPolicyWithMismatchedProbeRefs()
    {
      AssertRejects(reporting =>
      {
        Dictionary<string, object> metric =
          AsObject(Objects(reporting, "stage02BMetrics").First());
        string propertyId = (string)metric["propertyId"];
        metric["officialCarrierStatus"] = "VERIFIED";
        metric["officialProjectionCarrierId"] = "CARRIER.VALID";
        metric["officialCarrierProbeRef"] = "PROBE.VALID";
        reporting["officialProjectionCarriers"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["carrierId"] = "CARRIER.VALID",
            ["propertyId"] = propertyId,
            ["selectorKind"] = "PROJECT_INFORMATION",
            ["roleId"] = string.Empty,
            ["categoryBuiltInId"] = string.Empty,
            ["elementClass"] = "Autodesk.Revit.DB.ProjectInfo",
            ["bindingScope"] = "INSTANCE",
            ["parameterGuid"] = propertyId
          }
        };
        string sha = new string('a', 64);
        reporting["officialCarrierProbeRecords"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["probeId"] = "PROBE.VALID",
            ["propertyId"] = propertyId,
            ["sourceGoldenRvtSha256"] = sha,
            ["probeSeedManifestSha256"] = sha,
            ["probeRvtSha256"] = sha,
            ["probeIfcSha256"] = sha,
            ["hifcToolManifestSha256"] = sha,
            ["hifcToolDllSha256"] = sha,
            ["hifcToolProductVersion"] = "1.0.0",
            ["observedRevitUniqueId"] = "revit-uid",
            ["observedIfcGlobalId"] = "ifc-guid",
            ["observedBindingScope"] = "INSTANCE",
            ["observedParameterGuid"] = propertyId,
            ["observedSentinel"] = "700001.000001"
          }
        };
        Dictionary<string, object> policy =
          AsObject(Objects(reporting, "officialCarrierPolicies").First());
        policy["evidenceStatus"] = "VERIFIED";
      });
    }

    [Fact]
    public void RuntimeRejectsOrphanOfficialCarrier()
    {
      AssertRejects(reporting =>
      {
        string propertyId = (string)AsObject(
          Objects(reporting, "stage02BMetrics").First())["propertyId"];
        reporting["officialProjectionCarriers"] = new object[]
        {
          new Dictionary<string, object>
          {
            ["carrierId"] = "CARRIER.ORPHAN",
            ["propertyId"] = propertyId,
            ["selectorKind"] = "PROJECT_INFORMATION",
            ["roleId"] = string.Empty,
            ["categoryBuiltInId"] = string.Empty,
            ["elementClass"] = "Autodesk.Revit.DB.ProjectInfo",
            ["bindingScope"] = "INSTANCE",
            ["parameterGuid"] = propertyId
          }
        };
      });
    }

    [Fact]
    public void RuntimeRejectsOrphanInternalProperty()
    {
      AssertRejects(reporting =>
      {
        Dictionary<string, object> source =
          AsObject(Objects(reporting, "internalProperties").First());
        var orphan = source.ToDictionary(
          pair => pair.Key,
          pair => pair.Value,
          StringComparer.Ordinal);
        string propertyId = Guid.NewGuid().ToString("D");
        orphan["propertyId"] = propertyId;
        orphan["canonicalKey"] = "HBR_NATIVE_REPORTING_INTERNAL|1.0.0|ORPHAN";
        Dictionary<string, object> revit = AsObject(source["revit"])
          .ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        revit["parameterGuid"] = propertyId;
        orphan["revit"] = revit;
        reporting["internalProperties"] = Objects(reporting, "internalProperties")
          .Concat(new object[] { orphan }).ToArray();
      });
    }

    [Fact]
    public void RuntimeRejectsOfficialAcceptanceSetDrift()
    {
      AssertRejects(reporting =>
      {
        reporting["officialAcceptancePropertyIds"] =
          Objects(reporting, "officialAcceptancePropertyIds").Skip(1).ToArray();
      });
    }

    private static void AssertRejects(
      Action<Dictionary<string, object>> mutateReporting,
      string expectedMessage = null)
    {
      RulePackageEnvelope source = RulePackageIdentityReader.ReadEmbeddedEnvelope();
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };
      var root = AsObject(serializer.DeserializeObject(source.PayloadJson));
      mutateReporting(AsObject(root["nativeReporting"]));
      var mutated = new RulePackageEnvelope
      {
        Identity = source.Identity,
        PayloadJson = serializer.Serialize(root)
      };

      InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
        NativeReportingRuleCatalog.Load(
        mutated,
        NativeRuleCatalog.Current,
        NativeStage02RuleCatalog.Current));
      if (!string.IsNullOrEmpty(expectedMessage))
        Assert.Contains(expectedMessage, exception.Message);
    }

    private static Dictionary<string, object> ValidProbe(
      string probeId,
      string propertyId)
    {
      string sha = new string('a', 64);
      return new Dictionary<string, object>
      {
        ["probeId"] = probeId,
        ["propertyId"] = propertyId,
        ["sourceGoldenRvtSha256"] = sha,
        ["probeSeedManifestSha256"] = sha,
        ["probeRvtSha256"] = sha,
        ["probeIfcSha256"] = sha,
        ["hifcToolManifestSha256"] = sha,
        ["hifcToolDllSha256"] = sha,
        ["hifcToolProductVersion"] = "1.0.0",
        ["observedRevitUniqueId"] = "revit-uid",
        ["observedIfcGlobalId"] = "ifc-guid",
        ["observedBindingScope"] = "INSTANCE",
        ["observedParameterGuid"] = propertyId,
        ["observedSentinel"] = "700001.000001"
      };
    }

    private static Dictionary<string, object> AsObject(object value)
    {
      return Assert.IsType<Dictionary<string, object>>(value);
    }

    private static object[] Objects(
      Dictionary<string, object> value,
      string key)
    {
      return Assert.IsType<object[]>(value[key]);
    }
  }
}
