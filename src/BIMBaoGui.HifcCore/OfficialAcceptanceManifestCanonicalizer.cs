using System;
using System.Collections.Generic;
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
      OfficialAcceptanceIdentity identity = manifest.Identity
        ?? throw new ArgumentException(
          "Manifest identity is required.",
          nameof(manifest));
      OfficialAcceptancePropertyDefinition[] definitions = (manifest.Definitions
          ?? Array.Empty<OfficialAcceptancePropertyDefinition>())
        .Where(value => value != null)
        .OrderBy(value => value.PropertyId, StringComparer.Ordinal)
        .ThenBy(value => value.IfcEntity, StringComparer.Ordinal)
        .ThenBy(value => value.IfcPropertySet, StringComparer.Ordinal)
        .ThenBy(value => value.IfcProperty, StringComparer.Ordinal)
        .ThenBy(value => value.ParameterGuid, StringComparer.Ordinal)
        .ToArray();
      var builder = new StringBuilder(2048);
      builder.Append('{');
      Property(builder, "schemaVersion", manifest.SchemaVersion, false);
      Property(builder, "manifestVersion", manifest.ManifestVersion, true);
      builder.Append(",\"identity\":{");
      Property(builder, "documentFingerprint", identity.DocumentFingerprint, false);
      Property(builder, "rulePackageSha256", identity.RulePackageSha256, true);
      Property(builder, "stage01ResultHash", identity.Stage01ResultHash, true);
      Property(builder, "stage02AResultHash", identity.Stage02AResultHash, true);
      Property(builder, "stage02BResultHash", identity.Stage02BResultHash, true);
      Property(builder, "goldenRvtSha256", identity.GoldenRvtSha256, true);
      Property(builder, "officialIfcSha256", identity.OfficialIfcSha256, true);
      builder.Append("},\"definitions\":[");
      for (int index = 0; index < definitions.Length; index++)
      {
        if (index > 0) builder.Append(',');
        OfficialAcceptancePropertyDefinition definition = definitions[index];
        builder.Append('{');
        Property(builder, "propertyId", definition.PropertyId, false);
        Property(builder, "ifcEntity", definition.IfcEntity, true);
        Property(builder, "ifcPropertySet", definition.IfcPropertySet, true);
        Property(builder, "ifcProperty", definition.IfcProperty, true);
        Property(builder, "declaredIfcType", definition.DeclaredIfcType, true);
        Property(builder, "canonicalUnit", definition.CanonicalUnit, true);
        Property(builder, "parameterGuid", definition.ParameterGuid, true);
        builder.Append('}');
      }
      builder.Append("]}");
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

    private static void Property(
      StringBuilder builder,
      string name,
      string value,
      bool comma)
    {
      if (comma) builder.Append(',');
      builder.Append(Quote(name)).Append(':').Append(Quote(value));
    }

    private static string Quote(string value)
    {
      var builder = new StringBuilder((value ?? string.Empty).Length + 2);
      builder.Append('"');
      foreach (char character in value ?? string.Empty)
      {
        switch (character)
        {
          case '"': builder.Append("\\\""); break;
          case '\\': builder.Append("\\\\"); break;
          case '\b': builder.Append("\\b"); break;
          case '\f': builder.Append("\\f"); break;
          case '\n': builder.Append("\\n"); break;
          case '\r': builder.Append("\\r"); break;
          case '\t': builder.Append("\\t"); break;
          default:
            if (character < 0x20)
              builder.Append("\\u").Append(((int)character).ToString(
                "x4", CultureInfo.InvariantCulture));
            else
              builder.Append(character);
            break;
        }
      }
      return builder.Append('"').ToString();
    }
  }
}
