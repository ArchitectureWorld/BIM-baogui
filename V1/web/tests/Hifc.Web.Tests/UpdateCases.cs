using Hifc.Web.Contracts;
using Hifc.Web.Server;

internal static class UpdateCases
{
    internal static void Run(Action<string, Action> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "hifc-update-keys", Guid.NewGuid().ToString("N"), "signing.pem");
        using var signer = new PlanSigner(path);
        ClientUpdateManifest Offer() {
            var value = new ClientUpdateManifest { ReleaseId = "0.5.0-dev-20261019T120000Z", SourceCommit = new string('a', 40), Sha256 = new string('b', 64), Length = 512,
                PackageUrl = "https://192.168.6.45:8443/updates/packages/0.5.0-dev-20261019T120000Z.zip" };
            signer.Sign(value); return value;
        }
        static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe update was accepted."); }
        test("zetta update manifest is signed and accepted by the client trust policy", () => UpdateTrust.Validate(Offer(), signer.PublicKey));
        test("update cannot redirect the client to the Windows development machine", () => { var value = Offer(); value.PackageUrl = "https://192.168.218.195:8443/update.zip"; signer.Sign(value); Reject(() => UpdateTrust.Validate(value, signer.PublicKey)); });
        test("tampered update hash size release and revision all invalidate the signature", () => {
            foreach (var change in new Action<ClientUpdateManifest>[] { v => v.Sha256 = new string('c', 64), v => v.Length++, v => v.SourceCommit = new string('d', 40), v => v.ReleaseId = "0.5.0-dev-20261019T130000Z" }) {
                var value = Offer(); change(value); Reject(() => UpdateTrust.Validate(value, signer.PublicKey));
            }
        });
        test("an incompatible update channel or signing identity is rejected", () => {
            var value = Offer(); value.Channel = "other-product"; signer.Sign(value); Reject(() => UpdateTrust.Validate(value, signer.PublicKey));
            using var other = new PlanSigner(path + ".other"); Reject(() => UpdateTrust.Validate(Offer(), other.PublicKey));
        });
    }
}
