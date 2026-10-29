using Hifc.Web.Contracts;

namespace Hifc.Web.Server;

public static class PlatformLaunchEndpoints
{
    public static void Map(RouteGroupBuilder authenticated)
    {
        authenticated.MapPost("/platform/launch-permit", async (PlatformLaunchRequest request, HttpContext context, WorkflowStore workflows, PlatformLaunchStore launches) => {
            var account = (AccountInfo)context.Items["HifcAccount"];
            var export = await workflows.GetExportRecordAsync(Guid.Parse(account.Id), request.ExportPlanId, context.RequestAborted);
            return Results.Ok(await launches.IssueAsync(request, account, export.Plan, export.Receipt, context.RequestAborted));
        });
    }
    // A signed, narrowly scoped launch permit is the capability here; no Web login token is persisted in a worker.
    public static void MapConsume(WebApplication app)
    {
        app.MapPost("/api/v1/platform/consume", async (PlatformConsumeRequest request, PlatformLaunchStore launches, HttpContext context) => Results.Ok(await launches.ConsumeAsync(request, context.RequestAborted))).RequireRateLimiting("identity");
    }
}
