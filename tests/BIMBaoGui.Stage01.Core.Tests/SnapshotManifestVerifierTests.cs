using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class SnapshotManifestVerifierTests
  {
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

    [Fact]
    public void Embedded_manifest_strictly_matches_all_snapshot_bytes()
    {
      SnapshotManifestVerifier.VerifyEmbeddedSnapshots(
        typeof(SnapshotManifestVerifierTests).Assembly);
    }

    [Fact]
    public void Manifest_rejects_tampered_snapshot_bytes()
    {
      SnapshotFixture fixture = ReadFixture();
      var tampered = CloneSnapshots(fixture.Snapshots);
      string name = ExpectedSnapshotNames[0];
      tampered[name][0] ^= 0x01;

      InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
        SnapshotManifestVerifier.Verify(fixture.ManifestBytes, tampered));

      Assert.Contains(name, error.Message);
      Assert.Contains("SHA-256", error.Message);
    }

    [Fact]
    public void Manifest_rejects_a_missing_entry()
    {
      SnapshotFixture fixture = ReadFixture();
      ManifestDocument manifest = DeserializeManifest(fixture.ManifestBytes);
      string missing = manifest.files[0].name;
      manifest.files = manifest.files.Skip(1).ToArray();

      InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
        SnapshotManifestVerifier.Verify(
          SerializeManifest(manifest),
          fixture.Snapshots));

      Assert.Contains(missing, error.Message);
      Assert.Contains("missing", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Manifest_rejects_an_extra_entry()
    {
      SnapshotFixture fixture = ReadFixture();
      ManifestDocument manifest = DeserializeManifest(fixture.ManifestBytes);
      const string extra = "unexpected-snapshot.v1.json";
      manifest.files = manifest.files.Concat(new[]
      {
        new ManifestFile
        {
          name = extra,
          length = 0,
          sha256 = new string('0', 64)
        }
      }).ToArray();

      InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
        SnapshotManifestVerifier.Verify(
          SerializeManifest(manifest),
          fixture.Snapshots));

      Assert.Contains(extra, error.Message);
      Assert.Contains("extra", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Manifest_rejects_a_duplicate_entry_name()
    {
      SnapshotFixture fixture = ReadFixture();
      ManifestDocument manifest = DeserializeManifest(fixture.ManifestBytes);
      ManifestFile duplicate = manifest.files[0];
      manifest.files = manifest.files.Concat(new[] { duplicate }).ToArray();

      InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
        SnapshotManifestVerifier.Verify(
          SerializeManifest(manifest),
          fixture.Snapshots));

      Assert.Contains(duplicate.name, error.Message);
      Assert.Contains("duplicate", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void Manifest_rejects_an_extra_snapshot_resource()
    {
      SnapshotFixture fixture = ReadFixture();
      var snapshots = CloneSnapshots(fixture.Snapshots);
      const string extra = "unexpected-snapshot.v1.json";
      snapshots.Add(extra, Array.Empty<byte>());

      InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
        SnapshotManifestVerifier.Verify(fixture.ManifestBytes, snapshots));

      Assert.Contains(extra, error.Message);
      Assert.Contains("extra", error.Message.ToLowerInvariant());
    }

    [Theory]
    [InlineData("schemaVersion", "2.0")]
    [InlineData("algorithm", "MD5")]
    [InlineData("frozenFromCommit", "0000000000000000000000000000000000000000")]
    public void Manifest_rejects_unsupported_metadata(
      string field,
      string invalidValue)
    {
      SnapshotFixture fixture = ReadFixture();
      ManifestDocument manifest = DeserializeManifest(fixture.ManifestBytes);
      switch (field)
      {
        case "schemaVersion":
          manifest.schemaVersion = invalidValue;
          break;
        case "algorithm":
          manifest.algorithm = invalidValue;
          break;
        case "frozenFromCommit":
          manifest.frozenFromCommit = invalidValue;
          break;
        default:
          throw new InvalidOperationException("Unknown manifest field: " + field);
      }

      InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
        SnapshotManifestVerifier.Verify(
          SerializeManifest(manifest),
          fixture.Snapshots));

      Assert.Contains(field, error.Message);
    }

    private static SnapshotFixture ReadFixture()
    {
      Assembly assembly = typeof(SnapshotManifestVerifierTests).Assembly;
      byte[] manifestBytes = ReadResource(assembly, "manifest.sha256.v1.json");
      var snapshots = new Dictionary<string, byte[]>(StringComparer.Ordinal);
      foreach (string name in ExpectedSnapshotNames)
        snapshots.Add(name, ReadResource(assembly, name));
      return new SnapshotFixture(manifestBytes, snapshots);
    }

    private static byte[] ReadResource(Assembly assembly, string fileName)
    {
      string resourceName = assembly.GetManifestResourceNames().Single(name =>
        name.EndsWith("Snapshots." + fileName, StringComparison.Ordinal));
      using (Stream stream = assembly.GetManifestResourceStream(resourceName))
      using (var memory = new MemoryStream())
      {
        stream.CopyTo(memory);
        return memory.ToArray();
      }
    }

    private static Dictionary<string, byte[]> CloneSnapshots(
      IReadOnlyDictionary<string, byte[]> snapshots)
    {
      return snapshots.ToDictionary(
        pair => pair.Key,
        pair => (byte[])pair.Value.Clone(),
        StringComparer.Ordinal);
    }

    private static ManifestDocument DeserializeManifest(byte[] bytes)
    {
      return new JavaScriptSerializer().Deserialize<ManifestDocument>(
        new UTF8Encoding(false, true).GetString(bytes));
    }

    private static byte[] SerializeManifest(ManifestDocument manifest)
    {
      return new UTF8Encoding(false).GetBytes(
        new JavaScriptSerializer().Serialize(manifest));
    }

    private sealed class SnapshotFixture
    {
      internal SnapshotFixture(
        byte[] manifestBytes,
        IReadOnlyDictionary<string, byte[]> snapshots)
      {
        ManifestBytes = manifestBytes;
        Snapshots = snapshots;
      }

      internal byte[] ManifestBytes { get; }
      internal IReadOnlyDictionary<string, byte[]> Snapshots { get; }
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
