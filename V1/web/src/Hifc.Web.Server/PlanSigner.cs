using Hifc.Web.Contracts;
using System.Security.Cryptography;
using System.Text;

namespace Hifc.Web.Server;

public sealed class PlanSigner : IDisposable
{
    private readonly RSA rsa;
    private readonly object gate = new();
    public PlanSigner(string privateKeyPath)
    {
        var path = Path.GetFullPath(privateKeyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (!File.Exists(path))
        {
            using var created = RSA.Create(3072);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, created.ExportPkcs8PrivateKeyPem(), new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try { File.Move(temporary, path, false); }
            catch (IOException) when (File.Exists(path)) { File.Delete(temporary); }
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("签名密钥不允许使用符号链接。");
        rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(path));
        if (rsa.KeySize < 2048) throw new InvalidOperationException("签名密钥长度不足。");
    }
    public PublicSigningKey PublicKey
    {
        get { lock (gate) { var parameters = rsa.ExportParameters(false); return new PublicSigningKey { Modulus = Convert.ToBase64String(parameters.Modulus), Exponent = Convert.ToBase64String(parameters.Exponent) }; } }
    }
    private string Signature(string value)
    {
        lock (gate) return Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(value), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
    public void Sign(EditPlan plan) { plan.Hash = Canonical.PlanHash(plan); plan.Signature = Signature(plan.Hash); }
    public WritePermit IssuePermit(EditPlan plan, AccountInfo account, DateTime databaseNowUtc, string permitId)
    {
        if (account == null || !AccountPolicy.IsActive(account, databaseNowUtc)) throw new InvalidOperationException("账号已到期，请联系管理员延期；人工草稿会保留。");
        if (plan == null || databaseNowUtc >= plan.ExpiresAtUtc || plan.Hash != Canonical.PlanHash(plan) || string.IsNullOrWhiteSpace(permitId))
            throw new InvalidOperationException("方案已过期或散列无效，请重新检查。");
        var expiry = databaseNowUtc.AddSeconds(30);
        if (plan.ExpiresAtUtc < expiry) expiry = plan.ExpiresAtUtc;
        if (!account.IsAdmin && account.ExpiresAtUtc < expiry) expiry = account.ExpiresAtUtc;
        var permit = new WritePermit { Context = plan.Context, Id = permitId, PlanId = plan.Id, PlanHash = plan.Hash, ExpiresAtUtc = expiry };
        permit.Signature = Signature(Canonical.PermitPayload(permit)); return permit;
    }
    public void Dispose() => rsa.Dispose();
}
