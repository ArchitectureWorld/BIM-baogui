using System;
using System.Collections.Generic;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03ChecklistPresentation
  {
    internal static IReadOnlyList<NativeStage03ChecklistItem>
      CreateInitialChecklist()
    {
      NativeStage03ChecklistGenerationResult generation =
        NativeStage03ChecklistGenerator.Generate(
          "总平模型",
          new Dictionary<string, bool>(StringComparer.Ordinal),
          NativeReportingRuleCatalog.Current);
      return NativeStage03ChecklistEvaluator.Evaluate(
        generation.Definitions,
        new NativeStage03SourceEvidenceBundle { ScanExecuted = false });
    }

    internal static string Background(NativeStage03ChecklistStatus status)
    {
      switch (status)
      {
        case NativeStage03ChecklistStatus.Passed: return "#FFDCFCE7";
        case NativeStage03ChecklistStatus.Failed: return "#FFFEE2E2";
        case NativeStage03ChecklistStatus.Warning: return "#FFFEF3C7";
        default: return "#FFE5E7EB";
      }
    }

    internal static string StatusText(NativeStage03ChecklistStatus status)
    {
      switch (status)
      {
        case NativeStage03ChecklistStatus.Passed: return "通过";
        case NativeStage03ChecklistStatus.Failed: return "失败";
        case NativeStage03ChecklistStatus.Warning: return "警告";
        default: return "未检查";
      }
    }
  }
}
