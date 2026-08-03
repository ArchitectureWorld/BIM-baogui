using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;

namespace BIMBaoGui.Stage01.Revit.Parameters
{
  internal static class HbrTemporaryFileCleanup
  {
    internal static void Complete(
      Exception primaryException,
      Action restoreSharedParameterPath,
      Action deleteTemporaryFile)
    {
      if (restoreSharedParameterPath == null)
        throw new ArgumentNullException(nameof(restoreSharedParameterPath));
      if (deleteTemporaryFile == null)
        throw new ArgumentNullException(nameof(deleteTemporaryFile));

      var cleanupFailures = new List<Exception>();
      try
      {
        restoreSharedParameterPath();
      }
      catch (Exception restoreException)
      {
        cleanupFailures.Add(restoreException);
      }

      try
      {
        deleteTemporaryFile();
      }
      catch (Exception deleteException)
      {
        cleanupFailures.Add(deleteException);
      }

      if (cleanupFailures.Count == 0)
      {
        if (primaryException == null) return;
        ExceptionDispatchInfo.Capture(primaryException).Throw();
        return;
      }

      var allFailures = new List<Exception>();
      if (primaryException != null) allFailures.Add(primaryException);
      allFailures.AddRange(cleanupFailures);
      throw new AggregateException(
        "HBR 临时共享参数文件作用域清理失败。",
        allFailures);
    }

    internal static void DeleteTemporaryFile(string path)
    {
      if (string.IsNullOrWhiteSpace(path)) return;
      if (File.Exists(path)) File.Delete(path);
    }
  }
}
