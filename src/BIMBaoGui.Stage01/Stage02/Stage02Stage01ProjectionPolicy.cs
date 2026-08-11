using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BIMBaoGui.Stage01.Stage02
{
  internal sealed class Stage02Stage01ProjectionResult
  {
    internal Stage02Stage01ProjectionResult(
      IReadOnlyDictionary<string, string> values,
      string recordIdentity,
      IEnumerable<Stage02Blocker> blockers)
    {
      Values = new ReadOnlyDictionary<string, string>(
        (values ?? new Dictionary<string, string>())
          .ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal));
      RecordIdentity = recordIdentity ?? string.Empty;
      Blockers = new ReadOnlyCollection<Stage02Blocker>(
        (blockers ?? Array.Empty<Stage02Blocker>()).ToArray());
    }

    internal IReadOnlyDictionary<string, string> Values { get; }
    internal string RecordIdentity { get; }
    internal IReadOnlyList<Stage02Blocker> Blockers { get; }
    internal bool Success => Blockers.Count == 0;
  }

  internal static class Stage02Stage01ProjectionPolicy
  {
    internal const string OrganizationRoleId = "ORGANIZATION";
    internal const string OrganizationIdentityPrefix =
      "STAGE01_ORGANIZATION_INDEX:";

    internal static Stage02Stage01ProjectionResult Resolve(
      string roleId,
      string requestedRecordIdentity,
      IReadOnlyDictionary<string, string> rootValues,
      IEnumerable<IDictionary<string, string>> organizations)
    {
      if (!string.Equals(
        roleId,
        OrganizationRoleId,
        StringComparison.Ordinal))
      {
        return Success(rootValues, string.Empty);
      }

      IReadOnlyDictionary<string, string>[] records = (organizations
        ?? Array.Empty<IDictionary<string, string>>())
        .Where(record => record != null)
        .Select(record => (IReadOnlyDictionary<string, string>)
          new Dictionary<string, string>(record, StringComparer.Ordinal))
        .ToArray();
      string requested = (requestedRecordIdentity ?? string.Empty).Trim();
      if (requested.Length == 0)
      {
        if (records.Length == 1)
          return Success(records[0], IdentityFor(0));
        if (records.Length > 1)
        {
          return Blocked(
            Stage02Codes.AmbiguousStage01Organization,
            "Stage01 包含多个参建单位；必须为所选 ORGANIZATION 载体明确指定记录身份。");
        }
        return Success(
          new Dictionary<string, string>(StringComparer.Ordinal),
          string.Empty);
      }

      int index;
      if (!requested.StartsWith(
          OrganizationIdentityPrefix,
          StringComparison.Ordinal)
        || !int.TryParse(
          requested.Substring(OrganizationIdentityPrefix.Length),
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out index)
        || index < 0
        || index >= records.Length)
      {
        return Blocked(
          Stage02Codes.InvalidStage01OrganizationIdentity,
          "Stage01 参建单位记录身份无效或已失效；必须重新选择并预览。");
      }
      return Success(records[index], IdentityFor(index));
    }

    private static Stage02Stage01ProjectionResult Success(
      IReadOnlyDictionary<string, string> values,
      string recordIdentity)
    {
      return new Stage02Stage01ProjectionResult(
        values,
        recordIdentity,
        Array.Empty<Stage02Blocker>());
    }

    private static Stage02Stage01ProjectionResult Blocked(
      string code,
      string message)
    {
      return new Stage02Stage01ProjectionResult(
        new Dictionary<string, string>(StringComparer.Ordinal),
        string.Empty,
        new[] { new Stage02Blocker(code, message) });
    }

    private static string IdentityFor(int index)
    {
      return OrganizationIdentityPrefix
        + index.ToString(CultureInfo.InvariantCulture);
    }
  }
}
