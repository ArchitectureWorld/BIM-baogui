using System;
using System.Collections.Generic;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage02B;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal sealed class NativeStage03SourceEvidenceBundle
  {
    internal bool ScanExecuted { get; set; }
    internal NativeWorkflowIdentity CurrentIdentity { get; set; }
    internal NativeStage01ReadResult Stage01 { get; set; }
    internal string Stage01CurrentInputSnapshotHash { get; set; } = string.Empty;
    internal NativeWorkflowResultEnvelope Stage01Result { get; set; }
    internal NativeStage02Preview Stage02A { get; set; }
    internal string Stage02ACurrentInputSnapshotHash { get; set; } = string.Empty;
    internal NativeWorkflowResultEnvelope Stage02AResult { get; set; }
    internal NativeStage02BReadResult Stage02B { get; set; }
    internal string Stage02BCurrentInputSnapshotHash { get; set; } = string.Empty;
    internal NativeWorkflowResultEnvelope Stage02BResult { get; set; }
    internal NativeStage03TechnicalPreflightEvidence TechnicalPreflight
    {
      get;
      set;
    }
    internal IReadOnlyList<string> TechnicalFatalCodes { get; set; } =
      Array.Empty<string>();
  }

  internal sealed class NativeStage03TechnicalPreflightEvidence
  {
    internal string NormalizedOutputDirectory { get; set; } = string.Empty;
    internal bool DocumentReady { get; set; }
    internal bool OutputDirectoryWritable { get; set; }
    internal bool RevitIfcExporterAvailable { get; set; }
    internal bool TranslatorDependenciesAvailable { get; set; }
    internal bool ReportWriterAvailable { get; set; }
    internal IReadOnlyList<string> FatalCodes { get; set; } =
      Array.Empty<string>();
    internal string ProbeHash { get; set; } = string.Empty;
  }
}
