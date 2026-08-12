using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BIMBaoGui.RevitAddin.Runtime
{
  internal sealed class PluginRuntimeIdentity
  {
    private const string MissingAssemblyPath = "运行时未提供程序集路径";

    private PluginRuntimeIdentity(
      string productVersion,
      string buildNumber,
      string commitSha,
      string assemblyPath)
    {
      ProductVersion = NormalizeVersion(productVersion);
      BuildNumber = NormalizeValue(buildNumber, "local");
      CommitSha = NormalizeValue(commitSha, "unknown");
      ShortCommitSha = string.Equals(
        CommitSha,
        "unknown",
        StringComparison.Ordinal)
        ? CommitSha
        : CommitSha.Substring(0, Math.Min(8, CommitSha.Length));
      AssemblyPath = NormalizePath(assemblyPath);
    }

    internal string ProductVersion { get; }
    internal string BuildNumber { get; }
    internal string CommitSha { get; }
    internal string ShortCommitSha { get; }
    internal string AssemblyPath { get; }

    internal static PluginRuntimeIdentity Read(Assembly assembly)
    {
      if (assembly == null) throw new ArgumentNullException(nameof(assembly));

      IReadOnlyDictionary<string, string> metadata = assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .GroupBy(value => value.Key ?? string.Empty, StringComparer.Ordinal)
        .Where(group => group.Key.Length > 0)
        .ToDictionary(
          group => group.Key,
          group => group.Last().Value ?? string.Empty,
          StringComparer.Ordinal);

      string location = assembly.Location ?? string.Empty;
      string informational = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;
      string fileVersion = ReadFileVersion(location);
      string productVersion = informational
        ?? fileVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
      metadata.TryGetValue("HBR.BuildNumber", out string buildNumber);
      metadata.TryGetValue("HBR.CommitSha", out string commitSha);
      return new PluginRuntimeIdentity(
        productVersion,
        buildNumber,
        commitSha,
        location);
    }

    internal static PluginRuntimeIdentity Create(
      string productVersion,
      string buildNumber,
      string commitSha,
      string assemblyPath)
    {
      return new PluginRuntimeIdentity(
        productVersion,
        buildNumber,
        commitSha,
        assemblyPath);
    }

    private static string ReadFileVersion(string assemblyPath)
    {
      if (string.IsNullOrWhiteSpace(assemblyPath)) return null;
      try
      {
        return FileVersionInfo.GetVersionInfo(assemblyPath).FileVersion;
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is FileNotFoundException
        || exception is IOException
        || exception is System.Security.SecurityException)
      {
        return null;
      }
    }

    private static string NormalizeVersion(string value)
    {
      string normalized = NormalizeValue(value, "unknown");
      int metadataIndex = normalized.IndexOf('+');
      return metadataIndex < 0
        ? normalized
        : normalized.Substring(0, metadataIndex);
    }

    private static string NormalizeValue(string value, string fallback)
    {
      return string.IsNullOrWhiteSpace(value)
        ? fallback
        : value.Trim();
    }

    private static string NormalizePath(string value)
    {
      if (string.IsNullOrWhiteSpace(value)) return MissingAssemblyPath;
      try
      {
        return Path.GetFullPath(value.Trim());
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is NotSupportedException
        || exception is PathTooLongException)
      {
        return value.Trim();
      }
    }
  }
}
