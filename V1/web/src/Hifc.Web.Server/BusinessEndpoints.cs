using Hifc.Web.Contracts;

namespace Hifc.Web.Server;

public static class BusinessEndpoints
{
    private sealed class SessionFilter(IdentityStore identities) : IEndpointFilter
    {
        public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var header = context.HttpContext.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header.Substring(7) : null;
            var account = await identities.ResolveAsync(token, context.HttpContext.RequestAborted);
            if (account == null) return Results.Json(new ApiError { Code = "LOGIN_REQUIRED", Message = "请先登记或重新登录，已有草稿仍然保留。" }, statusCode: 401);
            context.HttpContext.Items["HifcAccount"] = account; return await next(context);
        }
    }
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v1/accounts/register", async (Registration request, IdentityStore identities, HttpContext context) => Results.Ok(await identities.RegisterAsync(request, context.RequestAborted))).RequireRateLimiting("identity");
        app.MapPost("/api/v1/sessions", async (LoginRequest request, IdentityStore identities, HttpContext context) => Results.Ok(await identities.LoginAsync(request, context.RequestAborted))).RequireRateLimiting("identity");
        var group = app.MapGroup("/api/v1").AddEndpointFilter<SessionFilter>();
        group.MapGet("/me", (HttpContext context) => Results.Ok((AccountInfo)context.Items["HifcAccount"]));
        group.MapPost("/sessions/logout", async (HttpContext context, IdentityStore identities) => {
            await identities.LogoutAsync(context.Request.Headers.Authorization.ToString().Substring(7), context.RequestAborted); return Results.NoContent();
        });
        group.MapPost("/checks/start", async (InventoryPage page, WorkflowStore workflow, HttpContext context) => Results.Ok(await workflow.StartAsync(Actor(context), page, context.RequestAborted)));
        group.MapPost("/checks/{scan}/capture-pages", async (string scan, CapturePage page, WorkflowStore workflow, HttpContext context) => {
            if (page.Context?.ScanId != scan) throw new InvalidOperationException("取数页不属于当前扫描。");
            await workflow.CaptureAsync(Actor(context), page, context.RequestAborted); return Results.NoContent();
        });
        group.MapPost("/checks/{scan}/complete", async (string scan, CompleteCheckRequest request, WorkflowStore workflow, HttpContext context) => {
            if (request.Context?.ScanId != scan) throw new InvalidOperationException("完成请求不属于当前扫描。");
            return Results.Ok(await workflow.CompleteAsync(Actor(context), request.Context, context.RequestAborted));
        });
        group.MapGet("/checks/{scan}", async (string scan, WorkflowStore workflow, HttpContext context) => Results.Ok(await workflow.GetResultAsync(Actor(context), scan, context.RequestAborted)));
        group.MapPost("/edit-plans", async (PreviewRequest request, WorkflowStore workflow, HttpContext context) => Results.Ok(await workflow.PreviewAsync(Actor(context), request, context.RequestAborted)));
        group.MapPost("/edit-plans/{plan}/authorize", async (string plan, AuthorizeRequest request, WorkflowStore workflow, HttpContext context) => Results.Ok(await workflow.AuthorizeAsync(Actor(context), plan, request, context.RequestAborted)));
        group.MapPost("/write-receipts", async (WriteReceipt receipt, WorkflowStore workflow, HttpContext context) => {
            await workflow.ReceiptAsync(Actor(context), receipt, context.RequestAborted); return Results.NoContent();
        });
    }
    private static Guid Actor(HttpContext context) => Guid.Parse(((AccountInfo)context.Items["HifcAccount"]).Id);
}
