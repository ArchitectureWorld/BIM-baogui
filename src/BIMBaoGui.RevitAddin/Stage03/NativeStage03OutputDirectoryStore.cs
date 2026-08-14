using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal sealed class NativeStage03OutputDirectoryStore
  {
    private static readonly object SyncRoot = new object();
    private readonly string _settingsPath;

    internal NativeStage03OutputDirectoryStore()
      : this(CreateDefaultSettingsPath())
    {
    }

    internal NativeStage03OutputDirectoryStore(string settingsPath)
    {
      if (string.IsNullOrWhiteSpace(settingsPath)
        || !Path.IsPathRooted(settingsPath))
      {
        throw new ArgumentException(
          "Stage03 输出目录记录文件必须使用绝对路径。",
          nameof(settingsPath));
      }
      _settingsPath = Path.GetFullPath(settingsPath);
    }

    internal string Resolve(string documentPath)
    {
      string normalizedDocumentPath = NormalizeDocumentPath(documentPath);
      string fallback = Path.GetDirectoryName(normalizedDocumentPath)
        ?? string.Empty;
      lock (SyncRoot)
      {
        try
        {
          string stored;
          if (Read().TryGetValue(normalizedDocumentPath, out stored)
            && TryNormalizeOutputDirectory(stored, out string normalized))
          {
            return normalized;
          }
        }
        catch
        {
          // 本机偏好损坏或不可读时，回退到当前 RVT 所在目录。
        }
      }
      return fallback;
    }

    internal void Remember(string documentPath, string outputDirectory)
    {
      string normalizedDocumentPath = NormalizeDocumentPath(documentPath);
      if (!TryNormalizeOutputDirectory(
        outputDirectory,
        out string normalizedOutputDirectory))
      {
        throw new ArgumentException(
          "Stage03 输出目录必须使用绝对路径。",
          nameof(outputDirectory));
      }

      lock (SyncRoot)
      {
        SortedDictionary<string, string> entries = Read();
        entries[normalizedDocumentPath] = normalizedOutputDirectory;
        Write(entries);
      }
    }

    internal bool TryRemember(
      string documentPath,
      string outputDirectory,
      out string error)
    {
      try
      {
        Remember(documentPath, outputDirectory);
        error = string.Empty;
        return true;
      }
      catch (Exception exception)
      {
        error = exception.Message;
        return false;
      }
    }

    internal static string NormalizeDocumentPath(string documentPath)
    {
      if (string.IsNullOrWhiteSpace(documentPath)
        || !Path.IsPathRooted(documentPath))
      {
        throw new ArgumentException(
          "Revit 模型路径必须使用绝对路径。",
          nameof(documentPath));
      }
      return Path.GetFullPath(documentPath.Trim());
    }

    internal static bool TryNormalizeOutputDirectory(
      string outputDirectory,
      out string normalized)
    {
      normalized = string.Empty;
      if (string.IsNullOrWhiteSpace(outputDirectory)
        || !Path.IsPathRooted(outputDirectory))
      {
        return false;
      }
      try
      {
        normalized = Path.GetFullPath(outputDirectory.Trim());
        return true;
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is NotSupportedException
        || exception is PathTooLongException)
      {
        return false;
      }
    }

    private SortedDictionary<string, string> Read()
    {
      var result = new SortedDictionary<string, string>(
        StringComparer.OrdinalIgnoreCase);
      if (!File.Exists(_settingsPath)) return result;

      try
      {
        string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
        var serializer = new JavaScriptSerializer();
        Dictionary<string, string> stored =
          serializer.Deserialize<Dictionary<string, string>>(json);
        foreach (KeyValuePair<string, string> entry in stored
          ?? new Dictionary<string, string>())
        {
          try
          {
            string document = NormalizeDocumentPath(entry.Key);
            if (TryNormalizeOutputDirectory(entry.Value, out string output))
              result[document] = output;
          }
          catch (ArgumentException)
          {
            // 单条无效记录不会污染其他模型的有效记录。
          }
        }
      }
      catch
      {
        // 损坏文件按空记录恢复，下一次保存会覆盖为合法 JSON。
      }
      return result;
    }

    private void Write(SortedDictionary<string, string> entries)
    {
      string directory = Path.GetDirectoryName(_settingsPath);
      if (string.IsNullOrWhiteSpace(directory))
        throw new InvalidOperationException("无法确定 Stage03 设置目录。");
      Directory.CreateDirectory(directory);

      string token = Guid.NewGuid().ToString("N");
      string temporaryPath = _settingsPath + "." + token + ".tmp";
      string backupPath = _settingsPath + "." + token + ".bak";
      try
      {
        var serializer = new JavaScriptSerializer();
        string json = serializer.Serialize(entries.ToDictionary(
          entry => entry.Key,
          entry => entry.Value,
          StringComparer.OrdinalIgnoreCase));
        File.WriteAllText(
          temporaryPath,
          json,
          new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(_settingsPath))
          File.Replace(temporaryPath, _settingsPath, backupPath, true);
        else
          File.Move(temporaryPath, _settingsPath);
      }
      finally
      {
        DeleteIfPresent(temporaryPath);
        DeleteIfPresent(backupPath);
      }
    }

    private static void DeleteIfPresent(string path)
    {
      try
      {
        if (File.Exists(path)) File.Delete(path);
      }
      catch
      {
        // 清理失败不能破坏已经成功发布的设置文件。
      }
    }

    private static string CreateDefaultSettingsPath()
    {
      string root = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData);
      if (string.IsNullOrWhiteSpace(root)) root = Path.GetTempPath();
      return Path.Combine(
        root,
        "BIMBaoGui",
        "RevitAddin",
        "stage03-output-directories.json");
    }
  }
}
