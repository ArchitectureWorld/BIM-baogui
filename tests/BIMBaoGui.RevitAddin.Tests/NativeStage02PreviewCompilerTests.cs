using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02PreviewCompilerTests
  {
    [Fact]
    public void PreviewHashAndOrderingAreIndependentOfInputOrder()
    {
      NativeStage02PropertyDefinition property = Property();
      NativeCarrierRoleDefinition role = Role(property);
      NativeStage02ElementEvidence a = Evidence(role, property, "A", false);
      NativeStage02ElementEvidence b = Evidence(role, property, "B", false);

      NativeStage02Preview left = Compile(property, b, a);
      NativeStage02Preview right = Compile(property, a, b);

      Assert.Equal(left.PreviewHash, right.PreviewHash);
      Assert.Equal(left.CanonicalJson, right.CanonicalJson);
      Assert.Equal(
        new[] { "A", "B" },
        left.Elements.Select(value => value.Element.UniqueId).ToArray());
    }

    [Fact]
    public void RuntimeNotImplementedNeverCreatesBindingOrWriteWork()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeStage02PropertyDefinition property = catalog.Properties.First(value =>
        value.RuntimeDecision.Status == NativeRuntimeStatuses.NotImplemented);
      NativeCarrierRoleDefinition role = Role(property);
      NativeStage02FieldPlan field = Field(
        Compile(property, Evidence(role, property, "blocked", false)),
        "blocked",
        property);

      Assert.Equal(NativeStage02FieldStatus.RuntimeBlocked, field.Status);
      Assert.Equal(NativeStage02BindingAction.None, field.BindingAction);
      Assert.Equal(NativeStage02ValueAction.None, field.ValueAction);
      Assert.Contains("OWNER_STRATEGY_NOT_IMPLEMENTED", field.Message);
    }

    [Fact]
    public void ConditionPolicyIsExplicitAndFailClosed()
    {
      const string conditionId = "building.roof";
      NativeStage02ConditionDecision missing =
        NativeStage02ConditionPolicy.Evaluate(
          conditionId,
          new Dictionary<string, bool>(StringComparer.Ordinal));
      NativeStage02ConditionDecision inactive =
        NativeStage02ConditionPolicy.Evaluate(
          conditionId,
          new Dictionary<string, bool>(StringComparer.Ordinal)
          {
            [conditionId] = false
          });
      NativeStage02ConditionDecision active =
        NativeStage02ConditionPolicy.Evaluate(
          conditionId,
          new Dictionary<string, bool>(StringComparer.Ordinal)
          {
            [conditionId] = true
          });

      Assert.Equal(NativeStage02ConditionStatus.Missing, missing.Status);
      Assert.Equal(
        NativeStage02ConditionStatus.NotApplicable,
        inactive.Status);
      Assert.Equal(NativeStage02ConditionStatus.Applicable, active.Status);
    }

    [Fact]
    public void MissingParameterIsPreparedWithoutInventingABusinessValue()
    {
      NativeStage02PropertyDefinition property = Property();
      NativeCarrierRoleDefinition role = Role(property);
      NativeStage02FieldPlan field = Field(
        Compile(property, Evidence(role, property, "new", false)),
        "new",
        property);

      Assert.Equal(NativeStage02FieldStatus.PendingBinding, field.Status);
      Assert.Equal(NativeStage02BindingAction.Create, field.BindingAction);
      Assert.Equal(NativeStage02ValueAction.PendingInput, field.ValueAction);
      Assert.Equal(string.Empty, field.ProposedCanonicalValue);
      Assert.False(field.StrictExportReady);
    }

    [Fact]
    public void ExistingAndApprovedAliasValuesProduceDeterministicPlans()
    {
      NativeStage02PropertyDefinition property = Property(requireAliases: true);
      NativeCarrierRoleDefinition role = Role(property);
      NativeStage02FieldPlan correct = Field(
        Compile(
          property,
          Evidence(role, property, "correct", true, "现有值")),
        "correct",
        property);
      string alias = property.SuggestionAliases[0];
      NativeStage02FieldPlan suggested = Field(
        Compile(
          property,
          Evidence(
            role,
            property,
            "suggested",
            true,
            aliases: new Dictionary<string, string>(StringComparer.Ordinal)
            {
              [alias] = "批准值"
            })),
        "suggested",
        property);

      Assert.Equal(NativeStage02FieldStatus.Correct, correct.Status);
      Assert.Equal(NativeStage02ValueAction.Keep, correct.ValueAction);
      Assert.Equal(NativeStage02FieldStatus.PendingWrite, suggested.Status);
      Assert.Equal(NativeStage02ValueAction.Set, suggested.ValueAction);
      Assert.Equal("批准值", suggested.ProposedCanonicalValue);
      Assert.Equal(alias, suggested.ValueSource);
    }

    [Fact]
    public void ConflictingAliasValuesBlockOnlyTheirElement()
    {
      NativeStage02PropertyDefinition property = Property(requireAliases: true);
      NativeCarrierRoleDefinition role = Role(property);
      string alias = property.SuggestionAliases[0];
      string legacy = property.LegacyParameterNames[0];
      NativeStage02ElementEvidence conflict = Evidence(
        role,
        property,
        "conflict",
        true,
        aliases: new Dictionary<string, string>(StringComparer.Ordinal)
        {
          [alias] = "值一",
          [legacy] = "值二"
        });
      NativeStage02ElementEvidence good = Evidence(
        role,
        property,
        "good",
        true,
        "正确值");

      NativeStage02Preview preview = Compile(property, conflict, good);
      Assert.Equal(
        NativeStage02FieldStatus.Blocked,
        Field(preview, "conflict", property).Status);
      Assert.Equal(
        NativeStage02FieldStatus.Correct,
        Field(preview, "good", property).Status);
      Assert.Equal(1, preview.BlockedElementCount);
    }

    [Fact]
    public void Preview_hash_includes_stage02a_business_inputs_and_excludes_audit_metadata()
    {
      NativeStage02PropertyDefinition property = Property();
      NativeCarrierRoleDefinition role = Role(property);
      NativeStage02Preview preview = Compile(
        property,
        Evidence(role, property, "A", true, "current"));
      NativeStage02ElementPlan element = Assert.Single(preview.Elements);
      element.Element.Geometry = new NativeStage02GeometryEvidence
      {
        BoundingBox = new NativeStage02BoundingBoxEvidence
        {
          Available = true,
          MinXFeet = 0,
          MinYFeet = 0,
          MinZFeet = 0,
          MaxXFeet = 10,
          MaxYFeet = 10,
          MaxZFeet = 1
        },
        LocationKind = "LocationPoint",
        LocationCoordinatesFeet = new[] { 1.0, 2.0, 3.0 },
        ApprovedProjectedAreaSquareMetres = 10,
        ProjectedAreaSource = "PLANAR_FACE",
        EvidenceHash = new string('b', 64)
      };
      element.ElementSnapshotHash = NativeStage02ElementSnapshotCanonicalizer
        .Sha256(element.Element);
      element.Candidates = new[]
      {
        new NativeStage02SemanticCandidate
        {
          RoleId = element.RoleId,
          Confidence = "HIGH",
          Evidence = new[] { "ALIAS:集中绿地" }
        }
      };
      element.RoleConfirmation = new NativeStage02RoleConfirmationDecision
      {
        Confirmed = true,
        Code = "ROLE_CONFIRMED",
        ResolvedRoleId = element.RoleId,
        Confirmation = new NativeStage02RoleConfirmation
        {
          ElementUniqueId = "A",
          RoleId = element.RoleId,
          ElementSnapshotHash = element.ElementSnapshotHash,
          RulePackageSha256 = preview.RulePackageSha256,
          ConfirmedUtc = "2026-08-17T00:00:00Z"
        }
      };
      element.TaskGeometry = new NativeStage02TaskGeometryEvaluation
      {
        TaskId = "SITE.GREEN",
        ElementUniqueId = "A",
        EvaluationHash = new string('c', 64),
        Checks = new[]
        {
          new NativeStage02GeometryCheckEvidence
          {
            CheckId = "check",
            RuleText = "绿地边界闭合",
            State = NativeStage02GeometryCheckState.Passed,
            Code = "GEOMETRY_CHECK_PASSED"
          }
        }
      };
      preview.ScopeMode = NativeStage02ScopeMode.FullModel;
      preview.RunId = "run-one";
      string baseline = Hash(preview);

      preview.RunId = "run-two";
      element.RoleConfirmation.Confirmation.ConfirmedUtc = "2026-08-18T00:00:00Z";
      Assert.Equal(baseline, Hash(preview));

      element.Element.Geometry.LocationCoordinatesFeet = new[] { 2.0, 2.0, 3.0 };
      element.ElementSnapshotHash = NativeStage02ElementSnapshotCanonicalizer.Sha256(element.Element);
      Assert.NotEqual(baseline, Hash(preview));
      element.Element.Geometry.LocationCoordinatesFeet = new[] { 1.0, 2.0, 3.0 };
      element.Element.Geometry.BoundingBox.MaxXFeet = 11;
      element.ElementSnapshotHash = NativeStage02ElementSnapshotCanonicalizer.Sha256(element.Element);
      Assert.NotEqual(baseline, Hash(preview));
      element.Element.Geometry.BoundingBox.MaxXFeet = 10;
      element.Element.Geometry.ApprovedProjectedAreaSquareMetres = 11;
      element.ElementSnapshotHash = NativeStage02ElementSnapshotCanonicalizer.Sha256(element.Element);
      Assert.NotEqual(baseline, Hash(preview));

      element.Element.Geometry.ApprovedProjectedAreaSquareMetres = 10;
      element.ElementSnapshotHash = NativeStage02ElementSnapshotCanonicalizer.Sha256(element.Element);
      element.TaskGeometry.EvaluationHash = new string('d', 64);
      Assert.NotEqual(baseline, Hash(preview));
      element.TaskGeometry.EvaluationHash = new string('c', 64);
      element.RoleId = "SITE_TOTAL_LAND";
      Assert.NotEqual(baseline, Hash(preview));
      element.RoleId = element.RoleConfirmation.ResolvedRoleId;
      element.Fields[0].CurrentCanonicalValue += "-changed";
      Assert.NotEqual(baseline, Hash(preview));
    }

    [Fact]
    public void Pending_confirmation_and_runtime_blocked_are_element_blockers()
    {
      NativeStage02PropertyDefinition property = Property();
      NativeStage02Preview preview = Compile(
        property,
        Evidence(Role(property), property, "A", false));
      NativeStage02ElementPlan element = Assert.Single(preview.Elements);
      element.RoleConfirmation = new NativeStage02RoleConfirmationDecision
      {
        Confirmed = false,
        Code = "ROLE_CONFIRMATION_REQUIRED"
      };
      element.Fields = element.Fields.Concat(new[]
      {
        new NativeStage02FieldPlan
        {
          Property = property,
          Status = NativeStage02FieldStatus.PendingConfirmation
        }
      }).ToArray();

      Assert.True(element.IsBlocked);
      element.RoleConfirmation.Confirmed = true;
      element.Fields = new[]
      {
        new NativeStage02FieldPlan
        {
          Property = property,
          Status = NativeStage02FieldStatus.RuntimeBlocked
        }
      };
      Assert.True(element.IsBlocked);
    }

    [Fact]
    public void Matched_role_with_null_confirmation_is_pending_and_blocked()
    {
      NativeStage02PropertyDefinition property = Property();
      NativeStage02ElementEvidence evidence = Evidence(
        Role(property),
        property,
        "A",
        false);
      evidence.RoleConfirmation = null;

      NativeStage02Preview preview = Compile(property, evidence);
      NativeStage02ElementPlan element = Assert.Single(preview.Elements);
      NativeStage02FieldPlan field = Assert.Single(element.Fields);

      Assert.Equal(NativeStage02FieldStatus.PendingConfirmation, field.Status);
      Assert.True(element.IsBlocked);
      Assert.Contains("ROLE_CONFIRMATION_REQUIRED", field.Message);
    }

    private static string Hash(NativeStage02Preview preview)
    {
      return NativeStage02PreviewCanonicalizer.Sha256(
        NativeStage02PreviewCanonicalizer.Build(preview));
    }

    private static NativeStage02Preview Compile(
      NativeStage02PropertyDefinition property,
      params NativeStage02ElementEvidence[] elements)
    {
      return NativeStage02PreviewCompiler.Compile(
        new NativeStage02PreviewInput
        {
          DocumentFingerprint = "DOC-001",
          ModelProfile = Role(property).ModelFileTypes[0],
          Conditions = new Dictionary<string, bool>(StringComparer.Ordinal),
          Elements = elements
        },
        NativeStage02RuleCatalog.Current);
    }

    private static NativeStage02ElementEvidence Evidence(
      NativeCarrierRoleDefinition role,
      NativeStage02PropertyDefinition property,
      string uniqueId,
      bool exists,
      string current = "",
      IDictionary<string, string> aliases = null)
    {
      return new NativeStage02ElementEvidence
      {
        Element = new NativeStage02ElementSnapshot
        {
          DocumentFingerprint = "DOC-001",
          UniqueId = uniqueId,
          ElementId = uniqueId.GetHashCode(),
          Category = role.RevitCategories[0],
          ElementKind = role.AllowedElementKinds[0],
          ElementName = role.DisplayName,
          FamilyName = role.DisplayName,
          TypeName = role.DisplayName,
          AssignedRoleId = role.RoleId,
          IsModelElement = true
        },
        RoleConfirmation = new NativeStage02RoleConfirmationDecision
        {
          Confirmed = true,
          Code = "ROLE_CONFIRMED",
          ResolvedRoleId = role.RoleId,
          Source = "TestFixture"
        },
        Parameters = new Dictionary<Guid, NativeStage02ParameterEvidence>
        {
          [property.ParameterGuid] = new NativeStage02ParameterEvidence
          {
            ParameterGuid = property.ParameterGuid,
            Exists = exists,
            ContractCompatible = true,
            BindingIncludesCategory = exists,
            CurrentCanonicalValue = current,
            AliasValues = aliases == null
              ? new Dictionary<string, string>(StringComparer.Ordinal)
              : new Dictionary<string, string>(aliases, StringComparer.Ordinal)
          }
        }
      };
    }

    private static NativeStage02FieldPlan Field(
      NativeStage02Preview preview,
      string uniqueId,
      NativeStage02PropertyDefinition property)
    {
      return preview.Elements.Single(value =>
          value.Element.UniqueId == uniqueId)
        .Fields.Single(value =>
          value.Property.PropertyId == property.PropertyId);
    }

    private static NativeStage02PropertyDefinition Property(
      bool requireAliases = false)
    {
      return NativeStage02RuleCatalog.Current.Properties.First(value =>
        value.RuntimeDecision.Status
          == NativeRuntimeStatuses.UnclassifiedRequirement
        && string.IsNullOrWhiteSpace(value.ConditionId)
        && (!requireAliases
          || (value.SuggestionAliases.Count > 0
            && value.LegacyParameterNames.Count > 0)));
    }

    private static NativeCarrierRoleDefinition Role(
      NativeStage02PropertyDefinition property)
    {
      return NativeStage02RuleCatalog.Current
        .CarrierRolesById[property.CarrierRoleIds[0]];
    }
  }
}
