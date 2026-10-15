using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Hifc.Revit
{
    internal static class Evidence
    {
        internal static string Json(object value) { return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }.Serialize(value); }
        internal static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, Json(value), new UTF8Encoding(false));
        }
        internal static string Hash(string value)
        {
            using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
        internal static string FileHash(string path)
        {
            using (var file = File.OpenRead(path)) using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(file));
        }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}
