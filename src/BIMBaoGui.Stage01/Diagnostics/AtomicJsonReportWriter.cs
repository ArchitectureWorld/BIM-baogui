using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.Stage01.Diagnostics
{
  public static class AtomicJsonReportWriter
  {
    private const int HResultFileExists = unchecked((int)0x80070050);
    private const int HResultAlreadyExists = unchecked((int)0x800700B7);
    private const string TemporaryCandidateAlphabet =
      "0123456789abcdefghijklmnopqrstuvwxyz";
    private static readonly UTF8Encoding StrictUtf8 =
      new UTF8Encoding(false, true);

    public static void Write(string targetPath, byte[] utf8Json)
    {
      Write(targetPath, utf8Json, File.Move);
    }

    internal static void Write(
      string targetPath,
      byte[] utf8Json,
      Action<string, string> publisher)
    {
      if (publisher == null) throw new ArgumentNullException(nameof(publisher));
      string target = NormalizeTarget(targetPath);
      byte[] payload = ValidateAndCopyPayload(utf8Json);
      WriteValidated(target, payload, publisher);
    }

    internal static void WriteTrustedJson(
      string targetPath,
      byte[] trustedUtf8Json)
    {
      string target = NormalizeTarget(targetPath);
      byte[] payload = RequireTrustedPayload(trustedUtf8Json);
      WriteValidated(target, payload, File.Move);
    }

    private static void WriteValidated(
      string target,
      byte[] payload,
      Action<string, string> publisher)
    {
      if (File.Exists(target) || Directory.Exists(target))
      {
        throw new IOException(
          "JSON 报告目标已存在，禁止覆盖：" + target,
          HResultAlreadyExists);
      }

      string temporaryPath = null;
      try
      {
        FileStream stream = CreateUniqueTemporaryFile(
          target,
          out temporaryPath);
        using (stream)
        {
          stream.Write(payload, 0, payload.Length);
          stream.Flush(true);
        }
        publisher(temporaryPath, target);
        temporaryPath = null;
      }
      finally
      {
        DeleteTemporaryBestEffort(temporaryPath);
      }
    }

    private static byte[] RequireTrustedPayload(byte[] trustedUtf8Json)
    {
      if (trustedUtf8Json == null)
        throw new ArgumentNullException(nameof(trustedUtf8Json));
      if (trustedUtf8Json.Length == 0)
      {
        throw new ArgumentException(
          "可信 JSON 报告内容不能为空。",
          nameof(trustedUtf8Json));
      }
      if (trustedUtf8Json.Length >= 3
        && trustedUtf8Json[0] == 0xEF
        && trustedUtf8Json[1] == 0xBB
        && trustedUtf8Json[2] == 0xBF)
      {
        throw new ArgumentException(
          "可信 JSON 报告必须使用 UTF-8 无 BOM。",
          nameof(trustedUtf8Json));
      }
      return trustedUtf8Json;
    }

    private static string NormalizeTarget(string targetPath)
    {
      if (string.IsNullOrWhiteSpace(targetPath))
        throw new ArgumentException("JSON 报告目标不能为空。", nameof(targetPath));
      string trimmed = targetPath.Trim();
      if (!string.Equals(targetPath, trimmed, StringComparison.Ordinal))
      {
        throw new ArgumentException(
          "JSON 报告目标不能包含首尾空白。",
          nameof(targetPath));
      }
      if (!Path.IsPathRooted(targetPath))
        throw new ArgumentException("JSON 报告目标必须是绝对路径。", nameof(targetPath));
      string fullPath = Path.GetFullPath(targetPath);
      if (!string.Equals(
        targetPath,
        fullPath,
        StringComparison.OrdinalIgnoreCase))
      {
        throw new ArgumentException(
          "JSON 报告目标必须是规范绝对路径。",
          nameof(targetPath));
      }
      string directory = Path.GetDirectoryName(fullPath);
      string fileName = Path.GetFileName(fullPath);
      if (string.IsNullOrWhiteSpace(directory)
        || string.IsNullOrWhiteSpace(fileName))
      {
        throw new ArgumentException("JSON 报告目标不是有效文件路径。", nameof(targetPath));
      }
      if (!string.Equals(
          Path.GetExtension(fileName),
          ".json",
          StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(fileName)))
      {
        throw new ArgumentException(
          "JSON 报告目标必须使用非空文件名和 .json 扩展名。",
          nameof(targetPath));
      }
      if (!Directory.Exists(directory))
        throw new DirectoryNotFoundException("JSON 报告目录不存在：" + directory);
      return fullPath;
    }

    private static byte[] ValidateAndCopyPayload(byte[] utf8Json)
    {
      if (utf8Json == null) throw new ArgumentNullException(nameof(utf8Json));
      if (utf8Json.Length == 0)
        throw new ArgumentException("JSON 报告内容不能为空。", nameof(utf8Json));
      if (utf8Json.Length >= 3
        && utf8Json[0] == 0xEF
        && utf8Json[1] == 0xBB
        && utf8Json[2] == 0xBF)
      {
        throw new ArgumentException("JSON 报告必须使用 UTF-8 无 BOM。", nameof(utf8Json));
      }
      string json = StrictUtf8.GetString(utf8Json);
      object parsed;
      try
      {
        parsed = new JavaScriptSerializer
        {
          MaxJsonLength = int.MaxValue,
          RecursionLimit = 256
        }.DeserializeObject(json);
      }
      catch (Exception exception)
      {
        throw new ArgumentException("JSON 报告内容不是合法 JSON。", nameof(utf8Json), exception);
      }
      if (!(parsed is System.Collections.IDictionary)
        && !(parsed is object[]))
      {
        throw new ArgumentException(
          "JSON 报告根节点必须是对象或数组。",
          nameof(utf8Json));
      }
      return (byte[])utf8Json.Clone();
    }

    private static FileStream CreateUniqueTemporaryFile(
      string target,
      out string temporaryPath)
    {
      string directory = Path.GetDirectoryName(target);
      for (int attempt = 0;
        attempt < TemporaryCandidateAlphabet.Length;
        attempt++)
      {
        temporaryPath = Path.Combine(
          directory,
          TemporaryCandidateAlphabet[attempt] + ".tmp");
        try
        {
          return new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        }
        catch (IOException exception) when (IsCreateNewCollision(exception))
        {
        }
        catch (UnauthorizedAccessException) when (
          Directory.Exists(temporaryPath))
        {
        }
      }
      temporaryPath = null;
      throw new IOException("无法分配唯一的同目录 JSON 临时文件。");
    }

    internal static bool IsCreateNewCollision(IOException exception)
    {
      return exception.HResult == HResultFileExists
        || exception.HResult == HResultAlreadyExists;
    }

    private static void DeleteTemporaryBestEffort(string path)
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
  }
}
