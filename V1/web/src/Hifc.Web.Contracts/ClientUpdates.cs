using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hifc.Web.Contracts
{
    public sealed class ClientUpdateManifest
    {
        public int Format { get; set; } = 1;
        public string Channel { get; set; } = "hifc-web-revit2020";
        public string ReleaseId { get; set; }
        public string SourceCommit { get; set; }
        public string PackageUrl { get; set; }
        public string Sha256 { get; set; }
        public long Length { get; set; }
        public string Signature { get; set; }
    }
    public sealed class ClientPackageFile
    {
        public string Name { get; set; }
        public string Sha256 { get; set; }
        public long Length { get; set; }
    }
    public sealed class ClientPackageInfo
    {
        public string Version { get; set; }
        public string AddInId { get; set; }
        public string ReleaseId { get; set; }
        public string SourceCommit { get; set; }
        public DateTime CreatedUtc { get; set; }
        public ClientPackageFile[] Files { get; set; } = new ClientPackageFile[0];
    }
    public static class UpdateTrust
    {
        public const string Server = "https://192.168.6.45:8443/";
        public const string AddInId = "53ef39d3-a187-4fb5-90f8-29e91d747602";
        public static readonly string[] PayloadNames = { "Hifc.Web.Revit.dll", "Hifc.Web.Client.dll", "Hifc.Web.Contracts.dll", "Newtonsoft.Json.dll", "Hifc.Web.Update.exe", "Hifc.Web.Update.exe.config" };
        public static bool IsRelease(string value) => value != null && Regex.IsMatch(value, "^0\\.5\\.0-dev-[0-9]{8}T[0-9]{6}Z$");
        public static string Payload(ClientUpdateManifest value) => string.Join("\n", "HIFC-WEB-UPDATE-1", value.Format.ToString(CultureInfo.InvariantCulture), value.Channel, value.ReleaseId, value.SourceCommit, value.PackageUrl, value.Sha256, value.Length.ToString(CultureInfo.InvariantCulture));
        public static void Validate(ClientUpdateManifest value, PublicSigningKey key, Uri server = null)
        {
            server = server ?? new Uri(Server);
            if (server.AbsoluteUri != Server && !(server.IsLoopback && server.Scheme == "http" && server.AbsolutePath == "/" && server.Query == "" && server.Fragment == "" && server.UserInfo == "")) throw new InvalidOperationException("更新源必须是 zetta HTTPS 服务。");
            if (value == null || value.Format != 1 || value.Channel != "hifc-web-revit2020" || !IsRelease(value.ReleaseId)
                || !Regex.IsMatch(value.SourceCommit ?? "", "^[a-f0-9]{40}$") || !Regex.IsMatch(value.Sha256 ?? "", "^[a-f0-9]{64}$") || value.Length < 1 || value.Length > 50 * 1024 * 1024
                || value.PackageUrl != server.AbsoluteUri + "updates/packages/" + value.ReleaseId + ".zip") throw new InvalidOperationException("更新清单的版本、来源或文件信息无效。");
            try {
                using (var rsa = RSA.Create()) {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(key.Modulus), Exponent = Convert.FromBase64String(key.Exponent) });
                    if (rsa.KeySize < 2048 || !rsa.VerifyData(Encoding.UTF8.GetBytes(Payload(value)), Convert.FromBase64String(value.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new CryptographicException();
                }
            } catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException || ex is NullReferenceException) { throw new InvalidOperationException("更新清单签名校验失败。", ex); }
        }
    }
}
