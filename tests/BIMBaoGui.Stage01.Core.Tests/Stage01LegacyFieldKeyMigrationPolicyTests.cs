using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage01LegacyFieldKeyMigrationPolicyTests
  {
    private const string LegacyBaseX =
      "IfcProject|Pset_申报信息属性集|基点坐标 X";
    private const string LegacyBaseY =
      "IfcProject|Pset_申报信息属性集|基点坐标 Y";

    [Fact]
    public void Apply_MovesLegacyCoordinatesToCurrentKeys()
    {
      var model = new Stage01Model();
      model.SetValue(LegacyBaseX, "3353559.52");
      model.SetValue(LegacyBaseY, "38345264.397");

      bool changed = Stage01LegacyFieldKeyMigrationPolicy.Apply(model);

      Assert.True(changed);
      Assert.Equal("3353559.52", model.GetValue(Stage01Keys.BaseX));
      Assert.Equal("38345264.397", model.GetValue(Stage01Keys.BaseY));
      Assert.DoesNotContain(LegacyBaseX, model.Values.Keys);
      Assert.DoesNotContain(LegacyBaseY, model.Values.Keys);
    }

    [Fact]
    public void Apply_PrefersCurrentCoordinateWhenLegacyValueConflicts()
    {
      var model = new Stage01Model();
      model.SetValue(LegacyBaseX, "OLD-X");
      model.SetValue(LegacyBaseY, "OLD-Y");
      model.SetValue(Stage01Keys.BaseX, "CURRENT-X");
      model.SetValue(Stage01Keys.BaseY, "CURRENT-Y");

      bool changed = Stage01LegacyFieldKeyMigrationPolicy.Apply(model);

      Assert.True(changed);
      Assert.Equal("CURRENT-X", model.GetValue(Stage01Keys.BaseX));
      Assert.Equal("CURRENT-Y", model.GetValue(Stage01Keys.BaseY));
      Assert.DoesNotContain(LegacyBaseX, model.Values.Keys);
      Assert.DoesNotContain(LegacyBaseY, model.Values.Keys);
    }

    [Fact]
    public void Apply_InheritsLegacyCoordinateWhenCurrentKeyIsEmpty()
    {
      var model = new Stage01Model();
      model.SetValue(LegacyBaseX, "3353559.52");
      model.SetValue(LegacyBaseY, "38345264.397");
      model.SetValue(Stage01Keys.BaseX, string.Empty);
      model.SetValue(Stage01Keys.BaseY, "   ");

      bool changed = Stage01LegacyFieldKeyMigrationPolicy.Apply(model);

      Assert.True(changed);
      Assert.Equal("3353559.52", model.GetValue(Stage01Keys.BaseX));
      Assert.Equal("38345264.397", model.GetValue(Stage01Keys.BaseY));
      Assert.DoesNotContain(LegacyBaseX, model.Values.Keys);
      Assert.DoesNotContain(LegacyBaseY, model.Values.Keys);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
      var model = new Stage01Model();
      model.SetValue(LegacyBaseX, "3353559.52");

      Assert.True(Stage01LegacyFieldKeyMigrationPolicy.Apply(model));
      string once = CanonicalPayload.Build(model);

      Assert.False(Stage01LegacyFieldKeyMigrationPolicy.Apply(model));
      Assert.Equal(once, CanonicalPayload.Build(model));
    }

    [Fact]
    public void Apply_LeavesOnlyUnspacedCoordinateKeysInCanonicalPayload()
    {
      var model = new Stage01Model();
      model.SetValue(LegacyBaseX, "3353559.52");
      model.SetValue(LegacyBaseY, "38345264.397");

      Stage01LegacyFieldKeyMigrationPolicy.Apply(model);
      string canonical = CanonicalPayload.Build(model);

      Assert.Contains(Stage01Keys.BaseX, canonical);
      Assert.Contains(Stage01Keys.BaseY, canonical);
      Assert.DoesNotContain(LegacyBaseX, canonical);
      Assert.DoesNotContain(LegacyBaseY, canonical);
    }

    [Fact]
    public void LegacyCanonicalPayloadWithMatchingHash_RemainsIntegrityValid()
    {
      var legacy = new Stage01Model();
      legacy.SetValue(LegacyBaseX, "3353559.52");
      legacy.SetValue(LegacyBaseY, "38345264.397");
      string payload = CanonicalPayload.Build(legacy);

      Stage01StoredPayloadIntegrityDecision decision =
        Stage01StoredPayloadIntegrityPolicy.Evaluate(
          payload,
          CanonicalPayload.Sha256(payload));

      Assert.True(decision.Success);
      Assert.Equal(payload, decision.CanonicalPayload);
    }
  }
}
