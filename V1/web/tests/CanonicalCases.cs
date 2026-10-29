using System;
using Hifc.Web.Contracts;

internal static class CanonicalCases
{
    internal static void Run(Action<string, Action> test)
    {
        ElementRecord Sample(double value) => new ElementRecord { Id = 1, UniqueId = "a", NativeAreaM2 = 0.1, ProjectedAreaM2 = value };
        test("inventory geometry has the same specified encoding in Framework and modern NET", () => {
            // Independently calculated from protocol-2 field order and IEEE-754 bits.
            const string expected = "c9a5d263eee3d5473635af479f8af8dfbd430f8d422f876168d4031726a95629";
            var actual = Canonical.InventoryHash(new[] { Sample(1.2345678901234567) });
            if (actual != expected) throw new Exception("Cross-runtime inventory digest differs: " + actual);
        });
        test("inventory geometry preserves adjacent representable measurements", () => {
            var value = 1.2345678901234567;
            var next = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) + 1);
            if (Canonical.InventoryHash(new[] { Sample(value) }) == Canonical.InventoryHash(new[] { Sample(next) })) throw new Exception("A geometry change was hidden by decimal formatting.");
        });
        test("positive and negative geometric zero have the same inventory identity", () => {
            if (Canonical.InventoryHash(new[] { Sample(0) }) != Canonical.InventoryHash(new[] { Sample(BitConverter.Int64BitsToDouble(long.MinValue)) })) throw new Exception("JSON zero normalization changed inventory identity.");
        });
    }
}
