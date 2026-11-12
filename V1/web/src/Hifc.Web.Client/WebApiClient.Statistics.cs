using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Contracts;

namespace Hifc.Web.Client
{
    public sealed partial class WebApiClient
    {
        public Task<StatisticsCapturePlan> StatisticsCaptureAsync(StatisticsRequest request, CancellationToken cancellation) =>
            Request<StatisticsCapturePlan>(HttpMethod.Post, "statistics/capture-plan", request, cancellation);

        public Task<StatisticsGeometryPlan> StatisticsGeometryAsync(StatisticsRequest request, CancellationToken cancellation) =>
            Request<StatisticsGeometryPlan>(HttpMethod.Post, "statistics/geometry-plan", request, cancellation);

        public Task<StatisticsResponse> StatisticsReportAsync(StatisticsRequest request, CancellationToken cancellation) =>
            Request<StatisticsResponse>(HttpMethod.Post, "statistics/report", request, cancellation);

        public Task<StatisticsWorkbook> StatisticsWorkbookAsync(StatisticsRequest request, CancellationToken cancellation) =>
            Request<StatisticsWorkbook>(HttpMethod.Post, "statistics/workbook", request, cancellation);
    }
}
