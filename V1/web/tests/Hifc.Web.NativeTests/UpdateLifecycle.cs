using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hifc.Web.Client;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;
using Hifc.Web.Update;

internal sealed class UpdateLifecycle
{
    private readonly string root, manifest;
    private readonly Dictionary<string, string> protectedFiles = new Dictionary<string, string>();
    private readonly string oldRelease;
    private UpdateLifecycle(string root, string manifest) { this.root = root; this.manifest = manifest; oldRelease = UpdateInstaller.Read<ClientPackageInfo>(Path.Combine(root, "package.json")).ReleaseId; }
    internal static async Task<UpdateLifecycle> StartAsync(string run, Action<string> log)
    {
        var path = Path.Combine(run, "update-lifecycle.json"); if (!File.Exists(path)) return null;
        var config = JsonWire.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        var lifecycle = new UpdateLifecycle(config["root"], config["manifest"]);
        var state = Path.Combine(config["localAppData"], "HIFC", "0.5.0-Web");
        var people = new ProfileStore(Path.Combine(state, "accounts")); people.Save("13900009998", "更新保留资料验收");
        var workspace = new WorkspaceStore(Path.Combine(state, "drafts")); workspace.Save(new SavedWorkspace { DocumentKey = Canonical.Hash("updater-preserve-drafts"), Drafts = new[] { new EditDraft { FieldKey = "synthetic", Value = "更新后保留草稿" } } });
        var files = Directory.GetFiles(lifecycle.root).Concat(Directory.GetFiles(Path.Combine(state, "accounts"))).Concat(Directory.GetFiles(Path.Combine(state, "drafts"))).Concat(new[] { lifecycle.manifest, config["localManifest"] });
        foreach (var file in files) lifecycle.protectedFiles[file] = UpdateInstaller.HashFile(file);
        var pending = await UpdateClient.StageAsync(lifecycle.root, lifecycle.root, new Uri(UpdateTrust.Server), CancellationToken.None);
        if (pending == null) throw new Exception("An older released client was not offered the zetta update.");
        var offer = UpdateInstaller.Read<PendingUpdate>(pending).Manifest;
        int pid; long ticks; using (var current = Process.GetCurrentProcess()) { pid = current.Id; ticks = current.StartTime.ToUniversalTime().Ticks; }
        var helperDirectory = Path.GetDirectoryName(typeof(UpdateInstaller).Assembly.Location);
        using (var worker = UpdateClient.StartWorker(helperDirectory, pending, pid, ticks, config["localAppData"], config["appData"])) {
            File.WriteAllText(Path.Combine(run, "updater-worker.json"), JsonWire.Serialize(new { pid = worker.Id, config, pending, offer, oldRelease = lifecycle.oldRelease, protectedFiles = lifecycle.protectedFiles }));
        }
        lifecycle.AssertDeferred(); log("PASS signed update downloaded directly from zetta; worker waits for this Revit process"); return lifecycle;
    }
    internal void AssertDeferred()
    {
        if (UpdateInstaller.Read<ClientPackageInfo>(Path.Combine(root, "package.json")).ReleaseId != oldRelease) throw new Exception("Update activated while Revit was still running.");
        foreach (var pair in protectedFiles) if (UpdateInstaller.HashFile(pair.Key) != pair.Value) throw new Exception("Update changed active payload or remembered data while Revit was running.");
    }
}
