using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BIMBaoGui.Stage01.Mvd;

namespace BIMBaoGui.HifcCore
{
  public static class HifcOwnerStrategies
  {
    public const string GlobalId = "GLOBAL_ID";
    public const string SingleEntityByType = "SINGLE_ENTITY_BY_TYPE";
  }

  public static class HifcCoreStatus
  {
    public const string InternalValidated = "INTERNAL_VALIDATED";
    public const string InternalFailed = "INTERNAL_FAILED";
    public const string IfcFluxManualPending = "IFCFLUX_MANUAL_PENDING";
  }

  public static class HifcCoreErrorCodes
  {
    public const string None = "";
    public const string RawMissing = "IFC_RAW_MISSING";
    public const string RawReadFailed = "IFC_RAW_READ_FAILED";
    public const string EncodingFailed = "IFC_ENCODING_FAILED";
    public const string StepParseFailed = "IFC_STEP_PARSE_FAILED";
    public const string SchemaUnsupported = "IFC_SCHEMA_UNSUPPORTED";
    public const string EnrichmentFailed = "IFC_PSET_MUTATION_FAILED";
    public const string CandidateWriteFailed = "IFC_CANDIDATE_WRITE_FAILED";
    public const string CandidateRereadFailed = "IFC_CANDIDATE_REREAD_FAILED";
    public const string ExactValidationFailed = "IFC_EXACT_VALIDATION_FAILED";
    public const string RawChanged = "RAW_IFC_CHANGED";
    public const string FinalPublishFailed = "IFC_FINAL_PUBLISH_FAILED";
    public const string OutputPathFailed = "OUTPUT_PATH_FAILED";
  }

  public sealed class HifcFieldRequest
  {
    public string PropertyIdentity { get; set; } = string.Empty;
    public string SemanticKey { get; set; } = string.Empty;
    public string OwnerEntityType { get; set; } = string.Empty;
    public string OwnerGlobalId { get; set; } = string.Empty;
    public string OwnerStrategy { get; set; } = HifcOwnerStrategies.GlobalId;
    public string PropertySetName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string DeclaredIfcType { get; set; } = string.Empty;
    public string CanonicalValue { get; set; } = string.Empty;
    public string CanonicalUnit { get; set; } = string.Empty;
  }

  public sealed class HifcFieldEvidence
  {
    public string PropertyIdentity { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? OwnerId { get; set; }
    public int? PropertySetId { get; set; }
    public int? PropertyId { get; set; }
    public int? RelationshipId { get; set; }
    public string ActualIfcType { get; set; } = string.Empty;
    public string TypedToken { get; set; } = string.Empty;
  }

  public sealed class HifcTranslationRequest
  {
    public string RawIfcPath { get; set; } = string.Empty;
    public string FinalIfcPath { get; set; } = string.Empty;
    public string QuarantineDirectory { get; set; } = string.Empty;
    public IReadOnlyList<HifcFieldRequest> Fields { get; set; } =
      Array.Empty<HifcFieldRequest>();
  }

  public sealed class HifcTranslationResult
  {
    public bool Success { get; set; }
    public string InternalStatus { get; set; } = HifcCoreStatus.InternalFailed;
    public string IfcFluxStatus { get; set; } =
      HifcCoreStatus.IfcFluxManualPending;
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string RawIfcPath { get; set; } = string.Empty;
    public long RawIfcLength { get; set; }
    public string RawIfcSha256 { get; set; } = string.Empty;
    public string CandidateIfcPath { get; set; } = string.Empty;
    public string FinalIfcPath { get; set; } = string.Empty;
    public long FinalIfcLength { get; set; }
    public string FinalIfcSha256 { get; set; } = string.Empty;
    public IReadOnlyList<HifcFieldEvidence> Fields { get; set; } =
      Array.Empty<HifcFieldEvidence>();
  }

  public sealed class HifcValidationResult
  {
    public bool Success { get; set; }
    public string InternalStatus { get; set; } = HifcCoreStatus.InternalFailed;
    public string IfcFluxStatus { get; set; } =
      HifcCoreStatus.IfcFluxManualPending;
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string IfcPath { get; set; } = string.Empty;
    public long IfcLength { get; set; }
    public string IfcSha256 { get; set; } = string.Empty;
    public IReadOnlyList<HifcFieldEvidence> Fields { get; set; } =
      Array.Empty<HifcFieldEvidence>();
  }

  public static class HifcCoreService
  {
    private static readonly UTF8Encoding StrictUtf8 =
      new UTF8Encoding(false, true);

    public static HifcTranslationResult Translate(
      HifcTranslationRequest request)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      string rawPath;
      string finalPath;
      string quarantineDirectory;
      try
      {
        rawPath = RequireAbsoluteFile(request.RawIfcPath, "RAW IFC");
        finalPath = RequireAbsoluteOutput(request.FinalIfcPath, "H-IFC");
        quarantineDirectory = RequireAbsoluteDirectory(
          request.QuarantineDirectory,
          "quarantine");
      }
      catch (Exception exception)
      {
        return Failure(
          HifcCoreErrorCodes.OutputPathFailed,
          exception.Message,
          request.RawIfcPath,
          request.FinalIfcPath);
      }

      HifcFieldRequest[] fields;
      try
      {
        fields = FreezeAndValidateFields(request.Fields);
      }
      catch (Exception exception)
      {
        return Failure(
          HifcCoreErrorCodes.ExactValidationFailed,
          exception.Message,
          rawPath,
          finalPath);
      }

      byte[] rawBytes;
      string rawHash;
      long rawLength;
      try
      {
        if (!File.Exists(rawPath))
          return Failure(
            HifcCoreErrorCodes.RawMissing,
            "RAW IFC 文件不存在。",
            rawPath,
            finalPath);
        rawBytes = File.ReadAllBytes(rawPath);
        if (rawBytes.Length == 0)
          return Failure(
            HifcCoreErrorCodes.RawMissing,
            "RAW IFC 文件为空。",
            rawPath,
            finalPath);
        rawLength = rawBytes.LongLength;
        rawHash = ComputeSha256(rawBytes);
      }
      catch (Exception exception)
      {
        return Failure(
          HifcCoreErrorCodes.RawReadFailed,
          exception.Message,
          rawPath,
          finalPath);
      }

      string rawText;
      try
      {
        rawText = StrictUtf8.GetString(rawBytes);
      }
      catch (Exception exception)
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.EncodingFailed,
          exception.Message,
          rawPath,
          finalPath,
          rawLength,
          rawHash);
      }

      IfcStepDocument document;
      try
      {
        document = IfcStepDocument.Parse(rawText);
        document.ValidateStructure();
      }
      catch (Exception exception)
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.StepParseFailed,
          exception.Message,
          rawPath,
          finalPath,
          rawLength,
          rawHash);
      }
      if (!string.Equals(document.Schema, "IFC4", StringComparison.OrdinalIgnoreCase))
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.SchemaUnsupported,
          "H-IFC 转译仅支持 IFC4，当前 schema=" + document.Schema,
          rawPath,
          finalPath,
          rawLength,
          rawHash,
          document.Schema);
      }

      HbrIfcEnrichmentValue[] internalValues = fields
        .Select(ToInternalValue)
        .ToArray();
      HbrIfcEnrichmentResult enrichment;
      try
      {
        enrichment = new HbrIfcEnricher().Apply(document, internalValues);
      }
      catch (Exception exception)
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.EnrichmentFailed,
          exception.Message,
          rawPath,
          finalPath,
          rawLength,
          rawHash,
          document.Schema);
      }
      if (enrichment == null || !enrichment.Success)
      {
        HbrIfcEnrichmentFieldResult failed = enrichment?.Fields?
          .FirstOrDefault(value => value == null || !value.Success);
        return FailureWithRaw(
          HifcCoreErrorCodes.EnrichmentFailed,
          failed == null
            ? "IFC enrichment 未返回成功结果。"
            : (failed.ErrorCode ?? string.Empty) + "："
              + (failed.Message ?? string.Empty),
          rawPath,
          finalPath,
          rawLength,
          rawHash,
          document.Schema,
          MapEnrichmentEvidence(enrichment));
      }

      HifcValidationResult memoryValidation = InspectDocument(
        document,
        internalValues,
        rawPath,
        rawLength,
        rawHash);
      if (!memoryValidation.Success)
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.ExactValidationFailed,
          "发布前内存 candidate 精确回读失败："
            + memoryValidation.Message,
          rawPath,
          finalPath,
          rawLength,
          rawHash,
          document.Schema,
          memoryValidation.Fields);
      }

      string candidatePath = Path.Combine(
        quarantineDirectory,
        "." + Path.GetFileName(finalPath) + "."
          + Guid.NewGuid().ToString("N") + ".candidate.ifc");
      try
      {
        Directory.CreateDirectory(quarantineDirectory);
        byte[] candidateBytes = StrictUtf8.GetBytes(document.Serialize());
        using (var stream = new FileStream(
          candidatePath,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None))
        {
          stream.Write(candidateBytes, 0, candidateBytes.Length);
          stream.Flush(true);
        }
      }
      catch (Exception exception)
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.CandidateWriteFailed,
          exception.Message,
          rawPath,
          finalPath,
          rawLength,
          rawHash,
          document.Schema,
          memoryValidation.Fields,
          candidatePath);
      }

      HifcValidationResult diskValidation;
      try
      {
        diskValidation = ValidateFile(candidatePath, fields);
      }
      catch (Exception exception)
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.CandidateRereadFailed,
          exception.Message,
          rawPath,
          finalPath,
          rawLength,
          rawHash,
          document.Schema,
          memoryValidation.Fields,
          candidatePath);
      }
      if (!diskValidation.Success)
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.ExactValidationFailed,
          "磁盘 candidate 精确回读失败：" + diskValidation.Message,
          rawPath,
          finalPath,
          rawLength,
          rawHash,
          document.Schema,
          diskValidation.Fields,
          candidatePath);
      }

      try
      {
        EnsureRawUnchanged(rawPath, rawLength, rawHash);
      }
      catch (Exception exception)
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.RawChanged,
          exception.Message,
          rawPath,
          finalPath,
          rawLength,
          rawHash,
          document.Schema,
          diskValidation.Fields,
          candidatePath);
      }

      try
      {
        if (File.Exists(finalPath) || Directory.Exists(finalPath))
          throw new IOException("H-IFC 正式目标已存在：" + finalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
        File.Move(candidatePath, finalPath);
      }
      catch (Exception exception)
      {
        return FailureWithRaw(
          HifcCoreErrorCodes.FinalPublishFailed,
          exception.Message,
          rawPath,
          finalPath,
          rawLength,
          rawHash,
          document.Schema,
          diskValidation.Fields,
          candidatePath);
      }

      var finalInfo = new FileInfo(finalPath);
      return new HifcTranslationResult
      {
        Success = true,
        InternalStatus = HifcCoreStatus.InternalValidated,
        IfcFluxStatus = HifcCoreStatus.IfcFluxManualPending,
        ErrorCode = HifcCoreErrorCodes.None,
        Message = "H-IFC 已通过插件内部 exact 回读，等待 IFCFlux 人工检查。",
        Schema = document.Schema,
        RawIfcPath = rawPath,
        RawIfcLength = rawLength,
        RawIfcSha256 = rawHash,
        CandidateIfcPath = string.Empty,
        FinalIfcPath = finalPath,
        FinalIfcLength = finalInfo.Length,
        FinalIfcSha256 = ComputeSha256(finalPath),
        Fields = diskValidation.Fields
      };
    }

    public static HifcValidationResult ValidateFile(
      string ifcPath,
      IEnumerable<HifcFieldRequest> fields)
    {
      string path = RequireAbsoluteFile(ifcPath, "IFC");
      HifcFieldRequest[] frozen = FreezeAndValidateFields(fields);
      byte[] bytes = File.ReadAllBytes(path);
      if (bytes.Length == 0)
      {
        return ValidationFailure(
          HifcCoreErrorCodes.RawMissing,
          "IFC 文件为空。",
          path,
          0,
          string.Empty,
          string.Empty);
      }
      string hash = ComputeSha256(bytes);
      string text;
      try
      {
        text = StrictUtf8.GetString(bytes);
      }
      catch (Exception exception)
      {
        return ValidationFailure(
          HifcCoreErrorCodes.EncodingFailed,
          exception.Message,
          path,
          bytes.LongLength,
          hash,
          string.Empty);
      }
      IfcStepDocument document;
      try
      {
        document = IfcStepDocument.Parse(text);
        document.ValidateStructure();
      }
      catch (Exception exception)
      {
        return ValidationFailure(
          HifcCoreErrorCodes.StepParseFailed,
          exception.Message,
          path,
          bytes.LongLength,
          hash,
          string.Empty);
      }
      if (!string.Equals(document.Schema, "IFC4", StringComparison.OrdinalIgnoreCase))
      {
        return ValidationFailure(
          HifcCoreErrorCodes.SchemaUnsupported,
          "H-IFC exact 回读仅支持 IFC4。",
          path,
          bytes.LongLength,
          hash,
          document.Schema);
      }
      return InspectDocument(
        document,
        frozen.Select(ToInternalValue).ToArray(),
        path,
        bytes.LongLength,
        hash);
    }

    public static string ComputeSha256(string path)
    {
      using (SHA256 algorithm = SHA256.Create())
      using (FileStream stream = File.OpenRead(path))
      {
        return ToLowerHex(algorithm.ComputeHash(stream));
      }
    }

    private static HifcValidationResult InspectDocument(
      IfcStepDocument document,
      IReadOnlyList<HbrIfcEnrichmentValue> values,
      string path,
      long length,
      string hash)
    {
      HbrIfcBatchInspectionResult inspection;
      try
      {
        inspection = new HbrIfcFieldInspector().InspectMany(document, values);
      }
      catch (Exception exception)
      {
        return ValidationFailure(
          HifcCoreErrorCodes.ExactValidationFailed,
          exception.Message,
          path,
          length,
          hash,
          document.Schema);
      }
      IReadOnlyList<HifcFieldEvidence> evidence = MapInspectionEvidence(inspection);
      if (inspection == null || !inspection.Success)
      {
        return ValidationFailure(
          HifcCoreErrorCodes.ExactValidationFailed,
          inspection == null ? "IFC inspection 未返回结果。" : inspection.Message,
          path,
          length,
          hash,
          document.Schema,
          evidence);
      }
      string[] expected = values.Select(value => value.PropertyIdentity)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string[] actual = evidence.Select(value => value.PropertyIdentity)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      if (!expected.SequenceEqual(actual, StringComparer.Ordinal)
        || evidence.Any(value => !value.Success))
      {
        return ValidationFailure(
          HifcCoreErrorCodes.ExactValidationFailed,
          "IFC inspection property identity 对账失败。",
          path,
          length,
          hash,
          document.Schema,
          evidence);
      }
      return new HifcValidationResult
      {
        Success = true,
        InternalStatus = HifcCoreStatus.InternalValidated,
        IfcFluxStatus = HifcCoreStatus.IfcFluxManualPending,
        ErrorCode = HifcCoreErrorCodes.None,
        Message = "IFC exact 回读通过。",
        Schema = document.Schema,
        IfcPath = path,
        IfcLength = length,
        IfcSha256 = hash,
        Fields = evidence
      };
    }

    private static HbrIfcEnrichmentValue ToInternalValue(HifcFieldRequest field)
    {
      return new HbrIfcEnrichmentValue
      {
        PropertyIdentity = field.PropertyIdentity,
        SemanticKey = string.IsNullOrWhiteSpace(field.SemanticKey)
          ? field.PropertyIdentity
          : field.SemanticKey,
        OwnerEntityType = field.OwnerEntityType,
        OwnerGlobalId = string.IsNullOrWhiteSpace(field.OwnerGlobalId)
          ? null
          : field.OwnerGlobalId,
        OwnerStrategy = field.OwnerStrategy,
        PropertySetName = field.PropertySetName,
        PropertyName = field.PropertyName,
        DeclaredIfcType = field.DeclaredIfcType,
        CanonicalValue = field.CanonicalValue
      };
    }

    private static HifcFieldRequest[] FreezeAndValidateFields(
      IEnumerable<HifcFieldRequest> fields)
    {
      HifcFieldRequest[] materialized = (fields
        ?? throw new ArgumentNullException(nameof(fields)))
        .Select(CloneField)
        .OrderBy(value => value.PropertyIdentity, StringComparer.Ordinal)
        .ToArray();
      if (materialized.Length == 0)
        throw new InvalidDataException("H-IFC 转译至少需要一个字段。" );
      if (materialized.Any(value =>
        string.IsNullOrWhiteSpace(value.PropertyIdentity)
        || string.IsNullOrWhiteSpace(value.OwnerEntityType)
        || string.IsNullOrWhiteSpace(value.OwnerStrategy)
        || string.IsNullOrWhiteSpace(value.PropertySetName)
        || string.IsNullOrWhiteSpace(value.PropertyName)
        || string.IsNullOrWhiteSpace(value.DeclaredIfcType)))
      {
        throw new InvalidDataException("H-IFC 字段请求不完整。" );
      }
      if (materialized.Select(value => value.PropertyIdentity)
        .Distinct(StringComparer.Ordinal).Count() != materialized.Length)
      {
        throw new InvalidDataException("H-IFC property identity 必须唯一。" );
      }
      foreach (HifcFieldRequest field in materialized)
      {
        if (string.Equals(
          field.OwnerStrategy,
          HifcOwnerStrategies.GlobalId,
          StringComparison.Ordinal)
          && string.IsNullOrWhiteSpace(field.OwnerGlobalId))
        {
          throw new InvalidDataException(
            "GLOBAL_ID owner strategy 必须提供 OwnerGlobalId："
            + field.PropertyIdentity);
        }
      }
      return materialized;
    }

    private static HifcFieldRequest CloneField(HifcFieldRequest source)
    {
      if (source == null) throw new InvalidDataException("H-IFC 字段不能为 null。" );
      return new HifcFieldRequest
      {
        PropertyIdentity = source.PropertyIdentity ?? string.Empty,
        SemanticKey = source.SemanticKey ?? string.Empty,
        OwnerEntityType = source.OwnerEntityType ?? string.Empty,
        OwnerGlobalId = source.OwnerGlobalId ?? string.Empty,
        OwnerStrategy = source.OwnerStrategy ?? string.Empty,
        PropertySetName = source.PropertySetName ?? string.Empty,
        PropertyName = source.PropertyName ?? string.Empty,
        DeclaredIfcType = source.DeclaredIfcType ?? string.Empty,
        CanonicalValue = source.CanonicalValue ?? string.Empty,
        CanonicalUnit = source.CanonicalUnit ?? string.Empty
      };
    }

    private static IReadOnlyList<HifcFieldEvidence> MapInspectionEvidence(
      HbrIfcBatchInspectionResult inspection)
    {
      if (inspection == null) return Array.Empty<HifcFieldEvidence>();
      return new ReadOnlyCollection<HifcFieldEvidence>(inspection.Fields
        .Select(value => new HifcFieldEvidence
        {
          PropertyIdentity = value.PropertyIdentity ?? string.Empty,
          Success = value.Success,
          ErrorCode = value.ErrorCode ?? string.Empty,
          Message = value.Message ?? string.Empty,
          OwnerId = value.OwnerId,
          PropertySetId = value.PropertySetId,
          PropertyId = value.PropertyId,
          RelationshipId = value.RelationshipId,
          ActualIfcType = value.ActualIfcType ?? string.Empty,
          TypedToken = value.TypedToken ?? string.Empty
        }).ToArray());
    }

    private static IReadOnlyList<HifcFieldEvidence> MapEnrichmentEvidence(
      HbrIfcEnrichmentResult enrichment)
    {
      if (enrichment == null) return Array.Empty<HifcFieldEvidence>();
      return new ReadOnlyCollection<HifcFieldEvidence>(enrichment.Fields
        .Where(value => value != null)
        .Select(value => new HifcFieldEvidence
        {
          PropertyIdentity = value.PropertyIdentity ?? string.Empty,
          Success = value.Success,
          ErrorCode = value.ErrorCode ?? string.Empty,
          Message = value.Message ?? string.Empty,
          OwnerId = value.OwnerId,
          PropertySetId = value.PropertySetId,
          PropertyId = value.PropertyId,
          RelationshipId = value.RelationshipId
        }).ToArray());
    }

    private static void EnsureRawUnchanged(
      string rawPath,
      long expectedLength,
      string expectedHash)
    {
      var info = new FileInfo(rawPath);
      if (!info.Exists
        || info.Length != expectedLength
        || !string.Equals(
          ComputeSha256(rawPath),
          expectedHash,
          StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidDataException(
          "RAW IFC 在 H-IFC 转译期间发生变化。" );
      }
    }

    private static string RequireAbsoluteFile(string path, string label)
    {
      if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        throw new ArgumentException(label + " 路径必须是绝对路径。" );
      return Path.GetFullPath(path);
    }

    private static string RequireAbsoluteOutput(string path, string label)
    {
      string full = RequireAbsoluteFile(path, label);
      string directory = Path.GetDirectoryName(full);
      if (string.IsNullOrWhiteSpace(directory))
        throw new ArgumentException(label + " 缺少输出目录。" );
      return full;
    }

    private static string RequireAbsoluteDirectory(string path, string label)
    {
      if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        throw new ArgumentException(label + " 目录必须是绝对路径。" );
      return Path.GetFullPath(path);
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
      return string.Concat(bytes.Select(value =>
        value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static HifcTranslationResult Failure(
      string errorCode,
      string message,
      string rawPath,
      string finalPath)
    {
      return FailureWithRaw(
        errorCode,
        message,
        rawPath,
        finalPath,
        0,
        string.Empty);
    }

    private static HifcTranslationResult FailureWithRaw(
      string errorCode,
      string message,
      string rawPath,
      string finalPath,
      long rawLength,
      string rawHash,
      string schema = "",
      IReadOnlyList<HifcFieldEvidence> fields = null,
      string candidatePath = "")
    {
      return new HifcTranslationResult
      {
        Success = false,
        InternalStatus = HifcCoreStatus.InternalFailed,
        IfcFluxStatus = HifcCoreStatus.IfcFluxManualPending,
        ErrorCode = errorCode ?? HifcCoreErrorCodes.ExactValidationFailed,
        Message = message ?? string.Empty,
        Schema = schema ?? string.Empty,
        RawIfcPath = rawPath ?? string.Empty,
        RawIfcLength = rawLength,
        RawIfcSha256 = rawHash ?? string.Empty,
        CandidateIfcPath = candidatePath ?? string.Empty,
        FinalIfcPath = finalPath ?? string.Empty,
        Fields = fields ?? Array.Empty<HifcFieldEvidence>()
      };
    }

    private static HifcValidationResult ValidationFailure(
      string errorCode,
      string message,
      string path,
      long length,
      string hash,
      string schema,
      IReadOnlyList<HifcFieldEvidence> fields = null)
    {
      return new HifcValidationResult
      {
        Success = false,
        InternalStatus = HifcCoreStatus.InternalFailed,
        IfcFluxStatus = HifcCoreStatus.IfcFluxManualPending,
        ErrorCode = errorCode ?? HifcCoreErrorCodes.ExactValidationFailed,
        Message = message ?? string.Empty,
        Schema = schema ?? string.Empty,
        IfcPath = path ?? string.Empty,
        IfcLength = length,
        IfcSha256 = hash ?? string.Empty,
        Fields = fields ?? Array.Empty<HifcFieldEvidence>()
      };
    }
  }
}
