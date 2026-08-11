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
