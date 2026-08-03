using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BIMBaoGui.Stage01.Revit.Parameters;

namespace BIMBaoGui.Stage01.Stage02
{
  public enum Stage02PreparationSelectionMode
  {
    ProjectInformation,
    ExplicitIds,
    ExplicitPick,
    CurrentSelection
  }

  public sealed class Stage02PreparationInputDecision
  {
    internal Stage02PreparationInputDecision(
      Stage02PreparationSelectionMode selectionMode,
      IEnumerable<int> elementIds,
      string roleHint,
      string inputSignature,
      IEnumerable<string> blockers)
    {
      SelectionMode = selectionMode;
      ElementIds = new ReadOnlyCollection<int>(
        (elementIds ?? Array.Empty<int>()).ToArray());
      RoleHint = roleHint ?? string.Empty;
      InputSignature = inputSignature ?? string.Empty;
      Blockers = new ReadOnlyCollection<string>(
        (blockers ?? Array.Empty<string>()).ToArray());
    }

    public bool Success => Blockers.Count == 0;
    public Stage02PreparationSelectionMode SelectionMode { get; }
    public IReadOnlyList<int> ElementIds { get; }
    public string RoleHint { get; }
    public string InputSignature { get; }
    public IReadOnlyList<string> Blockers { get; }
  }

  public static class Stage02PreparationInputPolicy
  {
    public static Stage02PreparationInputDecision Evaluate(
      string fileContextHash,
      IEnumerable<int> elementIds,
      string roleHint,
      bool explicitPick,
      bool projectInformation)
    {
      int[] normalizedIds = (elementIds ?? Array.Empty<int>())
        .Distinct()
        .OrderBy(value => value)
        .ToArray();
      string normalizedRole = (roleHint ?? string.Empty)
        .Trim()
        .ToUpperInvariant();
      Stage02PreparationSelectionMode mode = ResolveMode(
        normalizedIds.Length > 0,
        explicitPick,
        projectInformation);
      var blockers = new List<string>();

      if (projectInformation && (normalizedIds.Length > 0 || explicitPick))
      {
        blockers.Add(
          "项目信息入口不能与元素Id或交互点选同时启用；选择入口冲突。");
      }
      else if (normalizedIds.Length > 0 && explicitPick)
      {
        blockers.Add(
          "元素Id入口不能与交互点选同时启用；选择入口冲突。");
      }

      if (mode == Stage02PreparationSelectionMode.ProjectInformation)
      {
        if (normalizedRole.Length == 0) normalizedRole = "PROJECT";
        if (!IsProjectInformationRole(normalizedRole))
        {
          blockers.Add(
            "项目信息角色提示只接受 PROJECT、SITE 或 BUILDING。");
        }
      }

      string signature = ComputeSignature(
        fileContextHash,
        mode,
        normalizedIds,
        normalizedRole);
      return new Stage02PreparationInputDecision(
        mode,
        normalizedIds,
        normalizedRole,
        signature,
        blockers);
    }

    private static Stage02PreparationSelectionMode ResolveMode(
      bool hasElementIds,
      bool explicitPick,
      bool projectInformation)
    {
      if (projectInformation)
        return Stage02PreparationSelectionMode.ProjectInformation;
      if (hasElementIds)
        return Stage02PreparationSelectionMode.ExplicitIds;
      if (explicitPick)
        return Stage02PreparationSelectionMode.ExplicitPick;
      return Stage02PreparationSelectionMode.CurrentSelection;
    }

    private static bool IsProjectInformationRole(string roleHint)
    {
      return string.Equals(roleHint, "PROJECT", StringComparison.Ordinal)
        || string.Equals(roleHint, "SITE", StringComparison.Ordinal)
        || string.Equals(roleHint, "BUILDING", StringComparison.Ordinal);
    }

    private static string ComputeSignature(
      string fileContextHash,
      Stage02PreparationSelectionMode mode,
      IEnumerable<int> elementIds,
      string roleHint)
    {
      string canonical = string.Join("\n", new[]
      {
        "context=" + (fileContextHash ?? string.Empty).Trim(),
        "mode=" + mode,
        "ids=" + string.Join(",", (elementIds ?? Array.Empty<int>())
          .Select(value => value.ToString(CultureInfo.InvariantCulture))),
        "role=" + (roleHint ?? string.Empty)
      });
      using (SHA256 sha = SHA256.Create())
      {
        return BitConverter.ToString(
            sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
          .Replace("-", string.Empty)
          .ToLowerInvariant();
      }
    }
  }

  public sealed class Stage02PreparationEdgeDecision
  {
    internal Stage02PreparationEdgeDecision(
      bool shouldGeneratePreview,
      bool shouldConfirmWrite,
      bool confirmationDeferred)
    {
      ShouldGeneratePreview = shouldGeneratePreview;
      ShouldConfirmWrite = shouldConfirmWrite;
      ConfirmationDeferred = confirmationDeferred;
    }

    public bool ShouldGeneratePreview { get; }
    public bool ShouldConfirmWrite { get; }
    public bool ConfirmationDeferred { get; }
  }

  public static class Stage02PreparationExecutionPolicy
  {
    public static Stage02PreparationEdgeDecision Evaluate(
      bool previewEdge,
      bool confirmEdge)
    {
      return new Stage02PreparationEdgeDecision(
        previewEdge,
        confirmEdge && !previewEdge,
        previewEdge && confirmEdge);
    }
  }

  public enum Stage02PreparationWriteCompletionDisposition
  {
    Ignored,
    Discarded,
    Publish
  }

  public enum Stage02PreparationWriteAttemptPhase
  {
    Idle,
    Pending,
    StalePending
  }

  public sealed class Stage02PreparationWriteAttemptState
  {
    private Guid _activeAttemptToken = Guid.Empty;
    private string _lastFailureReportPath = string.Empty;

    public Stage02PreparationWriteAttemptPhase Phase { get; private set; } =
      Stage02PreparationWriteAttemptPhase.Idle;

    public bool IsPending => Phase != Stage02PreparationWriteAttemptPhase.Idle;

    public string LastFailureReportPath => _lastFailureReportPath;

    public Guid BeginAttempt()
    {
      if (IsPending)
      {
        throw new InvalidOperationException(
          "已有 Stage02 写入请求正在等待完成。");
      }

      _activeAttemptToken = Guid.NewGuid();
      Phase = Stage02PreparationWriteAttemptPhase.Pending;
      return _activeAttemptToken;
    }

    public bool IsActive(Guid attemptToken)
    {
      return attemptToken != Guid.Empty
        && attemptToken == _activeAttemptToken;
    }

    public void MarkActiveAttemptStale()
    {
      if (IsPending)
        Phase = Stage02PreparationWriteAttemptPhase.StalePending;
    }

    public Stage02PreparationWriteCompletionDisposition CompleteAttempt(
      Guid attemptToken,
      string failureReportPath)
    {
      if (!IsActive(attemptToken))
        return Stage02PreparationWriteCompletionDisposition.Ignored;

      bool publish = Phase == Stage02PreparationWriteAttemptPhase.Pending;
      _activeAttemptToken = Guid.Empty;
      Phase = Stage02PreparationWriteAttemptPhase.Idle;
      if (!string.IsNullOrWhiteSpace(failureReportPath))
        _lastFailureReportPath = failureReportPath;
      return publish
        ? Stage02PreparationWriteCompletionDisposition.Publish
        : Stage02PreparationWriteCompletionDisposition.Discarded;
    }
  }

  public sealed class Stage02PreparationPreviewCounts
  {
    private Stage02PreparationPreviewCounts(
      int pendingInstallCount,
      int alreadyInstalledCount,
      int totalParameterCount,
      int pendingWriteCount)
    {
      PendingInstallCount = pendingInstallCount;
      AlreadyInstalledCount = alreadyInstalledCount;
      TotalParameterCount = totalParameterCount;
      PendingWriteCount = pendingWriteCount;
    }

    public int PendingInstallCount { get; }
    public int AlreadyInstalledCount { get; }
    public int TotalParameterCount { get; }
    public int PendingWriteCount { get; }

    public static Stage02PreparationPreviewCounts Empty { get; } =
      new Stage02PreparationPreviewCounts(0, 0, 0, 0);

    public static Stage02PreparationPreviewCounts Calculate(
      Stage02Preview preview)
    {
      Stage02WriteOperation[] operations = preview == null
        ? Array.Empty<Stage02WriteOperation>()
        : preview.Elements
          .Where(element => element != null)
          .SelectMany(element =>
            element.Operations ?? Array.Empty<Stage02WriteOperation>())
          .Where(operation => operation != null)
          .ToArray();
      if (operations.Length == 0) return Empty;
      IGrouping<Guid, Stage02WriteOperation>[] parameterGroups = operations
        .GroupBy(operation => operation.ParameterGuid)
        .ToArray();
      int pendingInstallCount = parameterGroups.Count(group =>
        group.Any(operation => IsPendingInstall(operation.BindingAction)));
      int alreadyInstalledCount = parameterGroups.Count(group =>
        group.All(operation => string.Equals(
          operation.BindingAction,
          HbrBindingActions.Reuse,
          StringComparison.Ordinal)));
      int pendingWriteCount = operations.Count(operation => string.Equals(
        operation.ValueAction,
        "SET",
        StringComparison.Ordinal));
      return new Stage02PreparationPreviewCounts(
        pendingInstallCount,
        alreadyInstalledCount,
        parameterGroups.Length,
        pendingWriteCount);
    }

    private static bool IsPendingInstall(string bindingAction)
    {
      return string.Equals(
          bindingAction,
          HbrBindingActions.CreateAndBind,
          StringComparison.Ordinal)
        || string.Equals(
          bindingAction,
          HbrBindingActions.BindExisting,
          StringComparison.Ordinal)
        || string.Equals(
          bindingAction,
          HbrBindingActions.MergeCategories,
          StringComparison.Ordinal);
    }
  }

  public sealed class Stage02PreparationPreviewCountCache
  {
    private Stage02PreparationPreviewCounts _current =
      Stage02PreparationPreviewCounts.Empty;

    public Stage02PreparationPreviewCounts Current => _current;

    public void Publish(Stage02Preview preview)
    {
      _current = Stage02PreparationPreviewCounts.Calculate(preview);
    }

    public void Clear()
    {
      _current = Stage02PreparationPreviewCounts.Empty;
    }
  }

  public sealed class Stage02PreparationWritePublicationDecision
  {
    internal Stage02PreparationWritePublicationDecision(
      bool clearPreview,
      int installedCount,
      int writtenCount)
    {
      ClearPreview = clearPreview;
      InstalledCount = installedCount;
      WrittenCount = writtenCount;
    }

    public bool ClearPreview { get; }
    public int InstalledCount { get; }
    public int WrittenCount { get; }
  }

  public static class Stage02PreparationWritePublicationPolicy
  {
    public static Stage02PreparationWritePublicationDecision Evaluate(
      Stage02PreparationPreviewCounts previewCounts,
      int currentInstalledCount,
      bool success,
      bool requiresNewPreview)
    {
      if (success)
      {
        Stage02PreparationPreviewCounts counts = previewCounts
          ?? Stage02PreparationPreviewCounts.Empty;
        return new Stage02PreparationWritePublicationDecision(
          clearPreview: true,
          installedCount: counts.TotalParameterCount,
          writtenCount: counts.PendingWriteCount);
      }
      if (requiresNewPreview)
      {
        return new Stage02PreparationWritePublicationDecision(
          clearPreview: true,
          installedCount: 0,
          writtenCount: 0);
      }
      return new Stage02PreparationWritePublicationDecision(
        clearPreview: false,
        installedCount: currentInstalledCount,
        writtenCount: 0);
    }
  }

}
