using System;
using System.Threading.Tasks;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public sealed partial class WebModelSession
    {
        internal async Task<string> StatisticsRuleVersionAsync()
        {
            if (compatibility == null || identity == null)
            {
                var person = profiles.Current;
                if (person == null) throw new InvalidOperationException("请先在 Web 模型管理窗口填写手机号和姓名并连接。");
                await ConnectAsync(person.Phone, person.RealName, false);
            }
            await EnsureIdentity();
            return compatibility.RuleVersion;
        }
        internal async Task<StatisticsCapturePlan> StatisticsCaptureAsync(StatisticsRequest request)
        {
            await EnsureIdentity();
            return await api.StatisticsCaptureAsync(request, cancellation.Token);
        }
        internal async Task<StatisticsGeometryPlan> StatisticsGeometryAsync(StatisticsRequest request)
        {
            await EnsureIdentity();
            return await api.StatisticsGeometryAsync(request, cancellation.Token);
        }
        internal async Task<StatisticsResponse> StatisticsReportAsync(StatisticsRequest request)
        {
            await EnsureIdentity();
            return await api.StatisticsReportAsync(request, cancellation.Token);
        }
        internal async Task<StatisticsWorkbook> StatisticsWorkbookAsync(StatisticsRequest request)
        {
            await EnsureIdentity();
            return await api.StatisticsWorkbookAsync(request, cancellation.Token);
        }
        public Task OpenStatisticsAsync(string kind) => model.OpenStatisticsAsync(this, kind);
        public async Task<byte[]> BlankWorkbookAsync()
        {
            await StatisticsRuleVersionAsync();
            await ScanAsync();
            return await api.WorkbookTemplateAsync(Result.Context, cancellation.Token);
        }
        internal WebModelSession NewStatisticsSession(Autodesk.Revit.UI.UIApplication application) =>
            new WebModelSession(new NativeModelAccess(application, true), server, stateRoot);
    }
}
