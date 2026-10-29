using Hifc.Web.Contracts;

namespace Hifc.Web.Server;

public sealed partial class PlanSigner
{
    public void Sign(SignedComponentManifest manifest)
    {
        ComponentTrust.ValidateLock(manifest?.Lock);
        manifest.Signature = Signature(ComponentTrust.Payload(manifest));
        ComponentTrust.Validate(manifest, manifest.Lock, PublicKey, new Uri(UpdateTrust.Server));
    }
}
