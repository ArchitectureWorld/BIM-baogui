using System;
using System.Security.Cryptography;
using System.Text;
using Hifc.Web.Contracts;

namespace Hifc.Web.Client
{
    public static class WriteGuard
    {
        private static void Signature(string payload, string signature, PublicSigningKey key)
        {
            try
            {
                if (key == null || string.IsNullOrEmpty(signature) || signature.Length > 2048) throw new InvalidOperationException("缺少服务器签名。");
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(key.Modulus), Exponent = Convert.FromBase64String(key.Exponent) });
                    if (!rsa.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        throw new InvalidOperationException("服务器签名无效，已阻止写入。");
                }
            }
            catch (FormatException ex) { throw new InvalidOperationException("服务器签名格式无效。", ex); }
            catch (CryptographicException ex) { throw new InvalidOperationException("无法验证服务器签名。", ex); }
            catch (ArgumentException ex) { throw new InvalidOperationException("服务器签名密钥无效。", ex); }
        }
        public static void Plan(EditPlan plan, RequestContext expected, PublicSigningKey key, DateTime nowUtc)
        {
            if (plan == null || plan.Changes == null || string.IsNullOrWhiteSpace(plan.Id) || nowUtc >= plan.ExpiresAtUtc)
                throw new InvalidOperationException("写入方案已过期或不完整，请保留草稿并重新检查。");
            ProtocolGuard.Match(expected, plan.Context);
            if (plan.Hash != Canonical.PlanHash(plan)) throw new InvalidOperationException("写入方案散列不一致。");
            Signature(plan.Hash, plan.Signature, key);
        }
        public static void Permit(WritePermit permit, EditPlan plan, PublicSigningKey key, DateTime nowUtc)
        {
            Plan(plan, plan.Context, key, nowUtc);
            if (permit == null || string.IsNullOrWhiteSpace(permit.Id) || nowUtc >= permit.ExpiresAtUtc || permit.ExpiresAtUtc > plan.ExpiresAtUtc
                || permit.PlanId != plan.Id || permit.PlanHash != plan.Hash) throw new InvalidOperationException("写入许可已失效或不属于本轮方案。");
            ProtocolGuard.Match(plan.Context, permit.Context);
            Signature(Canonical.PermitPayload(permit), permit.Signature, key);
        }
        public static void Capture(CheckResult basis, ElementRecord[] current, ParameterValue[] values)
        {
            if (basis == null || !basis.Complete || current == null || values == null || basis.Context == null
                || basis.Context.SnapshotHash != Canonical.InventoryHash(current) || basis.CaptureHash != Canonical.ValuesHash(values))
                throw new InvalidOperationException("当前模型、类型、参数来源或原值已变化，请重新检查后合并草稿。");
        }
    }
}
