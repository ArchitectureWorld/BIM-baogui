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
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeStage02PropertyDefinition property = UnclassifiedProperty(catalog);
      NativeCarrierRoleDefinition role = RoleFor(catalog, property);
      NativeStage02ElementEvidence first = Evidence(
        Element(role, "B", 2),
        property,
        exists: false);
      NativeStage02ElementEvidence second = Evidence(
        Element(role, "A", 1),
        property,
        exists: false);

      NativeStage02Preview left = NativeStage02PreviewCompiler.Compile(
        Input(property, first, second),
        catalog);
      NativeStage02Preview right = NativeStage02PreviewCompiler.Compile(
        Input(property, second, first),
        catalog);

      Assert.Equal(left.PreviewHash, right.PreviewHash);
      Assert.Equal(left.CanonicalJson, right.CanonicalJson);
      Assert.Equal(
        new[] { "A", "B" },
        left.Elements.Select(value => value.Element.UniqueId).ToArray());
      Assert.Equal(
        left.Elements.SelectMany(value => value.Fields)
          .Select(value => value.Property.PropertyId),
        right.Elements.SelectMany(value => value.Fields)
          .Select(value => value.Property.PropertyId));
    }

    [Fact]
    public void RuntimeNotImplementedNeverCreatesBindingOrWriteWork()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeStage02PropertyDefinition property = catalog.Properties.First(value =>
        value.RuntimeDecision.Status == NativeRuntimeStatuses.NotImplemented);
      NativeCarrierRoleDefinition role = RoleFor(catalog, property);

      NativeStage02Preview preview = NativeStage02PreviewCompiler.Compile(
        Input(property, Evidence(Element(role, "blocked", 1), property, false)),
        catalog);
      NativeStage02FieldPlan field = Field(preview, "blocked", property);

      Assert.Equal(NativeStage02FieldStatus.RuntimeBlocked, field.Status);
      Assert.Equal(NativeStage02BindingAction.None, field.BindingAction);
      Assert.Equal(NativeStage02ValueAction.None, field.ValueAction);
      Assert.False(field.StrictExportReady);
      Assert.Contains("OWNER_STRATEGY_NOT_IMPLEMENTED", field.Message);
    }

    [Fact]
    public void ConditionsFailClosedAndFalseConditionsAreNotApplicable()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeStage02PropertyDefinition property = catalog.Properties.First(value =>
        value.RuntimeDecision.Status
          == NativeRuntimeStatuses.UnclassifiedRequirement
        && !string.IsNullOrWhiteSpace(value.ConditionId));
      NativeCarrierRoleDefinition role = RoleFor(catalog, property);
      NativeStage02ElementEvidence evidence = Evidence(
        Element(role, "conditional", 1),
        property,
        exists: false);

      NativeStage02Preview missing = NativeStage02PreviewCompiler.Compile(
        Input(property, evidence),
        catalog);
      Assert.Equal(
        NativeStage02FieldStatus.Blocked,
        Field(missing, "conditional", property).Status);

      NativeStage02Preview inactive = NativeStage02PreviewCompiler.Compile(
        Input(
          property,
          new Dictionary<string, bool>(StringComparer.Ordinal)
          {
            [property.ConditionId] = false
          },
          evidence),
        catalog);
      Assert.Equal(
        NativeStage02FieldStatus.NotApplicable,
        Field(inactive, "conditional", property).Status);
    }

    [Fact]
    public void MissingParameterIsPreparedWithoutInventingABusinessValue()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeStage02PropertyDefinition property = UnclassifiedProperty(catalog);
      NativeCarrierRoleDefinition role = RoleFor(catalog, property);

      NativeStage02Preview preview = NativeStage02PreviewCompiler.Compile(
        Input(property, Evidence(Element(role, "new", 1), property, false)),
        catalog);
      NativeStage02FieldPlan field = Field(preview, "new", property);

      Assert.Equal(NativeStage02FieldStatus.PendingBinding, field.Status);
      Assert.Equal(NativeStage02BindingAction.Create, field.BindingAction);
      Assert.Equal(NativeStage02ValueAction.PendingInput, field.ValueAction);
      Assert.Equal(string.Empty, field.ProposedCanonicalValue);
      Assert.False(field.StrictExportReady);
    }

    [Fact]
    public void ExistingValueAndUniqueApprovedAliasProduceDeterministicPlans()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeStage02PropertyDefinition property = catalog.Properties.First(value =>
        value.RuntimeDecision.Status
          == NativeRuntimeStatuses.UnclassifiedRequirement
        && value.SuggestionAliases.Count > 0
        && string.IsNullOrWhiteSpace(value.ConditionId));
      NativeCarrierRoleDefinition role = RoleFor(catalog, property);

      NativeStage02Preview correct = NativeStage02PreviewCompiler.Compile(
        Input(
          property,
          Evidence(
            Element(role, "correct", 1),
            property,
            exists: true,
            currentValue: "现有值")),
        catalog);
      NativeStage02FieldPlan correctField = Field(correct, "correct", property);
      Assert.Equal(NativeStage02FieldStatus.Correct, correctField.Status);
      Assert.Equal(NativeStage02ValueAction.Keep, correctField.ValueAction);

      string alias = property.SuggestionAliases[0];
      NativeStage02Preview suggested = NativeStage02PreviewCompiler.Compile(
        Input(
          property,
          Evidence(
            Element(role, "suggested", 2),
            property,
            exists: true,
            aliases: new Dictionary<string, string>(StringComparer.Ordinal)
            {
              [alias] = "批准值"
            })),
        catalog);
      NativeStage02FieldPlan suggestedField = Field(
        suggested,
        "suggested",
        property);
      Assert.Equal(NativeStage02FieldStatus.PendingWrite, suggestedField.Status);
      Assert.Equal(NativeStage02ValueAction.Set, suggestedField.ValueAction);
      Assert.Equal("批准值", suggestedField.ProposedCanonicalValue);
      Assert.Equal(alias, suggestedField.ValueSource);
    }

    [Fact]
    public void ConflictingAliasValuesBlockOnlyTheirElement()
    {
      NativeStage02RuleCatalog catalog = NativeStage02RuleCatalog.Current;
      NativeStage02PropertyDefinition property = catalog.Properties.First(value =>
        value.RuntimeDecision.Status
          == NativeRuntimeStatuses.UnclassifiedRequirement
        && value.SuggestionAliases.Count > 0
        && string.IsNullOrWhiteSpace(value.ConditionId));
      NativeCarrierRoleDefinition role = RoleFor(catalog, property);
      string alias = property.SuggestionAliases[0];
      string legacy = property.LegacyParameterNames.FirstOrDefault()
        ?? alias + "_legacy";

      NativeStage02ElementEvidence conflict = Evidence(
        Element(role, "conflict", 1),
        property,
        exists: true,
        aliases: new Dictionary<string, string>(StringComparer.Ordinal)
        {
          [alias] = "值一",
          [legacy] = "值二"
        });
      NativeStage02ElementEvidence good = Evidence(
        Element(role, "good", 2),
        property,
        exists: true,
        currentValue: "正确值");

      NativeStage02Preview preview = NativeStage02PreviewCompiler.Compile(
        Input(property, conflict, good),
        catalog);

      Assert.Equal(
        NativeStage02FieldStatus.Blocked,
        Field(preview, "conflict", property).Status);
      Assert.Equal(
        NativeStage02FieldStatus.Correct,
        Field(preview, "good", property).Status);
      Assert.Equal(1, preview.BlockedElementCount);
    }

    private static NativeStage02PreviewInput Input(
      NativeStage02PropertyDefinition property,
      params NativeStage02ElementEvidence[] elements)
    {
      return Input(
        property,
        new Dictionary<string, bool>(StringComparer.Ordinal),
        elements);
    }

    private static NativeStage02PreviewInput Input(
      NativeStage02PropertyDefinition property,
      IDictionary<string, bool> conditions,
      params NativeStage02ElementEvidence[] elements)
    {
      return new NativeStage02PreviewInput
      {
        DocumentFingerprint = "DOC-001",
        ModelProfile = RoleModelProfile(
          NativeStage02RuleCatalog.Current,
          property),
        Conditions = new Dictionary<string, bool>(
          conditions,
          StringComparer.Ordinal),
        Elements = elements
      };
    }

    private static NativeStage02ElementEvidence Evidence(
      NativeStage02ElementSnapshot element,
      NativeStage02PropertyDefinition property,
      bool exists,
      string currentValue = "",
      IDictionary<string, string> aliases = null)
    {
      var parameter = new NativeStage02ParameterEvidence
      {
        ParameterGuid = property.ParameterGuid,
        Exists = exists,
        ContractCompatible = true,
        BindingIncludesCategory = exists,
        IsReadOnly = false,
        CurrentCanonicalValue = currentValue,
        AliasValues = aliases == null
          ? new Dictionary<string, string>(StringComparer.Ordinal)
          : new Dictionary<string, string>(aliases, StringComparer.Ordinal)
      };
      return new NativeStage02ElementEvidence
      {
        Element = element,
        Parameters = new Dictionary<Guid, NativeStage02ParameterEvidence>
        {
          [property.ParameterGuid] = parameter
        }
      };
    }

    private static NativeStage02ElementSnapshot Element(
      NativeCarrierRoleDefinition role,
      string uniqueId,
      int elementId)
    {
      return new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "DOC-001",
        UniqueId = uniqueId,
        ElementId = elementId,
        Category = role.RevitCategories[0],
        ElementKind = role.AllowedElementKinds[0],
        ElementName = role.DisplayName,
        FamilyName = role.DisplayName,
        TypeName = role.DisplayName,
        AssignedRoleId = role.RoleId,
        IsModelElement = true
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

    private static NativeStage02PropertyDefinition UnclassifiedProperty(
      NativeStage02RuleCatalog catalog)
    {
      return catalog.Properties.First(value =>
        value.RuntimeDecision.Status
          == NativeRuntimeStatuses.UnclassifiedRequirement
        && string.IsNullOrWhiteSpace(value.ConditionId));
    }

    private static NativeCarrierRoleDefinition RoleFor(
      NativeStage02RuleCatalog catalog,
      NativeStage02PropertyDefinition property)
    {
      return catalog.CarrierRolesById[property.CarrierRoleIds[0]];
    }

    private static string RoleModelProfile(
      NativeStage02RuleCatalog catalog,
      NativeStage02PropertyDefinition property)
    {
      return RoleFor(catalog, property).ModelFileTypes[0];
    }
  }
}
