using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BIMBaoGui.HifcCore
{
  public static class OfficialAcceptanceManifestCanonicalizer
  {
    public static string SerializeCanonical(OfficialAcceptanceManifest manifest)
    {
      if (manifest == null) throw new ArgumentNullException(nameof(manifest));
      if (!string.Equals(manifest.ManifestVersion, "1.0.0",
        StringComparison.Ordinal))
        throw new ArgumentException(
          "Manifest version must be 1.0.0.", nameof(manifest));
      OfficialAcceptancePropertyDefinition[] definitions = (manifest.Definitions
          ?? Array.Empty<OfficialAcceptancePropertyDefinition>())
        .Select(ValidateAndNormalize)
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
        .ToArray();
      if (definitions.Length == 0
        || definitions.GroupBy(value => value.PropertyId, StringComparer.Ordinal)
          .Any(group => group.Count() != 1))
        throw new ArgumentException(
          "Manifest definitions must be non-empty and unique by propertyId.",
          nameof(manifest));
      var builder = new StringBuilder(2048);
      builder.Append("BIMBAOGUI_OFFICIAL_ACCEPTANCE_MANIFEST|1.0.0\n");
      foreach (OfficialAcceptancePropertyDefinition definition in definitions)
      {
        builder.Append(definition.PropertyId).Append('\u001f')
          .Append(definition.Identity).Append('\u001f')
          .Append(definition.DeclaredIfcType).Append('\u001f')
          .Append(definition.CanonicalUnit).Append('\u001f')
          .Append(definition.ParameterGuid).Append('\u001f')
          .Append(definition.BindingScope).Append('\u001f')
          .Append(definition.SourceStage).Append('\n');
      }
      return builder.ToString();
    }

    public static string ComputeSha256(OfficialAcceptanceManifest manifest)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        byte[] bytes = new UTF8Encoding(false).GetBytes(
          SerializeCanonical(manifest));
        return string.Concat(algorithm.ComputeHash(bytes).Select(value =>
          value.ToString("x2", CultureInfo.InvariantCulture)));
      }
    }

    private static OfficialAcceptancePropertyDefinition ValidateAndNormalize(
      OfficialAcceptancePropertyDefinition value)
    {
      if (value == null) throw new ArgumentException(
        "Manifest definition cannot be null.");
      Guid parameterGuid;
      if (!Guid.TryParse(value.ParameterGuid, out parameterGuid))
        throw new ArgumentException("Manifest parameterGuid is invalid.");
      string propertyId = Field(value.PropertyId, "propertyId", false);
      string identity = Field(value.Identity, "identity", false);
      string declaredType = Field(
        value.DeclaredIfcType, "declaredIfcType", false);
      string canonicalUnit = Field(
        value.CanonicalUnit, "canonicalUnit", true);
      string bindingScope = Field(
        value.BindingScope, "bindingScope", false);
      string sourceStage = Field(value.SourceStage, "sourceStage", false);
      if (sourceStage != "STAGE01" && sourceStage != "STAGE02A"
        && sourceStage != "STAGE02B")
        throw new ArgumentException("Manifest sourceStage is invalid.");
      return new OfficialAcceptancePropertyDefinition
      {
        PropertyId = propertyId,
        Identity = identity,
        DeclaredIfcType = declaredType,
        CanonicalUnit = canonicalUnit,
        ParameterGuid = parameterGuid.ToString("D").ToLowerInvariant(),
        BindingScope = bindingScope,
        SourceStage = sourceStage
      };
    }

    private static string Field(string value, string name, bool allowEmpty)
    {
      string normalized = (value ?? string.Empty).Trim();
      if ((!allowEmpty && normalized.Length == 0)
        || normalized.IndexOf('\u001f') >= 0
        || normalized.IndexOf('\r') >= 0
        || normalized.IndexOf('\n') >= 0)
        throw new ArgumentException("Manifest " + name + " is invalid.");
      return normalized;
    }
  }
}
