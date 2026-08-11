using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BIMBaoGui.Stage01.Stage03
{
  public static class Stage03OutputPathPolicy
  {
    private static readonly HashSet<string> WindowsReservedNames =
      new HashSet<string>(
        new[]
        {
          "CON", "PRN", "AUX", "NUL",
          "COM1", "COM2", "COM3", "COM4", "COM5",
          "COM6", "COM7", "COM8", "COM9",
          "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
          "LPT6", "LPT7", "LPT8", "LPT9"
        },
        StringComparer.OrdinalIgnoreCase);

    public static Stage03OutputPaths Create(
      string outputDirectory,
      string rvtStem,
      string runId)
    {
      string directory = NormalizeOutputDirectory(outputDirectory);
      string stem = ValidateStem(rvtStem);
      string normalizedRunId = ValidateRunId(runId);
      string prefix = stem + "-" + normalizedRunId;

      string rawIfc = DirectChild(directory, prefix + "-RAW.ifc");
      string finalIfc = DirectChild(
        directory,
        prefix + "-HIFC-MVD.ifc");
      string fieldReport = DirectChild(
        directory,
        prefix + "-fields.json");
      return new Stage03OutputPaths(
        directory,
        stem,
        normalizedRunId,
        rawIfc,
        finalIfc,
        fieldReport);
    }

    public static void ValidateUnused(Stage03OutputPaths paths)
    {
      if (paths == null) throw new ArgumentNullException(nameof(paths));
      string directory = NormalizeOutputDirectory(paths.OutputDirectory);
      string[] targets =
      {
        RequireExpectedDirectChild(
          directory,
          paths.RawIfc,
          paths.RvtStem + "-" + paths.RunId + "-RAW.ifc"),
        RequireExpectedDirectChild(
          directory,
          paths.FinalIfc,
          paths.RvtStem + "-" + paths.RunId + "-HIFC-MVD.ifc"),
        RequireExpectedDirectChild(
          directory,
          paths.FieldReport,
          paths.RvtStem + "-" + paths.RunId + "-fields.json")
      };
      if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()
        != targets.Length)
      {
        throw new ArgumentException(
          "Stage03 正式输出路径必须彼此不同。",
          nameof(paths));
      }
      string[] occupied = targets
        .Where(path => File.Exists(path) || Directory.Exists(path))
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
      if (occupied.Length > 0)
      {
        throw new IOException(
          "Stage03 正式输出已存在，禁止覆盖："
          + string.Join(", ", occupied));
      }
    }

    private static string NormalizeOutputDirectory(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException("输出目录不能为空。", nameof(value));
      string trimmed = value.Trim();
      if (!string.Equals(value, trimmed, StringComparison.Ordinal))
      {
        throw new ArgumentException(
          "输出目录不能包含首尾空白。",
          nameof(value));
      }
      string fullPath;
      try
      {
        fullPath = Path.GetFullPath(trimmed);
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is NotSupportedException
        || exception is PathTooLongException)
      {
        throw new ArgumentException("输出目录路径无效。", nameof(value), exception);
      }
      if (!Directory.Exists(fullPath))
        throw new DirectoryNotFoundException("输出目录不存在：" + fullPath);
      return TrimEndingSeparatorExceptRoot(fullPath);
    }

    private static string ValidateStem(string value)
    {
      string stem = ValidateSingleFileSegment(value, "RVT 文件名主体");
      string reservedCandidate = stem.Split('.')[0];
      if (WindowsReservedNames.Contains(reservedCandidate))
        throw new ArgumentException("RVT 文件名主体是 Windows 保留名称。", nameof(value));
      return stem;
    }

    private static string ValidateRunId(string value)
    {
      string runId = ValidateSingleFileSegment(value, "runId");
      if (!Stage03RunIdPolicy.IsValid(runId))
      {
        throw new ArgumentException(
          "runId 只能包含 ASCII 字母、数字和分隔单词的连字符。",
          nameof(value));
      }
      return runId;
    }

    private static string ValidateSingleFileSegment(
      string value,
      string label)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException(label + "不能为空。", nameof(value));
      string trimmed = value.Trim();
      if (!string.Equals(value, trimmed, StringComparison.Ordinal))
        throw new ArgumentException(label + "不能包含首尾空白。", nameof(value));
      if (trimmed == "." || trimmed == ".." || Path.IsPathRooted(trimmed))
        throw new ArgumentException(label + "不能是路径。", nameof(value));
      if (!string.Equals(Path.GetFileName(trimmed), trimmed, StringComparison.Ordinal)
        || trimmed.IndexOf(Path.DirectorySeparatorChar) >= 0
        || trimmed.IndexOf(Path.AltDirectorySeparatorChar) >= 0
        || trimmed.IndexOf('\\') >= 0
        || trimmed.IndexOf('/') >= 0)
      {
        throw new ArgumentException(label + "必须是单一文件名段。", nameof(value));
      }
      char[] invalid = Path.GetInvalidFileNameChars();
      if (trimmed.IndexOfAny(invalid) >= 0
        || trimmed.Any(character => character < ' '
          || "<>:\"/\\|?*".IndexOf(character) >= 0))
      {
        throw new ArgumentException(label + "包含非法文件名字符。", nameof(value));
      }
      return trimmed;
    }

    private static string DirectChild(string directory, string fileName)
    {
      if (fileName.Length > 255)
        throw new ArgumentException("Stage03 输出文件名过长。", nameof(fileName));
      string fullPath = Path.GetFullPath(Path.Combine(directory, fileName));
      if (!string.Equals(
        Path.GetDirectoryName(fullPath),
        directory,
        StringComparison.OrdinalIgnoreCase))
      {
        throw new ArgumentException("Stage03 输出路径越过了指定输出目录。");
      }
      return fullPath;
    }

    private static string RequireExpectedDirectChild(
      string directory,
      string path,
      string expectedName)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("Stage03 正式输出路径不能为空。", nameof(path));
      string expected = DirectChild(directory, expectedName);
      string actual = Path.GetFullPath(path);
      if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Stage03 正式输出路径与命名合同不一致。", nameof(path));
      return actual;
    }

    private static string TrimEndingSeparatorExceptRoot(string path)
    {
      string root = Path.GetPathRoot(path);
      if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        return path;
      return path.TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar);
    }
  }
}
