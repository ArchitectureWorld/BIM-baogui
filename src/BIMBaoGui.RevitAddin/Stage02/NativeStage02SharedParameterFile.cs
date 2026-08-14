using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02SharedParameterFile
  {
    private const string NewLine = "\r\n";

    internal static void Write(
      string path,
      IEnumerable<NativeStage02PropertyDefinition> properties)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("共享参数文件路径不能为空。", nameof(path));
      File.WriteAllBytes(path, CreateBytes(properties));
    }

    internal static byte[] CreateBytes(
      IEnumerable<NativeStage02PropertyDefinition> properties)
    {
      NativeStage02PropertyDefinition[] ordered = (properties
        ?? throw new ArgumentNullException(nameof(properties)))
        .OrderBy(value => value.ParameterGuid)
        .ToArray();
      if (ordered.Any(value => value == null
        || value.ParameterGuid == Guid.Empty
        || string.IsNullOrWhiteSpace(value.ParameterName)))
        throw new InvalidDataException("Stage02 共享参数定义不完整。" );
      if (ordered.Select(value => value.ParameterGuid)
        .Distinct().Count() != ordered.Length)
        throw new InvalidDataException("Stage02 共享参数包含重复 GUID。" );

      var builder = new StringBuilder(ordered.Length * 190);
      Append(builder, "# This is a Revit shared parameter file.");
      Append(builder, "# Generated from the embedded authoritative HBR rule package.");
      Append(builder, "*META\tVERSION\tMINVERSION");
      Append(builder, "META\t2\t1");
      Append(builder, "*GROUP\tID\tNAME");
      Append(builder, "GROUP\t1\tHBR");
      Append(
        builder,
        "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");
      foreach (NativeStage02PropertyDefinition property in ordered)
      {
        Append(builder, string.Join("\t", new[]
        {
          "PARAM",
          property.ParameterGuid.ToString("D").ToLowerInvariant(),
          Sanitize(property.ParameterName),
          NormalizeParameterType(property.ParameterType),
          string.Empty,
          "1",
          property.Visible ? "1" : "0",
          Sanitize(property.PropertyId),
          property.UserModifiable ? "1" : "0",
          "0"
        }));
      }

      Encoding encoding = Encoding.Unicode;
      byte[] preamble = encoding.GetPreamble();
      byte[] body = encoding.GetBytes(builder.ToString());
      var result = new byte[preamble.Length + body.Length];
      Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
      Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
      return result;
    }

    private static string NormalizeParameterType(string value)
    {
      string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
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
            "HBR Stage02 包含 Revit 2020 不支持的参数类型：" + value);
      }
    }

    private static string Sanitize(string value)
    {
      return (value ?? string.Empty)
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ');
    }

    private static void Append(StringBuilder builder, string value)
    {
      builder.Append(value).Append(NewLine);
    }
  }
}
