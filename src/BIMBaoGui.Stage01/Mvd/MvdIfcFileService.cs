using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class MvdIfcFileResult
  {
    public bool Success { get; set; }
    public string SourcePath { get; set; }
    public string OutputPath { get; set; }
    public string SourceSha256 { get; set; }
    public string OutputSha256 { get; set; }
    public MvdIfcNormalizationResult Normalization { get; set; }
    public MvdIfcValidationResult Validation { get; set; }
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
  }

  internal sealed class MvdIfcFileService
  {
    public MvdIfcFileResult Execute(string sourcePath, string destinationPath)
    {
      string source = NormalizePath(sourcePath, nameof(sourcePath));
      string destination = NormalizePath(destinationPath, nameof(destinationPath));
      ValidatePaths(source, destination);

      string temporaryPath = destination
        + "."
        + Guid.NewGuid().ToString("N")
        + ".tmp";
      string sourceHashBefore = ComputeSha256(source);
      try
      {
        Utf8IfcText sourceText = ReadUtf8Ifc(source);
        IfcStepDocument document = IfcStepDocument.Parse(sourceText.Text);
        var normalizer = new MvdIfcNormalizer();
        MvdIfcNormalizationResult normalization = normalizer.Normalize(document);
        if (!normalization.Success)
          throw new InvalidDataException("源 IFC 未找到可规范化的 MVD 属性。");

        string serialized = document.Serialize();
        WriteUtf8Ifc(temporaryPath, serialized, sourceText.HasBom);
        Utf8IfcText outputText = ReadUtf8Ifc(temporaryPath);
        IfcStepDocument outputDocument = IfcStepDocument.Parse(outputText.Text);
        MvdIfcValidationResult validation = normalizer.Validate(outputDocument);
        if (!validation.Success)
          throw new InvalidDataException(
            "MVD IFC 回读验收失败："
            + string.Join("；", validation.Messages ?? Array.Empty<string>()));
        if (validation.MatchingPropertyCount < normalization.MatchingPropertyCount)
          throw new InvalidDataException(
            "MVD IFC 回读匹配数量减少：写入 "
            + normalization.MatchingPropertyCount
            + "，回读 "
            + validation.MatchingPropertyCount);

        string sourceHashAfter = ComputeSha256(source);
        if (!string.Equals(
          sourceHashBefore,
          sourceHashAfter,
          StringComparison.OrdinalIgnoreCase))
          throw new IOException("源 IFC 在规范化过程中发生变化，已取消输出。");

        File.Move(temporaryPath, destination);
        temporaryPath = null;
        string outputHash = ComputeSha256(destination);
        var messages = new List<string>();
        messages.AddRange(normalization.Messages ?? Array.Empty<string>());
        messages.AddRange(validation.Messages ?? Array.Empty<string>());
        messages.Add("源 IFC SHA-256 保持不变：" + sourceHashBefore);
        messages.Add("输出 IFC：" + destination);
        return new MvdIfcFileResult
        {
          Success = true,
          SourcePath = source,
          OutputPath = destination,
          SourceSha256 = sourceHashBefore,
          OutputSha256 = outputHash,
          Normalization = normalization,
          Validation = validation,
          Messages = messages
        };
      }
      finally
      {
        DeleteBestEffort(temporaryPath);
      }
    }

    private static void ValidatePaths(string source, string destination)
    {
      if (!File.Exists(source))
        throw new FileNotFoundException("源 IFC 不存在。", source);
      if (!string.Equals(
        Path.GetExtension(source),
        ".ifc",
        StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("源文件扩展名必须为 .ifc。");
      if (!string.Equals(
        Path.GetExtension(destination),
        ".ifc",
        StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("输出文件扩展名必须为 .ifc。");
      if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("输出 IFC 不能覆盖源 IFC。");
      if (File.Exists(destination))
        throw new IOException("输出 IFC 已存在，拒绝覆盖：" + destination);
      string directory = Path.GetDirectoryName(destination);
      if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        throw new DirectoryNotFoundException("输出目录不存在：" + directory);
    }

    private static string NormalizePath(string path, string parameterName)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("IFC 路径不能为空。", parameterName);
      return Path.GetFullPath(path.Trim());
    }

    private static Utf8IfcText ReadUtf8Ifc(string path)
    {
      byte[] bytes = File.ReadAllBytes(path);
      bool hasBom = bytes.Length >= 3
        && bytes[0] == 0xef
        && bytes[1] == 0xbb
        && bytes[2] == 0xbf;
      var encoding = new UTF8Encoding(false, true);
      try
      {
        return new Utf8IfcText
        {
          HasBom = hasBom,
          Text = encoding.GetString(
            bytes,
            hasBom ? 3 : 0,
            bytes.Length - (hasBom ? 3 : 0))
        };
      }
      catch (DecoderFallbackException exception)
      {
        throw new InvalidDataException("IFC 文件不是有效 UTF-8。", exception);
      }
    }

    private static void WriteUtf8Ifc(string path, string text, bool emitBom)
    {
      var encoding = new UTF8Encoding(emitBom);
      byte[] bytes = encoding.GetBytes(text ?? string.Empty);
      if (emitBom)
      {
        byte[] preamble = encoding.GetPreamble();
        byte[] combined = new byte[preamble.Length + bytes.Length];
        Buffer.BlockCopy(preamble, 0, combined, 0, preamble.Length);
        Buffer.BlockCopy(bytes, 0, combined, preamble.Length, bytes.Length);
        bytes = combined;
      }
      using (var stream = new FileStream(
        path,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None))
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string ComputeSha256(string path)
    {
      using (FileStream stream = File.OpenRead(path))
      using (SHA256 algorithm = SHA256.Create())
        return string.Concat(algorithm.ComputeHash(stream)
          .Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static void DeleteBestEffort(string path)
    {
      if (string.IsNullOrWhiteSpace(path)) return;
      try
      {
        if (File.Exists(path)) File.Delete(path);
      }
      catch
      {
      }
    }

    private sealed class Utf8IfcText
    {
      public bool HasBom { get; set; }
      public string Text { get; set; }
    }
  }
}
