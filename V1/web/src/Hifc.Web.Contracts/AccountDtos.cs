using System;

namespace Hifc.Web.Contracts
{
    public sealed class Registration
    {
        public string Phone { get; set; }
        public string RealName { get; set; }
        public string Credential { get; set; }
    }
    public sealed class LoginRequest
    {
        public string Phone { get; set; }
        public string Credential { get; set; }
    }
    public sealed class RebindRequest
    {
        public string Phone { get; set; }
        public string Code { get; set; }
        public string Credential { get; set; }
    }
    public sealed class AccountInfo
    {
        public string Id { get; set; }
        public string Phone { get; set; }
        public string RealName { get; set; }
        public DateTime RegisteredAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime ServerTimeUtc { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsActive { get; set; }
        public long RowVersion { get; set; }
    }
    public sealed class AccountSession
    {
        public AccountInfo Account { get; set; }
        public string AccessToken { get; set; }
        public DateTime SessionExpiresAtUtc { get; set; }
    }
    public sealed class Compatibility
    {
        public int ProtocolMajor { get; set; } = 1;
        public string ProductVersion { get; set; } = "0.5.0";
        public string RuleVersion { get; set; }
        public DateTime ServerTimeUtc { get; set; }
        public PublicSigningKey SigningKey { get; set; }
    }
    public sealed class ApiError
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
