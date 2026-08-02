using System;
using System.Collections.Generic;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class RevitDocumentSnapshot
  {
    public bool HostAvailable { get; set; }
    public bool IsRevit2020 { get; set; }
    public bool IsProjectDocument { get; set; }
    public bool IsSaved { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsBlank { get; set; }
    public bool IsInitialized { get; set; }
    public bool PayloadMatches { get; set; }
    public bool RequiresWorkflowMigration { get; set; }
    public Stage01StorageDecision StorageDecision { get; set; } = Stage01StorageDecision.NoRecord;
    public string DocumentTitle { get; set; } = string.Empty;
    public string DocumentPath { get; set; } = string.Empty;
    public string RevitVersion { get; set; } = string.Empty;
    public string Status { get; set; } = "未连接";
    public string StoredPayloadHash { get; set; } = string.Empty;
    public string StoredPayloadJson { get; set; } = string.Empty;
    public string StoredWorkflowVersion { get; set; } = string.Empty;
    public IReadOnlyList<string> BlockingElements { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
  }

  internal sealed class CommitResult
  {
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
  }

  internal sealed class StoredInitialization
  {
    public string PayloadJson { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string FileGuid { get; set; } = string.Empty;
    public string WorkflowVersion { get; set; } = string.Empty;
    public string InitializedUtc { get; set; } = string.Empty;
  }
}
