using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BIMBaoGui.Stage01.Stage03
{
  internal static class Stage03SensitiveMetadataPolicy
  {
    private static readonly string[] IdentityMarkers =
    {
      "credential",
      "token",
      "secret",
      "password"
    };
    private static readonly string[] SecretPrefixes =
    {
      "sk-",
      "ghp_",
      "github_pat_",
      "akia",
      "aiza",
      "ya29.",
      "xoxb-",
      "xoxp-",
      "bearer-"
    };

    internal static bool Contains(string value)
    {
      string text = value ?? string.Empty;
      foreach (string marker in IdentityMarkers)
      {
        if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
          return true;
      }
      foreach (string prefix in SecretPrefixes)
      {
        if (HasPrefixAtSegmentBoundary(text, prefix)) return true;
      }
      return false;
    }

    private static bool HasPrefixAtSegmentBoundary(
      string value,
      string prefix)
    {
      int searchFrom = 0;
      while (searchFrom < value.Length)
      {
        int index = value.IndexOf(
          prefix,
          searchFrom,
          StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        if (index == 0 || !char.IsLetterOrDigit(value[index - 1]))
          return true;
        searchFrom = index + 1;
      }
      return false;
    }
  }

  internal static class Stage03RunIdPolicy
  {
    internal const int MaximumLength = 128;
    private static readonly Regex Pattern = new Regex(
      "^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$",
      RegexOptions.CultureInvariant);

    internal static bool IsValid(string value)
    {
      return !string.IsNullOrEmpty(value)
        && value.Length <= MaximumLength
        && Pattern.IsMatch(value)
        && !Stage03SensitiveMetadataPolicy.Contains(value);
    }
  }

  public enum Stage03FieldStatus
  {
    Pass,
    NotApplicable,
    MissingCarrier,
    CarrierCategoryMismatch,
    CarrierNameMismatch,
    AmbiguousCarrier,
    MissingParameter,
    EmptyRequiredValue,
    InvalidValue,
    RuleNotImplemented,
    UnclassifiedRequirement,
    IfcOwnerNotFound,
    IfcValueMismatch
  }

  public enum Stage03GateMode
  {
    Strict,
    Force
  }

  public static class Stage03TechnicalFatalCodes
  {
    public const string WrongDocument = "WRONG_DOCUMENT";
    public const string UnsupportedRevit = "UNSUPPORTED_REVIT";
    public const string DocumentUnavailable = "DOCUMENT_UNAVAILABLE";
    public const string OutputExists = "OUTPUT_EXISTS";
    public const string ExportFailed = "EXPORT_FAILED";
    public const string InvalidIfc = "INVALID_IFC";
    public const string ReportFailed = "REPORT_FAILED";
    public const string InvalidFieldStatus = "INVALID_FIELD_STATUS";
  }

  public static class Stage03FieldStatusCodes
  {
    public static string ToCode(Stage03FieldStatus status)
    {
      switch (status)
      {
        case Stage03FieldStatus.Pass:
          return "PASS";
        case Stage03FieldStatus.NotApplicable:
          return "NOT_APPLICABLE";
        case Stage03FieldStatus.MissingCarrier:
          return "MISSING_CARRIER";
        case Stage03FieldStatus.CarrierCategoryMismatch:
          return "CARRIER_CATEGORY_MISMATCH";
        case Stage03FieldStatus.CarrierNameMismatch:
          return "CARRIER_NAME_MISMATCH";
        case Stage03FieldStatus.AmbiguousCarrier:
          return "AMBIGUOUS_CARRIER";
        case Stage03FieldStatus.MissingParameter:
          return "MISSING_PARAMETER";
        case Stage03FieldStatus.EmptyRequiredValue:
          return "EMPTY_REQUIRED_VALUE";
        case Stage03FieldStatus.InvalidValue:
          return "INVALID_VALUE";
        case Stage03FieldStatus.RuleNotImplemented:
          return "RULE_NOT_IMPLEMENTED";
        case Stage03FieldStatus.UnclassifiedRequirement:
          return "UNCLASSIFIED_REQUIREMENT";
        case Stage03FieldStatus.IfcOwnerNotFound:
          return "IFC_OWNER_NOT_FOUND";
        case Stage03FieldStatus.IfcValueMismatch:
          return "IFC_VALUE_MISMATCH";
        default:
          throw new ArgumentOutOfRangeException(nameof(status));
      }
    }
  }

  public sealed class Stage03FieldResult
  {
    public string PropertyId { get; set; } = string.Empty;
    public string ContractKind { get; set; } = string.Empty;
    public string Requirement { get; set; } = string.Empty;
    public string Applicability { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string PropertySet { get; set; } = string.Empty;
    public string IfcProperty { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int ElementId { get; set; }
    public string OwnerUniqueId { get; set; } = string.Empty;
    public string ParameterGuid { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string ParameterScope { get; set; } = string.Empty;
    public Stage03FieldStatus CarrierStatus { get; set; } =
      Stage03FieldStatus.Pass;
    public Stage03FieldStatus ParameterStatus { get; set; } =
      Stage03FieldStatus.Pass;
    public Stage03FieldStatus RevitStatus { get; set; } =
      Stage03FieldStatus.Pass;
    public string RevitRawValue { get; set; } = string.Empty;
    public string RevitNormalizedValue { get; set; } = string.Empty;
    public string RevitValueSource { get; set; } = string.Empty;
    public string RawIfcOwner { get; set; } = string.Empty;
    public string RawIfcPropertySet { get; set; } = string.Empty;
    public string RawIfcProperty { get; set; } = string.Empty;
    public string RawIfcType { get; set; } = string.Empty;
    public string RawIfcValue { get; set; } = string.Empty;
    public Stage03FieldStatus RawIfcStatus { get; set; } =
      Stage03FieldStatus.Pass;
    public string FinalIfcOwner { get; set; } = string.Empty;
    public string FinalIfcPropertySet { get; set; } = string.Empty;
    public string FinalIfcProperty { get; set; } = string.Empty;
    public string FinalIfcType { get; set; } = string.Empty;
    public string FinalIfcValue { get; set; } = string.Empty;
    public Stage03FieldStatus FinalIfcStatus { get; set; } =
      Stage03FieldStatus.Pass;
    public Stage03FieldStatus Status { get; set; } =
      Stage03FieldStatus.Pass;
    public bool Active { get; set; }
    public bool IsBusinessBlocker { get; set; }
    public IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
  }

  public sealed class Stage03CarrierResult
  {
    public string Entity { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int ElementId { get; set; }
    public string UniqueId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Stage03FieldStatus Status { get; set; } =
      Stage03FieldStatus.Pass;
    public bool Active { get; set; }
    public bool IsBusinessBlocker { get; set; }
    public IReadOnlyList<string> Messages { get; set; } =
      Array.Empty<string>();
  }

  public sealed class Stage03Diagnostic
  {
    public string Code { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
  }

  public sealed class Stage03BusinessBlocker
  {
    internal Stage03BusinessBlocker(
      string entity,
      string ownerUniqueId,
      string role,
      int elementId,
      string propertyId,
      Stage03FieldStatus status,
      string requirement,
      string message)
    {
      Entity = entity ?? string.Empty;
      OwnerUniqueId = ownerUniqueId ?? string.Empty;
      Role = role ?? string.Empty;
      ElementId = elementId;
      PropertyId = propertyId ?? string.Empty;
      Status = status;
      StatusCode = Stage03FieldStatusCodes.ToCode(status);
      Requirement = requirement ?? string.Empty;
      Message = message ?? string.Empty;
    }

    public string Entity { get; }
    public string OwnerUniqueId { get; }
    public string Role { get; }
    public int ElementId { get; }
    public string PropertyId { get; }
    public Stage03FieldStatus Status { get; }
    public string StatusCode { get; }
    public string Requirement { get; }
    public string Message { get; }
  }

  public sealed class Stage03GateDecision
  {
    internal Stage03GateDecision(
      Stage03GateMode mode,
      bool allowExport,
      bool forced,
      string reason,
      IReadOnlyList<Stage03BusinessBlocker> businessBlockers,
      IReadOnlyList<string> technicalFatalCodes,
      IReadOnlyList<string> messages)
    {
      Mode = mode;
      AllowExport = allowExport;
      Forced = forced;
      Reason = reason ?? string.Empty;
      BusinessBlockers = businessBlockers
        ?? Array.Empty<Stage03BusinessBlocker>();
      TechnicalFatalCodes = technicalFatalCodes
        ?? Array.Empty<string>();
      Messages = messages ?? Array.Empty<string>();
    }

    public Stage03GateMode Mode { get; }
    public bool AllowExport { get; }
    public bool Forced { get; }
    public string Reason { get; }
    public IReadOnlyList<Stage03BusinessBlocker> BusinessBlockers { get; }
    public IReadOnlyList<string> TechnicalFatalCodes { get; }
    public IReadOnlyList<string> Messages { get; }
  }

  public sealed class Stage03OutputPaths
  {
    internal Stage03OutputPaths(
      string outputDirectory,
      string rvtStem,
      string runId,
      string rawIfc,
      string finalIfc,
      string fieldReport)
    {
      OutputDirectory = outputDirectory;
      RvtStem = rvtStem;
      RunId = runId;
      RawIfc = rawIfc;
      FinalIfc = finalIfc;
      FieldReport = fieldReport;
    }

    public string OutputDirectory { get; }
    public string RvtStem { get; }
    public string RunId { get; }
    public string RawIfc { get; }
    public string FinalIfc { get; }
    public string FieldReport { get; }
  }
}
