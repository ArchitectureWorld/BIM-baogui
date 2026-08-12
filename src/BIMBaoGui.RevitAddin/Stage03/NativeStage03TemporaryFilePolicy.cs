using System;
using System.IO;
using System.Threading;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal static class NativeStage03TemporaryFilePolicy
  {
    private const string Pattern = "BIMBaoGui.Stage03.Scan.*.json";
    private static readonly object SyncRoot = new object();
    private static Timer _timer;

    internal static void Start()
    {
      lock (SyncRoot)
      {
        if (_timer != null) return;
        Cleanup();
        _timer = new Timer(
          _ => Cleanup(),
          null,
          TimeSpan.FromMinutes(2),
          TimeSpan.FromMinutes(2));
      }
    }

    internal static void Stop()
    {
      lock (SyncRoot)
      {
        _timer?.Dispose();
        _timer = null;
      }
      Cleanup();
    }

    internal static void Cleanup()
    {
      try
      {
        string temporaryDirectory = Path.GetTempPath();
        foreach (string path in Directory.EnumerateFiles(
          temporaryDirectory,
          Pattern,
          SearchOption.TopDirectoryOnly))
        {
          try
          {
            File.Delete(path);
          }
          catch
          {
            // Another scan may still be hashing this file. The timer retries.
          }
        }
      }
      catch
      {
        // Temp cleanup is best-effort and must never disable the Revit add-in.
      }
    }
  }
}
