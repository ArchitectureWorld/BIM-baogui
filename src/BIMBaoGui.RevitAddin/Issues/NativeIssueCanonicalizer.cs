using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BIMBaoGui.RevitAddin.Issues
{
  internal static class NativeIssueCanonicalizer
  {
    internal static string ComputeId(NativeIssueRecord issue)
    {
      if (issue == null) throw new ArgumentNullException(nameof(issue));
      if (string.IsNullOrWhiteSpace(issue.DocumentFingerprint))
        throw new ArgumentException(
          "Issue document fingerprint is required.",
          nameof(issue));
      NativeIssueElementReference[] elements = (issue.Elements
        ?? Array.Empty<NativeIssueElementReference>()).ToArray();
      if (elements.Any(value =>
        value == null || string.IsNullOrWhiteSpace(value.UniqueId)))
      {
        throw new ArgumentException(
          "Every issue element requires a stable UniqueId.",
          nameof(issue));
      }
      string[] uniqueIds = elements
        .Select(value => value.UniqueId.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
      string raw = string.Join("|", new[]
      {
        Clean(issue.DocumentFingerprint),
        Clean(issue.SourceFeature),
        Clean(issue.CheckId),
        Clean(issue.Code),
        Clean(issue.FieldKey),
        Clean(issue.PropertyId),
        Clean(issue.RoleId),
        string.Join("\u001f", uniqueIds)
      });
      return Sha256(raw);
    }

    private static string Clean(string value)
    {
      return (value ?? string.Empty).Trim();
    }

    private static string Sha256(string value)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        return string.Concat(algorithm.ComputeHash(
          Encoding.UTF8.GetBytes(value ?? string.Empty))
          .Select(valueByte => valueByte.ToString("x2")));
      }
    }
  }
}
