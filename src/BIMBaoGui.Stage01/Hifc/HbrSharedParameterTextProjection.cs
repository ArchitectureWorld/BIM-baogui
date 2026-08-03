using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Hifc
{
  internal static class HbrSharedParameterTextProjection
  {
    private const string NewLine = "\r\n";

    internal static string CreateText(HbrRuleDatabase database)
    {
      if (database == null) throw new ArgumentNullException(nameof(database));
      OfficialHifcMapping[] mappings =
        OfficialHifcMappingCatalog.FromDatabase(database).Mappings.ToArray();
      OfficialHifcMapping[] aliases = DistinctOfficialAliases(mappings)
        .OrderBy(mapping => mapping.PropertySet, StringComparer.Ordinal)
        .ThenBy(
          mapping => mapping.OfficialSourceParameterName,
          StringComparer.Ordinal)
        .ToArray();
      AliasGroup[] aliasGroups = aliases
        .GroupBy(mapping => mapping.PropertySet, StringComparer.Ordinal)
        .Select((group, index) => new AliasGroup(
          1000 + index,
          group.Key,
          group.ToArray()))
        .ToArray();

      var builder = new StringBuilder(65536);
      AppendLine(builder, "# This is a Revit shared parameter file.");
      AppendLine(
        builder,
        "# Generated from GH_HIFC_开发基线_v1. Do not edit manually.");
      AppendLine(builder, "*META\tVERSION\tMINVERSION");
      AppendLine(builder, "META\t2\t1");
      AppendLine(builder, "*GROUP\tID\tNAME");
      AppendLine(builder, "GROUP\t1\tGH_HIFC_规划报建");
      foreach (AliasGroup group in aliasGroups)
      {
        AppendLine(
          builder,
          "GROUP\t"
          + group.Id.ToString(CultureInfo.InvariantCulture)
          + "\tGH_HIFC_官方源_"
          + Sanitize(group.PropertySet));
      }
      AppendLine(
        builder,
        "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");

      foreach (OfficialHifcMapping mapping in mappings.Where(mapping =>
        !string.IsNullOrWhiteSpace(mapping.Category)))
      {
        if (!database.PropertiesById.TryGetValue(
          mapping.PropertyId,
          out HbrRuleProperty property))
          throw new InvalidDataException(
            "Shared parameter mapping references unknown propertyId: "
            + mapping.PropertyId);
        AppendLine(
          builder,
          string.Join(
            "\t",
            "PARAM",
            mapping.ParameterGuid.ToString("D"),
            Sanitize(mapping.ParameterName),
            mapping.SharedParameterType,
            string.Empty,
            "1",
            property.Revit.Visible ? "1" : "0",
            Sanitize(mapping.IfcEntity)
              + " | "
              + Sanitize(mapping.PropertySet)
              + " | "
              + Sanitize(mapping.IfcProperty)
              + " | "
              + Sanitize(mapping.IfcDataType),
            property.Revit.UserModifiable ? "1" : "0",
            "0"));
      }

      foreach (AliasGroup group in aliasGroups)
      {
        foreach (OfficialHifcMapping mapping in group.Mappings)
        {
          AppendLine(
            builder,
            string.Join(
              "\t",
              "PARAM",
              mapping.OfficialSourceParameterGuid.ToString("D"),
              Sanitize(mapping.OfficialSourceParameterName),
              OfficialParameterTypeContract.Resolve(
                mapping.OfficialSourceParameterType).SemanticType,
              string.Empty,
              group.Id.ToString(CultureInfo.InvariantCulture),
              "1",
              "Official exact source alias | "
                + Sanitize(mapping.IfcEntity)
                + " | "
                + Sanitize(mapping.PropertySet)
                + " | "
                + Sanitize(mapping.IfcProperty),
              "1",
              "0"));
        }
      }
      return builder.ToString();
    }

    internal static byte[] CreateUtf8Bytes(HbrRuleDatabase database)
    {
      return new UTF8Encoding(false).GetBytes(CreateText(database));
    }

    internal static OfficialHifcMapping[] DistinctOfficialAliases(
      IEnumerable<OfficialHifcMapping> mappings)
    {
      if (mappings == null) throw new ArgumentNullException(nameof(mappings));
      var byGuid = new Dictionary<Guid, OfficialHifcMapping>();
      var distinct = new List<OfficialHifcMapping>();
      foreach (OfficialHifcMapping mapping in mappings)
      {
        if (mapping == null)
          throw new InvalidDataException(
            "Official source parameter alias is null.");
        Guid guid = mapping.OfficialSourceParameterGuid;
        if (!byGuid.TryGetValue(guid, out OfficialHifcMapping existing))
        {
          byGuid.Add(guid, mapping);
          distinct.Add(mapping);
          continue;
        }

        var conflicts = new List<string>();
        AddIdentityConflict(
          conflicts,
          "PropertySet",
          existing.PropertySet,
          mapping.PropertySet);
        AddIdentityConflict(
          conflicts,
          "OfficialSourceParameterName",
          existing.OfficialSourceParameterName,
          mapping.OfficialSourceParameterName);
        AddIdentityConflict(
          conflicts,
          "OfficialSourceParameterType",
          existing.OfficialSourceParameterType,
          mapping.OfficialSourceParameterType);
        if (conflicts.Count != 0)
          throw new InvalidDataException(
            "Official source parameter alias identity conflict for GUID "
            + guid.ToString("D")
            + ": "
            + string.Join("; ", conflicts));
      }
      return distinct.ToArray();
    }

    private static void AddIdentityConflict(
      ICollection<string> conflicts,
      string field,
      string first,
      string second)
    {
      if (string.Equals(first, second, StringComparison.Ordinal)) return;
      conflicts.Add(
        field
        + "='"
        + (first ?? "<null>")
        + "' vs '"
        + (second ?? "<null>")
        + "'");
    }

    private static void AppendLine(StringBuilder builder, string value)
    {
      builder.Append(value).Append(NewLine);
    }

    private static string Sanitize(string value)
    {
      return (value ?? string.Empty)
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ');
    }

    private sealed class AliasGroup
    {
      internal AliasGroup(
        int id,
        string propertySet,
        IReadOnlyList<OfficialHifcMapping> mappings)
      {
        Id = id;
        PropertySet = propertySet;
        Mappings = mappings;
      }

      internal int Id { get; }
      internal string PropertySet { get; }
      internal IReadOnlyList<OfficialHifcMapping> Mappings { get; }
    }
  }
}
