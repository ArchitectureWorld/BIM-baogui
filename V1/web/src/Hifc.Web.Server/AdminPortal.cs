using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

namespace Hifc.Web.Server;

public static class AdminPortal
{
    private const string Scheme = "HifcAdministrator", CsrfCookie = "__Host-Hifc.Web.Csrf";
    private static string E(string value) => HtmlEncoder.Default.Encode(value ?? "");
    public static void Configure(WebApplicationBuilder builder, WebRuntimeOptions options)
    {
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.SigningKeyPath)), "data-protection"); Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        builder.Services.AddDataProtection().SetApplicationName("HIFC.Web.Admin.v1").PersistKeysToFileSystem(new DirectoryInfo(directory));
        builder.Services.AddAuthentication(Scheme).AddCookie(Scheme, cookie => {
            cookie.Cookie.Name = "__Host-Hifc.Web.Admin"; cookie.Cookie.Path = "/"; cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always; cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8); cookie.SlidingExpiration = false;
            cookie.Events = new CookieAuthenticationEvents {
                OnRedirectToLogin = context => { if (HttpMethods.IsGet(context.Request.Method)) context.Response.Redirect("/admin/login"); else context.Response.StatusCode = 401; return Task.CompletedTask; },
                OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; },
                OnValidatePrincipal = async context => {
                    if (!Guid.TryParse(context.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || (await context.HttpContext.RequestServices.GetRequiredService<AccountStore>().GetAsync(id, context.HttpContext.RequestAborted))?.IsAdmin != true) context.RejectPrincipal();
                }
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(limits => {
            limits.RejectionStatusCode = 429;
            limits.AddPolicy("identity", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
    }
    private static IDataProtector Protector(HttpContext context) => context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("HIFC.Web.Admin.Csrf.v1");
    private static string Actor(HttpContext context) => context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : "anonymous";
    private static string Token(HttpContext context)
    {
        var nonce = context.Request.Cookies[CsrfCookie];
        if (nonce == null || nonce.Length != 64)
        {
            nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            context.Response.Cookies.Append(CsrfCookie, nonce, new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromHours(8) });
        }
        return Protector(context).Protect(nonce + "|" + Actor(context) + "|" + DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds());
    }
    private static async Task<IFormCollection> Form(HttpContext context)
    {
        if (!context.Request.HasFormContentType) throw new WebOperationException("CSRF_REJECTED", 400, "请从管理页面提交操作。");
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        try
        {
            var parts = Protector(context).Unprotect(form["csrf"].ToString()).Split('|'); var nonce = context.Request.Cookies[CsrfCookie] ?? "";
            if (parts.Length != 3 || parts[1] != Actor(context) || !long.TryParse(parts[2], out var expiry) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[0]), Encoding.UTF8.GetBytes(nonce))) throw new CryptographicException();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { throw new WebOperationException("CSRF_REJECTED", 400, "页面凭证已失效，请刷新后重试。"); }
        return form;
    }
    private static string Hidden(string csrf) => "<input type=\"hidden\" name=\"csrf\" value=\"" + E(csrf) + "\">";
    private static IResult Html(HttpContext context, string title, string content)
    {
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'none'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; form-action 'self'; base-uri 'none'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        return Results.Content(Page(title, content), "text/html; charset=utf-8");
    }
    private static string Page(string title, string content) => """
        <!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>
        """ + E(title) + """
        </title><style>*{box-sizing:border-box}body{margin:0;background:#f2f5f8;color:#18324c;font:15px/1.75 system-ui,"Microsoft YaHei",sans-serif}main{max-width:1180px;margin:45px auto;padding:35px;background:white;border-radius:16px;box-shadow:0 12px 50px #18344b12}nav{display:flex;gap:22px;border-bottom:1px solid #dbe4ed;padding-bottom:18px;margin-bottom:30px}h1{font-size:32px;margin:12px 0}small,.muted{color:#62788b}a{color:#245e8f}button,.button{display:inline-block;background:#245e8f;color:white;text-decoration:none;border:0;border-radius:6px;padding:10px 17px;cursor:pointer;font:inherit}input{font:inherit;padding:8px 10px;border:1px solid #c8d6e4;border-radius:6px;max-width:100%}label{display:block;margin:16px 0 5px}form{margin:10px 0}.login{max-width:420px}.login input{width:100%}table{width:100%;border-collapse:collapse}th,td{text-align:left;border-bottom:1px solid #e4eaf0;padding:14px 10px;vertical-align:top}td form{display:flex;gap:7px;flex-wrap:wrap}td input{width:155px}.active{color:#186347}.expired{color:#ae3f42}.steps{display:grid;grid-template-columns:repeat(4,1fr);gap:16px;margin:35px 0}.step{background:#eef4f8;border-radius:9px;padding:20px}.scroll{overflow:auto}code{word-break:break-all}textarea{width:100%;font:16px monospace;padding:14px}@media(max-width:800px){main{margin:14px;padding:22px}.steps{grid-template-columns:1fr 1fr}h1{font-size:26px}}</style>
        <main><nav><b>HIFC 0.5.0-Web</b><a href="/">客户端入口</a><a href="/admin">账号管理</a></nav>
        """ + content + "</main></html>";
    public static string Home() => Page("HIFC Web · 客户端入口", "<small>Revit 2020 · 开发体验版</small><h1>检查、编辑，并验证模型写入结果</h1><p>安装独立 Web 插件，在 Revit 中完成报规模型属性管理。模型保留在本机，服务器提供本轮检查与写入方案。</p><p><a class=\"button\" href=\"/downloads/HIFC-0.5.0-Web.zip\">下载 Web 插件与安装器</a></p><div class=\"steps\"><div class=\"step\">01<br><b>登记与连接</b><br>手机号、真实姓名</div><div class=\"step\">02<br><b>全面检查</b><br>盘点当前文档与字段</div><div class=\"step\">03<br><b>编辑与预览</b><br>草稿自动保留在本机</div><div class=\"step\">04<br><b>写入与复检</b><br>回读验证后自行保存</div></div><h2>开始体验</h2><p>解压后双击“安装 Web 版.cmd”，按安装器核对证书。打开模型副本，在 Revit 的“附加模块 → 外部工具”中选择“HIFC 0.5.0-Web”。</p><p class=\"muted\">普通账号首次登记有效 168 小时。重装或换机保留原期限；需要延期时联系管理员。Web 版使用独立安装目录和入口。</p>");
    public static void Map(WebApplication app)
    {
        app.MapGet("/downloads/HIFC-0.5.0-Web.zip", () => { var path = Path.Combine(AppContext.BaseDirectory, "downloads", "HIFC-0.5.0-Web.zip"); return File.Exists(path) ? Results.File(path, "application/zip", "HIFC-0.5.0-Web.zip") : Results.NotFound(); });
        app.MapGet("/admin/login", (HttpContext context) => Html(context, "管理员登录", "<h1>管理员登录</h1><p>普通成员在 Revit Web 插件内登记，无需登录管理后台。</p><form class=\"login\" method=\"post\" action=\"/admin/login\">" + Hidden(Token(context)) + "<label>管理员手机号</label><input name=\"phone\" autocomplete=\"username\" required><label>管理员密码</label><input name=\"password\" type=\"password\" autocomplete=\"current-password\" required><p><button>登录账号管理</button></p></form><p class=\"muted\">首次使用，请在 Windows 运行安装包中的“初始化管理员.cmd”。密码只在你本人输入时设置。</p>"));
        app.MapPost("/admin/login", async (HttpContext context, IdentityStore identities) => {
            var form = await Form(context); var account = await identities.AdministratorLoginAsync(form["phone"], form["password"], context.RequestAborted);
            await context.SignInAsync(Scheme, new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, account.Id), new Claim(ClaimTypes.Role, "Administrator") }, Scheme)), new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
            return Results.Redirect("/admin");
        }).RequireRateLimiting("identity");
        app.MapGet("/admin", async (HttpContext context, AccountStore accounts) => {
            var token = Token(context); var rows = await accounts.ListForAdministratorAsync(Guid.Parse(Actor(context)), context.RequestAborted); var html = new StringBuilder("<h1>账号管理</h1><p>期限按北京时间显示；普通账号只有“有效 / 已到期”。</p><form method=\"post\" action=\"/admin/logout\">" + Hidden(token) + "<button>退出管理后台</button></form><div class=\"scroll\"><table><tr><th>成员</th><th>登记 / 到期时间</th><th>状态</th><th>自定义延期</th><th>更换电脑</th></tr>");
            foreach (var row in rows.Where(a => !a.IsAdmin))
            {
                var path = "/admin/accounts/" + row.Id; var hidden = Hidden(token) + "<input type=\"hidden\" name=\"version\" value=\"" + row.RowVersion + "\">";
                string Date(DateTime utc) => new DateTimeOffset(utc).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                html.Append("<tr><td>").Append(E(row.RealName)).Append("<br>").Append(E(row.Phone)).Append("</td><td>").Append(Date(row.RegisteredAtUtc)).Append("<br>").Append(Date(row.ExpiresAtUtc)).Append("</td><td class=\"").Append(row.IsActive ? "active\">有效" : "expired\">已到期").Append("</td><td><form method=\"post\" action=\"").Append(path).Append("/extend\">").Append(hidden).Append("<input name=\"days\" type=\"number\" min=\"1\" max=\"36500\" placeholder=\"增加天数\" required><button>按天延期</button></form><form method=\"post\" action=\"").Append(path).Append("/extend\">").Append(hidden).Append("<input name=\"untilDate\" type=\"date\" required><button>有效至该日结束</button></form></td><td><form method=\"post\" action=\"").Append(path).Append("/rebind\">").Append(Hidden(token)).Append("<button>生成一次性绑定码</button></form></td></tr>");
            }
            html.Append("</table></div>"); return Html(context, "账号管理", html.ToString());
        }).RequireAuthorization();
        app.MapPost("/admin/accounts/{id:guid}/extend", async (Guid id, HttpContext context, AccountStore accounts) => {
            var form = await Form(context); if (!long.TryParse(form["version"], out var version)) throw new InvalidOperationException("页面版本已失效，请刷新。");
            int? days = string.IsNullOrEmpty(form["days"]) ? null : int.TryParse(form["days"], out var number) ? number : throw new InvalidOperationException("延期天数无效。");
            await accounts.ExtendAsync(Guid.Parse(Actor(context)), id, version, days, string.IsNullOrEmpty(form["untilDate"]) ? null : form["untilDate"].ToString(), context.RequestAborted); return Results.Redirect("/admin");
        }).RequireAuthorization();
        app.MapPost("/admin/accounts/{id:guid}/rebind", async (Guid id, HttpContext context, IdentityStore identities) => {
            await Form(context); var code = await identities.IssueRebindAsync(Guid.Parse(Actor(context)), id, context.RequestAborted);
            return Html(context, "一次性绑定码", "<h1>一次性绑定码</h1><p>请通过既有联系渠道交给该成员。10 分钟内有效，使用一次即失效；原到期日保持不变。</p><textarea readonly rows=\"2\">" + E(code) + "</textarea><p><a href=\"/admin\">返回账号管理</a></p>");
        }).RequireAuthorization();
        app.MapPost("/admin/logout", async (HttpContext context) => { await Form(context); await context.SignOutAsync(Scheme); context.Response.Cookies.Delete(CsrfCookie, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.Strict }); return Results.Redirect("/admin/login"); }).RequireAuthorization();
    }
}
