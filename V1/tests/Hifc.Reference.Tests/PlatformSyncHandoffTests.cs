using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Hifc.Reference;

internal static class PlatformSyncHandoffTests
{
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static void Assert(bool ok, string why) { if (!ok) throw new Exception(why); }
    static object Call(string name, params object[] args)
    {
        var type = typeof(ReferenceCatalog).Assembly.GetType("Hifc.Reference.PlatformSyncHandoff");
        if (type == null) throw new Exception("Platform sync success boundary is missing");
        try { return type.GetMethod(name).Invoke(null, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe export was accepted"); }
    static string Hash(string file) { using (var sha = SHA256.Create()) using (var input = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
    sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "hifc-sync-test-" + Guid.NewGuid().ToString("N"));
        internal string Ifc, Receipt, Document, Id = Guid.NewGuid().ToString("D");
        internal Dictionary<string, object> Data;
        internal Fixture()
        {
            Directory.CreateDirectory(Root); Ifc = Path.Combine(Root, "OUT_学校_报规_总图_20261023_V1.ifc"); Receipt = Path.Combine(Root, "export.receipt.json"); Document = Path.Combine(Root, "school.rvt");
            File.WriteAllText(Ifc, "ISO-10303-21;\nDATA;\nENDSEC;\nEND-ISO-10303-21;", new UTF8Encoding(false));
            Data = new Dictionary<string, object> { { "schema_version", "hifc.native-export-receipt/1.5" }, { "export_id", Id }, { "ifc_path", Ifc }, { "ifc_sha256", Hash(Ifc) }, { "source_snapshot_before", "snapshot" }, { "source_snapshot_after", "snapshot" }, { "model_transaction", "RolledBack" }, { "export_scope", "CURRENT_PROJECT" }, { "doc_export_calls", 1 }, { "ifc_postprocessed", false }, { "original_export_server_restored", true }, { "temporary_type_schedules_rolled_back", true } };
            Save();
        }
        internal void Save() { File.WriteAllText(Receipt, Json.Serialize(Data), new UTF8Encoding(false)); }
        internal object Register() { return Call("RegisterCompleted", Ifc, Receipt, Id, Document, "project-unique-id"); }
        internal string Freeze(object completed, string document = null, string unique = "project-unique-id") { return (string)Call("Freeze", completed, Path.Combine(Root, "frozen"), document ?? Document, unique); }
        public void Dispose() { Directory.Delete(Root, true); }
    }
    internal static void Run(Action<string, Action> check)
    {
        check("sync_only_freezes_a_full_success_receipt_and_publishes_matching_hashes", () => {
            using (var f = new Fixture()) {
                var path = f.Freeze(f.Register()); var manifest = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)); var folder = Path.GetDirectoryName(path);
                Assert((string)manifest["schema"] == "hifc.platform-sync-handoff/1" && (string)manifest["export_id"] == f.Id, "Invocation identity lost");
                Assert((string)manifest["ifc_sha256"] == Hash(Path.Combine(folder, "model.ifc")), "Frozen IFC hash differs");
                Assert((string)manifest["receipt_sha256"] == Hash(Path.Combine(folder, "native.receipt.json")), "Frozen receipt hash differs");
                Assert((string)manifest["original_name"] == Path.GetFileName(f.Ifc), "Original export name lost");
            }
        });
        check("sync_receipt_rejects_each_incomplete_post_export_condition", () => {
            foreach (var field in new[] { "source_snapshot_before", "source_snapshot_after", "model_transaction", "export_scope", "doc_export_calls", "ifc_postprocessed", "original_export_server_restored", "temporary_type_schedules_rolled_back", "export_id", "ifc_sha256", "ifc_path" })
                using (var f = new Fixture()) { f.Data.Remove(field); f.Save(); Reject(() => f.Register()); }
        });
        check("sync_receipt_rejects_failed_rollback_stale_invocation_and_local_scope", () => {
            foreach (var pair in new[] { new KeyValuePair<string, object>("source_snapshot_after", "different"), new KeyValuePair<string, object>("model_transaction", "Committed"), new KeyValuePair<string, object>("export_scope", "EXPLICIT_LOCAL_ACCEPTANCE"), new KeyValuePair<string, object>("export_id", Guid.NewGuid().ToString("D")), new KeyValuePair<string, object>("doc_export_calls", 2), new KeyValuePair<string, object>("ifc_postprocessed", true), new KeyValuePair<string, object>("original_export_server_restored", false), new KeyValuePair<string, object>("temporary_type_schedules_rolled_back", false) })
                using (var f = new Fixture()) { f.Data[pair.Key] = pair.Value; f.Save(); Reject(() => f.Register()); }
        });
        check("sync_file_presence_without_receipt_never_registers", () => {
            using (var f = new Fixture()) { File.Delete(f.Receipt); Reject(() => f.Register()); }
        });
        check("sync_modified_ifc_and_receipt_are_rechecked_before_freeze", () => {
            using (var f = new Fixture()) { var completed = f.Register(); File.AppendAllText(f.Ifc, "changed"); Reject(() => f.Freeze(completed)); }
            using (var f = new Fixture()) { var completed = f.Register(); File.AppendAllText(f.Receipt, " "); Reject(() => f.Freeze(completed)); }
        });
        check("sync_saved_as_copied_or_other_document_cannot_use_completed_export", () => {
            using (var f = new Fixture()) { var completed = f.Register(); Reject(() => f.Freeze(completed, Path.Combine(f.Root, "copied.rvt"))); Reject(() => f.Freeze(completed, unique: "other-id")); }
        });
        check("sync_unsaved_document_cannot_register_but_export_is_separate", () => {
            using (var f = new Fixture()) Reject(() => Call("RegisterCompleted", f.Ifc, f.Receipt, f.Id, "", "project-unique-id"));
        });
        check("sync_repeat_freeze_uses_distinct_immutable_directories", () => {
            using (var f = new Fixture()) { var completed = f.Register(); var first = f.Freeze(completed); var second = f.Freeze(completed); Assert(first != second && File.Exists(first) && File.Exists(second), "Frozen export overwritten"); }
        });
    }
}
