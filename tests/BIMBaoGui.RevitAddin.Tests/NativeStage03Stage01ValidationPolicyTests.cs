using System;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage03;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03Stage01ValidationPolicyTests
  {
    [Fact]
    public void Required_business_values_and_condition_schema_gaps_are_classified_differently()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01FieldDefinition field = catalog.Stage01Fields.First(value =>
        !value.IsOrganization && NativeStage01Validator.IsRequired(value));
      NativeConditionDefinition condition = catalog.Conditions.First();
      var validation = new NativeStage01ValidationResult(new[]
      {
        new NativeStage01ValidationMessage
        {
          Code = NativeStage01ValidationCodes.RequiredValueMissing,
          FieldKey = field.FieldKey,
          Message = "该字段为必填项。"
        },
        new NativeStage01ValidationMessage
        {
          Code = NativeStage01ValidationCodes.ConditionMissing,
          FieldKey = condition.ConditionId,
          Message = "项目条件键缺失。"
        }
      });

      NativeStage03Stage01ValidationClassification classification =
        NativeStage03Stage01ValidationPolicy.Classify(validation, catalog);

      Assert.Single(classification.TechnicalFatalCodes);
      Assert.Contains(
        NativeStage03Codes.Stage01Invalid + ":"
          + NativeStage01ValidationCodes.ConditionMissing + ":"
          + condition.ConditionId,
        classification.TechnicalFatalCodes);
      Assert.Single(classification.BusinessBlockers);
      Assert.Contains(
        NativeStage03Codes.Stage01BusinessInvalid + ":"
          + NativeStage01ValidationCodes.RequiredValueMissing + ":"
          + field.FieldKey,
        classification.BusinessBlockers);
      Assert.Contains(classification.Messages, message =>
        message.Contains(field.Label)
          && message.Contains(field.FieldKey)
          && message.Contains(
            NativeStage01ValidationCodes.RequiredValueMissing));
    }

    [Fact]
    public void Project_condition_declaration_errors_are_reserved_for_the_explicit_condition_gate()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      var validation = new NativeStage01ValidationResult(new[]
      {
        new NativeStage01ValidationMessage
        {
          Code = NativeStage01ValidationCodes.ProjectConditionDeclarationMissing,
          FieldKey = NativeProjectConditionDeclarationPolicy.NoneConditionId,
          Message = "项目条件声明缺失。"
        }
      });

      NativeStage03Stage01ValidationClassification classification =
        NativeStage03Stage01ValidationPolicy.Classify(validation, catalog);

      Assert.True(classification.HasProjectConditionError);
      Assert.Empty(classification.TechnicalFatalCodes);
      Assert.Empty(classification.BusinessBlockers);
    }

    [Fact]
    public void Forced_test_can_bypass_only_stage01_business_validation_when_fields_exist()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01FieldDefinition field = catalog.Stage01Fields.First(value =>
        !value.IsOrganization && NativeStage01Validator.IsRequired(value));
      var validation = new NativeStage01ValidationResult(new[]
      {
        new NativeStage01ValidationMessage
        {
          Code = NativeStage01ValidationCodes.RequiredValueMissing,
          FieldKey = field.FieldKey,
          Message = "该字段为必填项。"
        }
      });
      NativeStage03Stage01ValidationClassification classification =
        NativeStage03Stage01ValidationPolicy.Classify(validation, catalog);

      NativeStage03GateDecision decision = NativeStage03GatePolicy.Evaluate(
        NativeStage03Mode.ForcedTest,
        "IFCFlux 定位 Stage01 缺失业务值",
        classification.TechnicalFatalCodes,
        classification.BusinessBlockers,
        2);

      Assert.True(decision.AllowExport);
      Assert.True(decision.Forced);
      Assert.Single(decision.BypassedBusinessBlockers);
    }
  }
}
