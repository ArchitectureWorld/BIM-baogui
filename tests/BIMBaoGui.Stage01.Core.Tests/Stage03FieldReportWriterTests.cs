using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Diagnostics;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03FieldReportWriterTests
  {
    private const string ProductionAssemblyFixtureName =
      "BIMBaoGui.Stage01.gha";
    private static readonly Lazy<TrustedAssemblyIdentity>
      LazyTrustedAssemblyIdentity =
        new Lazy<TrustedAssemblyIdentity>(LoadTrustedAssemblyIdentity);

    [Fact]
    public void Atomic_writer_rejects_existing_target_without_overwrite()
    {
      string directory = NewDirectory();
      try
      {
        string target = Path.Combine(directory, "fields.json");
        File.WriteAllText(target, "original", new UTF8Encoding(false));

        IOException collision = Assert.Throws<IOException>(() =>
          AtomicJsonReportWriter.Write(
            target,
            Encoding.UTF8.GetBytes("{\"new\":true}")));

        Assert.True(
          AtomicJsonReportWriter.IsCreateNewCollision(collision),
          "Unexpected target collision HResult: 0x"
            + collision.HResult.ToString("X8"));
        Assert.Equal("original", File.ReadAllText(target, Encoding.UTF8));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Atomic_writer_classifies_existing_directory_as_name_collision()
    {
      string directory = NewDirectory();
      try
      {
        string target = Path.Combine(directory, "fields.json");
        Directory.CreateDirectory(target);

        IOException collision = Assert.Throws<IOException>(() =>
          AtomicJsonReportWriter.Write(
            target,
            Encoding.UTF8.GetBytes("{\"new\":true}")));

        Assert.True(
          AtomicJsonReportWriter.IsCreateNewCollision(collision),
          "Unexpected directory collision HResult: 0x"
            + collision.HResult.ToString("X8"));
        Assert.True(Directory.Exists(target));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Atomic_writer_uses_short_target_independent_temp_name_at_net48_max_path_boundary()
    {
      string root = NewDirectory();
      try
      {
        const string reportName = "x.json";
        const int targetPathLength = 259;
        int directoryLength = targetPathLength - 1 - reportName.Length;
        int segmentLength = directoryLength - root.Length - 1;
        Assert.InRange(segmentLength, 1, 240);
        string directory = Path.Combine(root, new string('d', segmentLength));
        Directory.CreateDirectory(@"\\?\" + directory);
        Assert.Equal(directoryLength, directory.Length);
        string target = Path.Combine(directory, reportName);
        Assert.Equal(targetPathLength, target.Length);
        string observedTemporaryPath = null;

        WriteWithPublisherSeam(
          target,
          Encoding.UTF8.GetBytes("{\"boundary\":true}"),
          (temporaryPath, finalPath) =>
          {
            observedTemporaryPath = temporaryPath;
            Assert.Equal(
              directory,
              Path.GetDirectoryName(temporaryPath));
            Assert.True(
              Path.GetFileName(temporaryPath).Length <= reportName.Length);
            File.Move(temporaryPath, finalPath);
          });

        Assert.NotNull(observedTemporaryPath);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(observedTemporaryPath));
        Assert.Equal(new[] { target }, Directory.GetFiles(directory));
      }
      finally
      {
        Directory.Delete(root, true);
      }
    }

    [Fact]
    public void Atomic_writer_rejects_non_json_target()
    {
      string directory = NewDirectory();
      try
      {
        string target = Path.Combine(directory, "report.txt");

        Assert.Throws<ArgumentException>(() => AtomicJsonReportWriter.Write(
          target,
          Encoding.UTF8.GetBytes("{\"invalidExtension\":true}")));

        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(directory));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Atomic_writer_rejects_json_name_without_stem()
    {
      string directory = NewDirectory();
      try
      {
        string target = Path.Combine(directory, ".json");

        Assert.Throws<ArgumentException>(() => AtomicJsonReportWriter.Write(
          target,
          Encoding.UTF8.GetBytes("{\"missingStem\":true}")));

        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(directory));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Atomic_writer_rejects_relative_json_target()
    {
      string relativeTarget =
        "atomic-relative-" + Guid.NewGuid().ToString("N") + ".json";
      string fullTarget = Path.GetFullPath(relativeTarget);
      try
      {
        Assert.Throws<ArgumentException>(() => AtomicJsonReportWriter.Write(
          relativeTarget,
          Encoding.UTF8.GetBytes("{\"relative\":true}")));

        Assert.False(File.Exists(fullTarget));
      }
      finally
      {
        if (File.Exists(fullTarget)) File.Delete(fullTarget);
      }
    }

    [Fact]
    public void Atomic_writer_skips_directory_collisions_and_uses_next_candidate()
    {
      const string occupiedCandidates = "0123456789abcdef";
      string directory = NewDirectory();
      try
      {
        foreach (char candidate in occupiedCandidates)
        {
          Directory.CreateDirectory(Path.Combine(
            directory,
            candidate + ".tmp"));
        }
        string target = Path.Combine(directory, "x.json");
        string observedTemporaryPath = null;

        WriteWithPublisherSeam(
          target,
          Encoding.UTF8.GetBytes("{\"collision\":true}"),
          (temporaryPath, finalPath) =>
          {
            observedTemporaryPath = temporaryPath;
            Assert.Equal("g.tmp", Path.GetFileName(temporaryPath));
            File.Move(temporaryPath, finalPath);
          });

        Assert.NotNull(observedTemporaryPath);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(observedTemporaryPath));
        Assert.Equal(occupiedCandidates.Length, Directory.GetDirectories(directory).Length);
        Assert.Equal(new[] { target }, Directory.GetFiles(directory));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Atomic_writer_classifies_create_new_collision_after_occupant_moves()
    {
      string directory = NewDirectory();
      try
      {
        string occupiedPath = Path.Combine(directory, "0.tmp");
        string movedPath = Path.Combine(directory, "moved-away.bin");
        string target = Path.Combine(directory, "x.json");
        File.WriteAllText(occupiedPath, "occupied", new UTF8Encoding(false));
        IOException observedCollision = null;
        int writerThreadId = Environment.CurrentManagedThreadId;
        EventHandler<FirstChanceExceptionEventArgs> handler = (sender, eventArgs) =>
        {
          if (Environment.CurrentManagedThreadId != writerThreadId) return;
          var candidate = eventArgs.Exception as IOException;
          if (candidate == null || observedCollision != null) return;
          int win32Error = candidate.HResult & 0xFFFF;
          if ((win32Error != 80 && win32Error != 183)
            || !File.Exists(occupiedPath))
          {
            return;
          }
          observedCollision = candidate;
          File.Move(occupiedPath, movedPath);
        };

        Exception failure;
        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
          failure = Record.Exception(() => AtomicJsonReportWriter.Write(
            target,
            Encoding.UTF8.GetBytes("{\"race\":true}")));
        }
        finally
        {
          AppDomain.CurrentDomain.FirstChanceException -= handler;
        }

        Assert.NotNull(observedCollision);
        Assert.Contains(observedCollision.HResult & 0xFFFF, new[] { 80, 183 });
        Assert.False(File.Exists(occupiedPath));
        Assert.True(File.Exists(movedPath));
        Assert.Null(failure);
        Assert.True(File.Exists(target));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Atomic_writer_collision_classifier_requires_win32_facility()
    {
      var wrongFacility = new IOException(
        "synthetic non-WIN32 facility",
        unchecked((int)0x80130050));
      Assert.Equal(80, wrongFacility.HResult & 0xFFFF);
      MethodInfo classifier = typeof(AtomicJsonReportWriter).GetMethod(
        "IsCreateNewCollision",
        BindingFlags.Static | BindingFlags.NonPublic);
      Assert.NotNull(classifier);

      bool classifiedAsCollision = (bool)classifier.Invoke(
        null,
        new object[] { wrongFacility });

      Assert.False(classifiedAsCollision);
    }

    [Fact]
    public void Atomic_writer_collision_classifier_rejects_real_non_collision_ioexception()
    {
      string directory = NewDirectory();
      try
      {
        string missingCandidate = Path.Combine(
          directory,
          "missing",
          "0.tmp");
        IOException nonCollision = Assert.ThrowsAny<IOException>(() =>
        {
          using (new FileStream(
            missingCandidate,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
          {
          }
        });
        Assert.IsType<DirectoryNotFoundException>(nonCollision);
        int win32Error = nonCollision.HResult & 0xFFFF;
        Assert.DoesNotContain(win32Error, new[] { 80, 183 });
        MethodInfo classifier = typeof(AtomicJsonReportWriter).GetMethod(
          "IsCreateNewCollision",
          BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(classifier);

        bool classifiedAsCollision = (bool)classifier.Invoke(
          null,
          new object[] { nonCollision });

        Assert.False(classifiedAsCollision);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Atomic_writer_reports_uniform_failure_when_all_candidates_are_occupied()
    {
      const string candidates = "0123456789abcdefghijklmnopqrstuvwxyz";
      string directory = NewDirectory();
      try
      {
        for (int index = 0; index < candidates.Length; index++)
        {
          string collision = Path.Combine(
            directory,
            candidates[index] + ".tmp");
          if (index % 2 == 0)
            Directory.CreateDirectory(collision);
          else
            File.WriteAllText(collision, "occupied", new UTF8Encoding(false));
        }
        string target = Path.Combine(directory, "x.json");

        IOException failure = Assert.Throws<IOException>(() =>
          AtomicJsonReportWriter.Write(
            target,
            Encoding.UTF8.GetBytes("{\"exhausted\":true}")));

        Assert.Equal("无法分配唯一的同目录 JSON 临时文件。", failure.Message);
        Assert.False(File.Exists(target));
        Assert.Equal(candidates.Length / 2, Directory.GetDirectories(directory).Length);
        Assert.Equal(candidates.Length / 2, Directory.GetFiles(directory).Length);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public async Task Concurrent_atomic_writes_to_one_target_allow_exactly_one_publisher()
    {
      string directory = NewDirectory();
      try
      {
        string target = Path.Combine(directory, "fields.json");
        byte[] payload = Encoding.UTF8.GetBytes("{\"ok\":true}");
        bool[] succeeded = await Task.WhenAll(
          Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
          {
            try
            {
              AtomicJsonReportWriter.Write(target, payload);
              return true;
            }
            catch (IOException)
            {
              return false;
            }
          })));

        Assert.Equal(1, succeeded.Count(value => value));
        Assert.Equal(
          "{\"ok\":true}",
          File.ReadAllText(target, new UTF8Encoding(false, true)));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Atomic_writer_cleans_temporary_file_when_publication_fails()
    {
      string directory = NewDirectory();
      try
      {
        string target = Path.Combine(directory, "publish-fails.json");
        byte[] payload = Encoding.UTF8.GetBytes("{\"ok\":true}");
        bool publicationAttempted = false;

        Exception failure = Record.Exception(() => WriteWithPublisherSeam(
          target,
          payload,
          (temporaryPath, finalPath) =>
          {
            publicationAttempted = true;
            Assert.True(File.Exists(temporaryPath));
            Assert.Equal(payload, File.ReadAllBytes(temporaryPath));
            Assert.Equal(target, finalPath);
            throw new IOException("injected move failure");
          }));

        Assert.NotNull(failure);
        Assert.True(publicationAttempted);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Internal_trusted_json_write_bypasses_full_parse_and_preserves_bytes()
    {
      string directory = NewDirectory();
      try
      {
        string json = new string('[', 300) + "0" + new string(']', 300);
        byte[] payload = new UTF8Encoding(false, true).GetBytes(json);
        string publicTarget = Path.Combine(directory, "public.json");
        string trustedTarget = Path.Combine(directory, "trusted.json");
        Assert.Throws<ArgumentException>(() =>
          AtomicJsonReportWriter.Write(publicTarget, payload));

        Exception trustedFailure = Record.Exception(() =>
          WriteTrustedJson(trustedTarget, payload));

        Assert.Null(trustedFailure);
        Assert.Equal(payload, File.ReadAllBytes(trustedTarget));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_publishes_utf8_without_bom_and_records_required_identity()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "模型",
          "run-001");
        Stage03FieldReportWriteResult result = Stage03FieldReportWriter.Write(
          MinimalContext(paths));

        byte[] bytes = File.ReadAllBytes(result.ReportPath);
        Assert.False(bytes.Length >= 3
          && bytes[0] == 0xEF
          && bytes[1] == 0xBB
          && bytes[2] == 0xBF);
        string json = new UTF8Encoding(false, true).GetString(bytes);
        Assert.Contains("\"schemaVersion\": \"1.0\"", json);
        Assert.Contains("\"runId\": \"run-001\"", json);
        Assert.Contains("document-fingerprint", json);
        Assert.Contains("file-context-hash", json);
        Assert.Contains("rule-package-sha", json);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_preserves_force_reason_business_blocker_code()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-force-reason");
        Stage03FieldReportContext context = MinimalContext(paths);
        context.GateDecision = Stage03ExportGatePolicy.Decide(
          Stage03GateMode.Force,
          " \t\r\n ",
          new[]
          {
            new Stage03FieldResult
            {
              PropertyId = "HBR.PASS",
              Active = true,
              Status = Stage03FieldStatus.Pass
            }
          },
          Array.Empty<string>());

        Stage03FieldReportWriter.Write(context);

        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(paths.FieldReport, Encoding.UTF8)));
        var gate = Assert.IsType<Dictionary<string, object>>(root["gate"]);
        var blocker = Assert.IsType<Dictionary<string, object>>(
          Assert.Single(Assert.IsType<object[]>(gate["businessBlockers"])));
        Assert.Equal("FORCE_REASON_REQUIRED", blocker["status"]);
        Assert.Equal(
          "Force 模式必须提供非空强制原因。",
          blocker["message"]);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_reports_exactly_two_required_serialization_passes()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-serialization-passes");
        var passes = new List<string>();

        WriteFieldReportWithObserver(
          MinimalContext(paths),
          passes.Add);

        Assert.Equal(new[] { "HASH_INPUT", "PUBLISHED" }, passes);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_rejects_run_id_whitespace_instead_of_normalizing()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-exact");
        Stage03FieldReportContext context = MinimalContext(paths);
        context.RunId = " run-exact ";

        Assert.Throws<ArgumentException>(() =>
          Stage03FieldReportWriter.Write(context));
        Assert.False(File.Exists(paths.FieldReport));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_is_byte_deterministic_and_preserves_every_unordered_entry()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-002");
        Stage03FieldReportContext forward = FullContext(paths, false);
        Stage03FieldReportContext reverse = FullContext(paths, true);

        Stage03FieldReportWriter.Write(forward);
        byte[] first = File.ReadAllBytes(paths.FieldReport);
        File.Delete(paths.FieldReport);
        Stage03FieldReportWriter.Write(reverse);
        byte[] second = File.ReadAllBytes(paths.FieldReport);

        Assert.Equal(first, second);
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            new UTF8Encoding(false, true).GetString(second)));
        object[] fields = Assert.IsType<object[]>(root["fields"]);
        Assert.Equal(4, fields.Length);
        Assert.Equal(
          new[]
          {
            "IfcBuilding|owner-a|property-z|raw-a",
            "IfcWall|owner-a|property-a|raw-a",
            "IfcWall|owner-a|property-a|raw-b",
            "IfcWall|owner-z|property-a|raw-z"
          },
          fields.Cast<Dictionary<string, object>>()
            .Select(item => string.Join("|", new[]
            {
              (string)item["entity"],
              (string)item["ownerUniqueId"],
              (string)item["propertyId"],
              (string)item["revitRawValue"]
            }))
            .ToArray());
        Assert.Equal(
          new[] { "IfcBuilding|owner-a", "IfcWall|owner-z" },
          Assert.IsType<object[]>(root["carriers"])
            .Cast<Dictionary<string, object>>()
            .Select(item => (string)item["entity"]
              + "|" + (string)item["uniqueId"])
            .ToArray());
        Assert.Equal(
          new[] { "A_CODE|CARRIER_SCAN", "Z_CODE|FINAL_IFC" },
          Assert.IsType<object[]>(root["diagnostics"])
            .Cast<Dictionary<string, object>>()
            .Select(item => (string)item["code"]
              + "|" + (string)item["stage"])
            .ToArray());

        var summary = Assert.IsType<Dictionary<string, object>>(root["summary"]);
        Assert.Equal(4, Assert.IsType<int>(summary["totalFields"]));
        Assert.Equal(
          2,
          Assert.IsType<int>(Assert.IsType<Dictionary<string, object>>(
            summary["byStatus"])["INVALID_VALUE"]));
        Assert.Equal(
          3,
          Assert.IsType<int>(Assert.IsType<Dictionary<string, object>>(
            summary["byEntity"])["IfcWall"]));
        Assert.Equal(
          3,
          Assert.IsType<int>(Assert.IsType<Dictionary<string, object>>(
            summary["byPropertySet"])["Pset_HBR"]));
        Assert.Equal(
          3,
          Assert.IsType<int>(Assert.IsType<Dictionary<string, object>>(
            summary["byRequirement"])["REQUIRED"]));

        var artifacts = Assert.IsType<Dictionary<string, object>>(
          root["artifacts"]);
        var raw = Assert.IsType<Dictionary<string, object>>(artifacts["rawIfc"]);
        var final = Assert.IsType<Dictionary<string, object>>(
          artifacts["finalIfc"]);
        var report = Assert.IsType<Dictionary<string, object>>(
          artifacts["report"]);
        Assert.Equal(paths.RawIfc, raw["path"]);
        Assert.Equal(new string('a', 64), raw["sha256"]);
        Assert.Equal(paths.FinalIfc, final["path"]);
        Assert.Equal(new string('b', 64), final["sha256"]);
        Assert.Equal(paths.FieldReport, report["path"]);
        Assert.Matches("^[0-9a-f]{64}$", Assert.IsType<string>(report["sha256"]));
        Assert.Equal(
          "REPORT_WITH_EMPTY_SHA256",
          report["sha256Scope"]);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_deep_freezes_mutable_context_dtos_and_messages()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-freeze");
        Stage03FieldReportContext context = MinimalContext(paths);
        Stage03FieldResult field = Field(
          "IfcWall",
          "owner-a",
          "property-a",
          "before-raw",
          Stage03FieldStatus.Pass,
          "REQUIRED");
        bool sourceDriftedAfterFirstEnumeration = false;
        DriftingReadOnlyList<string> messages = null;
        messages = new DriftingReadOnlyList<string>(
          new[] { "before-message" },
          enumeration =>
          {
            if (enumeration != 1) return;
            sourceDriftedAfterFirstEnumeration = true;
            context.DocumentTitle = "drift-document";
            field.RevitRawValue = "drift-raw";
            messages.Replace("drift-message");
          });
        field.Messages = messages;
        context.Fields = new[] { field };

        Stage03FieldReportWriter.Write(context);

        Assert.True(sourceDriftedAfterFirstEnumeration);
        string json = File.ReadAllText(paths.FieldReport, Encoding.UTF8);
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(json));
        var artifacts = Assert.IsType<Dictionary<string, object>>(
          root["artifacts"]);
        var report = Assert.IsType<Dictionary<string, object>>(
          artifacts["report"]);
        string embeddedSha = Assert.IsType<string>(report["sha256"]);
        string needle = "\"sha256\": \"" + embeddedSha + "\"";
        Assert.Contains(needle, json, StringComparison.Ordinal);
        string hashInput = json.Replace(
          needle,
          "\"sha256\": \"\"");

        Assert.Equal(embeddedSha, Sha256(hashInput));
        Assert.DoesNotContain("drift-", json, StringComparison.Ordinal);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_carrier_sort_ties_include_all_serialized_flags()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-carrier-sort");
        var inactiveBlocker = new Stage03CarrierResult
        {
          Entity = "IfcWall",
          Role = "wall",
          ElementId = 10,
          UniqueId = "same-owner",
          Category = "Walls",
          Name = "Same Wall",
          Status = Stage03FieldStatus.Pass,
          Active = false,
          IsBusinessBlocker = true,
          Messages = new[] { "same-message" }
        };
        var activeNonBlocker = new Stage03CarrierResult
        {
          Entity = "IfcWall",
          Role = "wall",
          ElementId = 10,
          UniqueId = "same-owner",
          Category = "Walls",
          Name = "Same Wall",
          Status = Stage03FieldStatus.Pass,
          Active = true,
          IsBusinessBlocker = false,
          Messages = new[] { "same-message" }
        };
        Stage03FieldReportContext forward = MinimalContext(paths);
        forward.Carriers = new[] { inactiveBlocker, activeNonBlocker };
        Stage03FieldReportContext reverse = MinimalContext(paths);
        reverse.Carriers = new[] { activeNonBlocker, inactiveBlocker };

        Stage03FieldReportWriter.Write(forward);
        byte[] first = File.ReadAllBytes(paths.FieldReport);
        File.Delete(paths.FieldReport);
        Stage03FieldReportWriter.Write(reverse);
        byte[] second = File.ReadAllBytes(paths.FieldReport);

        Assert.Equal(first, second);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_field_sort_ties_include_all_statuses_and_flags()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-field-sort");
        var fields = new List<Stage03FieldResult>();
        AddFieldTiePair(fields, "carrier-status", item =>
          item.CarrierStatus = Stage03FieldStatus.MissingCarrier);
        AddFieldTiePair(fields, "parameter-status", item =>
          item.ParameterStatus = Stage03FieldStatus.MissingParameter);
        AddFieldTiePair(fields, "revit-status", item =>
          item.RevitStatus = Stage03FieldStatus.InvalidValue);
        AddFieldTiePair(fields, "raw-ifc-status", item =>
          item.RawIfcStatus = Stage03FieldStatus.IfcOwnerNotFound);
        AddFieldTiePair(fields, "final-ifc-status", item =>
          item.FinalIfcStatus = Stage03FieldStatus.IfcValueMismatch);
        AddFieldTiePair(fields, "active", item => item.Active = false);
        AddFieldTiePair(fields, "business-blocker", item =>
          item.IsBusinessBlocker = true);
        Stage03FieldReportContext forward = MinimalContext(paths);
        forward.Fields = fields.ToArray();
        Stage03FieldReportContext reverse = MinimalContext(paths);
        reverse.Fields = fields.AsEnumerable().Reverse().ToArray();

        Stage03FieldReportWriter.Write(forward);
        byte[] first = File.ReadAllBytes(paths.FieldReport);
        File.Delete(paths.FieldReport);
        Stage03FieldReportWriter.Write(reverse);
        byte[] second = File.ReadAllBytes(paths.FieldReport);

        Assert.Equal(first, second);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_structural_comparer_short_circuits_without_full_record_key_allocation()
    {
      Stage03FieldResult left = TieField("same-property");
      Stage03FieldResult right = TieField("same-property");
      left.ContractKind = "A";
      right.ContractKind = "B";
      left.FinalIfcValue = new string('L', 1024 * 1024);
      right.FinalIfcValue = new string('R', 1024 * 1024);
      IComparer<Stage03FieldResult> comparer =
        FieldStructuralComparerWithReflection();
      MethodInfo allocationMethod = typeof(GC).GetMethod(
        "GetAllocatedBytesForCurrentThread",
        BindingFlags.Public | BindingFlags.Static);
      Assert.NotNull(allocationMethod);
      var allocatedBytes = (Func<long>)Delegate.CreateDelegate(
        typeof(Func<long>),
        allocationMethod);
      comparer.Compare(left, right);

      long before = allocatedBytes();
      int comparison = 0;
      for (int index = 0; index < 100; index++)
        comparison = comparer.Compare(left, right);
      long allocated = allocatedBytes() - before;

      Assert.True(comparison < 0);
      Assert.InRange(allocated, 0, 32 * 1024);
    }

    [Fact]
    public void Carrier_structural_comparer_short_circuits_before_1mib_message_tail()
    {
      var left = new Stage03CarrierResult
      {
        Entity = "A",
        Messages = new[] { new string('L', 1024 * 1024) }
      };
      var right = new Stage03CarrierResult
      {
        Entity = "B",
        Messages = new[] { new string('R', 1024 * 1024) }
      };
      IComparer<Stage03CarrierResult> comparer =
        CarrierStructuralComparerWithReflection();
      Func<long> allocatedBytes = AllocatedBytesForCurrentThread();
      comparer.Compare(left, right);

      long before = allocatedBytes();
      int comparison = 0;
      for (int index = 0; index < 100; index++)
        comparison = comparer.Compare(left, right);
      long allocated = allocatedBytes() - before;

      Assert.True(comparison < 0);
      Assert.InRange(allocated, 0, 32 * 1024);
    }

    [Fact]
    public void Diagnostic_structural_comparer_short_circuits_before_1mib_message_tail()
    {
      var left = new Stage03Diagnostic
      {
        Code = "A",
        Message = new string('L', 1024 * 1024)
      };
      var right = new Stage03Diagnostic
      {
        Code = "B",
        Message = new string('R', 1024 * 1024)
      };
      IComparer<Stage03Diagnostic> comparer =
        DiagnosticStructuralComparerWithReflection();
      Func<long> allocatedBytes = AllocatedBytesForCurrentThread();
      comparer.Compare(left, right);

      long before = allocatedBytes();
      int comparison = 0;
      for (int index = 0; index < 100; index++)
        comparison = comparer.Compare(left, right);
      long allocated = allocatedBytes() - before;

      Assert.True(comparison < 0);
      Assert.InRange(allocated, 0, 32 * 1024);
    }

    [Fact]
    public void Field_writer_sort_keys_cover_serialized_whitespace_differences()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-whitespace-sort");
        var carrierPlain = new Stage03CarrierResult
        {
          Entity = "IfcWall",
          Role = "wall",
          ElementId = 10,
          UniqueId = "same-owner",
          Category = "Walls",
          Name = "same-name",
          Status = Stage03FieldStatus.Pass,
          Active = true,
          Messages = new[] { "same-message" }
        };
        var carrierPadded = new Stage03CarrierResult
        {
          Entity = "IfcWall",
          Role = "wall",
          ElementId = 10,
          UniqueId = "same-owner",
          Category = "Walls",
          Name = " same-name ",
          Status = Stage03FieldStatus.Pass,
          Active = true,
          Messages = new[] { "same-message" }
        };
        Stage03FieldResult fieldPlain = TieField("same-property");
        fieldPlain.RevitRawValue = "same-value";
        Stage03FieldResult fieldPadded = TieField("same-property");
        fieldPadded.RevitRawValue = " same-value ";
        var diagnosticPlain = new Stage03Diagnostic
        {
          Code = "SAME_CODE",
          Stage = "SAME_STAGE",
          Severity = "INFO",
          Message = "same-message"
        };
        var diagnosticPadded = new Stage03Diagnostic
        {
          Code = "SAME_CODE",
          Stage = "SAME_STAGE",
          Severity = "INFO",
          Message = " same-message "
        };
        Stage03FieldReportContext forward = MinimalContext(paths);
        forward.Carriers = new[] { carrierPlain, carrierPadded };
        forward.Fields = new[] { fieldPlain, fieldPadded };
        forward.Diagnostics = new[] { diagnosticPlain, diagnosticPadded };
        Stage03FieldReportContext reverse = MinimalContext(paths);
        reverse.Carriers = new[] { carrierPadded, carrierPlain };
        reverse.Fields = new[] { fieldPadded, fieldPlain };
        reverse.Diagnostics = new[] { diagnosticPadded, diagnosticPlain };

        Stage03FieldReportWriter.Write(forward);
        byte[] first = File.ReadAllBytes(paths.FieldReport);
        File.Delete(paths.FieldReport);
        Stage03FieldReportWriter.Write(reverse);
        byte[] second = File.ReadAllBytes(paths.FieldReport);

        Assert.Equal(first, second);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_field_sort_key_is_unambiguous_across_nul_field_boundaries()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-nul-field");
        Stage03FieldResult first = TieField("same-property");
        first.ContractKind = "a\0b";
        first.Requirement = "c";
        Stage03FieldResult second = TieField("same-property");
        second.ContractKind = "a";
        second.Requirement = "b\0c";
        Stage03FieldReportContext forward = MinimalContext(paths);
        forward.Fields = new[] { first, second };
        Stage03FieldReportContext reverse = MinimalContext(paths);
        reverse.Fields = new[] { second, first };

        AssertDeterministicNulReport(paths, forward, reverse);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_carrier_sort_key_is_unambiguous_across_nul_field_boundaries()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-nul-carrier");
        var first = new Stage03CarrierResult
        {
          Entity = "a\0b",
          Role = "c",
          ElementId = 10,
          UniqueId = "same-owner",
          Category = "same-category",
          Name = "same-name",
          Status = Stage03FieldStatus.Pass,
          Active = true,
          Messages = new[] { "same-message" }
        };
        var second = new Stage03CarrierResult
        {
          Entity = "a",
          Role = "b\0c",
          ElementId = 10,
          UniqueId = "same-owner",
          Category = "same-category",
          Name = "same-name",
          Status = Stage03FieldStatus.Pass,
          Active = true,
          Messages = new[] { "same-message" }
        };
        Stage03FieldReportContext forward = MinimalContext(paths);
        forward.Carriers = new[] { first, second };
        Stage03FieldReportContext reverse = MinimalContext(paths);
        reverse.Carriers = new[] { second, first };

        AssertDeterministicNulReport(paths, forward, reverse);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_diagnostic_sort_key_is_unambiguous_across_nul_field_boundaries()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-nul-diagnostic");
        var first = new Stage03Diagnostic
        {
          Code = "a\0b",
          Stage = "c",
          Severity = "INFO",
          Message = "same-message"
        };
        var second = new Stage03Diagnostic
        {
          Code = "a",
          Stage = "b\0c",
          Severity = "INFO",
          Message = "same-message"
        };
        Stage03FieldReportContext forward = MinimalContext(paths);
        forward.Diagnostics = new[] { first, second };
        Stage03FieldReportContext reverse = MinimalContext(paths);
        reverse.Diagnostics = new[] { second, first };

        AssertDeterministicNulReport(paths, forward, reverse);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_message_sort_key_is_unambiguous_across_nul_array_boundaries()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-nul-messages");
        Stage03FieldResult first = TieField("same-property");
        first.Messages = new[] { "a\0b" };
        Stage03FieldResult second = TieField("same-property");
        second.Messages = new[] { "a", "b" };
        Stage03FieldReportContext forward = MinimalContext(paths);
        forward.Fields = new[] { first, second };
        Stage03FieldReportContext reverse = MinimalContext(paths);
        reverse.Fields = new[] { second, first };

        AssertDeterministicNulReport(paths, forward, reverse);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_records_distinct_raw_and_final_ifc_locations_deterministically()
    {
      string directory = NewDirectory();
      try
      {
        PropertyInfo rawPropertySet = typeof(Stage03FieldResult).GetProperty(
          "RawIfcPropertySet");
        PropertyInfo rawProperty = typeof(Stage03FieldResult).GetProperty(
          "RawIfcProperty");
        PropertyInfo finalPropertySet = typeof(Stage03FieldResult).GetProperty(
          "FinalIfcPropertySet");
        PropertyInfo finalProperty = typeof(Stage03FieldResult).GetProperty(
          "FinalIfcProperty");
        Assert.NotNull(rawPropertySet);
        Assert.NotNull(rawProperty);
        Assert.NotNull(finalPropertySet);
        Assert.NotNull(finalProperty);

        Stage03FieldResult first = Field(
          "IfcWall",
          "same-owner",
          "same-property",
          "same-value",
          Stage03FieldStatus.Pass,
          "REQUIRED");
        rawPropertySet.SetValue(first, "Pset_RAW_A");
        rawProperty.SetValue(first, "RAW_Property_A");
        finalPropertySet.SetValue(first, "Pset_FINAL_A");
        finalProperty.SetValue(first, "FINAL_Property_A");

        Stage03FieldResult second = Field(
          "IfcWall",
          "same-owner",
          "same-property",
          "same-value",
          Stage03FieldStatus.Pass,
          "REQUIRED");
        rawPropertySet.SetValue(second, "Pset_RAW_Z");
        rawProperty.SetValue(second, "RAW_Property_Z");
        finalPropertySet.SetValue(second, "Pset_FINAL_Z");
        finalProperty.SetValue(second, "FINAL_Property_Z");

        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-ifc-locations");
        Stage03FieldReportContext forward = MinimalContext(paths);
        forward.Fields = new[] { first, second };
        Stage03FieldReportContext reverse = MinimalContext(paths);
        reverse.Fields = new[] { second, first };

        Stage03FieldReportWriter.Write(forward);
        byte[] forwardBytes = File.ReadAllBytes(paths.FieldReport);
        File.Delete(paths.FieldReport);
        Stage03FieldReportWriter.Write(reverse);
        byte[] reverseBytes = File.ReadAllBytes(paths.FieldReport);

        Assert.Equal(forwardBytes, reverseBytes);
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            new UTF8Encoding(false, true).GetString(reverseBytes)));
        Dictionary<string, object>[] fields = Assert.IsType<object[]>(
            root["fields"])
          .Cast<Dictionary<string, object>>()
          .ToArray();
        Assert.Equal(2, fields.Length);
        Assert.Equal("Pset_RAW_A", fields[0]["rawIfcPropertySet"]);
        Assert.Equal("RAW_Property_A", fields[0]["rawIfcProperty"]);
        Assert.Equal("Pset_FINAL_A", fields[0]["finalIfcPropertySet"]);
        Assert.Equal("FINAL_Property_A", fields[0]["finalIfcProperty"]);
        Assert.Equal("Pset_RAW_Z", fields[1]["rawIfcPropertySet"]);
        Assert.Equal("RAW_Property_Z", fields[1]["rawIfcProperty"]);
        Assert.Equal("Pset_FINAL_Z", fields[1]["finalIfcPropertySet"]);
        Assert.Equal("FINAL_Property_Z", fields[1]["finalIfcProperty"]);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Field_writer_never_overwrites_an_existing_report()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-003");
        File.WriteAllText(
          paths.FieldReport,
          "existing-report",
          new UTF8Encoding(false));

        Assert.Throws<IOException>(() =>
          Stage03FieldReportWriter.Write(MinimalContext(paths)));

        Assert.Equal(
          "existing-report",
          File.ReadAllText(paths.FieldReport, Encoding.UTF8));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public async Task Concurrent_field_reports_to_one_target_allow_exactly_one_writer()
    {
      string directory = NewDirectory();
      try
      {
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "model",
          "run-004");
        bool[] succeeded = await Task.WhenAll(
          Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
          {
            try
            {
              Stage03FieldReportWriter.Write(MinimalContext(paths));
              return true;
            }
            catch (IOException)
            {
              return false;
            }
          })));

        Assert.Equal(1, succeeded.Count(value => value));
        Assert.True(File.Exists(paths.FieldReport));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_public_entry_resolves_real_production_gha_in_isolated_appdomain()
    {
      string directory = NewDirectory();
      AppDomain isolatedDomain = null;
      try
      {
        string fixture = Path.Combine(
          AppContext.BaseDirectory,
          ProductionAssemblyFixtureName);
        Assert.True(File.Exists(fixture), "缺少固定文件名生产 GHA：" + fixture);
        string ghaPath = Path.Combine(directory, ProductionAssemblyFixtureName);
        File.Copy(fixture, ghaPath);
        var setup = new AppDomainSetup
        {
          ApplicationBase = AppContext.BaseDirectory
        };
        isolatedDomain = AppDomain.CreateDomain(
          "Stage03ProductionResolver-" + Guid.NewGuid().ToString("N"),
          null,
          setup);
        var runner = (PublicFailureWriterRunner)
          isolatedDomain.CreateInstanceFromAndUnwrap(
            typeof(PublicFailureWriterRunner).Assembly.Location,
            typeof(PublicFailureWriterRunner).FullName);

        string[] evidence = runner.Run(ghaPath);

        Assert.Equal("True", evidence[0]);
        Assert.Equal(string.Empty, evidence[2]);
        Assert.Equal(Path.GetFullPath(ghaPath), evidence[3]);
        Assert.Equal("BIMBaoGui.Stage01", evidence[4]);
        Assert.Equal("0.9.0.0", evidence[5]);
        Assert.NotEqual(Guid.Empty, Guid.Parse(evidence[6]));
        Assert.Equal(
          Stage03PortableExecutableMetadataReader.ReadModuleVersionId(ghaPath),
          Guid.Parse(evidence[6]));
        Assert.Equal("8", evidence[7]);
        Assert.True(
          evidence[8] == "0" && evidence[9] == evidence[10],
          "public TryWrite 引发同名程序集驻留增长：events="
          + evidence[8] + ", before=" + evidence[9]
          + ", after=" + evidence[10]);
        Assert.Equal(directory, Path.GetDirectoryName(evidence[1]));
        Assert.StartsWith(
          "BIMBaoGui.Stage03.failure-",
          Path.GetFileName(evidence[1]),
          StringComparison.Ordinal);
        Assert.True(File.Exists(evidence[1]));
        var serializer = new JavaScriptSerializer();
        var report = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(evidence[1], Encoding.UTF8)));
        Assert.Equal("public-resolver-run", report["runId"]);
      }
      finally
      {
        if (isolatedDomain != null) AppDomain.Unload(isolatedDomain);
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_skips_file_and_directory_candidate_collisions_and_writes_third_candidate()
    {
      string directory = NewDirectory();
      try
      {
        var occurredLocal = new DateTimeOffset(
          2026, 8, 5, 9, 10, 11, 123, TimeSpan.FromHours(8));
        var firstGuid = new Guid("11111111-1111-1111-1111-111111111111");
        var secondGuid = new Guid("22222222-2222-2222-2222-222222222222");
        var thirdGuid = new Guid("33333333-3333-3333-3333-333333333333");
        const string prefix = "BIMBaoGui.Stage03.failure-20260805-091011-123-";
        string firstPath = Path.Combine(
          directory,
          prefix + firstGuid.ToString("N") + ".json");
        string secondPath = Path.Combine(
          directory,
          prefix + secondGuid.ToString("N") + ".json");
        string thirdPath = Path.Combine(
          directory,
          prefix + thirdGuid.ToString("N") + ".json");
        File.WriteAllText(firstPath, "occupied-file", new UTF8Encoding(false));
        Directory.CreateDirectory(secondPath);
        byte[] payload = Encoding.UTF8.GetBytes("{\"attempt\":3}\n");
        var candidates = new Queue<Guid>(new[]
        {
          firstGuid,
          secondGuid,
          thirdGuid
        });

        string reportPath = Stage03FailureReportWriter.AllocateAndWrite(
          directory,
          occurredLocal,
          payload,
          () => candidates.Dequeue());

        Assert.Equal(thirdPath, reportPath);
        Assert.Equal(payload, File.ReadAllBytes(thirdPath));
        Assert.Equal(
          "occupied-file",
          File.ReadAllText(firstPath, Encoding.UTF8));
        Assert.True(Directory.Exists(secondPath));
        Assert.Empty(candidates);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_uses_resolved_active_gha_directory_and_omits_exception_secrets()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        Stage03FailureReportWriteResult result =
          Stage03FailureReportWriter.TryWrite(
            new Stage03FailureReportContext
            {
            RunId = "run-001",
            TechnicalCode = "EXPORT_FAILED",
            RootCauseStage = "RAW_IFC_EXPORT",
            PluginVersion = "0.9.0",
            RevitVersion = "2020",
            SafeDiagnosticCodes = new[]
            {
              "Z_SAFE_DIAGNOSTIC",
              "A_SAFE_DIAGNOSTIC",
              "Z_SAFE_DIAGNOSTIC"
            },
            Exception = new InvalidDataException(
              "secret-business-value token=credential-secret"),
            OccurredUtc = new DateTimeOffset(
              2026, 8, 3, 1, 2, 3, TimeSpan.Zero),
              OccurredLocal = new DateTimeOffset(
                2026, 8, 3, 9, 2, 3, TimeSpan.FromHours(8))
            },
            TrustedGhaResolver(ghaPath));

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Assert.Equal(directory, Path.GetDirectoryName(result.ReportPath));
        Assert.StartsWith(
          "BIMBaoGui.Stage03.failure-",
          Path.GetFileName(result.ReportPath),
          StringComparison.Ordinal);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.Contains("EXPORT_FAILED", json);
        Assert.Contains("RAW_IFC_EXPORT", json);
        Assert.Contains(typeof(InvalidDataException).FullName, json);
        Assert.Contains("\"hResult\"", json);
        Assert.DoesNotContain("secret-business-value", json);
        Assert.DoesNotContain("credential-secret", json);
        Assert.DoesNotContain(ghaPath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(directory, json, StringComparison.OrdinalIgnoreCase);
        Assert.True(
          json.IndexOf("A_SAFE_DIAGNOSTIC", StringComparison.Ordinal)
          < json.IndexOf("Z_SAFE_DIAGNOSTIC", StringComparison.Ordinal));
        Assert.Single(Directory.GetFiles(directory, "*.gha"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Assert.DoesNotContain(
          Directory.GetFiles(directory).Select(Path.GetFileName),
          name => name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_rejects_fake_text_disguised_as_active_gha()
    {
      string directory = NewDirectory();
      try
      {
        string fakeGha = Path.Combine(directory, "BIMBaoGui.Stage01.gha");
        File.WriteAllText(fakeGha, "not-a-managed-assembly");

        Stage03FailureReportWriteResult result =
          Stage03FailureReportWriter.TryWrite(
            FailureContext(),
            TrustedGhaResolver(fakeGha));

        Assert.False(result.Success);
        Assert.Equal("REPORT_FAILED", result.ErrorCode);
        Assert.Empty(Directory.GetFiles(directory, "*.json"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Portable_executable_mvid_reader_rejects_truncated_and_fake_pe()
    {
      string directory = NewDirectory();
      try
      {
        string fixture = Path.Combine(
          AppContext.BaseDirectory,
          ProductionAssemblyFixtureName);
        byte[] productionImage = File.ReadAllBytes(fixture);
        string truncated = Path.Combine(directory, "truncated.gha");
        File.WriteAllBytes(truncated, productionImage.Take(128).ToArray());
        string fake = Path.Combine(directory, "fake.gha");
        File.WriteAllBytes(fake, Encoding.ASCII.GetBytes("MZ-fake-pe"));

        Assert.Throws<BadImageFormatException>(() =>
          Stage03PortableExecutableMetadataReader.ReadModuleVersionId(
            truncated));
        Assert.Throws<BadImageFormatException>(() =>
          Stage03PortableExecutableMetadataReader.ReadModuleVersionId(fake));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Maximum_length_run_id_is_shared_by_output_paths_and_failure_report()
    {
      string directory = NewDirectory();
      try
      {
        string runId = new string('R', 128);
        Stage03OutputPaths paths = Stage03OutputPathPolicy.Create(
          directory,
          "m",
          runId);
        string ghaPath = CreateValidGha(directory);
        Stage03FailureReportContext context = FailureContext();
        context.RunId = runId;

        Stage03FailureReportWriteResult result =
          Stage03FailureReportWriter.TryWrite(
            context,
            TrustedGhaResolver(ghaPath));

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Assert.Equal(runId, paths.RunId);
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(result.ReportPath, Encoding.UTF8)));
        Assert.Equal(runId, root["runId"]);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_rejects_noncanonical_or_nonproduction_resolver_paths()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        string wrongName = Path.Combine(directory, "Other.gha");
        File.Copy(ghaPath, wrongName);
        string relativeGhaPath = RelativeToCurrentDirectory(ghaPath);
        string noncanonical = Path.Combine(
          directory,
          ".",
          "BIMBaoGui.Stage01.gha");
        string[] invalidPaths =
        {
          string.Empty,
          directory,
          relativeGhaPath,
          wrongName,
          Path.Combine(directory, "missing", "BIMBaoGui.Stage01.gha"),
          noncanonical
        };
        Stage03FailureReportWriteResult[] results = invalidPaths
          .Select(path => Stage03FailureReportWriter.TryWrite(
            FailureContext(),
            TrustedGhaResolver(path)))
          .ToArray();

        Assert.All(results, result =>
        {
          Assert.False(result.Success);
          Assert.Equal("REPORT_FAILED", result.ErrorCode);
          Assert.True(string.IsNullOrEmpty(result.ReportPath));
        });
        Assert.Empty(Directory.GetFiles(directory, "*.json"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_rejects_old_mvid_and_other_assembly_and_uses_trusted_resolver()
    {
      string trustedDirectory = NewDirectory();
      string otherDirectory = NewDirectory();
      try
      {
        string trustedGha = CreateValidGha(trustedDirectory);
        string otherAssemblyGha = Path.Combine(
          otherDirectory,
          "BIMBaoGui.Stage01.gha");
        File.Copy(typeof(Stage03FieldReportWriterTests).Assembly.Location,
          otherAssemblyGha);

        Stage03FailureReportWriteResult trusted =
          Stage03FailureReportWriter.TryWrite(
            FailureContext(),
            TrustedGhaResolver(trustedGha));
        Stage03FailureReportWriteResult oldMvid =
          Stage03FailureReportWriter.TryWrite(
            FailureContext(),
            TrustedGhaResolver(trustedGha, Guid.NewGuid()));
        Stage03FailureReportWriteResult otherAssembly =
          Stage03FailureReportWriter.TryWrite(
            FailureContext(),
            TrustedGhaResolver(otherAssemblyGha));

        Assert.True(trusted.Success, trusted.ReportWriteErrorSummary);
        Assert.Equal(trustedDirectory, Path.GetDirectoryName(trusted.ReportPath));
        Assert.False(oldMvid.Success);
        Assert.False(otherAssembly.Success);
        Assert.Equal("REPORT_FAILED", oldMvid.ErrorCode);
        Assert.Equal("REPORT_FAILED", otherAssembly.ErrorCode);
        Assert.Empty(Directory.GetFiles(otherDirectory, "*.json"));
      }
      finally
      {
        Directory.Delete(trustedDirectory, true);
        Directory.Delete(otherDirectory, true);
      }
    }

    [Fact]
    public void Failure_writer_redacts_unsafe_identity_metadata()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        string maximumGenericIdentityPlusOne = new string('A', 65);
        string[] unsafeValues =
        {
          "credential-marker",
          "token-marker",
          "secret-marker",
          "password-marker",
          "https://example.test/private",
          @"C:\Users\person\private",
          maximumGenericIdentityPlusOne,
          new string('B', Stage03RunIdPolicy.MaximumLength + 1),
          "sk-proj-abc123",
          "ghp_abc123",
          "github_pat_abc123",
          "AKIA_TEST_MARKER"
        };

        foreach (string unsafeValue in unsafeValues)
        {
          Stage03FailureReportContext context = FailureContext();
          context.RunId = unsafeValue;
          context.PluginVersion = unsafeValue;
          context.RevitVersion = unsafeValue;

          Stage03FailureReportWriteResult result =
            Stage03FailureReportWriter.TryWrite(
              context,
              TrustedGhaResolver(ghaPath));

          Assert.True(result.Success, result.ReportWriteErrorSummary);
          string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
          var serializer = new JavaScriptSerializer();
          var root = Assert.IsType<Dictionary<string, object>>(
            serializer.DeserializeObject(json));
          Assert.Equal(
            unsafeValue == maximumGenericIdentityPlusOne
              ? maximumGenericIdentityPlusOne
              : "REDACTED_RUN_ID",
            root["runId"]);
          Assert.Equal("REDACTED_PLUGIN_VERSION", root["pluginVersion"]);
          Assert.Equal("REDACTED_REVIT_VERSION", root["revitVersion"]);
          if (unsafeValue != maximumGenericIdentityPlusOne)
          {
            Assert.DoesNotContain(
              unsafeValue,
              json,
              StringComparison.OrdinalIgnoreCase);
          }
        }
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_redacts_sensitive_codes_and_preserves_safe_unknown_codes()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        Stage03FailureReportContext unsafeContext = FailureContext();
        unsafeContext.TechnicalCode = "TOKEN_CREDENTIAL_SECRET";
        unsafeContext.RootCauseStage = "PASSWORD_STAGE";
        unsafeContext.SafeDiagnosticCodes = new[]
        {
          "GHP_ABC123",
          "FUTURE_UNKNOWN_DIAGNOSTIC"
        };

        Stage03FailureReportWriteResult unsafeResult =
          Stage03FailureReportWriter.TryWrite(
            unsafeContext,
            TrustedGhaResolver(ghaPath));

        Assert.True(unsafeResult.Success, unsafeResult.ReportWriteErrorSummary);
        var serializer = new JavaScriptSerializer();
        var unsafeRoot = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(unsafeResult.ReportPath, Encoding.UTF8)));
        Assert.Equal("UNKNOWN_TECHNICAL_FATAL", unsafeRoot["technicalCode"]);
        Assert.Equal("UNKNOWN_STAGE", unsafeRoot["rootCauseStage"]);
        string[] diagnostics = Assert.IsType<object[]>(
            unsafeRoot["safeDiagnosticCodes"])
          .Cast<string>()
          .ToArray();
        Assert.Contains("UNSAFE_DIAGNOSTIC_CODE_REDACTED", diagnostics);
        Assert.Contains("FUTURE_UNKNOWN_DIAGNOSTIC", diagnostics);
        string unsafeJson = File.ReadAllText(
          unsafeResult.ReportPath,
          Encoding.UTF8);
        Assert.DoesNotContain(
          "TOKEN_CREDENTIAL_SECRET",
          unsafeJson,
          StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
          "PASSWORD_STAGE",
          unsafeJson,
          StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
          "GHP_ABC123",
          unsafeJson,
          StringComparison.OrdinalIgnoreCase);

        Stage03FailureReportContext safeContext = FailureContext();
        safeContext.TechnicalCode = "FUTURE_TECHNICAL_FATAL";
        safeContext.RootCauseStage = "FUTURE_ROOT_CAUSE_STAGE";
        safeContext.SafeDiagnosticCodes = new[]
        {
          "FUTURE_DIAGNOSTIC_CODE"
        };
        Stage03FailureReportWriteResult safeResult =
          Stage03FailureReportWriter.TryWrite(
            safeContext,
            TrustedGhaResolver(ghaPath));
        Assert.True(safeResult.Success, safeResult.ReportWriteErrorSummary);
        var safeRoot = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(safeResult.ReportPath, Encoding.UTF8)));
        Assert.Equal("FUTURE_TECHNICAL_FATAL", safeRoot["technicalCode"]);
        Assert.Equal("FUTURE_ROOT_CAUSE_STAGE", safeRoot["rootCauseStage"]);
        Assert.Contains(
          "FUTURE_DIAGNOSTIC_CODE",
          Assert.IsType<object[]>(safeRoot["safeDiagnosticCodes"])
            .Cast<string>());
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_detects_secret_prefixes_at_internal_segment_boundaries()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        Stage03FailureReportContext context = FailureContext();
        context.RunId = "run-sk-project-secret-value";
        context.TechnicalCode = "ERR_GHP_ABC123";
        context.RootCauseStage = "FUTURE_SKETCH_DIAGNOSTIC";
        context.SafeDiagnosticCodes = new[]
        {
          "SAFE_AKIA1234567890",
          "FUTURE_SKETCH_DIAGNOSTIC"
        };

        Stage03FailureReportWriteResult result =
          Stage03FailureReportWriter.TryWrite(
            context,
            TrustedGhaResolver(ghaPath));

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        var serializer = new JavaScriptSerializer();
        var root = Assert.IsType<Dictionary<string, object>>(
          serializer.DeserializeObject(
            File.ReadAllText(result.ReportPath, Encoding.UTF8)));
        Assert.Equal("REDACTED_RUN_ID", root["runId"]);
        Assert.Equal("UNKNOWN_TECHNICAL_FATAL", root["technicalCode"]);
        Assert.Equal("FUTURE_SKETCH_DIAGNOSTIC", root["rootCauseStage"]);
        string[] codes = Assert.IsType<object[]>(root["safeDiagnosticCodes"])
          .Cast<string>()
          .ToArray();
        Assert.Contains("UNSAFE_DIAGNOSTIC_CODE_REDACTED", codes);
        Assert.Contains("FUTURE_SKETCH_DIAGNOSTIC", codes);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_traverses_aggregate_graph_with_sibling_and_parent_indexes()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        var nested = new FormatException("nested-secret");
        var first = new InvalidOperationException("first-secret", nested);
        var second = new IOException("second-secret");
        var shared = new ArgumentException("shared-secret");
        Stage03FailureReportContext context = FailureContext();
        context.Exception = new AggregateException(
          first,
          second,
          shared,
          shared);

        Stage03FailureReportWriteResult result =
          Stage03FailureReportWriter.TryWrite(
            context,
            TrustedGhaResolver(ghaPath));

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Dictionary<string, object>[] nodes = ExceptionNodes(result.ReportPath);
        Dictionary<string, object>[] payloadNodes = nodes
          .Where(node => !node.ContainsKey("reference"))
          .ToArray();
        Assert.Equal(5, payloadNodes.Length);
        Assert.Equal(-1, payloadNodes[0]["parentIndex"]);
        Assert.Equal(
          new[] { 0, 1, 2 },
          payloadNodes.Skip(1).Take(3)
            .Select(node => Convert.ToInt32(node["branchIndex"]))
            .ToArray());
        Assert.All(payloadNodes.Skip(1).Take(3), node =>
          Assert.Equal(0, Convert.ToInt32(node["parentIndex"])));
        Dictionary<string, object> nestedNode = Assert.Single(
          payloadNodes,
          node => Equals(node["type"], typeof(FormatException).FullName));
        Assert.Equal(1, Convert.ToInt32(nestedNode["parentIndex"]));
        Dictionary<string, object> sharedNode = Assert.Single(
          payloadNodes,
          node => Equals(node["type"], typeof(ArgumentException).FullName));
        Dictionary<string, object> sharedReference = Assert.Single(
          nodes,
          node => node.ContainsKey("reference")
            && Equals(node["reference"], true));
        Assert.Equal(0, Convert.ToInt32(sharedReference["parentIndex"]));
        Assert.Equal(3, Convert.ToInt32(sharedReference["branchIndex"]));
        Assert.Equal(1, Convert.ToInt32(sharedReference["depth"]));
        Assert.Equal(
          Convert.ToInt32(sharedNode["nodeIndex"]),
          Convert.ToInt32(sharedReference["targetNodeIndex"]));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_bounds_duplicate_reference_edges_and_reports_omitted_branches()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        var shared = new InvalidOperationException("shared-secret");
        Stage03FailureReportContext context = FailureContext();
        context.Exception = new AggregateException(
          Enumerable.Repeat<Exception>(shared, 10000));

        Stage03FailureReportWriteResult result =
          Stage03FailureReportWriter.TryWrite(
            context,
            TrustedGhaResolver(ghaPath));

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Dictionary<string, object>[] nodes = ExceptionNodes(result.ReportPath);
        Assert.Equal(64, nodes.Length);
        Assert.Equal(
          2,
          nodes.Count(node => !node.ContainsKey("reference")
            && !Equals(node["truncated"], true)));
        Assert.Equal(
          61,
          nodes.Count(node => node.ContainsKey("reference")
            && Equals(node["reference"], true)));
        Dictionary<string, object> marker = Assert.Single(
          nodes,
          node => Equals(node["truncated"], true));
        Assert.Equal("RECORD_LIMIT", marker["reason"]);
        Assert.Equal(0, Convert.ToInt32(marker["parentIndex"]));
        Assert.Equal(62, Convert.ToInt32(marker["branchIndex"]));
        Assert.Equal(1, Convert.ToInt32(marker["depth"]));
        Assert.Equal(9938L, Convert.ToInt64(marker["omittedCount"]));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_marks_exception_graph_truncation_explicitly()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        Exception chain = new InvalidOperationException("leaf-secret");
        for (int index = 0; index < 70; index++)
          chain = new InvalidOperationException("node-secret", chain);
        Stage03FailureReportContext context = FailureContext();
        context.Exception = chain;

        Stage03FailureReportWriteResult result =
          Stage03FailureReportWriter.TryWrite(
            context,
            TrustedGhaResolver(ghaPath));

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        Dictionary<string, object>[] nodes = ExceptionNodes(result.ReportPath);
        Assert.Equal(64, nodes.Length);
        Dictionary<string, object> marker = Assert.Single(
          nodes,
          node => node.ContainsKey("truncated")
            && Equals(node["truncated"], true));
        Assert.Equal("NODE_LIMIT", marker["reason"]);
        Assert.Equal(62, Convert.ToInt32(marker["parentIndex"]));
        Assert.Equal(0, Convert.ToInt32(marker["branchIndex"]));
        Assert.Equal(63, Convert.ToInt32(marker["depth"]));
        Assert.Equal(1L, Convert.ToInt64(marker["omittedCount"]));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public void Failure_writer_snapshots_exception_before_mutable_codes_drift()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        Stage03FailureReportContext context = FailureContext();
        context.Exception = new InvalidOperationException("original-marker");
        context.SafeDiagnosticCodes = new DriftingReadOnlyList<string>(
          new[] { "SAFE_CODE" },
          enumeration =>
          {
            if (enumeration != 1) return;
            context.RunId = "drift-run";
            context.Exception = new IOException("drift-secret-marker");
          });

        Stage03FailureReportWriteResult result =
          Stage03FailureReportWriter.TryWrite(
            context,
            TrustedGhaResolver(ghaPath));

        Assert.True(result.Success, result.ReportWriteErrorSummary);
        string json = File.ReadAllText(result.ReportPath, Encoding.UTF8);
        Assert.Contains(
          typeof(InvalidOperationException).FullName,
          json,
          StringComparison.Ordinal);
        Assert.DoesNotContain(
          typeof(IOException).FullName,
          json,
          StringComparison.Ordinal);
        Assert.DoesNotContain("drift-", json, StringComparison.Ordinal);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Fact]
    public async Task Concurrent_failure_reports_are_unique_and_atomic_in_one_gha_directory()
    {
      string directory = NewDirectory();
      try
      {
        string ghaPath = CreateValidGha(directory);
        IStage03ActiveGhaResolver resolver = TrustedGhaResolver(ghaPath);
        Stage03FailureReportWriteResult[] results = await Task.WhenAll(
          Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
            Stage03FailureReportWriter.TryWrite(
              FailureContext(),
              resolver))));

        Assert.All(results, result => Assert.True(
          result.Success,
          result.ReportWriteErrorSummary));
        Assert.Equal(
          results.Length,
          results.Select(result => result.ReportPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
        Assert.All(results, result => Assert.Equal(
          directory,
          Path.GetDirectoryName(result.ReportPath)));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Assert.Single(Directory.GetFiles(directory, "*.gha"));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    private static Stage03FieldReportContext MinimalContext(
      Stage03OutputPaths paths)
    {
      return new Stage03FieldReportContext
      {
        RunId = paths.RunId,
        StartedUtc = new DateTimeOffset(2026, 8, 3, 1, 0, 0, TimeSpan.Zero),
        CompletedUtc = new DateTimeOffset(2026, 8, 3, 1, 1, 0, TimeSpan.Zero),
        PluginVersion = "0.9.0",
        RevitVersion = "2020",
        DocumentTitle = "model.rvt",
        DocumentPath = @"D:\models\model.rvt",
        DocumentFingerprint = "document-fingerprint",
        FileGuid = "file-guid",
        FileContextHash = "file-context-hash",
        RulePackageId = "hbr-rules",
        RulePackageVersion = "1.0.0",
        RulePackageSha256 = "rule-package-sha",
        GateDecision = Stage03ExportGatePolicy.Decide(
          Stage03GateMode.Strict,
          string.Empty,
          Array.Empty<Stage03FieldResult>(),
          Array.Empty<string>()),
        OutputPaths = paths,
        RawIfcSha256 = new string('a', 64),
        FinalIfcSha256 = new string('b', 64),
        Carriers = Array.Empty<Stage03CarrierResult>(),
        Fields = Array.Empty<Stage03FieldResult>(),
        Diagnostics = Array.Empty<Stage03Diagnostic>()
      };
    }

    private static Stage03FieldReportContext FullContext(
      Stage03OutputPaths paths,
      bool reverse)
    {
      Stage03FieldResult[] fields =
      {
        Field("IfcWall", "owner-z", "property-a", "raw-z",
          Stage03FieldStatus.MissingParameter, "OPTIONAL"),
        Field("IfcWall", "owner-a", "property-a", "raw-b",
          Stage03FieldStatus.InvalidValue, "REQUIRED"),
        Field("IfcBuilding", "owner-a", "property-z", "raw-a",
          Stage03FieldStatus.Pass, "REQUIRED"),
        Field("IfcWall", "owner-a", "property-a", "raw-a",
          Stage03FieldStatus.InvalidValue, "REQUIRED")
      };
      Stage03CarrierResult[] carriers =
      {
        new Stage03CarrierResult
        {
          Entity = "IfcWall",
          Role = "wall",
          ElementId = 20,
          UniqueId = "owner-z",
          Category = "Walls",
          Name = "Wall",
          Status = Stage03FieldStatus.Pass,
          Active = true,
          Messages = new[] { "z-message", "a-message" }
        },
        new Stage03CarrierResult
        {
          Entity = "IfcBuilding",
          Role = "building",
          ElementId = 10,
          UniqueId = "owner-a",
          Category = "ProjectInformation",
          Name = "Building",
          Status = Stage03FieldStatus.Pass,
          Active = true,
          Messages = new[] { "building-message" }
        }
      };
      Stage03Diagnostic[] diagnostics =
      {
        new Stage03Diagnostic
        {
          Code = "Z_CODE",
          Stage = "FINAL_IFC",
          Severity = "WARNING",
          Message = "z-message"
        },
        new Stage03Diagnostic
        {
          Code = "A_CODE",
          Stage = "CARRIER_SCAN",
          Severity = "INFO",
          Message = "a-message"
        }
      };
      Stage03FieldReportContext context = MinimalContext(paths);
      context.Carriers = reverse ? carriers.Reverse().ToArray() : carriers;
      context.Fields = reverse ? fields.Reverse().ToArray() : fields;
      context.Diagnostics = reverse
        ? diagnostics.Reverse().ToArray()
        : diagnostics;
      return context;
    }

    private static Stage03FieldResult Field(
      string entity,
      string ownerUniqueId,
      string propertyId,
      string rawValue,
      Stage03FieldStatus status,
      string requirement)
    {
      return new Stage03FieldResult
      {
        PropertyId = propertyId,
        ContractKind = "TEXT",
        Requirement = requirement,
        Applicability = "APPLICABLE",
        Entity = entity,
        PropertySet = entity == "IfcWall" ? "Pset_HBR" : "Pset_Building",
        IfcProperty = "HBR_Property",
        Role = "role",
        ElementId = ownerUniqueId == "owner-a" ? 10 : 20,
        OwnerUniqueId = ownerUniqueId,
        ParameterGuid = "00000000-0000-0000-0000-000000000001",
        ParameterName = "HBR Parameter",
        ParameterScope = "INSTANCE",
        CarrierStatus = Stage03FieldStatus.Pass,
        ParameterStatus = Stage03FieldStatus.Pass,
        RevitStatus = status,
        RevitRawValue = rawValue,
        RevitNormalizedValue = rawValue.ToUpperInvariant(),
        RevitValueSource = "SHARED_PARAMETER",
        RawIfcOwner = ownerUniqueId,
        RawIfcType = "IfcLabel",
        RawIfcValue = rawValue,
        RawIfcStatus = status,
        FinalIfcOwner = ownerUniqueId,
        FinalIfcType = "IfcLabel",
        FinalIfcValue = rawValue,
        FinalIfcStatus = status,
        Status = status,
        Active = true,
        IsBusinessBlocker = requirement == "REQUIRED"
          && status != Stage03FieldStatus.Pass,
        Messages = new[] { "z-field-message", "a-field-message" }
      };
    }

    private static Stage03FailureReportContext FailureContext()
    {
      return new Stage03FailureReportContext
      {
        RunId = "run-005",
        PluginVersion = "0.9.0",
        RevitVersion = "2020",
        TechnicalCode = "INVALID_IFC",
        RootCauseStage = "FINAL_IFC_VALIDATE",
        SafeDiagnosticCodes = new[] { "INVALID_STEP_SYNTAX" },
        Exception = new InvalidDataException("business-secret"),
        OccurredUtc = new DateTimeOffset(
          2026, 8, 3, 1, 2, 3, TimeSpan.Zero),
        OccurredLocal = new DateTimeOffset(
          2026, 8, 3, 9, 2, 3, TimeSpan.FromHours(8))
      };
    }

    private static string CreateValidGha(string directory)
    {
      string source = Path.Combine(
        AppContext.BaseDirectory,
        ProductionAssemblyFixtureName);
      Assert.True(File.Exists(source), "缺少生产程序集测试夹具：" + source);
      string destination = Path.Combine(
        directory,
        "BIMBaoGui.Stage01.gha");
      File.Copy(source, destination);
      return destination;
    }

    private static void WriteWithPublisherSeam(
      string targetPath,
      byte[] payload,
      Action<string, string> publisher)
    {
      AtomicJsonReportWriter.Write(targetPath, payload, publisher);
    }

    private static void WriteTrustedJson(
      string targetPath,
      byte[] payload)
    {
      AtomicJsonReportWriter.WriteTrustedJson(targetPath, payload);
    }

    private static IComparer<Stage03FieldResult>
      FieldStructuralComparerWithReflection()
    {
      Type comparerType = typeof(Stage03FieldReportWriter).GetNestedType(
        "FieldResultComparer",
        BindingFlags.NonPublic);
      Assert.NotNull(comparerType);
      FieldInfo instance = comparerType.GetField(
        "Instance",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
      Assert.NotNull(instance);
      return Assert.IsAssignableFrom<IComparer<Stage03FieldResult>>(
        instance.GetValue(null));
    }

    private static IComparer<Stage03CarrierResult>
      CarrierStructuralComparerWithReflection()
    {
      Type comparerType = typeof(Stage03FieldReportWriter).GetNestedType(
        "CarrierResultComparer",
        BindingFlags.NonPublic);
      Assert.NotNull(comparerType);
      FieldInfo instance = comparerType.GetField(
        "Instance",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
      Assert.NotNull(instance);
      return Assert.IsAssignableFrom<IComparer<Stage03CarrierResult>>(
        instance.GetValue(null));
    }

    private static IComparer<Stage03Diagnostic>
      DiagnosticStructuralComparerWithReflection()
    {
      Type comparerType = typeof(Stage03FieldReportWriter).GetNestedType(
        "DiagnosticComparer",
        BindingFlags.NonPublic);
      Assert.NotNull(comparerType);
      FieldInfo instance = comparerType.GetField(
        "Instance",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
      Assert.NotNull(instance);
      return Assert.IsAssignableFrom<IComparer<Stage03Diagnostic>>(
        instance.GetValue(null));
    }

    private static Func<long> AllocatedBytesForCurrentThread()
    {
      MethodInfo allocationMethod = typeof(GC).GetMethod(
        "GetAllocatedBytesForCurrentThread",
        BindingFlags.Public | BindingFlags.Static);
      Assert.NotNull(allocationMethod);
      return (Func<long>)Delegate.CreateDelegate(
        typeof(Func<long>),
        allocationMethod);
    }

    private static void WriteFieldReportWithObserver(
      Stage03FieldReportContext context,
      Action<string> observer)
    {
      Stage03FieldReportWriter.Write(context, observer);
    }

    private static Dictionary<string, object>[] ExceptionNodes(
      string reportPath)
    {
      var serializer = new JavaScriptSerializer();
      var root = Assert.IsType<Dictionary<string, object>>(
        serializer.DeserializeObject(
          File.ReadAllText(reportPath, Encoding.UTF8)));
      return Assert.IsType<object[]>(root["exceptionChain"])
        .Select(node => Assert.IsType<Dictionary<string, object>>(node))
        .ToArray();
    }

    private static IStage03ActiveGhaResolver TrustedGhaResolver(
      string activeGhaPath,
      Guid? trustedModuleVersionId = null)
    {
      TrustedAssemblyIdentity trusted = LazyTrustedAssemblyIdentity.Value;
      return new FixedStage03ActiveGhaResolver(
        new Stage03ActiveGhaResolution(
          activeGhaPath,
          trusted.Name,
          trusted.Version,
          trustedModuleVersionId ?? trusted.ModuleVersionId));
    }

    private static TrustedAssemblyIdentity LoadTrustedAssemblyIdentity()
    {
      string path = Path.Combine(
        AppContext.BaseDirectory,
        ProductionAssemblyFixtureName);
      AssemblyName name = AssemblyName.GetAssemblyName(path);
      return new TrustedAssemblyIdentity(
        name.Name,
        name.Version,
        Stage03PortableExecutableMetadataReader.ReadModuleVersionId(path));
    }

    private static string RelativeToCurrentDirectory(string absolutePath)
    {
      string currentDirectory = Environment.CurrentDirectory.TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
      return Uri.UnescapeDataString(
          new Uri(currentDirectory).MakeRelativeUri(new Uri(absolutePath))
            .ToString())
        .Replace('/', Path.DirectorySeparatorChar);
    }

    private static void AssertDeterministicNulReport(
      Stage03OutputPaths paths,
      Stage03FieldReportContext forward,
      Stage03FieldReportContext reverse)
    {
      Stage03FieldReportWriter.Write(forward);
      byte[] first = File.ReadAllBytes(paths.FieldReport);
      File.Delete(paths.FieldReport);
      Stage03FieldReportWriter.Write(reverse);
      byte[] second = File.ReadAllBytes(paths.FieldReport);

      Assert.Equal(first, second);
      Assert.Contains(
        "\\u0000",
        new UTF8Encoding(false, true).GetString(second),
        StringComparison.Ordinal);
    }

    private static void AddFieldTiePair(
      ICollection<Stage03FieldResult> fields,
      string propertyId,
      Action<Stage03FieldResult> configureSecond)
    {
      Stage03FieldResult first = TieField(propertyId);
      Stage03FieldResult second = TieField(propertyId);
      configureSecond(second);
      fields.Add(first);
      fields.Add(second);
    }

    private static Stage03FieldResult TieField(string propertyId)
    {
      Stage03FieldResult value = Field(
        "IfcWall",
        "same-owner",
        propertyId,
        "same-value",
        Stage03FieldStatus.Pass,
        "OPTIONAL");
      value.Status = Stage03FieldStatus.Pass;
      value.Active = true;
      value.IsBusinessBlocker = false;
      value.Messages = new[] { "same-message" };
      return value;
    }

    private static string Sha256(string value)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        return string.Concat(algorithm.ComputeHash(
          new UTF8Encoding(false, true).GetBytes(value)).Select(item =>
            item.ToString("x2")));
      }
    }

    private sealed class DriftingReadOnlyList<T> : IReadOnlyList<T>
    {
      private T[] _values;
      private readonly Action<int> _afterEnumeration;
      private int _enumerationCount;

      internal DriftingReadOnlyList(
        IEnumerable<T> values,
        Action<int> afterEnumeration)
      {
        _values = (values ?? Array.Empty<T>()).ToArray();
        _afterEnumeration = afterEnumeration;
      }

      public T this[int index] => _values[index];

      public int Count => _values.Length;

      public IEnumerator<T> GetEnumerator()
      {
        int enumeration = ++_enumerationCount;
        T[] snapshot = _values.ToArray();
        foreach (T value in snapshot) yield return value;
        _afterEnumeration?.Invoke(enumeration);
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
        return GetEnumerator();
      }

      internal void Replace(params T[] values)
      {
        _values = (values ?? Array.Empty<T>()).ToArray();
      }
    }

    private sealed class FixedStage03ActiveGhaResolver
      : IStage03ActiveGhaResolver
    {
      private readonly Stage03ActiveGhaResolution _resolution;

      internal FixedStage03ActiveGhaResolver(
        Stage03ActiveGhaResolution resolution)
      {
        _resolution = resolution;
      }

      public Stage03ActiveGhaResolution Resolve()
      {
        return _resolution;
      }
    }

    private sealed class TrustedAssemblyIdentity
    {
      internal TrustedAssemblyIdentity(
        string name,
        Version version,
        Guid moduleVersionId)
      {
        Name = name;
        Version = version;
        ModuleVersionId = moduleVersionId;
      }

      internal string Name { get; }
      internal Version Version { get; }
      internal Guid ModuleVersionId { get; }
    }

    public sealed class PublicFailureWriterRunner : MarshalByRefObject
    {
      public string[] Run(string ghaPath)
      {
        Assembly productionAssembly = Assembly.LoadFrom(ghaPath);
        Type contextType = productionAssembly.GetType(
          "BIMBaoGui.Stage01.Diagnostics.Stage03FailureReportContext",
          true);
        object context = Activator.CreateInstance(contextType);
        SetProperty(contextType, context, "RunId", "public-resolver-run");
        SetProperty(contextType, context, "PluginVersion", "0.9.0");
        SetProperty(contextType, context, "RevitVersion", "2020");
        SetProperty(contextType, context, "TechnicalCode", "INVALID_IFC");
        SetProperty(
          contextType,
          context,
          "RootCauseStage",
          "PUBLIC_RESOLVER_TEST");
        SetProperty(
          contextType,
          context,
          "SafeDiagnosticCodes",
          new[] { "PUBLIC_RESOLVER_TEST" });
        SetProperty(
          contextType,
          context,
          "Exception",
          new InvalidDataException("isolated-test-secret"));
        SetProperty(
          contextType,
          context,
          "OccurredUtc",
          new DateTimeOffset(2026, 8, 3, 1, 2, 3, TimeSpan.Zero));
        SetProperty(
          contextType,
          context,
          "OccurredLocal",
          new DateTimeOffset(
            2026,
            8,
            3,
            9,
            2,
            3,
            TimeSpan.FromHours(8)));
        Type writerType = productionAssembly.GetType(
          "BIMBaoGui.Stage01.Diagnostics.Stage03FailureReportWriter",
          true);
        MethodInfo publicTryWrite = writerType.GetMethod(
          "TryWrite",
          BindingFlags.Public | BindingFlags.Static,
          null,
          new[] { contextType },
          null);
        if (publicTryWrite == null)
          throw new MissingMethodException(writerType.FullName, "TryWrite");
        const int writeCount = 8;
        int assemblyLoadCount = 0;
        int sameNameBefore = AppDomain.CurrentDomain.GetAssemblies().Count(
          assembly => string.Equals(
            assembly.GetName().Name,
            "BIMBaoGui.Stage01",
            StringComparison.Ordinal));
        AssemblyLoadEventHandler handler = (_, arguments) =>
        {
          if (string.Equals(
            arguments.LoadedAssembly.GetName().Name,
            "BIMBaoGui.Stage01",
            StringComparison.Ordinal))
          {
            assemblyLoadCount++;
          }
        };
        object result = null;
        bool allSucceeded = true;
        AppDomain.CurrentDomain.AssemblyLoad += handler;
        try
        {
          for (int index = 0; index < writeCount; index++)
          {
            result = publicTryWrite.Invoke(null, new[] { context });
            Type currentResultType = result.GetType();
            allSucceeded &= Convert.ToBoolean(
              GetProperty(currentResultType, result, "Success"));
          }
        }
        finally
        {
          AppDomain.CurrentDomain.AssemblyLoad -= handler;
        }
        int sameNameAfter = AppDomain.CurrentDomain.GetAssemblies().Count(
          assembly => string.Equals(
            assembly.GetName().Name,
            "BIMBaoGui.Stage01",
            StringComparison.Ordinal));
        Type resultType = result.GetType();
        AssemblyName name = productionAssembly.GetName();
        return new[]
        {
          Convert.ToString(allSucceeded),
          Convert.ToString(GetProperty(resultType, result, "ReportPath")),
          Convert.ToString(GetProperty(resultType, result, "ErrorCode")),
          productionAssembly.Location,
          name.Name,
          Convert.ToString(name.Version),
          Convert.ToString(
            productionAssembly.ManifestModule.ModuleVersionId),
          Convert.ToString(writeCount),
          Convert.ToString(assemblyLoadCount),
          Convert.ToString(sameNameBefore),
          Convert.ToString(sameNameAfter)
        };
      }

      public override object InitializeLifetimeService()
      {
        return null;
      }

      private static void SetProperty(
        Type type,
        object instance,
        string name,
        object value)
      {
        PropertyInfo property = type.GetProperty(
          name,
          BindingFlags.Public | BindingFlags.Instance);
        if (property == null)
          throw new MissingMemberException(type.FullName, name);
        property.SetValue(instance, value);
      }

      private static object GetProperty(
        Type type,
        object instance,
        string name)
      {
        PropertyInfo property = type.GetProperty(
          name,
          BindingFlags.Public | BindingFlags.Instance);
        if (property == null)
          throw new MissingMemberException(type.FullName, name);
        return property.GetValue(instance);
      }
    }

    private static string NewDirectory()
    {
      string directory = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui-Stage03ReportTests-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      return directory;
    }
  }
}
