using System;
using System.Globalization;
using System.Linq;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Revit;

namespace BIMBaoGui.Stage01.Context
{
  internal static class HBRFileContextFactory
  {
    public static HBRFileContext Create(Stage01Model model, RevitDocumentSnapshot snapshot, bool initializationPassed)
    {
      model = model ?? new Stage01Model();
      snapshot = snapshot ?? new RevitDocumentSnapshot();
      string modelFileType = model.GetValue(Stage01Keys.ModelFileType);
      RuleActivationResult activation = RuleActivationCatalog.Compile(modelFileType, model.Conditions);
      var spatial = new HBRSpatialReference(
        model.GetValue(Stage01Keys.CoordinateSystem),
        model.GetValue(Stage01Keys.ElevationSystem),
        Decimal(model.GetValue(Stage01Keys.BaseX)),
        Decimal(model.GetValue(Stage01Keys.BaseY)),
        Decimal(model.GetValue(Stage01Keys.BaseElevation)),
        Decimal(model.GetValue(Stage01Keys.TrueNorthAngle)),
        model.GetValue(Stage01Keys.LengthUnit),
        model.GetValue(Stage01Keys.AreaUnit),
        model.GetValue(Stage01Keys.AngleUnit));
      string payload = CanonicalPayload.Build(model);
      var provisional = new HBRFileContext(
        HBRContextVersions.FileContextSchema,
        model.GetValue(Stage01Keys.WorkflowVersion),
        model.GetValue(Stage01Keys.FileGuid),
        HBRDocumentFingerprint.Compute(snapshot.DocumentPath, snapshot.DocumentTitle, snapshot.RevitVersion),
        snapshot.DocumentTitle,
        model.GetValue(Stage01Keys.ProjectNumber),
        model.GetValue(Stage01Keys.ProjectName),
        model.GetValue(Stage01Keys.SubitemCode),
        model.GetValue(Stage01Keys.SubitemName),
        modelFileType,
        model.GetValue(Stage01Keys.ModelScope),
        spatial,
        model.PlanningTargets.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
        model.Conditions.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
        activation.Activated,
        activation.NotApplicable,
        initializationPassed,
        HBRContextVersions.RulePack,
        CanonicalPayload.Sha256(payload),
        string.Empty);
      return provisional.WithHash(HBRFileContextCanonicalizer.ComputeHash(provisional));
    }

    private static decimal Decimal(string value)
    {
      if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal invariant))
        return invariant;
      if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal current))
        return current;
      return 0m;
    }
  }
}
