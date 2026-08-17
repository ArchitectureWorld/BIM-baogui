using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BIMBaoGui.HifcCore;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03ChecklistGenerator
  {
    internal static NativeStage03ChecklistGenerationResult Generate(
      string modelFileType,
      IReadOnlyDictionary<string, bool> projectConditions,
      NativeReportingRuleCatalog catalog)
    {
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));
      string profile = (modelFileType ?? string.Empty).Trim();
      if (!string.Equals(profile, "总平模型", StringComparison.Ordinal))
      {
        return new NativeStage03ChecklistGenerationResult
        {
          Supported = false,
          Code = NativeStage03Codes.ModelProfileNotImplemented,
          ModelFileType = profile,
          OfficialAcceptanceManifest = EmptyManifest()
        };
      }

      IReadOnlyDictionary<string, bool> conditions = projectConditions
        ?? new Dictionary<string, bool>(StringComparer.Ordinal);
      NativeReportingCheckDefinition[] definitions = catalog.GetChecks(profile)
        .Where(value => value != null
          && (string.IsNullOrWhiteSpace(value.ConditionId)
            || conditions.TryGetValue(value.ConditionId, out bool enabled)
              && enabled))
        .OrderBy(value => value.Sequence)
        .ThenBy(value => value.CheckId, StringComparer.Ordinal)
        .ToArray();
      NativeOfficialAcceptanceManifest manifest = BuildManifest(catalog);
      return new NativeStage03ChecklistGenerationResult
      {
        Supported = true,
        Code = "CHECKLIST_GENERATED",
        ModelFileType = profile,
        OfficialAcceptanceManifest = manifest,
        Definitions = new ReadOnlyCollection<NativeReportingCheckDefinition>(
          definitions)
      };
    }

    internal static OfficialAcceptanceManifest ToHifcManifest(
      NativeOfficialAcceptanceManifest manifest)
    {
      if (manifest == null) throw new ArgumentNullException(nameof(manifest));
      return new OfficialAcceptanceManifest
      {
        SchemaVersion = "HBR_OFFICIAL_ACCEPTANCE_MANIFEST_V1",
        ManifestVersion = manifest.SchemaVersion,
        Definitions = new ReadOnlyCollection<
          OfficialAcceptancePropertyDefinition>((manifest.Properties
            ?? Array.Empty<NativeOfficialAcceptanceManifestEntry>())
          .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
          .Select(ToHifcDefinition)
          .ToArray())
      };
    }

    private static NativeOfficialAcceptanceManifest BuildManifest(
      NativeReportingRuleCatalog catalog)
    {
      NativeOfficialAcceptanceManifestEntry[] entries = catalog
        .OfficialAcceptanceProperties
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
        .Select(value => new NativeOfficialAcceptanceManifestEntry
        {
          PropertyId = value.PropertyId,
          Identity = value.Identity,
          DeclaredIfcType = value.DeclaredIfcType,
          CanonicalUnit = value.CanonicalUnit ?? string.Empty,
          ParameterGuid = value.ParameterGuid.ToString("D").ToLowerInvariant(),
          BindingScope = value.BindingScope,
          SourceStage = value.SourceStage
        })
        .ToArray();
      var manifest = new NativeOfficialAcceptanceManifest
      {
        SchemaVersion = "1.0.0",
        Properties = new ReadOnlyCollection<
          NativeOfficialAcceptanceManifestEntry>(entries)
      };
      manifest.Sha256 = OfficialAcceptanceManifestCanonicalizer.ComputeSha256(
        ToHifcManifest(manifest));
      return manifest;
    }

    private static NativeOfficialAcceptanceManifest EmptyManifest()
    {
      return new NativeOfficialAcceptanceManifest
      {
        Properties = Array.Empty<NativeOfficialAcceptanceManifestEntry>()
      };
    }

    private static OfficialAcceptancePropertyDefinition ToHifcDefinition(
      NativeOfficialAcceptanceManifestEntry value)
    {
      string[] identity = (value.Identity ?? string.Empty).Split('|');
      return new OfficialAcceptancePropertyDefinition
      {
        PropertyId = value.PropertyId,
        Identity = value.Identity,
        IfcEntity = identity.Length == 3 ? identity[0] : string.Empty,
        IfcPropertySet = identity.Length == 3 ? identity[1] : string.Empty,
        IfcProperty = identity.Length == 3 ? identity[2] : string.Empty,
        DeclaredIfcType = value.DeclaredIfcType,
        CanonicalUnit = value.CanonicalUnit,
        ParameterGuid = value.ParameterGuid,
        BindingScope = value.BindingScope,
        SourceStage = SourceStage(value.SourceStage)
      };
    }

    private static string SourceStage(NativeReportingSourceStage value)
    {
      switch (value)
      {
        case NativeReportingSourceStage.Stage01: return "STAGE01";
        case NativeReportingSourceStage.Stage02A: return "STAGE02A";
        case NativeReportingSourceStage.Stage02B: return "STAGE02B";
        default:
          throw new InvalidOperationException(
            "Official acceptance property source stage is invalid: " + value);
      }
    }
  }
}
