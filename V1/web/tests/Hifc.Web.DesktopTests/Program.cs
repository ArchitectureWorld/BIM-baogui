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
        test("remembered phone and name survive restart encrypted without a computer binding", () => {
            var folder = Path.Combine(root, "people"); var store = new ProfileStore(folder); store.Save("+86 13900000001", "内网人员验收");
            var current = new ProfileStore(folder).Current;
            require(current.Phone == "13900000001" && current.RealName == "内网人员验收", "Remembered person was lost or normalized incorrectly.");
            require(Directory.GetFiles(folder).All(path => !File.ReadAllText(path).Contains(current.Phone) && !File.ReadAllText(path).Contains(current.RealName)), "Person details were written in plaintext.");
        });
        test("invalid person input preserves previously remembered details", () => {
            var store = new ProfileStore(Path.Combine(root, "profile-validation")); store.Save("13900000002", "原人员验收");
            bool rejected = false; try { store.Save("13900000002", "一"); } catch (InvalidOperationException) { rejected = true; }
            require(rejected && store.Current.RealName == "原人员验收", "Invalid input destroyed remembered details.");
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
        CanonicalCases.Run(test);
        UpdateInstallCases.Run(test);
        DesktopHttpCase.Run(test);
        Console.WriteLine("RESULT " + passed + " passed; " + failed + " failed"); return failed == 0 ? 0 : 1;
    }
}
