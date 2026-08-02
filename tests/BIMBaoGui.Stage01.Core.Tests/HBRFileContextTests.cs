using System;
using System.Collections.Generic;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HBRFileContextTests
  {
    [Fact]
    public void Hash_IsDeterministicAcrossDictionaryInsertionOrder()
    {
      HBRFileContext first = BuildContext(reverse: false, green: true);
      HBRFileContext second = BuildContext(reverse: true, green: true);

      Assert.Equal(first.FileContextHash, second.FileContextHash);
      Assert.Equal(HBRFileContextCanonicalizer.ToJson(first), HBRFileContextCanonicalizer.ToJson(second));
    }

    [Fact]
    public void Hash_ChangesWhenProjectConditionChanges()
    {
      HBRFileContext first = BuildContext(reverse: false, green: true);
      HBRFileContext second = BuildContext(reverse: false, green: false);

      Assert.NotEqual(first.FileContextHash, second.FileContextHash);
    }

    [Fact]
    public void Hash_ChangesWhenOfficialCompatibilityChanges()
    {
      HBRFileContext compatible = BuildContext(reverse: false, green: true, officialCompatible: true);
      HBRFileContext incompatible = BuildContext(reverse: false, green: true, officialCompatible: false);

      Assert.NotEqual(compatible.FileContextHash, incompatible.FileContextHash);
      Assert.True(compatible.IsReady);
      Assert.False(incompatible.IsReady);
    }

    [Fact]
    public void CanonicalJson_RoundTripsAndVerifiesHash()
    {
      HBRFileContext source = BuildContext(reverse: false, green: true);
      string json = HBRFileContextCanonicalizer.ToJson(source);

      Assert.True(HBRFileContextCanonicalizer.TryParse(json, out HBRFileContext restored, out string error), error);
      Assert.Equal(source.FileContextHash, restored.FileContextHash);
      Assert.Equal(source.ModelFileType, restored.ModelFileType);
      Assert.True(restored.OfficialProtocolCompatible);
      Assert.True(
        json.IndexOf("\"officialProtocolCompatible\"", StringComparison.Ordinal)
          < json.IndexOf("\"rulePackVersion\"", StringComparison.Ordinal));
      Assert.Equal("≤2.00", restored.PlanningTargets[PlanningTargetCatalog.FloorAreaRatioCode].ToMvdText());
    }

    private static HBRFileContext BuildContext(
      bool reverse,
      bool green,
      bool officialCompatible = true)
    {
      var targets = new Dictionary<string, PlanningTargetValue>(StringComparer.Ordinal);
      if (reverse)
      {
        targets[PlanningTargetCatalog.GreenRateCode] = Target(PlanningTargetCatalog.GreenRateCode, PlanningTargetOperator.GreaterOrEqual, "35");
        targets[PlanningTargetCatalog.FloorAreaRatioCode] = Target(PlanningTargetCatalog.FloorAreaRatioCode, PlanningTargetOperator.LessOrEqual, "2.00");
      }
      else
      {
        targets[PlanningTargetCatalog.FloorAreaRatioCode] = Target(PlanningTargetCatalog.FloorAreaRatioCode, PlanningTargetOperator.LessOrEqual, "2.00");
        targets[PlanningTargetCatalog.GreenRateCode] = Target(PlanningTargetCatalog.GreenRateCode, PlanningTargetOperator.GreaterOrEqual, "35");
      }
      var conditions = new Dictionary<string, bool>(StringComparer.Ordinal);
      if (reverse)
      {
        conditions["site.civil_defense"] = false;
        conditions["site.green"] = green;
      }
      else
      {
        conditions["site.green"] = green;
        conditions["site.civil_defense"] = false;
      }
      var provisional = new HBRFileContext(
        "0.5.0",
        "0.5.0",
        "file-guid",
        "document-fingerprint",
        "总平测试.rvt",
        "P-001",
        "测试项目",
        "S-01",
        "总平",
        PlanningTargetRequirementPolicy.SiteModel,
        "项目总平面报规模型",
        new HBRSpatialReference("CGCS2000", "1985国家高程基准", 1m, 2m, 3m, 0m, "m", "m²", "°"),
        targets,
        conditions,
        new[] { "HBR.SITE.BASE", "HBR.SITE.GREEN" },
        new[] { "HBR.SITE.OUTDOOR_PARKING" },
        true,
        officialCompatible,
        "0.1.0",
        "payload-hash",
        string.Empty);
      return provisional.WithHash(HBRFileContextCanonicalizer.ComputeHash(provisional));
    }

    private static PlanningTargetValue Target(string metricCode, PlanningTargetOperator op, string value)
    {
      PlanningTargetDefinition definition = PlanningTargetCatalog.Get(metricCode);
      Assert.True(PlanningTargetValue.TryCreate(metricCode, op, value, null, definition.Unit, "项目初始化", out PlanningTargetValue target, out string error), error);
      return target;
    }
  }
}
