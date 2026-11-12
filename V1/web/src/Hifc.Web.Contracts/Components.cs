using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hifc.Web.Contracts
{
    public sealed class ComponentLock
    {
        public string MainReleaseId { get; set; }
        public string MainSourceCommit { get; set; }
        public string ComponentId { get; set; }
        public string Version { get; set; }
        public string RuntimeIdentifier { get; set; } = "windows-x64";
        public int ProtocolMajor { get; set; } = 3;
        public string ArchiveSha256 { get; set; }
        public long ArchiveLength { get; set; }
        public string EntryPoint { get; set; }
    }
    public sealed class SignedComponentManifest
    {
        public ComponentLock Lock { get; set; }
        public string ArchiveUrl { get; set; }
        public string Signature { get; set; }
    }
    public static class ComponentTrust
    {
        public const long MaximumArchiveBytes = 256L * 1024 * 1024;
        public static string LockPayload(ComponentLock value) => string.Join("\n", value.MainReleaseId, value.MainSourceCommit, value.ComponentId, value.Version, value.RuntimeIdentifier, value.ProtocolMajor.ToString(CultureInfo.InvariantCulture), value.ArchiveSha256, value.ArchiveLength.ToString(CultureInfo.InvariantCulture), value.EntryPoint);
        public static string Payload(SignedComponentManifest value) => "HIFC-WEB-COMPONENT-1\n" + LockPayload(value.Lock) + "\n" + value.ArchiveUrl;
        public static Uri ManifestUri(Uri server, ComponentLock value) => new Uri(server, "components/" + value.MainReleaseId + "/" + value.ComponentId + "/" + value.Version + "/manifest");
        public static void ValidateLock(ComponentLock value)
        {
            if (value == null || !UpdateTrust.IsRelease(value.MainReleaseId)
                || !Regex.IsMatch(value.MainSourceCommit ?? "", "^[a-f0-9]{40}$")
                || !Regex.IsMatch(value.ComponentId ?? "", "^[a-z][a-z0-9-]{0,47}$")
                || !Regex.IsMatch(value.Version ?? "", "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[a-z0-9]+(?:[.-][a-z0-9]+)*)?$")
                || value.RuntimeIdentifier != "windows-x64" || value.ProtocolMajor != 3
                || !Regex.IsMatch(value.ArchiveSha256 ?? "", "^[a-f0-9]{64}$")
                || value.ArchiveLength < 1 || value.ArchiveLength > MaximumArchiveBytes
                || string.IsNullOrEmpty(value.EntryPoint) || value.EntryPoint.Length > 180
                || !Regex.IsMatch(value.EntryPoint, "^[A-Za-z0-9_./-]+$")
                || value.EntryPoint.StartsWith("/") || value.EntryPoint.Contains("..") || value.EntryPoint.Contains("//"))
                throw new InvalidOperationException("组件锁定信息无效，请重新安装完整的 Web 发行包。");
        }
        public static void ValidateServer(Uri server)
        {
            if (server == null || !server.IsAbsoluteUri || server.Scheme != "https" || server.AbsolutePath != "/" || server.Query != "" || server.Fragment != "" || server.UserInfo != "")
                throw new InvalidOperationException("组件源必须是同源 HTTPS 服务。");
        }
        public static void Validate(SignedComponentManifest value, ComponentLock expected, PublicSigningKey key, Uri server)
        {
            ValidateServer(server); ValidateLock(expected); ValidateLock(value?.Lock);
            if (LockPayload(value.Lock) != LockPayload(expected) || value.ArchiveUrl != new Uri(ManifestUri(server, expected).AbsoluteUri.Replace("/manifest", "/archive.zip")).AbsoluteUri)
                throw new InvalidOperationException("组件版本、主包来源或下载地址与锁定信息不符。");
            try {
                using (var rsa = RSA.Create()) {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(key.Modulus), Exponent = Convert.FromBase64String(key.Exponent) });
                    if (rsa.KeySize < 2048 || !rsa.VerifyData(Encoding.UTF8.GetBytes(Payload(value)), Convert.FromBase64String(value.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new CryptographicException();
                }
            } catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException || ex is NullReferenceException) { throw new InvalidOperationException("组件清单签名校验失败。", ex); }
        }
    }
}
