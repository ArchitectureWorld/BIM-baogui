using System;
using System.IO;
using BIMBaoGui.McpContracts;

namespace BIMBaoGui.RevitAddin.McpBridge
{
  internal sealed class McpBridgeDiscoveryWriter
  {
    private readonly int _processId;

    internal McpBridgeDiscoveryWriter(int processId)
    {
      if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
      _processId = processId;
    }

    internal string DiscoveryDirectory => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "BIMBaoGui",
      "Revit2020",
      "bridges");

    internal string DiscoveryPath => Path.Combine(
      DiscoveryDirectory,
      _processId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ".json");

    internal void Write(BridgeSessionDescriptor descriptor)
    {
      if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
      Directory.CreateDirectory(DiscoveryDirectory);
      string temporaryPath = DiscoveryPath + ".tmp." + Guid.NewGuid().ToString("N");
      descriptor.DiscoveryPath = DiscoveryPath;
      File.WriteAllText(
        temporaryPath,
        McpBridgeJson.Serialize(descriptor),
        new System.Text.UTF8Encoding(false));
      if (File.Exists(DiscoveryPath))
      {
        string backupPath = DiscoveryPath + ".bak";
        try
        {
          File.Replace(temporaryPath, DiscoveryPath, backupPath, true);
          if (File.Exists(backupPath)) File.Delete(backupPath);
        }
        finally
        {
          if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
      }
      else
      {
        File.Move(temporaryPath, DiscoveryPath);
      }
    }

    internal void Delete()
    {
      try
      {
        if (File.Exists(DiscoveryPath)) File.Delete(DiscoveryPath);
      }
      catch
      {
      }
      try
      {
        foreach (string temporary in Directory.Exists(DiscoveryDirectory)
          ? Directory.GetFiles(
            DiscoveryDirectory,
            _processId + ".json.tmp.*")
          : Array.Empty<string>())
          File.Delete(temporary);
      }
      catch
      {
      }
    }
  }
}
