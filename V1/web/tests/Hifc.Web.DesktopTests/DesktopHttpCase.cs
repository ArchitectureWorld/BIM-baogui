using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

internal static class DesktopHttpCase
{
    internal static void Run(Action<string, Action> test)
    {
        var configured = Environment.GetEnvironmentVariable("HIFC_TEST_EXPERIENCE_URL");
        if (string.IsNullOrEmpty(configured)) return;
        var server = new Uri(configured);
        if (!server.IsLoopback) throw new InvalidOperationException("Desktop transport fixtures require the isolated loopback test host.");
        test("Framework client and modern NET server preserve geometry through real HTTP checks and signed previews", () => {
            var bytes = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            using (var client = new WebApiClient(server)) {
                var ct = CancellationToken.None; var compatibility = client.CompatibilityAsync(ct).GetAwaiter().GetResult();
                var phone = "139" + (BitConverter.ToUInt32(bytes, 0) % 90000000 + 10000000);
                client.RegisterAsync(new Registration { Phone = phone, RealName = "跨运行时传输验收", Credential = Convert.ToBase64String(bytes) }, ct).GetAwaiter().GetResult();
                var rows = new[] { new ElementRecord { Id = 1, UniqueId = "desktop-wire-element", Entity = "IfcSlab", Name = "规划净用地", DeclaredSlot = "规划净用地", NativeAreaM2 = 0.1, ProjectedAreaM2 = 1.2345678901234567 } };
                var context = new RequestContext { RuleVersion = compatibility.RuleVersion, RequestId = Guid.NewGuid().ToString("N"), DocumentSessionId = Guid.NewGuid().ToString("N"), ScanId = Guid.NewGuid().ToString("N"), SnapshotHash = Canonical.InventoryHash(rows) };
                var capture = client.StartCheckAsync(new InventoryPage { Context = context, Stage = "报规", ModelKind = "总图", PageCount = 1, TotalElements = 1, Elements = rows, PageHash = context.SnapshotHash }, ct).GetAwaiter().GetResult();
                var values = capture.Targets.GroupBy(t => t.OwnerUniqueId + "|" + t.Origin + "|" + t.Source).Select(g => g.First()).Select(t => new ParameterValue { OwnerId = t.OwnerId, OwnerUniqueId = t.OwnerUniqueId, Origin = t.Origin, Source = t.Source }).ToArray();
                client.CaptureAsync(new CapturePage { Context = context, PageCount = 1, TotalValues = values.Length, Values = values, PageHash = Canonical.ValuesHash(values) }, ct).GetAwaiter().GetResult();
                var check = client.CompleteAsync(context, ct).GetAwaiter().GetResult();
                if (!check.Complete) throw new Exception("Cross-runtime model check did not finish.");
                var plan = client.PreviewAsync(new PreviewRequest { Context = context }, ct).GetAwaiter().GetResult();
                var permit = client.AuthorizeAsync(plan, Guid.NewGuid().ToString("N"), ct).GetAwaiter().GetResult();
                if (plan.Changes.Length == 0 || permit.PlanId != plan.Id) throw new Exception("Cross-runtime signed preview is incomplete.");
            }
        });
    }
}
