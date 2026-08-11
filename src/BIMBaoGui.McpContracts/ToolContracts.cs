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
        Stage02Write
      }.OrderBy(value => value, StringComparer.Ordinal).ToArray());
  }

  public sealed class RevitSessionSelector
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
  }

  public sealed class Stage02WriteCommand : RevitSessionSelector
  {
    public string PreviewHash { get; set; } = string.Empty;
    public bool Confirm { get; set; }
  }
}
