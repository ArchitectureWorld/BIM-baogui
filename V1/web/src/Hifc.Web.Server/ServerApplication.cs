using Hifc.Web.Contracts;
using Hifc.Web.RuleEngine;
using Npgsql;

namespace Hifc.Web.Server;

public sealed class WebRuntimeOptions
{
    public string DatabaseConnectionString { get; init; }
    public string SigningKeyPath { get; init; }
    public string BuildRevision { get; init; } = "development";

    public static WebRuntimeOptions FromEnvironment()
    {
        var passwordPath = Environment.GetEnvironmentVariable("HIFC_DATABASE_PASSWORD_FILE") ?? "/run/secrets/app_db_password";
        var connection = new NpgsqlConnectionStringBuilder {
            Host = Environment.GetEnvironmentVariable("HIFC_DATABASE_HOST") ?? "postgres",
            Database = Environment.GetEnvironmentVariable("HIFC_DATABASE_NAME") ?? "hifc_web",
            Username = Environment.GetEnvironmentVariable("HIFC_DATABASE_USER") ?? "hifc_web",
            Password = File.ReadAllText(passwordPath).TrimEnd('\r', '\n'), SslMode = SslMode.Disable, GssEncryptionMode = GssEncryptionMode.Disable,
            Timeout = 15, CommandTimeout = 15
        };
        if (string.IsNullOrEmpty(connection.Password)) throw new InvalidOperationException("Database secret is empty.");
        return new WebRuntimeOptions {
            DatabaseConnectionString = connection.ConnectionString,
            SigningKeyPath = Environment.GetEnvironmentVariable("HIFC_SIGNING_KEY_PATH") ?? "/var/lib/hifc/signing.pem",
            BuildRevision = Environment.GetEnvironmentVariable("HIFC_BUILD_REVISION") ?? "development"
        };
    }
}

public static class ServerApplication
{
    public static WebApplication Create(string[] args, WebRuntimeOptions options)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(settings => settings.AddServerHeader = false);
        builder.Services.AddSingleton(NpgsqlDataSource.Create(options.DatabaseConnectionString));
        builder.Services.AddSingleton<AccountStore>();
        builder.Services.AddSingleton(new PlanSigner(options.SigningKeyPath));
        builder.Services.AddSingleton<RuleAdapter>();
        builder.Services.AddSingleton<WorkflowStore>();
        var app = builder.Build();
        app.Use(async (context, next) => {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next();
        });
        app.MapGet("/health/live", () => Results.Ok(new { State = "live", Version = "0.5.0-dev", options.BuildRevision, AuthenticationConfigured = false }));
        app.MapGet("/health/ready", async (NpgsqlDataSource source, HttpContext context) => {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            try {
                await using var command = source.CreateCommand("SELECT clock_timestamp(), version, to_regclass('hifc_web.accounts') IS NOT NULL AND to_regclass('hifc_web.account_audit') IS NOT NULL FROM hifc_web.schema_version WHERE singleton");
                await using var reader = await command.ExecuteReaderAsync(deadline.Token);
                if (!await reader.ReadAsync(deadline.Token) || reader.GetInt32(1) != 1 || !reader.GetBoolean(2)) throw new InvalidOperationException("Schema unavailable.");
                return Results.Ok(new { State = "ready", Version = "0.5.0-dev", Database = "connected", AuthenticationConfigured = false, ServerTimeUtc = reader.GetDateTime(0) });
            } catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or InvalidOperationException) {
                return Results.Json(new { State = "not-ready", Code = "DATABASE_UNAVAILABLE" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        app.MapGet("/api/v1/compatibility", (RuleAdapter rules, PlanSigner signer) => Results.Ok(new Compatibility {
            RuleVersion = rules.RuleVersion, ServerTimeUtc = DateTime.UtcNow, SigningKey = signer.PublicKey
        }));
        static IResult Closed() => Results.Json(new ApiError { Code = "AUTHENTICATION_NOT_CONFIGURED", Message = "登录方案尚未确定，账号和模型操作暂未开放。" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        var methods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD" };
        app.MapMethods("/api/v1/{**path}", methods, Closed);
        app.MapMethods("/admin/{**path}", methods, Closed);
        app.MapGet("/", () => Results.Content(StatusPage, "text/html; charset=utf-8"));
        return app;
    }

    private const string StatusPage = """
        <!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>HIFC Web · 服务状态</title>
        <style>body{margin:0;background:#f2f5f8;color:#19324c;font:16px/1.8 system-ui,"Microsoft YaHei",sans-serif}main{max-width:650px;margin:12vh auto;padding:40px;background:white;border-radius:16px;box-shadow:0 12px 50px #18344b12}small{color:#62788b}h1{font-size:36px;line-height:1.3}#state{padding:16px 20px;background:#eef4f8;border-radius:8px}button{padding:9px 18px;border:0;border-radius:6px;color:white;background:#245e8f;cursor:pointer}@media(max-width:700px){main{margin:24px;padding:26px}}</style>
        <main><small>HIFC WEB · 开发环境</small><h1>服务器已启动</h1><p id="state">正在核对数据库连接……</p>
        <p>当前开放服务状态与版本检查。账号登录、模型检查和写入尚未开放。</p><p>服务器部署完成后，仍需完成客户端联调与模型体验验收。</p>
        <button id="refresh">刷新状态</button><p><small>Web 0.5.0 开发版</small></p></main>
        <script>async function refresh(){const output=document.getElementById('state');try{const reply=await fetch('/health/ready',{cache:'no-store'});output.textContent=reply.ok?'数据库已连接，服务基础环境就绪。':'数据库暂未就绪，请稍后刷新。';}catch{output.textContent='连接暂时中断，请稍后刷新。';}}document.getElementById('refresh').onclick=refresh;refresh();</script></html>
        """;
}
