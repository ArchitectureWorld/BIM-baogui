using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

internal static class FullFlow
{
    internal static void Start(UIApplication ui, string run)
    {
        var bridge = new Bridge(); var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(new Action(async () => {
            var results = new List<object>(); var saved = new List<Tuple<string, CaptureTarget, string>>();
            var config = JsonWire.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(run, "full-flow.json")));
            var models = JsonWire.Deserialize<string[]>(File.ReadAllText(Path.Combine(run, "models.json")));
            Action<string> log = value => File.AppendAllText(Path.Combine(run, "full-progress.log"), DateTime.UtcNow.ToString("O") + " " + value + Environment.NewLine);
            try
            {
                foreach (var path in models)
                {
                    log("Opening " + Path.GetFileName(path));
                    var access = await bridge.Run(application => { var previous = application.ActiveUIDocument?.Document; application.OpenAndActivateDocument(path); if (previous != null && previous.PathName != path) previous.Close(false); return new NativeModelAccess(application); });
                    var session = new WebModelSession(access, new Uri(config["server"]), Path.Combine(run, "desktop-state", Path.GetFileNameWithoutExtension(path)));
                    var window = new WebModelWindow(session); window.Show(); System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                    session.Changed += () => log(session.Status);
                    try
                    {
                        await session.ConnectAsync("139" + RandomPhoneDigits(), "模型体验验收", true); await session.ScanAsync();
                        if (session.Result?.Complete != true) throw new Exception("The actual model check did not complete.");
                        var target = session.Result.Elements.FirstOrDefault(e => e.Element.Entity == "IfcSlab");
                        if (target != null && session.Result.AvailableSlots.Contains("规划净用地")) { session.Assign(target.Element.UniqueId, "规划净用地"); await session.ScanAsync(); }
                        var field = session.Result.Fields.FirstOrDefault(f => f.Editable && f.Binding == "实例" && (f.RawType == "Text" || f.RawType == "Label") && (f.Property == "名称" || f.Property == "建筑名称" || f.Property == "楼层名称"))
                            ?? session.Result.Fields.FirstOrDefault(f => f.Editable && (f.RawType == "Text" || f.RawType == "Label"));
                        if (field == null) throw new Exception("No real editable text field is available.");
                        var value = "Web体验验收-" + Guid.NewGuid().ToString("N").Substring(0, 8); session.SetDraft(field, value);
                        await session.PreviewAsync(); if (session.Plan == null || session.Plan.Changes.Length == 0) throw new Exception("Write preview contains no real changes.");
                        var tab = ((DockPanel)window.Content).Children.OfType<TabControl>().Single(); tab.SelectedIndex = 3; window.UpdateLayout();
                        Snapshot(window, Path.Combine(run, Path.GetFileNameWithoutExtension(path) + "-preview.png"));
                        var changes = session.Plan.Changes.Length; await session.WriteAsync();
                        var receipt = session.Workspace.LastReceipt;
                        if (receipt?.TransactionCommitted != true || !receipt.ReadbackPassed || !receipt.RecheckCompleted) throw new Exception("Full native write/recheck failed: " + session.Status);
                        await session.SaveModelAsync(); if (!receipt.RvtSaved) throw new Exception("Saved model receipt is missing.");
                        saved.Add(Tuple.Create(path, new CaptureTarget { OwnerId = field.OwnerId, OwnerUniqueId = field.OwnerUniqueId, Source = field.Source, Origin = field.Binding == "类型" ? "type" : "instance" }, value));
                        results.Add(new { model = Path.GetFileName(path), changes, receipt.TransactionCommitted, receipt.ReadbackPassed, receipt.RecheckCompleted, receipt.RvtSaved });
                        log("PASS complete native workflow " + Path.GetFileName(path));
                    }
                    finally { window.Close(); }
                }
                foreach (var sample in saved)
                {
                    await bridge.Run(application => { var previous = application.ActiveUIDocument?.Document; application.OpenAndActivateDocument(sample.Item1); if (previous != null && previous.PathName != sample.Item1) previous.Close(false);
                        var actual = NativeInventory.ReadValues(application.ActiveUIDocument.Document, new[] { sample.Item2 }).Single(); if (actual.RawValue != sample.Item3) throw new Exception("Saved business value did not survive reopening."); return true; });
                    log("PASS reopen actual business value " + Path.GetFileName(sample.Item1));
                }
                File.WriteAllText(Path.Combine(run, "full-results.json"), JsonWire.Serialize(new { passed = results.Count, failed = 0, results, reopened = saved.Count, scope = "Real Revit 2020 model copies and actual HTTP client against the dedicated PostgreSQL test database; production HTTPS deployment remains a separate check" }));
                await bridge.Run(application => { application.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit)); return true; });
            }
            catch (Exception ex) { log("FULL_FLOW_ERROR " + ex); File.WriteAllText(Path.Combine(run, "full-error.json"), JsonWire.Serialize(new { error = ex.ToString(), results })); }
        }));
    }
    private static string RandomPhoneDigits() { var bytes = new byte[4]; using (var random = System.Security.Cryptography.RandomNumberGenerator.Create()) random.GetBytes(bytes); return (BitConverter.ToUInt32(bytes, 0) % 90000000 + 10000000).ToString(System.Globalization.CultureInfo.InvariantCulture); }
    private static void Snapshot(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
    }
    private sealed class Bridge : IExternalEventHandler
    {
        private readonly ExternalEvent external; private readonly ConcurrentQueue<Action<UIApplication>> jobs = new ConcurrentQueue<Action<UIApplication>>();
        internal Bridge() { external = ExternalEvent.Create(this); }
        internal Task<T> Run<T>(Func<UIApplication, T> action) { var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously); jobs.Enqueue(ui => { try { result.SetResult(action(ui)); } catch (Exception ex) { result.SetException(ex); } }); external.Raise(); return result.Task; }
        public void Execute(UIApplication ui) { while (jobs.TryDequeue(out var job)) job(ui); }
        public string GetName() => "HIFC Web full model-copy experience validation";
    }
}
