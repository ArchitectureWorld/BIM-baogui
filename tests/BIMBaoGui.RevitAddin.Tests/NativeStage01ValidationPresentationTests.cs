using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage01;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage01ValidationPresentationTests
  {
    [Fact]
    public void Required_value_messages_identify_the_exact_field_label_and_key()
    {
      NativeRuleCatalog catalog = NativeRuleCatalog.Current;
      NativeStage01FieldDefinition field = catalog.Stage01Fields.First(value =>
        !value.IsOrganization
          && NativeStage01Validator.IsRequired(value)
          && string.IsNullOrWhiteSpace(
            catalog.CreateDefaultStage01Model().GetValue(value.FieldKey)));
      NativeStage01Model model = catalog.CreateDefaultStage01Model();

      NativeStage01ValidationMessage message = NativeStage01Validator
        .Validate(model, catalog)
        .Messages
        .Single(value =>
          value.Code == NativeStage01ValidationCodes.RequiredValueMissing
            && value.FieldKey == field.FieldKey);

      Assert.Contains(field.Label, message.Message);
      Assert.Contains(field.FieldKey, message.Message);
    }
  }
}
