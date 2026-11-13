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
        </title><style>*{box-sizing:border-box}body{margin:0;background:#f2f5f8;color:#18324c;font:15px/1.75 system-ui,"Microsoft YaHei",sans-serif}main{max-width:1180px;margin:45px auto;padding:35px;background:white;border-radius:16px;box-shadow:0 12px 50px #18344b12}nav{display:flex;gap:22px;border-bottom:1px solid #dbe4ed;padding-bottom:18px;margin-bottom:30px}h1{font-size:32px;margin:12px 0}small,.muted{color:#62788b}a{color:#245e8f}button,.button{display:inline-block;background:#245e8f;color:white;text-decoration:none;border:0;border-radius:6px;padding:10px 17px;cursor:pointer;font:inherit}input{font:inherit;padding:8px 10px;border:1px solid #c8d6e4;border-radius:6px;max-width:100%}label{display:block;margin:16px 0 5px}form{margin:10px 0}.login{max-width:420px}.login input{width:100%}table{width:100%;border-collapse:collapse}th,td{text-align:left;border-bottom:1px solid #e4eaf0;padding:14px 10px;vertical-align:top}td form{display:flex;gap:7px;flex-wrap:wrap}td input{width:155px}.danger{background:#b53e49}.delete-link{color:#b53e49}.notice{padding:12px 16px;background:#edf8f2;border-radius:8px;color:#186347}.active{color:#186347}.expired{color:#ae3f42}.steps{display:grid;grid-template-columns:repeat(4,1fr);gap:16px;margin:35px 0}.step{background:#eef4f8;border-radius:9px;padding:20px}.scroll{overflow:auto}code{word-break:break-all}textarea{width:100%;font:16px monospace;padding:14px}@media(max-width:800px){main{margin:14px;padding:22px}.steps{grid-template-columns:1fr 1fr}h1{font-size:26px}}</style>
        <style>
        .account-heading{display:flex;align-items:center;justify-content:space-between;gap:24px;margin:4px 0 24px}
        .account-heading h1{font-size:28px;line-height:1.35;margin:0 0 6px;letter-spacing:-.5px}.account-heading p{margin:0;color:#7a8998;font-size:13px}.account-heading form{margin:0}
        .secondary-button{background:white;color:#52687d;border:1px solid #dce5ee;padding:7px 14px;font-size:13px}
        .account-table{border:1px solid #e3e9f0;border-radius:12px;overflow:auto}.account-table table{min-width:760px}
        .account-table th{background:#f7f9fc;color:#738499;font-size:12px;font-weight:500;padding:13px 20px;white-space:nowrap}
        .account-table td{padding:17px 20px;vertical-align:middle;border-color:#edf1f5}.account-table tr:last-child td{border-bottom:0}.account-table th:last-child{text-align:right}
        .member-name{display:block;font-size:14px;font-weight:600;color:#243e57}.member-phone{display:block;margin-top:2px;font-size:12px;color:#8b99a8;font-variant-numeric:tabular-nums}
        .date-cell{font-size:13px;color:#607489;white-space:nowrap;font-variant-numeric:tabular-nums}.account-status{display:inline-block;border-radius:20px;padding:3px 10px;font-size:12px;font-weight:500;white-space:nowrap}
        .account-status.active{background:#edf8f1;color:#26764d}.account-status.expired{background:#fff1ef;color:#af564c}
        .account-actions{display:flex;align-items:center;justify-content:flex-end;gap:10px}.extend-button{background:#edf4fc;color:#2e66a0;padding:6px 15px;font-size:13px;border:1px solid #dce9f8;border-radius:7px;white-space:nowrap}
        .account-actions .delete-link{padding:6px 8px;color:#a76b70;text-decoration:none;font-size:13px;white-space:nowrap}.account-actions .delete-link:hover{color:#b33444;background:#fff4f5;border-radius:7px}
        .extension-panel{width:min(440px,calc(100vw - 32px));padding:26px;border:1px solid #dfe7ef;border-radius:18px;color:#243e57;box-shadow:0 24px 80px #16314b35}
        .extension-panel::backdrop{background:#132e4933;backdrop-filter:blur(2px)}.extension-header{display:flex;align-items:center;justify-content:space-between;margin-bottom:20px}.extension-header h2{margin:0;font-size:20px;letter-spacing:-.4px}
        .close-panel{background:transparent;color:#8494a4;padding:0 6px;line-height:1.3;font-size:26px}.extension-person{padding:14px 16px;background:#f7f9fc;border:1px solid #e8eef5;border-radius:10px;margin-bottom:20px}.extension-person strong{font-size:15px}.extension-person span{margin-left:10px;font-size:12px;color:#8494a4}.extension-person p{margin:6px 0 0;font-size:12px;color:#708398}
        .extension-mode{display:flex;gap:4px;border:0;padding:4px;margin:0 0 22px;background:#f0f4f8;border-radius:9px}.extension-mode label{flex:1;margin:0;padding:8px;text-align:center;font-size:13px;border-radius:6px;cursor:pointer;color:#7c8b9a}.extension-mode label:has(input:checked){background:white;color:#2d6096;box-shadow:0 1px 4px #16314b12}.extension-mode input{width:auto;margin:0 6px 0 0;accent-color:#336da5}
        .extension-panel:has(.mode-days:checked) .date-pane,.extension-panel:has(.mode-date:checked) .days-pane{display:none}
        .extension-panel form{margin:0}.extension-panel .field-label{margin:0 0 8px;font-size:13px;color:#52687e}.extension-panel .field-input{display:block;width:100%;height:46px;border-color:#d9e3ed;border-radius:8px;padding:10px 12px;outline:none}.extension-panel .field-input:focus{border-color:#4383bc;box-shadow:0 0 0 3px #4083bb16}
        .extension-help{font-size:12px;color:#8b99a7;line-height:1.7;margin:10px 0 24px}.extension-footer{display:flex;justify-content:flex-end;gap:9px}.extension-footer button{font-size:13px;padding:8px 18px;border-radius:7px}.extension-footer .secondary-button{border:1px solid #dce5ee}
        </style>
        <main><nav><b>HIFC 0.5.0-Web</b><a href="/">客户端入口</a><a href="/admin">账号管理</a></nav>
        """ + content + "</main></html>";
    public static string Home() => Page("HIFC Web · 客户端入口", "<small>Revit 2020 · 开发体验版</small><h1>检查、编辑，并验证模型写入结果</h1><p>安装独立 Web 插件，在 Revit 中完成报规模型属性管理。模型保留在本机，服务器提供本轮检查与写入方案。</p><p><a class=\"button\" href=\"/downloads/HIFC-0.5.0-Web.zip\">下载 Web 插件与安装器</a></p><div class=\"steps\"><div class=\"step\">01<br><b>登记与连接</b><br>手机号、真实姓名</div><div class=\"step\">02<br><b>全面检查</b><br>盘点当前文档与字段</div><div class=\"step\">03<br><b>编辑与预览</b><br>草稿自动保留在本机</div><div class=\"step\">04<br><b>写入与复检</b><br>回读验证后自行保存</div></div><h2>开始体验</h2><p>解压后双击“安装 Web 版.cmd”，按安装器核对证书。打开模型副本，在 Revit 的独立“HIFC-V1-Web”页签中点击“模型检查 / 属性管理”。</p><p class=\"muted\">普通账号首次登记有效 168 小时。重装或换机保留原期限；需要延期时联系管理员。Web 版使用独立安装目录和入口。</p>");
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
            var token = Token(context); var rows = await accounts.ListForAdministratorAsync(Guid.Parse(Actor(context)), context.RequestAborted);
            var html = new StringBuilder("<div class=\"account-heading\"><div><h1>账号管理</h1><p>账号期限均按北京时间显示</p></div><form method=\"post\" action=\"/admin/logout\">" + Hidden(token) + "<button class=\"secondary-button\">退出登录</button></form></div><div class=\"account-table\"><table><tr><th>成员</th><th>登记时间</th><th>到期时间</th><th>状态</th><th>操作</th></tr>");
            var editors = new StringBuilder();
            foreach (var row in rows.Where(a => !a.IsAdmin))
            {
                var path = "/admin/accounts/" + row.Id; var hidden = Hidden(token) + "<input type=\"hidden\" name=\"version\" value=\"" + row.RowVersion + "\">";
                string Date(DateTime utc) => new DateTimeOffset(utc).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                var earliestDate = new DateTimeOffset(row.ExpiresAtUtc > row.ServerTimeUtc ? row.ExpiresAtUtc : row.ServerTimeUtc)
                    .ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var editorId = "extension-" + row.Id;
                html.Append($$"""
                    <tr><td><span class="member-name">{{E(row.RealName)}}</span><span class="member-phone">{{E(row.Phone)}}</span></td>
                    <td class="date-cell">{{Date(row.RegisteredAtUtc)}}</td><td class="date-cell">{{Date(row.ExpiresAtUtc)}}</td>
                    <td><span class="account-status {{(row.IsActive ? "active" : "expired")}}">{{(row.IsActive ? "有效" : "已到期")}}</span></td>
                    <td><div class="account-actions"><button type="button" class="extend-button" popovertarget="{{editorId}}">延期</button><a class="delete-link" href="{{path}}/delete">删除</a></div></td></tr>
                    """);
                editors.Append($$"""
                    <section id="{{editorId}}" class="extension-panel" popover aria-labelledby="{{editorId}}-title">
                    <div class="extension-header"><h2 id="{{editorId}}-title">调整使用期限</h2><button type="button" class="close-panel" aria-label="关闭延期面板" popovertarget="{{editorId}}" popovertargetaction="hide">×</button></div>
                    <div class="extension-person"><strong>{{E(row.RealName)}}</strong><span>{{E(row.Phone)}}</span><p>当前到期时间 {{Date(row.ExpiresAtUtc)}}</p></div>
                    <fieldset class="extension-mode" aria-label="延期方式">
                    <label><input type="radio" class="mode-days" name="mode-{{row.Id}}" value="days" checked>按天数</label>
                    <label><input type="radio" class="mode-date" name="mode-{{row.Id}}" value="date">指定日期</label>
                    </fieldset>
                    <div class="days-pane"><form method="post" action="{{path}}/extend">{{hidden}}
                    <label class="field-label" for="{{editorId}}-days">延期天数</label><input class="field-input" id="{{editorId}}-days" type="number" name="days" min="1" max="36500" step="1" placeholder="输入天数" required>
                    <p class="extension-help">有效账号从现有期限顺延，已到期账号从当前时间起算。</p>
                    <div class="extension-footer"><button type="button" class="secondary-button" popovertarget="{{editorId}}" popovertargetaction="hide">取消</button><button type="submit">确认延期</button></div>
                    </form></div>
                    <div class="date-pane"><form method="post" action="{{path}}/extend">{{hidden}}
                    <label class="field-label" for="{{editorId}}-date">到期日期</label><input class="field-input" id="{{editorId}}-date" type="date" name="untilDate" min="{{earliestDate}}" required>
                    <p class="extension-help">包含所选日期当天，按北京时间计算。</p>
                    <div class="extension-footer"><button type="button" class="secondary-button" popovertarget="{{editorId}}" popovertargetaction="hide">取消</button><button type="submit">确认延期</button></div>
                    </form></div></section>
                    """);
            }
            html.Append("</table></div>").Append(editors);
            if (context.Request.Query["extended"] == "1") html.Insert(0, "<p class=\"notice\" role=\"status\">延期已保存，请核对更新后的到期时间。</p>");
            if (context.Request.Query["deleted"] == "1") html.Insert(0, "<p class=\"notice\" role=\"status\">账号已删除，登录凭证和服务器检查数据已清除。</p>");
            return Html(context, "账号管理", html.ToString());
        }).RequireAuthorization();
        app.MapGet("/admin/accounts/{id:guid}/delete", async (Guid id, HttpContext context, AccountStore accounts) => {
            var account = await accounts.GetAsync(id, context.RequestAborted);
            if (account == null) return Html(context, "账号已不存在", "<h1>账号已不存在</h1><p><a href=\"/admin\">返回账号管理</a></p>");
            if (account.IsAdmin) throw new WebOperationException("ADMIN_PROTECTED", 403, "管理员账号不能删除。");
            return Html(context, "确认删除账号", "<h1>确认删除账号</h1><p>即将删除成员 <b>" + E(account.RealName) + "</b>（" + E(account.Phone) + "）。</p><p>该账号在所有电脑上的登录将立即失效，服务器上的检查与写入记录一并清除。删除操作会留存审计；本机模型和草稿保留。</p><p class=\"expired\">删除后无法撤销。请核对成员信息后确认。</p><form method=\"post\" action=\"/admin/accounts/" + account.Id + "/delete\">" + Hidden(Token(context)) + "<input type=\"hidden\" name=\"version\" value=\"" + account.RowVersion + "\"><button class=\"danger\" name=\"confirm\" value=\"delete\">确认删除</button> <a href=\"/admin\">取消，返回账号管理</a></form>");
        }).RequireAuthorization();
        app.MapPost("/admin/accounts/{id:guid}/delete", async (Guid id, HttpContext context, AccountStore accounts) => {
            var form = await Form(context);
            if (form["confirm"] != "delete" || !long.TryParse(form["version"], out var version)) throw new WebOperationException("DELETE_CONFIRMATION_REQUIRED", 400, "请核对成员信息并确认删除。");
            await accounts.DeleteAsync(Guid.Parse(Actor(context)), id, version, context.RequestAborted);
            return Results.Redirect("/admin?deleted=1");
        }).RequireAuthorization();
        app.MapPost("/admin/accounts/{id:guid}/extend", async (Guid id, HttpContext context, AccountStore accounts) => {
            var form = await Form(context); if (!long.TryParse(form["version"], out var version)) throw new InvalidOperationException("页面版本已失效，请刷新。");
            int? days = string.IsNullOrEmpty(form["days"]) ? null : int.TryParse(form["days"], out var number) ? number : throw new InvalidOperationException("延期天数无效。");
            await accounts.ExtendAsync(Guid.Parse(Actor(context)), id, version, days, string.IsNullOrEmpty(form["untilDate"]) ? null : form["untilDate"].ToString(), context.RequestAborted); return Results.Redirect("/admin?extended=1");
        }).RequireAuthorization();
        app.MapPost("/admin/logout", async (HttpContext context) => { await Form(context); await context.SignOutAsync(Scheme); context.Response.Cookies.Delete(CsrfCookie, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.Strict }); return Results.Redirect("/admin/login"); }).RequireAuthorization();
    }
}
