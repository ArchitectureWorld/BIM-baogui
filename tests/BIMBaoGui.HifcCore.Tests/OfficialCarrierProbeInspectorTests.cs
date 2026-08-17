using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BIMBaoGui.HifcCore;
using Xunit;

namespace BIMBaoGui.HifcCore.Tests
{
  public sealed class OfficialCarrierProbeInspectorTests
  {
    [Fact]
    public void Exact_identity_owner_and_typed_sentinel_are_resolved()
    {
      OfficialCarrierProbeSeedManifest manifest = Manifest(
        Seed("property-real", "IfcProject", "Pset_Test", "RealValue",
          "IfcReal", "701001.125", "candidate-1"));

      OfficialCarrierProbeInspectionResult result =
        OfficialCarrierProbeInspector.InspectText(
          Ifc(
            "#1=IFCPROJECT('owner-one',$,$,$,$,$,$,$,$);",
            "#2=IFCPROPERTYSINGLEVALUE('RealValue',$,IFCREAL(701001.125),$);",
            "#3=IFCPROPERTYSET('pset-one',$,'Pset_Test',$,(#2));",
            "#4=IFCRELDEFINESBYPROPERTIES('rel-one',$,$,$,(#1),#3);"),
          manifest);

      Assert.True(result.Success, result.ErrorCode + ": " + result.Message);
      OfficialCarrierProbeInspectionItem item = Assert.Single(result.Items);
      Assert.Equal("owner-one", item.OwnerGlobalId);
      Assert.Equal("701001.125", item.CanonicalValue);
      Assert.Equal(1, item.MatchCount);
    }

    [Fact]
    public void Unknown_seed_value_and_wrong_declared_type_fail_closed()
    {
      string ifc = Ifc(
        "#1=IFCPROJECT('owner-one',$,$,$,$,$,$,$,$);",
        "#2=IFCPROPERTYSINGLEVALUE('RealValue',$,IFCREAL(999),$);",
        "#3=IFCPROPERTYSET('pset-one',$,'Pset_Test',$,(#2));",
        "#4=IFCRELDEFINESBYPROPERTIES('rel-one',$,$,$,(#1),#3);");

      Assert.Equal("PROBE_SENTINEL_UNKNOWN",
        OfficialCarrierProbeInspector.InspectText(ifc,
          Manifest(Seed("property-real", "IfcProject", "Pset_Test",
            "RealValue", "IfcReal", "701001.125", "candidate-1")))
          .ErrorCode);
      Assert.Equal("PROBE_IFC_TYPE_MISMATCH",
        OfficialCarrierProbeInspector.InspectText(ifc,
          Manifest(Seed("property-real", "IfcProject", "Pset_Test",
            "RealValue", "IfcInteger", "999", "candidate-1")))
          .ErrorCode);
    }

    [Fact]
    public void Same_sentinel_on_multiple_owners_fails_closed()
    {
      string ifc = Ifc(
        "#1=IFCPROJECT('owner-one',$,$,$,$,$,$,$,$);",
        "#5=IFCPROJECT('owner-two',$,$,$,$,$,$,$,$);",
        "#2=IFCPROPERTYSINGLEVALUE('RealValue',$,IFCREAL(701001.125),$);",
        "#3=IFCPROPERTYSET('pset-one',$,'Pset_Test',$,(#2));",
        "#4=IFCRELDEFINESBYPROPERTIES('rel-one',$,$,$,(#1,#5),#3);");

      Assert.Equal("PROBE_OWNER_AMBIGUOUS",
        OfficialCarrierProbeInspector.InspectText(ifc,
          Manifest(Seed("property-real", "IfcProject", "Pset_Test",
            "RealValue", "IfcReal", "701001.125", "candidate-1")))
          .ErrorCode);
    }

    [Fact]
    public void One_sentinel_cannot_be_declared_for_multiple_identities()
    {
      OfficialCarrierProbeSeedItem first = Seed(
        "property-real-a", "IfcProject", "Pset_Test", "RealValue",
        "IfcReal", "701001.125", "candidate-1");
      OfficialCarrierProbeSeedItem second = Seed(
        "property-real-b", "IfcProject", "Pset_Test", "OtherValue",
        "IfcReal", "701001.125", "candidate-1");

      OfficialCarrierProbeInspectionResult result =
        OfficialCarrierProbeInspector.InspectText(
          Ifc("#1=IFCPROJECT('owner-one',$,$,$,$,$,$,$,$);"),
          Manifest(first, second));

      Assert.False(result.Success);
      Assert.Equal("PROBE_SENTINEL_IDENTITY_AMBIGUOUS", result.ErrorCode);
    }

    [Fact]
    public void One_sentinel_cannot_appear_under_an_unrelated_ifc_identity()
    {
      OfficialCarrierProbeInspectionResult result =
        OfficialCarrierProbeInspector.InspectText(
          Ifc(
            "#1=IFCPROJECT('owner-one',$,$,$,$,$,$,$,$);",
            "#2=IFCPROPERTYSINGLEVALUE('RealValue',$,IFCREAL(701001.125),$);",
            "#3=IFCPROPERTYSET('pset-one',$,'Pset_Test',$,(#2));",
            "#4=IFCRELDEFINESBYPROPERTIES('rel-one',$,$,$,(#1),#3);",
            "#5=IFCPROPERTYSINGLEVALUE('OtherValue',$,IFCREAL(701001.125),$);",
            "#6=IFCPROPERTYSET('pset-two',$,'Pset_Other',$,(#5));",
            "#7=IFCRELDEFINESBYPROPERTIES('rel-two',$,$,$,(#1),#6);"),
          Manifest(Seed("property-real", "IfcProject", "Pset_Test",
            "RealValue", "IfcReal", "701001.125", "candidate-1")));

      Assert.False(result.Success);
      Assert.Equal("PROBE_SENTINEL_IDENTITY_AMBIGUOUS", result.ErrorCode);
    }

    [Fact]
    public void Expected_and_wrong_owner_entities_for_one_sentinel_fail_closed()
    {
      OfficialCarrierProbeInspectionResult result =
        OfficialCarrierProbeInspector.InspectText(
          Ifc(
            "#1=IFCPROJECT('owner-project',$,$,$,$,$,$,$,$);",
            "#5=IFCSITE('owner-site',$,$,$,$,$,$,$,$,$,$,$,$,$);",
            "#2=IFCPROPERTYSINGLEVALUE('RealValue',$,IFCREAL(701001.125),$);",
            "#3=IFCPROPERTYSET('pset-one',$,'Pset_Test',$,(#2));",
            "#4=IFCRELDEFINESBYPROPERTIES('rel-one',$,$,$,(#1,#5),#3);"),
          Manifest(Seed("property-real", "IfcProject", "Pset_Test",
            "RealValue", "IfcReal", "701001.125", "candidate-1")));

      Assert.False(result.Success);
      Assert.Equal("PROBE_SENTINEL_IDENTITY_AMBIGUOUS", result.ErrorCode);
    }

    [Theory]
    [InlineData("IfcLabel", "  原 样  ", "  原 样  ")]
    [InlineData("IfcText", "line 1\nline 2", "line 1\nline 2")]
    [InlineData("IfcInteger", "+00042", "42")]
    [InlineData("IfcReal", "1.25", "1.25")]
    [InlineData("IfcDateTime", "2026-08-14T08:00:00+08:00", "2026-08-14T00:00:00.0000000+00:00")]
    public void Final_readback_canonicalizes_all_manifest_types(
      string declaredType,
      string raw,
      string expected)
    {
      Assert.Equal(expected,
        OfficialCarrierProbeInspector.CanonicalizeFinalValue(
          declaredType, raw));
    }

    [Theory]
    [InlineData("IfcReal", "NaN")]
    [InlineData("IfcReal", "Infinity")]
    [InlineData("IfcInteger", "1.2")]
    [InlineData("IfcBoolean", "true")]
    public void Final_readback_rejects_noncanonical_or_unknown_types(
      string declaredType,
      string raw)
    {
      Assert.ThrowsAny<Exception>(() =>
        OfficialCarrierProbeInspector.CanonicalizeFinalValue(
          declaredType, raw));
    }

    [Fact]
    public void Final_readback_uses_dynamic_manifest_and_matches_owner_value_sets()
    {
      using (var sandbox = new FinalReadbackSandbox())
      {
        OfficialPropertyReadbackResult result =
          OfficialCarrierProbeInspector.ResolveFinalReadback(sandbox.Request());

        Assert.True(result.Success, result.ErrorCode + ": " + result.Message);
        Assert.Equal(new[] { "property-real", "property-text" },
          result.Records.Select(value => value.PropertyId));
        Assert.All(result.Records, value => Assert.Single(value.Values));
        Assert.Equal("1.25", result.Records[0].Values[0].OfficialIfcCanonicalValue);
        Assert.Equal("owner-text", result.Records[1].Values[0].OwnerGlobalId);
      }
    }

    [Fact]
    public void Final_readback_rejects_identity_or_owner_value_set_mismatch()
    {
      using (var sandbox = new FinalReadbackSandbox())
      {
        OfficialPropertyReadbackRequest identityMismatch = sandbox.Request();
        identityMismatch.OfficialExportIdentity.DocumentFingerprint = "other";
        Assert.Equal("FINAL_IDENTITY_MISMATCH",
          OfficialCarrierProbeInspector.ResolveFinalReadback(identityMismatch)
            .ErrorCode);

        OfficialPropertyReadbackRequest ownerMismatch = sandbox.Request();
        ownerMismatch.RevitReadbacks[0].CanonicalValue = "9.5";
        Assert.Equal("FINAL_OWNER_VALUE_SET_MISMATCH",
          OfficialCarrierProbeInspector.ResolveFinalReadback(ownerMismatch)
            .ErrorCode);
      }
    }

    [Fact]
    public void Final_readback_rejects_tampered_manifest_content_with_old_hash()
    {
      using (var sandbox = new FinalReadbackSandbox())
      {
        OfficialPropertyReadbackRequest request = sandbox.Request();
        request.Manifest.Definitions[0].IfcProperty = "TamperedValue";

        OfficialPropertyReadbackResult result =
          OfficialCarrierProbeInspector.ResolveFinalReadback(request);

        Assert.False(result.Success);
        Assert.Equal("FINAL_MANIFEST_SHA_MISMATCH", result.ErrorCode);
      }
    }

    [Fact]
    public void Manifest_content_hash_is_order_stable_and_excludes_its_own_field()
    {
      using (var sandbox = new FinalReadbackSandbox())
      {
        OfficialAcceptanceManifest manifest = sandbox.Request().Manifest;
        string original = OfficialAcceptanceManifestCanonicalizer.ComputeSha256(
          manifest);
        manifest.Identity.ManifestSha256 = new string('f', 64);
        manifest.Definitions = manifest.Definitions.Reverse().ToArray();
        string reordered =
          OfficialAcceptanceManifestCanonicalizer.ComputeSha256(manifest);
        manifest.Definitions[0].CanonicalUnit = "m2";
        string changed =
          OfficialAcceptanceManifestCanonicalizer.ComputeSha256(manifest);

        Assert.Equal(original, reordered);
        Assert.NotEqual(original, changed);
      }
    }

    private static OfficialCarrierProbeSeedManifest Manifest(
      params OfficialCarrierProbeSeedItem[] items)
    {
      return new OfficialCarrierProbeSeedManifest
      {
        SchemaVersion = "HBR_OFFICIAL_CARRIER_PROBE_SEED_V1",
        ContextSha256 = new string('a', 64),
        ProbeRvtSha256 = new string('b', 64),
        Items = items
      };
    }

    private static OfficialCarrierProbeSeedItem Seed(
      string propertyId,
      string entity,
      string pset,
      string property,
      string type,
      string sentinel,
      string candidate)
    {
      return new OfficialCarrierProbeSeedItem
      {
        PropertyId = propertyId,
        IfcEntity = entity,
        IfcPropertySet = pset,
        IfcProperty = property,
        ExactSourceName = property,
        DeclaredIfcType = type,
        CandidateUniqueId = candidate,
        ParameterGuid = Guid.NewGuid().ToString("D"),
        Sentinel = sentinel,
        Readback = sentinel
      };
    }

    private static string Ifc(params string[] entities)
    {
      return "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\n"
        + "DATA;\n" + string.Join("\n", entities)
        + "\nENDSEC;\nEND-ISO-10303-21;\n";
    }

    private sealed class FinalReadbackSandbox : IDisposable
    {
      internal FinalReadbackSandbox()
      {
        Root = Path.Combine(Path.GetTempPath(), "hbr-final-readback-"
          + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        RvtPath = Path.Combine(Root, "golden.rvt");
        IfcPath = Path.Combine(Root, "official.ifc");
        File.WriteAllText(RvtPath, "golden");
        File.WriteAllText(IfcPath, Ifc(
          "#1=IFCPROJECT('owner-real',$,$,$,$,$,$,$,$);",
          "#2=IFCPROPERTYSINGLEVALUE('RealValue',$,IFCREAL(1.25),$);",
          "#3=IFCPROPERTYSET('pset-real',$,'Pset_Test',$,(#2));",
          "#4=IFCRELDEFINESBYPROPERTIES('rel-real',$,$,$,(#1),#3);",
          "#5=IFCPROJECT('owner-text',$,$,$,$,$,$,$,$);",
          "#6=IFCPROPERTYSINGLEVALUE('TextValue',$,IFCTEXT('exact text'),$);",
          "#7=IFCPROPERTYSET('pset-text',$,'Pset_Test',$,(#6));",
          "#8=IFCRELDEFINESBYPROPERTIES('rel-text',$,$,$,(#5),#7);"));
      }

      internal string Root { get; }
      internal string RvtPath { get; }
      internal string IfcPath { get; }

      internal OfficialPropertyReadbackRequest Request()
      {
        string realGuid = "11111111-1111-1111-1111-111111111111";
        string textGuid = "22222222-2222-2222-2222-222222222222";
        OfficialAcceptanceIdentity identity = Identity();
        var manifest = new OfficialAcceptanceManifest
        {
          SchemaVersion = "HBR_OFFICIAL_ACCEPTANCE_MANIFEST_V1",
          ManifestVersion = "1.0.0",
          Identity = identity,
          Definitions = new[]
          {
            new OfficialAcceptancePropertyDefinition
            {
              PropertyId = "property-real",
              IfcEntity = "IfcProject",
              IfcPropertySet = "Pset_Test",
              IfcProperty = "RealValue",
              DeclaredIfcType = "IfcReal",
              ParameterGuid = realGuid
            },
            new OfficialAcceptancePropertyDefinition
            {
              PropertyId = "property-text",
              IfcEntity = "IfcProject",
              IfcPropertySet = "Pset_Test",
              IfcProperty = "TextValue",
              DeclaredIfcType = "IfcText",
              ParameterGuid = textGuid
            }
          }
        };
        identity.ManifestSha256 =
          OfficialAcceptanceManifestCanonicalizer.ComputeSha256(manifest);
        return new OfficialPropertyReadbackRequest
        {
          GoldenRvtPath = RvtPath,
          OfficialIfcPath = IfcPath,
          Manifest = manifest,
          RevitReadbacks = new[]
          {
            new OfficialAcceptanceRevitReadback
            {
              PropertyId = "property-real",
              OwnerGlobalId = "owner-real",
              OwnerRevitUniqueId = "revit-real",
              ParameterGuid = realGuid,
              CanonicalValue = "1.25",
              SourceStage = "STAGE02B",
              SourceResultHash = identity.Stage02BResultHash
            },
            new OfficialAcceptanceRevitReadback
            {
              PropertyId = "property-text",
              OwnerGlobalId = "owner-text",
              OwnerRevitUniqueId = "revit-text",
              ParameterGuid = textGuid,
              CanonicalValue = "exact text",
              SourceStage = "STAGE02B",
              SourceResultHash = identity.Stage02BResultHash
            }
          },
          StrictValidationIdentity = Clone(identity),
          OfficialExportIdentity = Clone(identity)
        };
      }

      private OfficialAcceptanceIdentity Identity()
      {
        return new OfficialAcceptanceIdentity
        {
          DocumentFingerprint = "document",
          RulePackageSha256 = new string('a', 64),
          Stage01ResultHash = new string('b', 64),
          Stage02AResultHash = new string('c', 64),
          Stage02BResultHash = new string('d', 64),
          ManifestSha256 = new string('e', 64),
          GoldenRvtSha256 = HifcCoreService.ComputeSha256(RvtPath),
          OfficialIfcSha256 = HifcCoreService.ComputeSha256(IfcPath)
        };
      }

      private static OfficialAcceptanceIdentity Clone(
        OfficialAcceptanceIdentity value)
      {
        return new OfficialAcceptanceIdentity
        {
          DocumentFingerprint = value.DocumentFingerprint,
          RulePackageSha256 = value.RulePackageSha256,
          Stage01ResultHash = value.Stage01ResultHash,
          Stage02AResultHash = value.Stage02AResultHash,
          Stage02BResultHash = value.Stage02BResultHash,
          ManifestSha256 = value.ManifestSha256,
          GoldenRvtSha256 = value.GoldenRvtSha256,
          OfficialIfcSha256 = value.OfficialIfcSha256
        };
      }

      public void Dispose()
      {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        catch { }
      }
    }
  }
}
