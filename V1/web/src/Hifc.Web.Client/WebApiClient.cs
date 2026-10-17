using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Contracts;

namespace Hifc.Web.Client
{
    public sealed class WebApiException : Exception
    {
        public string Code { get; }
        public WebApiException(string code, string message) : base(message) { Code = code; }
    }
    public sealed class WebApiClient : IDisposable
    {
        private readonly HttpClient http;
        private readonly TimeSpan requestTimeout;
        private string accessToken;
        private Compatibility compatibility;
        private DateTime serverTime;
        private long clockStart;
        public PublicSigningKey SigningKey => compatibility?.SigningKey;
        public DateTime ServerNowUtc
        {
            get
            {
                if (compatibility == null) throw new InvalidOperationException("请先连接并核对服务器版本。");
                return serverTime.AddSeconds((double)(Stopwatch.GetTimestamp() - clockStart) / Stopwatch.Frequency);
            }
        }
        public WebApiClient(Uri baseUri, TimeSpan? requestTimeout = null)
        {
            if (baseUri == null || !baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback)
                || baseUri.UserInfo != "" || baseUri.Query != "" || baseUri.Fragment != "" || baseUri.AbsolutePath != "/")
                throw new InvalidOperationException("请填写 HTTPS 服务器根地址；HTTP 仅供本机回环验证使用。");
            this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(90);
            if (this.requestTimeout <= TimeSpan.Zero || this.requestTimeout > TimeSpan.FromMinutes(5)) throw new InvalidOperationException("请求超时须大于零且不超过五分钟。");
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { BaseAddress = baseUri, Timeout = Timeout.InfiniteTimeSpan };
        }
        public void SetSession(AccountSession session) { accessToken = session?.AccessToken; }
        private async Task<AccountSession> SignInAsync(string path, object request, CancellationToken ct)
        {
            var session = await Request<AccountSession>(HttpMethod.Post, path, request, ct, false).ConfigureAwait(false);
            if (session?.Account == null || string.IsNullOrEmpty(session.AccessToken)) throw new InvalidOperationException("服务器没有返回完整的登录结果。");
            SetSession(session); return session;
        }
        public Task<AccountSession> RegisterAsync(Registration request, CancellationToken ct) => SignInAsync("accounts/register", request, ct);
        public Task<AccountSession> LoginAsync(LoginRequest request, CancellationToken ct) => SignInAsync("sessions", request, ct);
        public Task<AccountSession> RebindAsync(RebindRequest request, CancellationToken ct) => SignInAsync("accounts/rebind", request, ct);
        private async Task<T> Request<T>(HttpMethod method, string path, object body, CancellationToken ct, bool authenticated = true)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            using (var request = new HttpRequestMessage(method, "api/v1/" + path))
            {
                deadline.CancelAfter(requestTimeout);
                if (authenticated && !string.IsNullOrEmpty(accessToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                if (body != null) request.Content = new StringContent(JsonWire.Serialize(body), Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                    {
                        var text = await Read(response.Content, deadline.Token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            ApiError error = null;
                            try { if (text.Length < 16384) error = JsonWire.Deserialize<ApiError>(text); } catch (Newtonsoft.Json.JsonException) { }
                            throw new WebApiException(error?.Code ?? "HTTP_" + (int)response.StatusCode, error?.Message ?? "服务器返回 " + (int)response.StatusCode + "；请保留草稿并检查连接或登录状态。");
                        }
                        return string.IsNullOrWhiteSpace(text) ? default(T) : JsonWire.Deserialize<T>(text);
                    }
                }
                catch (HttpRequestException ex) { throw new WebApiException("NETWORK_UNAVAILABLE", "无法连接服务器；已有人工草稿仍然保留。" + ex.Message); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new WebApiException("NETWORK_TIMEOUT", "服务器响应超时；已有人工草稿仍然保留。"); }
            }
        }
        private static async Task<string> Read(HttpContent content, CancellationToken ct)
        {
            const int maximum = 32 * 1024 * 1024;
            if (content.Headers.ContentLength > maximum) throw new InvalidOperationException("服务器响应过大。");
            using (var source = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            {
                var bytes = new byte[8192]; int read;
                while ((read = await source.ReadAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > maximum) throw new InvalidOperationException("服务器响应过大。");
                    buffer.Write(bytes, 0, read);
                }
                return new UTF8Encoding(false, true).GetString(buffer.ToArray());
            }
        }
        public async Task<Compatibility> CompatibilityAsync(CancellationToken ct)
        {
            var result = await Request<Compatibility>(HttpMethod.Get, "compatibility", null, ct, false).ConfigureAwait(false);
            if (result == null || result.ProtocolMajor != 1 || string.IsNullOrWhiteSpace(result.RuleVersion) || result.SigningKey == null)
                throw new InvalidOperationException("服务器协议、规则版本或签名信息不完整。");
            compatibility = result; serverTime = DateTime.SpecifyKind(result.ServerTimeUtc, DateTimeKind.Utc); clockStart = Stopwatch.GetTimestamp(); return result;
        }
        public async Task<AccountInfo> MeAsync(CancellationToken ct) => await Request<AccountInfo>(HttpMethod.Get, "me", null, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("服务器没有返回账号信息。");
        public async Task<CapturePlan> StartCheckAsync(InventoryPage page, CancellationToken ct)
        {
            var result = await Request<CapturePlan>(HttpMethod.Post, "checks/start", page, ct).ConfigureAwait(false);
            ProtocolGuard.Match(page.Context, result?.Context); return result;
        }
        public async Task CaptureAsync(CapturePage page, CancellationToken ct) =>
            await Request<object>(HttpMethod.Post, "checks/" + Uri.EscapeDataString(page.Context.ScanId) + "/capture-pages", page, ct).ConfigureAwait(false);
        public async Task<CheckResult> CompleteAsync(RequestContext context, CancellationToken ct)
        {
            var result = await Request<CheckResult>(HttpMethod.Post, "checks/" + Uri.EscapeDataString(context.ScanId) + "/complete", new { Context = context }, ct).ConfigureAwait(false);
            ProtocolGuard.Match(context, result?.Context);
            if (!result.Complete) throw new InvalidOperationException("全面检查未完成，不能生成写入方案。");
            return result;
        }
        public async Task<EditPlan> PreviewAsync(PreviewRequest request, CancellationToken ct)
        {
            var plan = await Request<EditPlan>(HttpMethod.Post, "edit-plans", request, ct).ConfigureAwait(false);
            WriteGuard.Plan(plan, request.Context, SigningKey, ServerNowUtc); return plan;
        }
        public async Task<WritePermit> AuthorizeAsync(EditPlan plan, string idempotencyKey, CancellationToken ct)
        {
            WriteGuard.Plan(plan, plan.Context, SigningKey, ServerNowUtc);
            var permit = await Request<WritePermit>(HttpMethod.Post, "edit-plans/" + Uri.EscapeDataString(plan.Id) + "/authorize",
                new AuthorizeRequest { PlanHash = plan.Hash, IdempotencyKey = idempotencyKey }, ct).ConfigureAwait(false);
            WriteGuard.Permit(permit, plan, SigningKey, ServerNowUtc); return permit;
        }
        public async Task ReceiptAsync(WriteReceipt receipt, CancellationToken ct) => await Request<object>(HttpMethod.Post, "write-receipts", receipt, ct).ConfigureAwait(false);
        public void Dispose() => http.Dispose();
    }
}
