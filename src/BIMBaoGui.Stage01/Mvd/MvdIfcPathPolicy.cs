using System;
using System.IO;

namespace BIMBaoGui.Stage01.Mvd
{
  internal static class MvdIfcPathPolicy
  {
    public static string ResolveDestination(
      string sourcePath,
      string destinationPath)
    {
      string source = NormalizeIfcPath(sourcePath, "源 IFC");
      string destination;
      if (string.IsNullOrWhiteSpace(destinationPath))
      {
        destination = Path.Combine(
          Path.GetDirectoryName(source) ?? string.Empty,
          Path.GetFileNameWithoutExtension(source) + "-MVD.ifc");
      }
      else
      {
        destination = NormalizeIfcPath(destinationPath, "输出 IFC");
      }

      destination = Path.GetFullPath(destination);
      if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("输出 IFC 不能覆盖源 IFC。");
      if (File.Exists(destination))
        throw new IOException("输出 IFC 已存在，拒绝覆盖：" + destination);
      return destination;
    }

    private static string NormalizeIfcPath(string path, string label)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException(label + "路径不能为空。", nameof(path));
      string fullPath = Path.GetFullPath(path.Trim());
      if (!string.Equals(
        Path.GetExtension(fullPath),
        ".ifc",
        StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException(label + "扩展名必须为 .ifc。");
      return fullPath;
    }
  }
}
