using Hifc.Web.Contracts;
using Hifc.Web.RuleEngine;

namespace Hifc.Web.Server;

public static class StatisticsEndpoints
{
    public static void Map(RouteGroupBuilder authenticated)
    {
        var group = authenticated.MapGroup("/statistics").AddEndpointFilter(async (context, next) => {
            var account = context.HttpContext.Items["HifcAccount"] as AccountInfo;
            if (account?.IsActive != true)
                return Results.Json(new ApiError { Code = "ACCOUNT_EXPIRED", Message = "账号已到期，请联系管理员延期。" }, statusCode: 403);
            return await next(context);
        });
        group.MapPost("/capture-plan", (StatisticsRequest request, RuleAdapter rules) => Results.Ok(rules.PlanStatisticsCapture(request)));
        group.MapPost("/geometry-plan", (StatisticsRequest request, RuleAdapter rules) => Results.Ok(rules.PlanStatisticsGeometry(request)));
        group.MapPost("/report", (StatisticsRequest request, RuleAdapter rules) => Results.Ok(rules.EvaluateStatistics(request)));
        group.MapPost("/workbook", (StatisticsRequest request, RuleAdapter rules) => Results.Ok(rules.ExportStatisticsWorkbook(request)));
    }
}
