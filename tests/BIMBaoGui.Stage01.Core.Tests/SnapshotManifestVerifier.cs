using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace BIMBaoGui.Stage01.Core.Tests
{
  internal static class SnapshotManifestVerifier
  {
    private const string ManifestName = "manifest.sha256.v1.json";
    private const string ExpectedSchemaVersion = "1.0";
    private const string ExpectedAlgorithm = "SHA-256";
    private const string ExpectedFrozenCommit =
      "91a2db05ed57ae2335e63ff532b4bf5fe6109dfb";

    private static readonly string[] ExpectedSnapshotNames =
    {
      "mvd-ifc-normalization.v1.json",
      "official-hifc-mappings.v1.json",
      "official-plugin-compatibility.v1.json",
      "rule-activation.v1.json",
      "shared-parameters-canonical.v1.json",
      "stage01-registry.v1.json",
      "task-rules.v1.json"
    };

    internal static void VerifyEmbeddedSnapshots(Assembly assembly)
    {
      if (assembly == null) throw new ArgumentNullException(nameof(assembly));
      var resources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
      const string marker = ".Snapshots.";
      foreach (string resourceName in assembly.GetManifestResourceNames()
        .Where(name => name.IndexOf(marker, StringComparison.Ordinal) >= 0))
      {
        int markerIndex = resourceName.LastIndexOf(
          marker,
          StringComparison.Ordinal);
        string fileName = resourceName.Substring(markerIndex + marker.Length);
        if (resources.ContainsKey(fileName))
          throw new InvalidDataException(
            "Embedded snapshot resource name is duplicate: " + fileName);
        resources.Add(fileName, ReadResourceBytes(assembly, resourceName));
      }

      if (!resources.TryGetValue(ManifestName, out byte[] manifestBytes))
        throw new InvalidDataException(
          "Embedded snapshot manifest is missing: " + ManifestName);
      resources.Remove(ManifestName);
      Verify(manifestBytes, resources);
    }

    internal static void Verify(
      byte[] manifestBytes,
      IReadOnlyDictionary<string, byte[]> snapshotBytes)
    {
      if (manifestBytes == null)
        throw new ArgumentNullException(nameof(manifestBytes));
      if (snapshotBytes == null)
        throw new ArgumentNullException(nameof(snapshotBytes));

      ManifestDocument manifest = DeserializeManifest(manifestBytes);
      AssertMetadata(
        "schemaVersion",
        ExpectedSchemaVersion,
        manifest.schemaVersion);
      AssertMetadata("algorithm", ExpectedAlgorithm, manifest.algorithm);
      AssertMetadata(
        "frozenFromCommit",
        ExpectedFrozenCommit,
        manifest.frozenFromCommit);
      if (manifest.files == null)
        throw new InvalidDataException("Snapshot manifest files is null.");

      var entries = new Dictionary<string, ManifestFile>(StringComparer.Ordinal);
      foreach (ManifestFile entry in manifest.files)
      {
        if (entry == null || string.IsNullOrWhiteSpace(entry.name))
          throw new InvalidDataException(
            "Snapshot manifest contains an incomplete file entry.");
        if (entries.ContainsKey(entry.name))
          throw new InvalidDataException(
            "Snapshot manifest file name is duplicate: " + entry.name);
        entries.Add(entry.name, entry);
      }

      AssertExactNameSet("Snapshot manifest entries", entries.Keys);
      AssertExactNameSet("Embedded snapshot resources", snapshotBytes.Keys);
      foreach (string name in ExpectedSnapshotNames)
      {
        byte[] bytes = snapshotBytes[name];
        if (bytes == null)
          throw new InvalidDataException(
            "Embedded snapshot bytes are null: " + name);
        ManifestFile entry = entries[name];
        if (entry.length != bytes.LongLength)
          throw new InvalidDataException(
            "Snapshot byte length mismatch for "
            + name
            + ": manifest="
            + entry.length
            + ", actual="
            + bytes.LongLength);
        string actualHash = ComputeSha256(bytes);
        if (!string.Equals(entry.sha256, actualHash, StringComparison.Ordinal))
          throw new InvalidDataException(
            "Snapshot SHA-256 mismatch for "
            + name
            + ": manifest="
            + (entry.sha256 ?? "<null>")
            + ", actual="
            + actualHash);
      }
    }

    private static ManifestDocument DeserializeManifest(byte[] bytes)
    {
      try
      {
        string json = new UTF8Encoding(false, true).GetString(bytes);
        ManifestDocument manifest = new JavaScriptSerializer
        {
          MaxJsonLength = int.MaxValue,
          RecursionLimit = 64
        }.Deserialize<ManifestDocument>(json);
        return manifest ?? throw new InvalidDataException(
          "Snapshot manifest deserialized to null.");
      }
      catch (InvalidDataException)
      {
        throw;
      }
      catch (Exception exception)
      {
        throw new InvalidDataException(
          "Snapshot manifest is not valid UTF-8 JSON.",
          exception);
      }
    }

    private static void AssertMetadata(
      string field,
      string expected,
      string actual)
    {
      if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidDataException(
          "Snapshot manifest "
          + field
          + " mismatch: expected="
          + expected
          + ", actual="
          + (actual ?? "<null>"));
    }

    private static void AssertExactNameSet(
      string label,
      IEnumerable<string> actualNames)
    {
      var actual = new HashSet<string>(
        actualNames ?? Enumerable.Empty<string>(),
        StringComparer.Ordinal);
      string[] missing = ExpectedSnapshotNames
        .Where(name => !actual.Contains(name))
        .ToArray();
      string[] extra = actual
        .Where(name => !ExpectedSnapshotNames.Contains(
          name,
          StringComparer.Ordinal))
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
      if (missing.Length == 0
        && extra.Length == 0
        && actual.Count == ExpectedSnapshotNames.Length)
        return;
      throw new InvalidDataException(
        label
        + " must contain exactly seven behavior snapshots; missing="
        + FormatNames(missing)
        + "; extra="
        + FormatNames(extra));
    }

    private static string FormatNames(IEnumerable<string> names)
    {
      string[] values = (names ?? Enumerable.Empty<string>()).ToArray();
      return values.Length == 0 ? "<none>" : string.Join(",", values);
    }

    private static byte[] ReadResourceBytes(
      Assembly assembly,
      string resourceName)
    {
      using (Stream stream = assembly.GetManifestResourceStream(resourceName))
      {
        if (stream == null)
          throw new InvalidDataException(
            "Embedded snapshot resource cannot be opened: " + resourceName);
        using (var memory = new MemoryStream())
        {
          stream.CopyTo(memory);
          return memory.ToArray();
        }
      }
    }

    private static string ComputeSha256(byte[] bytes)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        return BitConverter.ToString(algorithm.ComputeHash(bytes))
          .Replace("-", string.Empty)
          .ToLowerInvariant();
      }
    }

    private sealed class ManifestDocument
    {
      public string schemaVersion { get; set; }
      public string frozenFromCommit { get; set; }
      public string algorithm { get; set; }
      public ManifestFile[] files { get; set; }
    }

    private sealed class ManifestFile
    {
      public string name { get; set; }
      public long length { get; set; }
      public string sha256 { get; set; }
    }
  }
}
