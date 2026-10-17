using System;
using System.IO;
using System.Linq;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

internal static class Program
{
    private static int Main()
    {
        int passed = 0, failed = 0;
        Action<string, Action> test = (name, action) => { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message); } };
        Action<bool, string> require = (condition, message) => { if (!condition) throw new Exception(message); };
        var root = Path.Combine(Environment.CurrentDirectory, ".local", "desktop-tests", Guid.NewGuid().ToString("N"));
        test("staged registration credentials survive a restart without appearing in plaintext files", () => {
            var store = new DeviceStore(Path.Combine(root, "devices")); var staged = store.Stage("13900000001"); var reopened = new DeviceStore(Path.Combine(root, "devices"));
            require(reopened.Candidates(staged.Phone).Single().Credential == staged.Credential, "A lost registration reply would lose the device credential.");
            require(Directory.GetFiles(Path.Combine(root, "devices")).All(p => !File.ReadAllText(p).Contains(staged.Credential)), "A credential was stored as plaintext.");
        });
        test("pending rebinding preserves the old device until the new credential is accepted", () => {
            var store = new DeviceStore(Path.Combine(root, "rebind")); var original = store.Stage("13900000002"); store.Accept(original); var pending = store.Stage("13900000002");
            require(pending.Credential != original.Credential && store.Candidates(original.Phone).Length == 2, "Pending rebind destroyed the current credential.");
            store.Accept(pending); var reopened = new DeviceStore(Path.Combine(root, "rebind"));
            require(reopened.ActivePhone == original.Phone && reopened.Candidates(original.Phone).Single().Credential == pending.Credential, "Accepted rebind did not persist atomically.");
        });
        test("workspace persists empty zero and false drafts together with the last visible check", () => {
            var key = new string('a', 64); var store = new WorkspaceStore(Path.Combine(root, "drafts"));
            store.Save(new SavedWorkspace { DocumentKey = key, Drafts = new[] { new EditDraft { FieldKey = "empty", Value = "" }, new EditDraft { FieldKey = "zero", Value = "0" }, new EditDraft { FieldKey = "false", Value = "false" } }, LastCheck = new CheckResult { Complete = true, ModelKind = "总图" } });
            var loaded = new WorkspaceStore(Path.Combine(root, "drafts")).Load(key);
            require(loaded.Drafts.Length == 3 && loaded.Drafts[0].Value == "" && loaded.Drafts[1].Value == "0" && loaded.Drafts[2].Value == "false" && loaded.LastCheck.ModelKind == "总图", "Offline drafts were collapsed or lost.");
        });
        test("workspace identifiers cannot escape the dedicated draft directory", () => {
            bool refused = false; try { new WorkspaceStore(Path.Combine(root, "drafts")).Load("../outside"); } catch (InvalidOperationException) { refused = true; }
            require(refused, "A draft path escaped its root.");
        });
        Console.WriteLine("RESULT " + passed + " passed; " + failed + " failed"); return failed == 0 ? 0 : 1;
    }
}
