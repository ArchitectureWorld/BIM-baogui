using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal static class NativeStage01SharedParameterFile
  {
    private const string NewLine = "\r\n";

    internal static void Write(
      string path,
      IEnumerable<NativeStage01FieldDefinition> fields)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("共享参数文件路径不能为空。", nameof(path));
      File.WriteAllBytes(path, CreateBytes(fields));
    }

    internal static byte[] CreateBytes(
      IEnumerable<NativeStage01FieldDefinition> fields)
    {
      NativeStage01FieldDefinition[] ordered = (fields
        ?? throw new ArgumentNullException(nameof(fields)))
        .OrderBy(value => value.ParameterGuid)
        .ToArray();
      if (ordered.Any(value => value == null || !value.ParameterGuid.HasValue))
        throw new InvalidDataException("Stage01 共享参数定义不完整。");
      if (ordered.Select(value => value.ParameterGuid.Value)
        .Distinct().Count() != ordered.Length)
        throw new InvalidDataException("Stage01 共享参数包含重复 GUID。");

      var builder = new StringBuilder(ordered.Length * 180);
      Append(builder, "# This is a Revit shared parameter file.");
      Append(builder, "# Generated from the embedded authoritative HBR rule package.");
      Append(builder, "*META\tVERSION\tMINVERSION");
      Append(builder, "META\t2\t1");
      Append(builder, "*GROUP\tID\tNAME");
      Append(builder, "GROUP\t1\tHBR");
      Append(
        builder,
        "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");
      foreach (NativeStage01FieldDefinition field in ordered)
      {
        Append(builder, string.Join("\t", new[]
        {
          "PARAM",
          field.ParameterGuid.Value.ToString("D").ToLowerInvariant(),
          Sanitize(field.ParameterName),
          NormalizeParameterType(field.ParameterType),
          string.Empty,
          "1",
          "1",
          Sanitize(field.PropertyId),
          "1",
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
            "HBR 数据库包含 Revit 2020 不支持的参数类型：" + value);
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
