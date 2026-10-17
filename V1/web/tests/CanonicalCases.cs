using System;
using Hifc.Web.Contracts;

internal static class CanonicalCases
{
    internal static void Run(Action<string, Action> test)
    {
        ElementRecord Sample(double value) => new ElementRecord { Id = 1, UniqueId = "a", NativeAreaM2 = 0.1, ProjectedAreaM2 = value };
        test("inventory geometry has the same specified encoding in Framework and modern NET", () => {
            const string expected = "b74ceea2c8facd8cb79466d48cce5497be3053b49946699dfb2b2a44c87b8b27";
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
