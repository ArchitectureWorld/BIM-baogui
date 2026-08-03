using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BIMBaoGui.Stage01.Rules;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  internal static class HbrSharedParameterDefinitionText
  {
    private const string NewLine = "\r\n";

    internal static string Create(IEnumerable<HbrRuleProperty> properties)
    {
      HbrRuleProperty[] ordered = (properties
        ?? throw new ArgumentNullException(nameof(properties)))
        .OrderBy(property => property.Revit.ParameterGuid)
        .ToArray();
      if (ordered.Any(property => property == null))
        throw new InvalidDataException("HBR 共享参数投影包含空规则属性。");
      if (ordered.Select(property => property.Revit.ParameterGuid)
        .Distinct()
        .Count() != ordered.Length)
      {
        throw new InvalidDataException("HBR 共享参数投影包含重复 GUID。");
      }

      var builder = new StringBuilder(ordered.Length * 160);
      Append(builder, "# This is a Revit shared parameter file.");
      Append(builder, "# Generated from the active HBR rule package.");
      Append(builder, "*META\tVERSION\tMINVERSION");
      Append(builder, "META\t2\t1");
      Append(builder, "*GROUP\tID\tNAME");
      Append(builder, "GROUP\t1\tHBR");
      Append(
        builder,
        "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");

      foreach (HbrRuleProperty property in ordered)
      {
        Append(builder, string.Join("\t", new[]
        {
          "PARAM",
          property.Revit.ParameterGuid.ToString("D").ToLowerInvariant(),
          Sanitize(property.Revit.ParameterName),
          NormalizeParameterType(property.Revit.ParameterType),
          string.Empty,
          "1",
          property.Revit.Visible ? "1" : "0",
          Sanitize(property.PropertyId),
          property.Revit.UserModifiable ? "1" : "0",
          "0"
        }));
      }
      return builder.ToString();
    }

    internal static byte[] CreateRevitBytes(
      IEnumerable<HbrRuleProperty> properties)
    {
      Encoding encoding = Encoding.Unicode;
      byte[] preamble = encoding.GetPreamble();
      byte[] content = encoding.GetBytes(Create(properties));
      var result = new byte[preamble.Length + content.Length];
      Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
      Buffer.BlockCopy(
        content,
        0,
        result,
        preamble.Length,
        content.Length);
      return result;
    }

    internal static void WriteRevitFile(
      string path,
      IEnumerable<HbrRuleProperty> properties)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("共享参数文件路径不能为空。", nameof(path));
      File.WriteAllBytes(path, CreateRevitBytes(properties));
    }

    private static string NormalizeParameterType(string value)
    {
      string normalized = (value ?? string.Empty)
        .Trim()
        .ToUpperInvariant();
      switch (normalized)
      {
        case "TEXT":
        case "INTEGER":
        case "YESNO":
        case "LENGTH":
        case "AREA":
        case "VOLUME":
        case "ANGLE":
        case "NUMBER":
          return normalized;
        default:
          throw new InvalidDataException(
            "HBR 规则包含 Revit 2020 不支持的参数类型：" + value);
      }
    }

    private static string Sanitize(string value)
    {
      return (value ?? string.Empty)
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ');
    }

    private static void Append(StringBuilder builder, string line)
    {
      builder.Append(line).Append(NewLine);
    }
  }
}
