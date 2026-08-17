using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.HifcCore;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03TechnicalPreflightService
  {
    internal static NativeStage03TechnicalPreflightEvidence Probe(
      UIApplication application,
      string outputDirectory)
    {
      Document document = application?.ActiveUIDocument?.Document;
      bool documentReady = document != null && !document.IsFamilyDocument;
      bool exporterReady = CanConstructIfcExporter();
      bool translatorReady = AssemblyReadable(typeof(HifcCoreService).Assembly);
      bool reportWriterReady = AssemblyReadable(
        typeof(NativeStage03ReportWriter).Assembly);
      return Probe(outputDirectory, documentReady, exporterReady,
        translatorReady, reportWriterReady);
    }

    internal static NativeStage03TechnicalPreflightEvidence Probe(
      string outputDirectory,
      bool documentReady,
      bool revitIfcExporterAvailable,
      bool translatorDependenciesAvailable,
      bool reportWriterAvailable)
    {
      var fatal = new SortedSet<string>(StringComparer.Ordinal);
      string normalized = Normalize(outputDirectory);
      bool writable = false;
      if (normalized.Length == 0)
        fatal.Add(NativeStage03Codes.InvalidOutputDirectory);
      else
      {
        writable = ProbeDirectory(normalized);
        if (!writable)
          fatal.Add(NativeStage03Codes.OutputDirectoryNotWritable);
      }
      if (!documentReady) fatal.Add(NativeStage03Codes.DocumentUnavailable);
      if (!revitIfcExporterAvailable)
        fatal.Add(NativeStage03Codes.IfcExporterUnavailable);
      if (!translatorDependenciesAvailable)
        fatal.Add(NativeStage03Codes.TranslatorDependencyUnavailable);
      if (!reportWriterAvailable)
        fatal.Add(NativeStage03Codes.ReportWriterUnavailable);
      string[] codes = fatal.ToArray();
      var result = new NativeStage03TechnicalPreflightEvidence
      {
        NormalizedOutputDirectory = normalized,
        DocumentReady = documentReady,
        OutputDirectoryWritable = writable,
        RevitIfcExporterAvailable = revitIfcExporterAvailable,
        TranslatorDependenciesAvailable = translatorDependenciesAvailable,
        ReportWriterAvailable = reportWriterAvailable,
        FatalCodes = new ReadOnlyCollection<string>(codes)
      };
      result.ProbeHash = Sha256(string.Join("\u001f", new[]
      {
        normalized,
        documentReady ? "true" : "false",
        writable ? "true" : "false",
        revitIfcExporterAvailable ? "true" : "false",
        translatorDependenciesAvailable ? "true" : "false",
        reportWriterAvailable ? "true" : "false",
        string.Join("\u001e", codes)
      }));
      return result;
    }

    private static string Normalize(string outputDirectory)
    {
      if (string.IsNullOrWhiteSpace(outputDirectory)
        || !Path.IsPathRooted(outputDirectory)) return string.Empty;
      try
      {
        return Path.GetFullPath(outputDirectory.Trim());
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is NotSupportedException
        || exception is PathTooLongException)
      {
        return string.Empty;
      }
    }

    private static bool ProbeDirectory(string directory)
    {
      string probePath = string.Empty;
      try
      {
        Directory.CreateDirectory(directory);
        probePath = Path.Combine(directory,
          ".bimbaogui-stage03-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (FileStream stream = new FileStream(
          probePath,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None,
          4096,
          FileOptions.WriteThrough))
        {
          byte[] bytes = new UTF8Encoding(false).GetBytes("BIMBaoGui Stage03");
          stream.Write(bytes, 0, bytes.Length);
          stream.Flush(true);
        }
        File.Delete(probePath);
        return !File.Exists(probePath);
      }
      catch
      {
        return false;
      }
      finally
      {
        try
        {
          if (probePath.Length > 0 && File.Exists(probePath))
            File.Delete(probePath);
        }
        catch { }
      }
    }

    private static bool CanConstructIfcExporter()
    {
      try
      {
        using (var options = new IFCExportOptions())
          return options != null;
      }
      catch
      {
        return false;
      }
    }

    private static bool AssemblyReadable(System.Reflection.Assembly assembly)
    {
      try
      {
        string path = assembly?.Location ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        using (FileStream stream = File.Open(
          path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
          return stream.Length > 0;
      }
      catch
      {
        return false;
      }
    }

    private static string Sha256(string value)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        return string.Concat(algorithm.ComputeHash(
          new UTF8Encoding(false).GetBytes(value ?? string.Empty))
          .Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
      }
    }
  }
}
