using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hifc.Web.Contracts
{
    public class PlatformLaunchRequest
    {
        public string ExportPlanId { get; set; }
        public string DocumentSessionId { get; set; }
        public string ProjectId { get; set; }
        public string TargetModelId { get; set; }
        public string TargetGeneration { get; set; }
        public string IfcSha256 { get; set; }
        public string RunId { get; set; }
        public string MainReleaseId { get; set; }
        public string MainSourceCommit { get; set; }
        public string ComponentVersion { get; set; } = "0.2.1-web.1";
        public string NodeVersion { get; set; } = "25.4.0";
        public bool ConfirmedReplacement { get; set; }
    }
    public sealed class PlatformLaunchPermit : PlatformLaunchRequest
    {
        public string Id { get; set; }
        public string AccountId { get; set; }
        public string ExportPlanHash { get; set; }
        public string ReceiptIdentityHash { get; set; }
        public long IssuedAtUnixSeconds { get; set; }
        public long ExpiresAtUnixSeconds { get; set; }
        public string Signature { get; set; }
    }
    public sealed class PlatformConsumeRequest
    {
        public PlatformLaunchPermit Permit { get; set; }
        public string ConsumerNonce { get; set; }
    }
    public sealed class PlatformLaunchConsumption
    {
        public string PermitId { get; set; }
        public string PermitHash { get; set; }
        public string AccountId { get; set; }
        public string RunId { get; set; }
        public string ConsumerNonce { get; set; }
        public long ConsumedAtUnixSeconds { get; set; }
        public string Signature { get; set; }
    }
    public static class PlatformLaunchTrust
    {
        public static string RequestPayload(PlatformLaunchRequest x) => string.Join("\n", x.ExportPlanId, x.DocumentSessionId, x.ProjectId, x.TargetModelId, x.TargetGeneration, x.IfcSha256, x.RunId, x.MainReleaseId, x.MainSourceCommit, x.ComponentVersion, x.NodeVersion, x.ConfirmedReplacement ? "1" : "0");
        public static string Payload(PlatformLaunchPermit x) => string.Join("\n", "HIFC-WEB-PLATFORM-LAUNCH-1", RequestPayload(x), x.Id, x.AccountId, x.ExportPlanHash, x.ReceiptIdentityHash, x.IssuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture), x.ExpiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture));
        public static string ConsumptionPayload(PlatformLaunchConsumption x) => string.Join("\n", "HIFC-WEB-PLATFORM-CONSUME-1", x.PermitId, x.PermitHash, x.AccountId, x.RunId, x.ConsumerNonce, x.ConsumedAtUnixSeconds.ToString(CultureInfo.InvariantCulture));
        public static string ReceiptIdentity(ExportReceipt x) => Canonical.Hash(string.Join("\n", "HIFC-WEB-EXPORT-RECEIPT-3", x.PlanId, x.DocumentSessionId, x.IfcHash, x.SnapshotBefore, x.SnapshotAfter, x.SourceRestored ? "1" : "0", x.Exported ? "1" : "0", RequiredInputProof.Identity(x.InputValidation)));
        public static void ValidateRequest(PlatformLaunchRequest x)
        {
            if (x == null || new[] { x.ExportPlanId, x.DocumentSessionId, x.ProjectId, x.TargetModelId, x.TargetGeneration, x.RunId }.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 256 || v.Any(char.IsControl))
                || !Guid.TryParse(x.RunId, out _) || !Regex.IsMatch(x.IfcSha256 ?? "", "^[a-f0-9]{64}$") || !UpdateTrust.IsRelease(x.MainReleaseId) || !Regex.IsMatch(x.MainSourceCommit ?? "", "^[a-f0-9]{40}$")
                || x.ComponentVersion != "0.2.1-web.1" || x.NodeVersion != "25.4.0" || !x.ConfirmedReplacement) throw new InvalidOperationException("平台启动请求缺少明确确认、目标或运行组件信息。");
        }
        public static void Validate(PlatformLaunchPermit permit, PlatformLaunchRequest expected, string accountId, PublicSigningKey key, DateTime nowUtc)
        {
            ValidateRequest(permit); ValidateRequest(expected);
            var now = new DateTimeOffset(nowUtc).ToUnixTimeSeconds();
            if (RequestPayload(permit) != RequestPayload(expected) || permit.AccountId != accountId || !Guid.TryParse(permit.Id, out _) || !Guid.TryParse(permit.AccountId, out _)
                || !Regex.IsMatch(permit.ExportPlanHash ?? "", "^[a-f0-9]{64}$") || !Regex.IsMatch(permit.ReceiptIdentityHash ?? "", "^[a-f0-9]{64}$")
                || permit.IssuedAtUnixSeconds > now + 120 || permit.ExpiresAtUnixSeconds <= now || permit.ExpiresAtUnixSeconds <= permit.IssuedAtUnixSeconds || permit.ExpiresAtUnixSeconds - permit.IssuedAtUnixSeconds > 900) throw new InvalidOperationException("平台启动许可已过期或与当前预览不符。");
            Verify(Payload(permit), permit.Signature, key);
        }
        public static void Verify(string payload, string signature, PublicSigningKey key)
        {
            try { using (var rsa = RSA.Create()) { rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(key.Modulus), Exponent = Convert.FromBase64String(key.Exponent) }); if (rsa.KeySize < 2048 || !rsa.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new CryptographicException(); } }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException || ex is NullReferenceException) { throw new InvalidOperationException("平台启动许可签名无效。", ex); }
        }
    }
}
