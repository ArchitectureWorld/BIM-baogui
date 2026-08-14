using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BIMBaoGui.Stage01.Hifc
{
  internal static class DeterministicGuidV5
  {
    public static Guid Create(Guid namespaceId, string name)
    {
      byte[] namespaceBytes = namespaceId.ToByteArray();
      SwapByteOrder(namespaceBytes);
      byte[] nameBytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
      byte[] hash;
      using (SHA1 algorithm = SHA1.Create())
      {
        hash = algorithm.ComputeHash(namespaceBytes.Concat(nameBytes).ToArray());
      }

      byte[] guidBytes = hash.Take(16).ToArray();
      guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
      guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
      SwapByteOrder(guidBytes);
      return new Guid(guidBytes);
    }

    private static void SwapByteOrder(byte[] guid)
    {
      Swap(guid, 0, 3);
      Swap(guid, 1, 2);
      Swap(guid, 4, 5);
      Swap(guid, 6, 7);
    }

    private static void Swap(byte[] value, int left, int right)
    {
      byte current = value[left];
      value[left] = value[right];
      value[right] = current;
    }
  }
}
