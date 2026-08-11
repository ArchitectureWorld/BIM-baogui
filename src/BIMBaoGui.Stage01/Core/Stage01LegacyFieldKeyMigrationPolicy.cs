using System;

namespace BIMBaoGui.Stage01.Core
{
  internal static class Stage01LegacyFieldKeyMigrationPolicy
  {
    private const string LegacyBaseX =
      "IfcProject|Pset_申报信息属性集|基点坐标 X";
    private const string LegacyBaseY =
      "IfcProject|Pset_申报信息属性集|基点坐标 Y";

    internal static bool Apply(Stage01Model model)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));

      bool changed = Migrate(model, LegacyBaseX, Stage01Keys.BaseX);
      return Migrate(model, LegacyBaseY, Stage01Keys.BaseY) || changed;
    }

    private static bool Migrate(
      Stage01Model model,
      string legacyKey,
      string currentKey)
    {
      if (!model.Values.TryGetValue(legacyKey, out string legacyValue))
        return false;

      if (string.IsNullOrWhiteSpace(model.GetValue(currentKey)))
        model.SetValue(currentKey, legacyValue);
      model.Values.Remove(legacyKey);
      return true;
    }
  }
}
