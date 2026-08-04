using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BIMBaoGui.Stage01.Mvd;

namespace BIMBaoGui.Stage01.Stage03
{
  internal sealed class Stage03IfcTranslationService
  {
    private static readonly UTF8Encoding StrictUtf8 =
      new UTF8Encoding(false, true);
    private readonly Action<string> _candidateWritten;

    internal Stage03IfcTranslationService()
      : this(null)
    {
    }

    internal Stage03IfcTranslationService(Action<string> candidateWritten)
    {
      _candidateWritten = candidateWritten;
    }

    internal Task<Stage03WorkflowTranslationResult> TranslateAsync(
      Stage03WorkflowTranslationRequest request)
    {
      return Task.Run(() => Translate(request));
    }

    private Stage03WorkflowTranslationResult Translate(
      Stage03WorkflowTranslationRequest request)
    {
      IReadOnlyList<Stage03FieldResult> fields = CloneFields(
        request == null ? null : request.Fields);
      HbrIfcBatchInspectionResult prePublishInspection = null;
      HbrIfcBatchInspectionResult reReadCandidateInspection = null;
      try
      {
        if (request == null)
          throw new ArgumentNullException(nameof(request));
        string rawPath = RequireAbsolutePath(
          request.RawIfcPath,
          "RAW IFC");
        string finalPath = RequireAbsolutePath(
          request.FinalIfcPath,
          "HIFC-MVD IFC");
        if (string.Equals(
          rawPath,
          finalPath,
          StringComparison.OrdinalIgnoreCase))
        {
          throw new InvalidDataException(
            "RAW IFC 与 HIFC-MVD IFC 不能使用同一路径。");
        }
        string finalDirectory = Path.GetDirectoryName(finalPath)
          ?? string.Empty;
        if (!Directory.Exists(finalDirectory))
          throw new DirectoryNotFoundException(
            "HIFC-MVD IFC 输出目录不存在：" + finalDirectory);

        byte[] rawBytes = File.ReadAllBytes(rawPath);
        string rawHash = ComputeSha256(rawBytes);
        long rawLength = rawBytes.LongLength;
        string rawText = StrictUtf8.GetString(rawBytes);
        IfcStepDocument candidate = IfcStepDocument.Parse(rawText);

        HbrIfcEnrichmentValue[] enrichmentValues = request.EnrichmentValues
          .Select(Stage03WorkflowCoordinator.CloneEnrichmentValue)
          .ToArray();
        HbrIfcEnrichmentResult enrichment =
          new HbrIfcEnricher().Apply(candidate, enrichmentValues);
        if (enrichment == null || !enrichment.Success)
          throw new InvalidDataException(BuildEnrichmentFailure(enrichment));

        var inspector = new HbrIfcFieldInspector();
        prePublishInspection = inspector.InspectMany(
          candidate,
          enrichmentValues);
        RequireSuccessfulInspection(
          prePublishInspection,
          enrichmentValues,
          "发布前内存 candidate");
        fields = MapInspectionEvidence(
          fields,
          enrichmentValues,
          prePublishInspection,
          null);

        string candidatePath = UniqueCandidatePath(finalPath);
        byte[] candidateBytes = StrictUtf8.GetBytes(candidate.Serialize());
        using (var stream = new FileStream(
          candidatePath,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None))
        {
          stream.Write(candidateBytes, 0, candidateBytes.Length);
          stream.Flush(true);
        }
        _candidateWritten?.Invoke(candidatePath);

        string reReadText = StrictUtf8.GetString(
          File.ReadAllBytes(candidatePath));
        IfcStepDocument reReadCandidate = IfcStepDocument.Parse(reReadText);
        reReadCandidateInspection = inspector.InspectMany(
          reReadCandidate,
          enrichmentValues);
        RequireSuccessfulInspection(
          reReadCandidateInspection,
          enrichmentValues,
          "磁盘临时 candidate");
        fields = MapInspectionEvidence(
          fields,
          enrichmentValues,
          prePublishInspection,
          reReadCandidateInspection);

        EnsureRawUnchanged(rawPath, rawLength, rawHash);
        if (File.Exists(finalPath) || Directory.Exists(finalPath))
          throw new IOException(
            "HIFC-MVD IFC 正式目标已被占用：" + finalPath);
        File.Move(candidatePath, finalPath);

        var finalFile = new FileInfo(finalPath);
        return new Stage03WorkflowTranslationResult
        {
          Success = true,
          TechnicalFatalCodes = Array.Empty<string>(),
          RawInspection = prePublishInspection,
          FinalInspection = reReadCandidateInspection,
          FinalIfcPath = finalPath,
          FinalIfcLength = finalFile.Length,
          FinalIfcSha256 = ComputeSha256(finalPath),
          Fields = fields,
          Diagnostics = Array.Empty<Stage03Diagnostic>()
        };
      }
      catch (Exception exception)
      {
        return Failure(
          fields,
          prePublishInspection,
          reReadCandidateInspection,
          exception);
      }
    }

    private static IReadOnlyList<Stage03FieldResult> MapInspectionEvidence(
      IReadOnlyList<Stage03FieldResult> sourceFields,
      IReadOnlyList<HbrIfcEnrichmentValue> enrichmentValues,
      HbrIfcBatchInspectionResult rawInspection,
      HbrIfcBatchInspectionResult finalInspection)
    {
      Stage03FieldResult[] fields = (sourceFields
        ?? Array.Empty<Stage03FieldResult>())
        .Select(Stage03WorkflowCoordinator.CloneField)
        .ToArray();
      var valuesByIdentity = enrichmentValues.ToDictionary(
        value => value.PropertyIdentity,
        StringComparer.Ordinal);
      var fieldsByEnrichmentIdentity =
        new Dictionary<string, Stage03FieldResult>(StringComparer.Ordinal);
      foreach (Stage03FieldResult field in fields)
      {
        string identity = EnrichmentIdentity(field);
        if (!valuesByIdentity.ContainsKey(identity)) continue;
        if (fieldsByEnrichmentIdentity.ContainsKey(identity))
          throw new InvalidDataException(
            "多个扫描字段竞争同一 IFC enrichment identity：" + identity);
        fieldsByEnrichmentIdentity.Add(identity, field);
      }

      ApplyInspection(
        fieldsByEnrichmentIdentity,
        valuesByIdentity,
        rawInspection,
        true);
      if (finalInspection != null)
      {
        ApplyInspection(
          fieldsByEnrichmentIdentity,
          valuesByIdentity,
          finalInspection,
          false);
      }
      return Freeze(fields);
    }

    private static void ApplyInspection(
      IReadOnlyDictionary<string, Stage03FieldResult> fieldsByIdentity,
      IReadOnlyDictionary<string, HbrIfcEnrichmentValue> valuesByIdentity,
      HbrIfcBatchInspectionResult inspection,
      bool raw)
    {
      foreach (HbrIfcFieldInspectionResult evidence in inspection.Fields)
      {
        if (!valuesByIdentity.TryGetValue(
          evidence.PropertyIdentity ?? string.Empty,
          out HbrIfcEnrichmentValue value))
        {
          throw new InvalidDataException(
            "IFC inspection 返回了未知 property identity。");
        }
        if (!fieldsByIdentity.TryGetValue(
          value.PropertyIdentity,
          out Stage03FieldResult field))
        {
          throw new InvalidDataException(
            "IFC inspection 无法映射到扫描字段："
            + value.PropertyIdentity);
        }
        if (!evidence.Success || evidence.OwnerId.GetValueOrDefault() <= 0)
          throw new InvalidDataException(
            "IFC inspection 证据不完整：" + value.PropertyIdentity);

        string owner = "#" + evidence.OwnerId.Value.ToString(
          CultureInfo.InvariantCulture);
        if (raw)
        {
          field.RawIfcOwner = owner;
          field.RawIfcPropertySet = value.PropertySetName;
          field.RawIfcProperty = value.PropertyName;
          field.RawIfcType = value.DeclaredIfcType;
          field.RawIfcValue = value.CanonicalValue;
          field.RawIfcStatus = Stage03FieldStatus.Pass;
        }
        else
        {
          field.FinalIfcOwner = owner;
          field.FinalIfcPropertySet = value.PropertySetName;
          field.FinalIfcProperty = value.PropertyName;
          field.FinalIfcType = value.DeclaredIfcType;
          field.FinalIfcValue = value.CanonicalValue;
          field.FinalIfcStatus = Stage03FieldStatus.Pass;
        }
      }
    }

    private static void RequireSuccessfulInspection(
      HbrIfcBatchInspectionResult inspection,
      IReadOnlyList<HbrIfcEnrichmentValue> enrichmentValues,
      string stage)
    {
      if (inspection == null || !inspection.Success)
        throw new InvalidDataException(
          stage + " IFC exact inspection 未通过："
          + (inspection == null ? "未返回结果" : inspection.Message));
      string[] expected = enrichmentValues
        .Select(value => value.PropertyIdentity ?? string.Empty)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string[] actual = inspection.Fields
        .Select(value => value.PropertyIdentity ?? string.Empty)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (expected.Length != expected.Distinct(StringComparer.Ordinal).Count()
        || actual.Length != actual.Distinct(StringComparer.Ordinal).Count()
        || !expected.SequenceEqual(actual, StringComparer.Ordinal))
      {
        throw new InvalidDataException(
          stage + " IFC inspection property identity 对账失败。");
      }
    }

    private static void EnsureRawUnchanged(
      string rawPath,
      long expectedLength,
      string expectedHash)
    {
      var file = new FileInfo(rawPath);
      if (!file.Exists
        || file.Length != expectedLength
        || !string.Equals(
          ComputeSha256(rawPath),
          expectedHash,
          StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidDataException(
          "RAW IFC 在转译期间发生变化，拒绝发布 HIFC-MVD IFC。");
      }
    }

    private static string UniqueCandidatePath(string finalPath)
    {
      string directory = Path.GetDirectoryName(finalPath) ?? string.Empty;
      string name = Path.GetFileName(finalPath);
      return Path.Combine(
        directory,
        "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp");
    }

    private static string RequireAbsolutePath(string path, string label)
    {
      if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        throw new ArgumentException(label + " 路径必须是绝对路径。");
      return Path.GetFullPath(path);
    }

    private static string BuildEnrichmentFailure(
      HbrIfcEnrichmentResult enrichment)
    {
      if (enrichment == null) return "IFC enrichment 未返回结果。";
      HbrIfcEnrichmentFieldResult failure = enrichment.Fields
        .FirstOrDefault(field => field == null || !field.Success);
      return failure == null
        ? "IFC enrichment 返回失败。"
        : "IFC enrichment 失败：" + (failure == null
          ? string.Empty
          : failure.ErrorCode + " " + failure.Message);
    }

    private static Stage03WorkflowTranslationResult Failure(
      IReadOnlyList<Stage03FieldResult> fields,
      HbrIfcBatchInspectionResult rawInspection,
      HbrIfcBatchInspectionResult finalInspection,
      Exception exception)
    {
      Exception failureException = exception
        ?? new InvalidOperationException(
          "IFC translator 未返回失败原因。");
      return new Stage03WorkflowTranslationResult
      {
        Success = false,
        FailureException = failureException,
        TechnicalFatalCodes = new[]
        {
          Stage03TechnicalFatalCodes.InvalidIfc
        },
        RawInspection = rawInspection,
        FinalInspection = finalInspection,
        FinalIfcPath = string.Empty,
        FinalIfcLength = 0L,
        FinalIfcSha256 = string.Empty,
        Fields = CloneFields(fields),
        Diagnostics = new[]
        {
          new Stage03Diagnostic
          {
            Code = Stage03TechnicalFatalCodes.InvalidIfc,
            Stage = "IFC_TRANSLATION",
            Severity = "ERROR",
            Message = failureException.Message
          }
        }
      };
    }

    private static string EnrichmentIdentity(Stage03FieldResult field)
    {
      if (field == null) return string.Empty;
      return (field.PropertyId ?? string.Empty) + "|"
        + (field.Role ?? string.Empty) + "|"
        + (field.OwnerUniqueId ?? string.Empty);
    }

    private static IReadOnlyList<Stage03FieldResult> CloneFields(
      IEnumerable<Stage03FieldResult> fields)
    {
      return Freeze((fields ?? Array.Empty<Stage03FieldResult>())
        .Select(Stage03WorkflowCoordinator.CloneField));
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
    {
      return new ReadOnlyCollection<T>(
        (values ?? Enumerable.Empty<T>()).ToArray());
    }

    private static string ComputeSha256(string path)
    {
      using (SHA256 algorithm = SHA256.Create())
      using (FileStream stream = File.OpenRead(path))
      {
        return ToLowerHex(algorithm.ComputeHash(stream));
      }
    }

    private static string ComputeSha256(byte[] bytes)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        return ToLowerHex(algorithm.ComputeHash(bytes));
      }
    }

    private static string ToLowerHex(byte[] bytes)
    {
      return BitConverter.ToString(bytes)
        .Replace("-", string.Empty)
        .ToLowerInvariant();
    }
  }
}
