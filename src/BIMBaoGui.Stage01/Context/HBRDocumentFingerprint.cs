using System;
using System.IO;
using BIMBaoGui.Stage01.Core;

namespace BIMBaoGui.Stage01.Context
{
  public static class HBRDocumentFingerprint
  {
    public static string Compute(string documentPath, string documentTitle, string revitVersion)
    {
      string normalizedPath;
      try
      {
        normalizedPath = string.IsNullOrWhiteSpace(documentPath)
          ? string.Empty
          : Path.GetFullPath(documentPath).Trim().ToLowerInvariant();
      }
      catch
      {
        normalizedPath = (documentPath ?? string.Empty).Trim().ToLowerInvariant();
      }

      string source = string.Join("|", new[]
      {
        normalizedPath,
        (documentTitle ?? string.Empty).Trim(),
        (revitVersion ?? string.Empty).Trim()
      });
      return CanonicalPayload.Sha256(source);
    }
  }
}
