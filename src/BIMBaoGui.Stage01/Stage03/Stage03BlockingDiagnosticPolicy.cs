using System;

namespace BIMBaoGui.Stage01.Stage03
{
  internal static class Stage03BlockingDiagnosticPolicy
  {
    internal static bool IsBlocking(Stage03Diagnostic diagnostic)
    {
      if (diagnostic == null) return true;
      string severity = (diagnostic.Severity ?? string.Empty).Trim();
      if (string.Equals(severity, "ERROR", StringComparison.OrdinalIgnoreCase)
        || string.Equals(severity, "FATAL", StringComparison.OrdinalIgnoreCase)
        || string.Equals(
          severity,
          "CRITICAL",
          StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }

      string code = (diagnostic.Code ?? string.Empty).Trim();
      return string.Equals(code, Stage03TechnicalFatalCodes.WrongDocument,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.UnsupportedRevit,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.DocumentUnavailable,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.OutputExists,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.ExportFailed,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.InvalidIfc,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.ReportFailed,
          StringComparison.Ordinal)
        || string.Equals(code, Stage03TechnicalFatalCodes.InvalidFieldStatus,
          StringComparison.Ordinal)
        || code.IndexOf("FATAL", StringComparison.OrdinalIgnoreCase) >= 0;
    }
  }
}
