using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Stage03;

namespace BIMBaoGui.Stage01.Diagnostics
{
  public sealed class Stage03FieldReportContext
  {
    public string RunId { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public string PluginVersion { get; set; } = string.Empty;
    public string RevitVersion { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public string DocumentPath { get; set; } = string.Empty;
    public string DocumentFingerprint { get; set; } = string.Empty;
    public string FileGuid { get; set; } = string.Empty;
    public string FileContextHash { get; set; } = string.Empty;
    public string RulePackageId { get; set; } = string.Empty;
    public string RulePackageVersion { get; set; } = string.Empty;
    public string RulePackageSha256 { get; set; } = string.Empty;
    public Stage03GateDecision GateDecision { get; set; }
    public Stage03OutputPaths OutputPaths { get; set; }
    public string RawIfcSha256 { get; set; } = string.Empty;
    public string FinalIfcSha256 { get; set; } = string.Empty;
    public IReadOnlyList<Stage03CarrierResult> Carriers { get; set; }
      = Array.Empty<Stage03CarrierResult>();
    public IReadOnlyList<Stage03FieldResult> Fields { get; set; }
      = Array.Empty<Stage03FieldResult>();
    public IReadOnlyList<Stage03Diagnostic> Diagnostics { get; set; }
      = Array.Empty<Stage03Diagnostic>();
  }

  public sealed class Stage03FieldReportWriteResult
  {
    internal Stage03FieldReportWriteResult(
      string reportPath,
      string payloadSha256,
      string publishedSha256)
    {
      ReportPath = reportPath;
      PayloadSha256 = payloadSha256;
      PublishedSha256 = publishedSha256;
    }

    public string ReportPath { get; }
    public string PayloadSha256 { get; }
    public string PublishedSha256 { get; }
  }

  public static class Stage03FieldReportWriter
  {
    private const string SchemaVersion = "1.0";
    private const string ReportHashScope = "REPORT_WITH_EMPTY_SHA256";
    private static readonly UTF8Encoding Utf8WithoutBom =
      new UTF8Encoding(false, true);

    public static Stage03FieldReportWriteResult Write(
      Stage03FieldReportContext context)
    {
      return Write(context, null);
    }

    internal static Stage03FieldReportWriteResult Write(
      Stage03FieldReportContext context,
      Action<string> serializationPassObserver)
    {
      ReportSnapshot snapshot = ValidateAndSnapshot(context);
      string payloadSha256 = ComputePayloadSha256(
        snapshot,
        serializationPassObserver);
      serializationPassObserver?.Invoke("PUBLISHED");
      byte[] published = Serialize(snapshot, payloadSha256);
      AtomicJsonReportWriter.WriteTrustedJson(
        snapshot.Paths.FieldReport,
        published);
      return new Stage03FieldReportWriteResult(
        snapshot.Paths.FieldReport,
        payloadSha256,
        Sha256(published));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ComputePayloadSha256(
      ReportSnapshot snapshot,
      Action<string> serializationPassObserver)
    {
      serializationPassObserver?.Invoke("HASH_INPUT");
      return Sha256(Serialize(snapshot, string.Empty));
    }

    private static ReportSnapshot ValidateAndSnapshot(
      Stage03FieldReportContext context)
    {
      if (context == null) throw new ArgumentNullException(nameof(context));
      if (context.OutputPaths == null)
        throw new ArgumentException("Stage03 输出路径不能为空。", nameof(context));
      if (context.GateDecision == null)
        throw new ArgumentException("Stage03 门禁决策不能为空。", nameof(context));
      if (context.Carriers == null || context.Fields == null
        || context.Diagnostics == null)
      {
        throw new ArgumentException(
          "Stage03 carriers、fields、diagnostics 数组不能为 null。",
          nameof(context));
      }
      if (!string.Equals(
        context.RunId ?? string.Empty,
        context.OutputPaths.RunId,
        StringComparison.Ordinal))
      {
        throw new ArgumentException(
          "字段报告 runId 与三件套路径 runId 不一致。",
          nameof(context));
      }
      if (context.CompletedUtc < context.StartedUtc)
        throw new ArgumentException("字段报告完成时间早于开始时间。", nameof(context));

      Stage03FieldResult[] fields = context.Fields.ToArray();
      Stage03CarrierResult[] carriers = context.Carriers.ToArray();
      Stage03Diagnostic[] diagnostics = context.Diagnostics.ToArray();
      if (fields.Any(item => item == null)
        || carriers.Any(item => item == null)
        || diagnostics.Any(item => item == null))
      {
        throw new ArgumentException(
          "Stage03 报告数组不能包含 null 项。",
          nameof(context));
      }

      Stage03OutputPaths paths = SnapshotPaths(context.OutputPaths);
      var frozenContext = new Stage03FieldReportContext
      {
        RunId = context.RunId ?? string.Empty,
        StartedUtc = context.StartedUtc,
        CompletedUtc = context.CompletedUtc,
        PluginVersion = context.PluginVersion ?? string.Empty,
        RevitVersion = context.RevitVersion ?? string.Empty,
        DocumentTitle = context.DocumentTitle ?? string.Empty,
        DocumentPath = context.DocumentPath ?? string.Empty,
        DocumentFingerprint = context.DocumentFingerprint ?? string.Empty,
        FileGuid = context.FileGuid ?? string.Empty,
        FileContextHash = context.FileContextHash ?? string.Empty,
        RulePackageId = context.RulePackageId ?? string.Empty,
        RulePackageVersion = context.RulePackageVersion ?? string.Empty,
        RulePackageSha256 = context.RulePackageSha256 ?? string.Empty,
        GateDecision = SnapshotGateDecision(context.GateDecision),
        OutputPaths = paths,
        RawIfcSha256 = context.RawIfcSha256 ?? string.Empty,
        FinalIfcSha256 = context.FinalIfcSha256 ?? string.Empty
      };
      Stage03CarrierResult[] frozenCarriers = carriers
        .Select(SnapshotCarrier)
        .OrderBy(item => item, CarrierResultComparer.Instance)
        .ToArray();
      Stage03FieldResult[] frozenFields = fields
        .Select(SnapshotField)
        .OrderBy(item => SortText(item.Entity), StringComparer.Ordinal)
        .ThenBy(item => SortText(item.OwnerUniqueId), StringComparer.Ordinal)
        .ThenBy(item => SortText(item.PropertyId), StringComparer.Ordinal)
        .ThenBy(item => item, FieldResultComparer.Instance)
        .ToArray();
      Stage03Diagnostic[] frozenDiagnostics = diagnostics
        .Select(SnapshotDiagnostic)
        .OrderBy(item => item, DiagnosticComparer.Instance)
        .ToArray();
      frozenContext.Carriers = frozenCarriers;
      frozenContext.Fields = frozenFields;
      frozenContext.Diagnostics = frozenDiagnostics;
      return new ReportSnapshot(
        frozenContext,
        frozenCarriers,
        frozenFields,
        frozenDiagnostics);
    }

    private static Stage03OutputPaths SnapshotPaths(Stage03OutputPaths value)
    {
      return new Stage03OutputPaths(
        value.OutputDirectory ?? string.Empty,
        value.RvtStem ?? string.Empty,
        value.RunId ?? string.Empty,
        value.RawIfc ?? string.Empty,
        value.FinalIfc ?? string.Empty,
        value.FieldReport ?? string.Empty);
    }

    private static Stage03GateDecision SnapshotGateDecision(
      Stage03GateDecision value)
    {
      Stage03BusinessBlocker[] blockers =
        (value.BusinessBlockers ?? Array.Empty<Stage03BusinessBlocker>())
          .ToArray();
      if (blockers.Any(item => item == null))
        throw new ArgumentException("Stage03 门禁 blocker 不能包含 null 项。");
      return new Stage03GateDecision(
        value.Mode,
        value.AllowExport,
        value.Forced,
        value.Reason ?? string.Empty,
        blockers.Select(item => new Stage03BusinessBlocker(
          item.Entity,
          item.OwnerUniqueId,
          item.Role,
          item.ElementId,
          item.PropertyId,
          item.Status,
          item.StatusCode,
          item.Requirement,
          item.Message)).ToArray(),
        SnapshotStrings(value.TechnicalFatalCodes),
        SnapshotStrings(value.Messages));
    }

    private static Stage03CarrierResult SnapshotCarrier(
      Stage03CarrierResult value)
    {
      return new Stage03CarrierResult
      {
        Entity = value.Entity ?? string.Empty,
        Role = value.Role ?? string.Empty,
        ElementId = value.ElementId,
        UniqueId = value.UniqueId ?? string.Empty,
        Category = value.Category ?? string.Empty,
        Name = value.Name ?? string.Empty,
        Status = value.Status,
        Active = value.Active,
        IsBusinessBlocker = value.IsBusinessBlocker,
        Messages = SortStrings(value.Messages)
      };
    }

    private static Stage03FieldResult SnapshotField(Stage03FieldResult value)
    {
      return new Stage03FieldResult
      {
        PropertyId = value.PropertyId ?? string.Empty,
        ContractKind = value.ContractKind ?? string.Empty,
        Requirement = value.Requirement ?? string.Empty,
        Applicability = value.Applicability ?? string.Empty,
        Entity = value.Entity ?? string.Empty,
        PropertySet = value.PropertySet ?? string.Empty,
        IfcProperty = value.IfcProperty ?? string.Empty,
        Role = value.Role ?? string.Empty,
        ElementId = value.ElementId,
        OwnerUniqueId = value.OwnerUniqueId ?? string.Empty,
        ParameterGuid = value.ParameterGuid ?? string.Empty,
        ParameterName = value.ParameterName ?? string.Empty,
        ParameterScope = value.ParameterScope ?? string.Empty,
        CarrierStatus = value.CarrierStatus,
        ParameterStatus = value.ParameterStatus,
        RevitStatus = value.RevitStatus,
        RevitRawValue = value.RevitRawValue ?? string.Empty,
        RevitNormalizedValue = value.RevitNormalizedValue ?? string.Empty,
        RevitValueSource = value.RevitValueSource ?? string.Empty,
        RawIfcOwner = value.RawIfcOwner ?? string.Empty,
        RawIfcPropertySet = value.RawIfcPropertySet ?? string.Empty,
        RawIfcProperty = value.RawIfcProperty ?? string.Empty,
        RawIfcType = value.RawIfcType ?? string.Empty,
        RawIfcValue = value.RawIfcValue ?? string.Empty,
        RawIfcStatus = value.RawIfcStatus,
        FinalIfcOwner = value.FinalIfcOwner ?? string.Empty,
        FinalIfcPropertySet = value.FinalIfcPropertySet ?? string.Empty,
        FinalIfcProperty = value.FinalIfcProperty ?? string.Empty,
        FinalIfcType = value.FinalIfcType ?? string.Empty,
        FinalIfcValue = value.FinalIfcValue ?? string.Empty,
        FinalIfcStatus = value.FinalIfcStatus,
        Status = value.Status,
        Active = value.Active,
        IsBusinessBlocker = value.IsBusinessBlocker,
        Messages = SortStrings(value.Messages)
      };
    }

    private static Stage03Diagnostic SnapshotDiagnostic(Stage03Diagnostic value)
    {
      return new Stage03Diagnostic
      {
        Code = value.Code ?? string.Empty,
        Stage = value.Stage ?? string.Empty,
        Severity = value.Severity ?? string.Empty,
        Message = value.Message ?? string.Empty
      };
    }

    private static string[] SnapshotStrings(IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>())
        .Select(value => value ?? string.Empty)
        .ToArray();
    }

    private static byte[] Serialize(
      ReportSnapshot snapshot,
      string reportSha256)
    {
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 256
      };
      string minified = serializer.Serialize(BuildReport(
        snapshot,
        reportSha256));
      return Utf8WithoutBom.GetBytes(FormatJson(minified));
    }

    private static Dictionary<string, object> BuildReport(
      ReportSnapshot snapshot,
      string reportSha256)
    {
      Stage03FieldReportContext context = snapshot.Context;
      return new Dictionary<string, object>
      {
        ["schemaVersion"] = SchemaVersion,
        ["runId"] = context.RunId,
        ["startedUtc"] = context.StartedUtc.ToString(
          "O", CultureInfo.InvariantCulture),
        ["completedUtc"] = context.CompletedUtc.ToString(
          "O", CultureInfo.InvariantCulture),
        ["pluginVersion"] = context.PluginVersion,
        ["revitVersion"] = context.RevitVersion,
        ["document"] = new Dictionary<string, object>
        {
          ["title"] = context.DocumentTitle,
          ["path"] = context.DocumentPath,
          ["fingerprint"] = context.DocumentFingerprint,
          ["fileGuid"] = context.FileGuid
        },
        ["fileContextHash"] = context.FileContextHash,
        ["rulePackage"] = new Dictionary<string, object>
        {
          ["id"] = context.RulePackageId,
          ["version"] = context.RulePackageVersion,
          ["hash"] = context.RulePackageSha256
        },
        ["gate"] = BuildGate(context.GateDecision),
        ["artifacts"] = new Dictionary<string, object>
        {
          ["rawIfc"] = Artifact(
            context.OutputPaths.RawIfc,
            context.RawIfcSha256),
          ["finalIfc"] = Artifact(
            context.OutputPaths.FinalIfc,
            context.FinalIfcSha256),
          ["report"] = new Dictionary<string, object>
          {
            ["path"] = context.OutputPaths.FieldReport,
            ["sha256"] = reportSha256,
            ["sha256Scope"] = ReportHashScope
          }
        },
        ["summary"] = BuildSummary(snapshot.Fields),
        ["carriers"] = snapshot.Carriers.Select(BuildCarrier).ToArray(),
        ["fields"] = snapshot.Fields.Select(BuildField).ToArray(),
        ["diagnostics"] = snapshot.Diagnostics.Select(BuildDiagnostic).ToArray()
      };
    }

    private static Dictionary<string, object> BuildGate(
      Stage03GateDecision decision)
    {
      return new Dictionary<string, object>
      {
        ["mode"] = decision.Mode.ToString(),
        ["forced"] = decision.Forced,
        ["reason"] = decision.Reason,
        ["decision"] = decision.AllowExport ? "ALLOW" : "BLOCK",
        ["allowExport"] = decision.AllowExport,
        ["businessBlockers"] = decision.BusinessBlockers
          .OrderBy(item => item.Entity, StringComparer.Ordinal)
          .ThenBy(item => item.OwnerUniqueId, StringComparer.Ordinal)
          .ThenBy(item => item.PropertyId, StringComparer.Ordinal)
          .ThenBy(item => item.StatusCode, StringComparer.Ordinal)
          .Select(item => new Dictionary<string, object>
          {
            ["entity"] = item.Entity,
            ["ownerUniqueId"] = item.OwnerUniqueId,
            ["role"] = item.Role,
            ["elementId"] = item.ElementId,
            ["propertyId"] = item.PropertyId,
            ["status"] = item.StatusCode,
            ["requirement"] = item.Requirement,
            ["message"] = item.Message
          }).ToArray(),
        ["technicalFatalCodes"] = SortStrings(
          decision.TechnicalFatalCodes),
        ["messages"] = SortStrings(decision.Messages)
      };
    }

    private static Dictionary<string, object> BuildSummary(
      IEnumerable<Stage03FieldResult> fields)
    {
      Stage03FieldResult[] values = fields.ToArray();
      return new Dictionary<string, object>
      {
        ["totalFields"] = values.Length,
        ["activeFields"] = values.Count(item => item.Active),
        ["businessBlockers"] = values.Count(item => item.Active
          && (item.IsBusinessBlocker
            || item.Status == Stage03FieldStatus.UnclassifiedRequirement)
          && item.Status != Stage03FieldStatus.Pass
          && item.Status != Stage03FieldStatus.NotApplicable),
        ["byStatus"] = Counts(values.Select(item =>
          Stage03FieldStatusCodes.ToCode(item.Status))),
        ["byEntity"] = Counts(values.Select(item => Normalize(item.Entity))),
        ["byPropertySet"] = Counts(values.Select(item =>
          Normalize(item.PropertySet))),
        ["byRequirement"] = Counts(values.Select(item =>
          Normalize(item.Requirement)))
      };
    }

    private static Dictionary<string, object> BuildCarrier(
      Stage03CarrierResult carrier)
    {
      return new Dictionary<string, object>
      {
        ["entity"] = carrier.Entity,
        ["role"] = carrier.Role,
        ["elementId"] = carrier.ElementId,
        ["uniqueId"] = carrier.UniqueId,
        ["category"] = carrier.Category,
        ["name"] = carrier.Name,
        ["status"] = Stage03FieldStatusCodes.ToCode(carrier.Status),
        ["active"] = carrier.Active,
        ["isBusinessBlocker"] = carrier.IsBusinessBlocker,
        ["messages"] = SortStrings(carrier.Messages)
      };
    }

    private static Dictionary<string, object> BuildField(
      Stage03FieldResult field)
    {
      return new Dictionary<string, object>
      {
        ["propertyId"] = field.PropertyId,
        ["contractKind"] = field.ContractKind,
        ["requirement"] = field.Requirement,
        ["applicability"] = field.Applicability,
        ["entity"] = field.Entity,
        ["propertySet"] = field.PropertySet,
        ["ifcProperty"] = field.IfcProperty,
        ["role"] = field.Role,
        ["elementId"] = field.ElementId,
        ["ownerUniqueId"] = field.OwnerUniqueId,
        ["parameterGuid"] = field.ParameterGuid,
        ["parameterName"] = field.ParameterName,
        ["parameterScope"] = field.ParameterScope,
        ["carrierStatus"] = Stage03FieldStatusCodes.ToCode(
          field.CarrierStatus),
        ["parameterStatus"] = Stage03FieldStatusCodes.ToCode(
          field.ParameterStatus),
        ["revitStatus"] = Stage03FieldStatusCodes.ToCode(field.RevitStatus),
        ["revitRawValue"] = field.RevitRawValue,
        ["revitNormalizedValue"] = field.RevitNormalizedValue,
        ["revitValueSource"] = field.RevitValueSource,
        ["rawIfcOwner"] = field.RawIfcOwner,
        ["rawIfcPropertySet"] = field.RawIfcPropertySet,
        ["rawIfcProperty"] = field.RawIfcProperty,
        ["rawIfcType"] = field.RawIfcType,
        ["rawIfcValue"] = field.RawIfcValue,
        ["rawIfcStatus"] = Stage03FieldStatusCodes.ToCode(
          field.RawIfcStatus),
        ["finalIfcOwner"] = field.FinalIfcOwner,
        ["finalIfcPropertySet"] = field.FinalIfcPropertySet,
        ["finalIfcProperty"] = field.FinalIfcProperty,
        ["finalIfcType"] = field.FinalIfcType,
        ["finalIfcValue"] = field.FinalIfcValue,
        ["finalIfcStatus"] = Stage03FieldStatusCodes.ToCode(
          field.FinalIfcStatus),
        ["status"] = Stage03FieldStatusCodes.ToCode(field.Status),
        ["active"] = field.Active,
        ["isBusinessBlocker"] = field.IsBusinessBlocker,
        ["messages"] = SortStrings(field.Messages)
      };
    }

    private static Dictionary<string, object> BuildDiagnostic(
      Stage03Diagnostic diagnostic)
    {
      return new Dictionary<string, object>
      {
        ["code"] = diagnostic.Code,
        ["stage"] = diagnostic.Stage,
        ["severity"] = diagnostic.Severity,
        ["message"] = diagnostic.Message
      };
    }

    private static Dictionary<string, object> Artifact(
      string path,
      string sha256)
    {
      return new Dictionary<string, object>
      {
        ["path"] = path,
        ["sha256"] = sha256 ?? string.Empty
      };
    }

    private static Dictionary<string, object> Counts(IEnumerable<string> values)
    {
      var result = new Dictionary<string, object>();
      foreach (IGrouping<string, string> group in values
        .GroupBy(value => value ?? string.Empty, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal))
      {
        result.Add(group.Key, group.Count());
      }
      return result;
    }

    private static string[] SortStrings(IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>())
        .Select(value => value ?? string.Empty)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    private static string Normalize(string value)
    {
      return (value ?? string.Empty).Trim();
    }

    private static string SortText(string value)
    {
      return value ?? string.Empty;
    }

    private static string Sha256(byte[] bytes)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        return string.Concat(algorithm.ComputeHash(bytes).Select(item =>
          item.ToString("x2", CultureInfo.InvariantCulture)));
      }
    }

    private static string FormatJson(string json)
    {
      var builder = new StringBuilder(json.Length + 512);
      int indentation = 0;
      bool insideString = false;
      bool escaped = false;
      foreach (char character in json)
      {
        if (insideString)
        {
          builder.Append(character);
          if (escaped) escaped = false;
          else if (character == '\\') escaped = true;
          else if (character == '"') insideString = false;
          continue;
        }
        switch (character)
        {
          case '"':
            insideString = true;
            builder.Append(character);
            break;
          case '{':
          case '[':
            builder.Append(character);
            AppendLine(builder, ++indentation);
            break;
          case '}':
          case ']':
            AppendLine(builder, --indentation);
            builder.Append(character);
            break;
          case ',':
            builder.Append(character);
            AppendLine(builder, indentation);
            break;
          case ':':
            builder.Append(": ");
            break;
          default:
            if (!char.IsWhiteSpace(character)) builder.Append(character);
            break;
        }
      }
      builder.Append('\n');
      return builder.ToString();
    }

    private static void AppendLine(StringBuilder builder, int indentation)
    {
      builder.Append('\n');
      builder.Append(' ', Math.Max(0, indentation) * 2);
    }

    private sealed class CarrierResultComparer
      : IComparer<Stage03CarrierResult>
    {
      internal static readonly CarrierResultComparer Instance =
        new CarrierResultComparer();

      public int Compare(Stage03CarrierResult left, Stage03CarrierResult right)
      {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        int comparison = CompareText(left.Entity, right.Entity);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.Role, right.Role);
        if (comparison != 0) return comparison;
        comparison = left.ElementId.CompareTo(right.ElementId);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.UniqueId, right.UniqueId);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.Category, right.Category);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.Name, right.Name);
        if (comparison != 0) return comparison;
        comparison = CompareStatus(left.Status, right.Status);
        if (comparison != 0) return comparison;
        comparison = left.Active.CompareTo(right.Active);
        if (comparison != 0) return comparison;
        comparison = left.IsBusinessBlocker.CompareTo(right.IsBusinessBlocker);
        return comparison != 0
          ? comparison
          : CompareMessages(left.Messages, right.Messages);
      }

      private static int CompareText(string left, string right)
      {
        return string.Compare(
          left ?? string.Empty,
          right ?? string.Empty,
          StringComparison.Ordinal);
      }

      private static int CompareStatus(
        Stage03FieldStatus left,
        Stage03FieldStatus right)
      {
        return CompareText(
          Stage03FieldStatusCodes.ToCode(left),
          Stage03FieldStatusCodes.ToCode(right));
      }

      private static int CompareMessages(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
      {
        IReadOnlyList<string> leftValues = left ?? Array.Empty<string>();
        IReadOnlyList<string> rightValues = right ?? Array.Empty<string>();
        int shared = Math.Min(leftValues.Count, rightValues.Count);
        for (int index = 0; index < shared; index++)
        {
          int comparison = CompareText(
            leftValues[index],
            rightValues[index]);
          if (comparison != 0) return comparison;
        }
        return leftValues.Count.CompareTo(rightValues.Count);
      }
    }

    private sealed class DiagnosticComparer
      : IComparer<Stage03Diagnostic>
    {
      internal static readonly DiagnosticComparer Instance =
        new DiagnosticComparer();

      public int Compare(Stage03Diagnostic left, Stage03Diagnostic right)
      {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        int comparison = CompareText(left.Code, right.Code);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.Stage, right.Stage);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.Severity, right.Severity);
        return comparison != 0
          ? comparison
          : CompareText(left.Message, right.Message);
      }

      private static int CompareText(string left, string right)
      {
        return string.Compare(
          left ?? string.Empty,
          right ?? string.Empty,
          StringComparison.Ordinal);
      }
    }

    private sealed class FieldResultComparer
      : IComparer<Stage03FieldResult>
    {
      internal static readonly FieldResultComparer Instance =
        new FieldResultComparer();

      public int Compare(Stage03FieldResult left, Stage03FieldResult right)
      {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        int comparison = CompareText(left.PropertyId, right.PropertyId);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.ContractKind, right.ContractKind);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.Requirement, right.Requirement);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.Applicability, right.Applicability);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.Entity, right.Entity);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.PropertySet, right.PropertySet);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.IfcProperty, right.IfcProperty);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.Role, right.Role);
        if (comparison != 0) return comparison;
        comparison = left.ElementId.CompareTo(right.ElementId);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.OwnerUniqueId, right.OwnerUniqueId);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.ParameterGuid, right.ParameterGuid);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.ParameterName, right.ParameterName);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.ParameterScope, right.ParameterScope);
        if (comparison != 0) return comparison;
        comparison = CompareStatus(left.CarrierStatus, right.CarrierStatus);
        if (comparison != 0) return comparison;
        comparison = CompareStatus(left.ParameterStatus, right.ParameterStatus);
        if (comparison != 0) return comparison;
        comparison = CompareStatus(left.RevitStatus, right.RevitStatus);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.RevitRawValue, right.RevitRawValue);
        if (comparison != 0) return comparison;
        comparison = CompareText(
          left.RevitNormalizedValue,
          right.RevitNormalizedValue);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.RevitValueSource, right.RevitValueSource);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.RawIfcOwner, right.RawIfcOwner);
        if (comparison != 0) return comparison;
        comparison = CompareText(
          left.RawIfcPropertySet,
          right.RawIfcPropertySet);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.RawIfcProperty, right.RawIfcProperty);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.RawIfcType, right.RawIfcType);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.RawIfcValue, right.RawIfcValue);
        if (comparison != 0) return comparison;
        comparison = CompareStatus(left.RawIfcStatus, right.RawIfcStatus);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.FinalIfcOwner, right.FinalIfcOwner);
        if (comparison != 0) return comparison;
        comparison = CompareText(
          left.FinalIfcPropertySet,
          right.FinalIfcPropertySet);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.FinalIfcProperty, right.FinalIfcProperty);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.FinalIfcType, right.FinalIfcType);
        if (comparison != 0) return comparison;
        comparison = CompareText(left.FinalIfcValue, right.FinalIfcValue);
        if (comparison != 0) return comparison;
        comparison = CompareStatus(left.FinalIfcStatus, right.FinalIfcStatus);
        if (comparison != 0) return comparison;
        comparison = CompareStatus(left.Status, right.Status);
        if (comparison != 0) return comparison;
        comparison = left.Active.CompareTo(right.Active);
        if (comparison != 0) return comparison;
        comparison = left.IsBusinessBlocker.CompareTo(right.IsBusinessBlocker);
        return comparison != 0
          ? comparison
          : CompareMessages(left.Messages, right.Messages);
      }

      private static int CompareText(string left, string right)
      {
        return string.Compare(
          left ?? string.Empty,
          right ?? string.Empty,
          StringComparison.Ordinal);
      }

      private static int CompareStatus(
        Stage03FieldStatus left,
        Stage03FieldStatus right)
      {
        return CompareText(
          Stage03FieldStatusCodes.ToCode(left),
          Stage03FieldStatusCodes.ToCode(right));
      }

      private static int CompareMessages(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
      {
        IReadOnlyList<string> leftValues = left ?? Array.Empty<string>();
        IReadOnlyList<string> rightValues = right ?? Array.Empty<string>();
        int shared = Math.Min(leftValues.Count, rightValues.Count);
        for (int index = 0; index < shared; index++)
        {
          int comparison = CompareText(
            leftValues[index],
            rightValues[index]);
          if (comparison != 0) return comparison;
        }
        return leftValues.Count.CompareTo(rightValues.Count);
      }
    }

    private sealed class ReportSnapshot
    {
      public ReportSnapshot(
        Stage03FieldReportContext context,
        Stage03CarrierResult[] carriers,
        Stage03FieldResult[] fields,
        Stage03Diagnostic[] diagnostics)
      {
        Context = context;
        Paths = context.OutputPaths;
        Carriers = carriers;
        Fields = fields;
        Diagnostics = diagnostics;
      }

      public Stage03FieldReportContext Context { get; }
      public Stage03OutputPaths Paths { get; }
      public Stage03CarrierResult[] Carriers { get; }
      public Stage03FieldResult[] Fields { get; }
      public Stage03Diagnostic[] Diagnostics { get; }
    }
  }
}
