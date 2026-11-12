using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Hifc.Reference;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Npgsql;

internal static class StatisticsEndpointCases
{
    internal static async Task Run(Func<string, Func<Task>, Task> test, Func<Task<AccountSession>> register, Uri address, NpgsqlDataSource source)
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        using var anonymous = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = address };
        await test("statistics HTTP endpoints require an authenticated account", async () => {
            using var response = await anonymous.PostAsync("api/v1/statistics/capture-plan", new StringContent("{}", Encoding.UTF8, "application/json"));
            Require(response.StatusCode == HttpStatusCode.Unauthorized, "Anonymous statistics capture was accepted.");
        });
        await test("statistics normal HTTP flow preserves scope and produces its Excel workbook", async () => {
            var account = await register();
            using var client = new WebApiClient(address); client.SetSession(account);
            var compatibility = await client.CompatibilityAsync(CancellationToken.None);
            var request = new StatisticsRequest { Kind = "building", RuleVersion = compatibility.RuleVersion, DocumentKey = "statistics-doc", DocumentTitle = "单体测试",
                SourcePath = "C:/not-a-server-model.rvt", Sources = new[] { new StatisticsSourceCapture {
                    Source = new SiteStatisticsSource { Key = "statistics-doc", Name = "单体测试", IsHost = true, Selected = true },
                    PlacementIdentity = "statistics-doc", Elements = new[] {
                        new ElementRecord { Id = 1, UniqueId = "level", Entity = "IfcBuildingStorey", SourceEntity = "IfcBuildingStorey", Name = "1F", Level = "1F", LevelUniqueId = "level", LevelElevationM = 0 },
                        new ElementRecord { Id = 2, UniqueId = "floor", Entity = "IfcSlab", SourceEntity = "IfcSlab", Name = "建筑区域", DeclaredSlot = "建筑区域", Level = "1F", LevelUniqueId = "level", LevelElevationM = 0, ProjectedAreaM2 = 100 }
                    }
                } } };
            var plan = await client.StatisticsCaptureAsync(request, CancellationToken.None);
            request.Sources[0].Values = plan.Sources.Single().Targets.DistinctBy(t => t.OwnerUniqueId + "|" + t.Source + "|" + t.Origin)
                .Select(t => new ParameterValue { OwnerId = t.OwnerId, OwnerUniqueId = t.OwnerUniqueId, Source = t.Source, Origin = t.Origin }).ToArray();
            var geometry = await client.StatisticsGeometryAsync(request, CancellationToken.None);
            request.CaptureHash = geometry.CaptureHash;
            request.Geometry = geometry.Groups.Select(g => new StatisticsGeometryResult { Key = g.Key, Verified = true,
                UnionArea = g.Members.Sum(m => m.GeometricArea ?? 0), ComponentAreaSum = g.Members.Sum(m => m.GeometricArea ?? 0), OverlapArea = 0 }).ToArray();
            var report = await client.StatisticsReportAsync(request, CancellationToken.None);
            Require(report.CaptureHash == request.CaptureHash && report.Building.Components.Length == 1, "Statistics HTTP report lost its source or component.");
            var workbook = await client.StatisticsWorkbookAsync(request, CancellationToken.None);
            var bytes = Convert.FromBase64String(workbook.ContentBase64);
            Require(workbook.CaptureHash == report.CaptureHash && bytes.Length > 100 && bytes[0] == 'P' && bytes[1] == 'K', "Workbook does not belong to the verified report.");
        });
        await test("statistics HTTP endpoint rechecks the account deadline", async () => {
            var account = await register();
            await using (var update = source.CreateCommand("UPDATE hifc_web.accounts SET expires_at=clock_timestamp() WHERE id=@id"))
            { update.Parameters.AddWithValue("id", Guid.Parse(account.Account.Id)); await update.ExecuteNonQueryAsync(); }
            using var expired = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = address };
            expired.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            using var response = await expired.PostAsync("api/v1/statistics/capture-plan", new StringContent("{}", Encoding.UTF8, "application/json"));
            Require(response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized, "Expired account can still use statistics.");
        });
    }
}
