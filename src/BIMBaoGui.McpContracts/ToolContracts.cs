using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIMBaoGui.McpContracts
{
  public static class McpToolNames
  {
    public const string ListRevitSessions = "bimbaogui_list_revit_sessions";
    public const string GetDocumentStatus = "bimbaogui_get_document_status";
    public const string GetRulePackageIdentity =
      "bimbaogui_get_rule_package_identity";
    public const string Stage01GetFormSchema =
      "bimbaogui_stage01_get_form_schema";
    public const string Stage01Read = "bimbaogui_stage01_read";
    public const string Stage01Validate = "bimbaogui_stage01_validate";
    public const string Stage01Write = "bimbaogui_stage01_write";
    public const string Stage02Preview = "bimbaogui_stage02_preview";
    public const string Stage02Write = "bimbaogui_stage02_write";
    public const string Stage03Scan = "bimbaogui_stage03_scan";
    public const string Stage03Export = "bimbaogui_stage03_export";
    public const string Stage03GetLastResult =
      "bimbaogui_stage03_get_last_result";
    public const string Stage03RevalidateFile =
      "bimbaogui_stage03_revalidate_file";

    public static readonly IReadOnlyList<string> Approved =
      new ReadOnlyCollection<string>(new[]
      {
        GetDocumentStatus,
        GetRulePackageIdentity,
        ListRevitSessions,
        Stage01GetFormSchema,
        Stage01Read,
        Stage01Validate,
        Stage01Write,
        Stage02Preview,
        Stage02Write,
        Stage03Scan,
        Stage03Export,
        Stage03GetLastResult,
        Stage03RevalidateFile
      }.OrderBy(value => value, StringComparer.Ordinal).ToArray());
  }

  public class RevitSessionSelector
  {
    public int? RevitProcessId { get; set; }
  }

  public sealed class Stage01ValidateCommand : RevitSessionSelector
  {
    public string PayloadJson { get; set; } = string.Empty;
  }

  public sealed class Stage01WriteCommand : RevitSessionSelector
  {
    public string ValidationHash { get; set; } = string.Empty;
    public bool Confirm { get; set; }
    public bool ConfirmBlankProject { get; set; }
    public bool AllowReinitialize { get; set; }
  }

  public sealed class Stage02PreviewCommand : RevitSessionSelector
  {
    public string Scope { get; set; } = "full_model";
    public string IdentificationMode { get; set; } = "automatic";
    public string BulkRoleId { get; set; } = string.Empty;
    public IReadOnlyList<Stage02RoleOverrideCommand> RoleOverrides { get; set; } =
      Array.Empty<Stage02RoleOverrideCommand>();
  }

  public sealed class Stage02RoleOverrideCommand
  {
    public string ElementUniqueId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
  }

  public sealed class Stage02WriteCommand : RevitSessionSelector
  {
    public string PreviewHash { get; set; } = string.Empty;
    public bool Confirm { get; set; }
  }

  public sealed class Stage03ScanCommand : RevitSessionSelector
  {
    public string Mode { get; set; } = "strict";
    public string ForceReason { get; set; } = string.Empty;
  }

  public sealed class Stage03ExportCommand : RevitSessionSelector
  {
    public string ScanHash { get; set; } = string.Empty;
    public bool Confirm { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
  }

  public sealed class Stage03RevalidateFileCommand : RevitSessionSelector
  {
    public string IfcPath { get; set; } = string.Empty;
  }
}
