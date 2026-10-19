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
using Autodesk.Revit.UI.Events;
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
                if (config["server"] == "https://192.168.6.45:8443/" && !RootTrusted()) {
                    log("等待用户完成 Windows 根证书安全确认；客户端将使用系统证书验证。");
                    while (!RootTrusted()) await Task.Delay(1000);
                }
                var ribbonVerified = await bridge.Run(application => application.GetRibbonPanels("HIFC-V1-Web").Any(panel => panel.GetItems().Any(item => item.Name == "HifcV1WebManager")));
                if (!ribbonVerified) throw new Exception("The installed independent HIFC-V1-Web Ribbon entry is missing.");
                log("PASS installed independent HIFC-V1-Web Ribbon entry");
                var update = await UpdateLifecycle.StartAsync(run, log);
                foreach (var path in models)
                {
                    log("Opening " + Path.GetFileName(path));
                    var access = await bridge.Run(application => { OpenCopy(application, path, run, log); return new NativeModelAccess(application); });
                    var session = new WebModelSession(access, new Uri(config["server"]), Path.Combine(run, "desktop-state", Path.GetFileNameWithoutExtension(path)));
                    var window = new WebModelWindow(session); window.Show(); System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                    session.Changed += () => log(session.Status);
                    try
                    {
                        await session.ConnectAsync("139" + RandomPhoneDigits(), "模型体验验收", true);
                        using (var peer = new WebApiClient(new Uri(config["server"]))) {
                            var login = await peer.LoginAsync(new LoginRequest { Phone = session.Account.Phone, RealName = session.Account.RealName }, System.Threading.CancellationToken.None);
                            if (login.Account.Id != session.Account.Id || login.Account.ExpiresAtUtc != session.Account.ExpiresAtUtc) throw new Exception("Independent phone/name login changed the person or original deadline.");
                        }
                        window.ShowAccountWindow(); var accountWindow = window.OwnedWindows.Cast<Window>().Single(); accountWindow.UpdateLayout(); Snapshot(accountWindow, Path.Combine(run, Path.GetFileNameWithoutExtension(path) + "-account.png")); accountWindow.Close();
                        window.UpdateLayout(); Snapshot(window, Path.Combine(run, Path.GetFileNameWithoutExtension(path) + "-connection.png"));
                        await session.ScanAsync();
                        if (session.Result?.Complete != true) throw new Exception("The actual model check did not complete.");
                        var target = session.Result.Elements.FirstOrDefault(e => e.Element.Entity == "IfcSlab");
                        var classificationAssigned = target != null && session.Result.AvailableSlots.Contains("规划净用地");
                        if (classificationAssigned) { session.Assign(target.Element.UniqueId, "规划净用地"); await session.ScanAsync(); }
                        window.ShowObjects(); Snapshot(window, Path.Combine(run, Path.GetFileNameWithoutExtension(path) + "-inventory.png"));
                        var field = session.Result.Fields.FirstOrDefault(f => f.Editable && f.Binding == "实例" && (f.RawType == "Text" || f.RawType == "Label") && (f.Property == "名称" || f.Property == "建筑名称" || f.Property == "楼层名称"))
                            ?? session.Result.Fields.FirstOrDefault(f => f.Editable && (f.RawType == "Text" || f.RawType == "Label"));
                        if (field == null) throw new Exception("No real editable text field is available.");
                        var value = "Web体验验收-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        window.SelectElement(field.ElementId); window.SelectField(field.Key); window.EditSelectedValue(value);
                        if (session.Workspace.Drafts.SingleOrDefault(draft => draft.FieldKey == field.Key)?.Value != value) throw new Exception("Editing the actual property control did not save the draft.");
                        window.UpdateLayout(); Snapshot(window, Path.Combine(run, Path.GetFileNameWithoutExtension(path) + "-fields.png"));
                        await session.PreviewAsync(); if (session.Plan == null || session.Plan.Changes.Length == 0) throw new Exception("Write preview contains no real changes.");
                        window.ShowWritePreview();
                        Snapshot(window, Path.Combine(run, Path.GetFileNameWithoutExtension(path) + "-preview.png"));
                        var changes = session.Plan.Changes.Length; await session.WriteAsync();
                        var receipt = session.Workspace.LastReceipt;
                        if (receipt?.TransactionCommitted != true || !receipt.ReadbackPassed || !receipt.RecheckCompleted) throw new Exception("Full native write/recheck failed: " + session.Status);
                        if (classificationAssigned && session.Result.Elements.Single(e => e.Element.UniqueId == target.Element.UniqueId).Element.DeclaredSlot != "规划净用地") throw new Exception("The assigned business classification did not survive native write and recheck.");
                        await session.SaveModelAsync(); if (!receipt.RvtSaved) throw new Exception("Saved model receipt is missing.");
                        saved.Add(Tuple.Create(path, new CaptureTarget { OwnerId = field.OwnerId, OwnerUniqueId = field.OwnerUniqueId, Source = field.Source, Origin = field.Binding == "类型" ? "type" : "instance" }, value));
                        results.Add(new { model = Path.GetFileName(path), changes, classificationAssigned, receipt.TransactionCommitted, receipt.ReadbackPassed, receipt.RecheckCompleted, receipt.RvtSaved });
                        log("PASS complete native workflow " + Path.GetFileName(path));
                    }
                    finally { window.Close(); }
                }
                foreach (var sample in saved)
                {
                    await bridge.Run(application => { OpenCopy(application, sample.Item1, run, log);
                        var actual = NativeInventory.ReadValues(application.ActiveUIDocument.Document, new[] { sample.Item2 }).Single(); if (actual.RawValue != sample.Item3) throw new Exception("Saved business value did not survive reopening."); return true; });
                    log("PASS reopen actual business value " + Path.GetFileName(sample.Item1));
                }
                update?.AssertDeferred();
                File.WriteAllText(Path.Combine(run, "full-results.json"), JsonWire.Serialize(new { passed = results.Count, failed = 0, results, reopened = saved.Count, ribbonVerified, propertyControlEditingVerified = true, independentPhoneNameLoginVerified = true, autoUpdateDownloadedFromZetta = update != null, activationDeferredUntilRevitExit = update != null, server = config["server"], scope = config["scope"], productionHttpsVerified = config["server"] == "https://192.168.6.45:8443/" }));
                await bridge.Run(application => { application.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit)); return true; });
            }
            catch (Exception ex) { log("FULL_FLOW_ERROR " + ex); File.WriteAllText(Path.Combine(run, "full-error.json"), JsonWire.Serialize(new { error = ex.ToString(), results })); }
        }));
    }
    private static void OpenCopy(UIApplication application, string path, string run, Action<string> log)
    {
        var allowedDirectory = Path.GetFullPath(Path.Combine(run, "models")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(allowedDirectory, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only this run's verified model copies can be opened.");
        EventHandler<DialogBoxShowingEventArgs> handleReferences = (sender, args) => {
            if (args is TaskDialogShowingEventArgs dialog && dialog.DialogId == "TaskDialog_Unresolved_References") {
                log("Continuing verified model copy without unavailable external references: " + Path.GetFileName(path));
                dialog.OverrideResult((int)TaskDialogResult.CommandLink2);
            }
        };
        application.DialogBoxShowing += handleReferences;
        try {
            var previous = application.ActiveUIDocument?.Document;
            application.OpenAndActivateDocument(path);
            if (previous != null && previous.PathName != path) previous.Close(false);
        }
        finally { application.DialogBoxShowing -= handleReferences; }
    }
    private static bool RootTrusted()
    {
        foreach (var location in new[] { System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser, System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine })
            using (var store = new System.Security.Cryptography.X509Certificates.X509Store(System.Security.Cryptography.X509Certificates.StoreName.Root, location)) {
                store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                if (store.Certificates.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint, "90DEB5EDA2139C762DE841D4C76C4BE973DE081F", false).Count > 0) return true;
            }
        return false;
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
