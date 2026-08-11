using System;
using System.Collections.Generic;

namespace BIMBaoGui.Stage01.Context
{
  internal static class HBRLiveContextPolicy
  {
    public static IReadOnlyList<string> Validate(
      string contextFileGuid,
      string contextPayloadHash,
      bool liveInitialized,
      string liveFileGuid,
      string livePayloadHash,
      string liveWorkflowVersion)
    {
      if (!liveInitialized)
      {
        return new[]
        {
          "当前 Revit 文件没有有效的 Stage01 初始化记录。请重新运行 01 文件初始化。"
        };
      }

      var blockers = new List<string>();
      if (!string.Equals(
        contextFileGuid ?? string.Empty,
        liveFileGuid ?? string.Empty,
        StringComparison.OrdinalIgnoreCase))
      {
        blockers.Add(
          "文件上下文的文件唯一 ID 与当前 Revit 文件不一致。请重新运行 01 文件初始化。");
      }
      if (!string.Equals(
        contextPayloadHash ?? string.Empty,
        livePayloadHash ?? string.Empty,
        StringComparison.OrdinalIgnoreCase))
      {
        blockers.Add(
          "文件上下文的 Stage01 载荷哈希与当前 Revit 文件不一致。请重新运行 01 文件初始化。");
      }
      if (!string.Equals(
        liveWorkflowVersion ?? string.Empty,
        HBRContextVersions.FileContextSchema,
        StringComparison.Ordinal))
      {
        blockers.Add(
          "当前 Revit 文件的 Stage01 工作流版本为 "
          + (liveWorkflowVersion ?? string.Empty)
          + "，当前需要 "
          + HBRContextVersions.FileContextSchema
          + "。请重新提交 01 文件初始化完成升级。");
      }
      return blockers;
    }
  }
}
