using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Hifc.Reference
{
    /// <summary>A receipt accepted only from the caller that completed this export invocation.</summary>
    public sealed class CompletedPlatformExport
    {
        public string ExportId { get; private set; }
        public string IfcPath { get; private set; }
        public string ReceiptPath { get; private set; }
        public string DocumentPath { get; private set; }
        public string ProjectUniqueId { get; private set; }
        public string IfcHash { get; private set; }
        public string ReceiptHash { get; private set; }
        internal CompletedPlatformExport(string id, string ifc, string receipt, string document, string unique, string ifcHash, string receiptHash)
        { ExportId = id; IfcPath = ifc; ReceiptPath = receipt; DocumentPath = document; ProjectUniqueId = unique; IfcHash = ifcHash; ReceiptHash = receiptHash; }
    }

    public static class PlatformSyncHandoff
    {
        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }; }
        static string Absolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidOperationException("平台同步需要已保存模型和绝对文件路径。");
            return Path.GetFullPath(path);
        }
        public static bool SameDocument(string expectedPath, string expectedUniqueId, string currentPath, string currentUniqueId)
        {
            if (string.IsNullOrWhiteSpace(expectedPath) || string.IsNullOrWhiteSpace(currentPath) || string.IsNullOrWhiteSpace(expectedUniqueId)) return false;
            return string.Equals(Absolute(expectedPath), Absolute(currentPath), StringComparison.OrdinalIgnoreCase)
                && string.Equals(expectedUniqueId, currentUniqueId, StringComparison.Ordinal);
        }
        static void NoLinks(string path)
        {
            for (var part = path; !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
                if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("平台交接不接受链接或重定向目录。");
        }
        public static string Sha256(string path)
        {
            using (var hash = SHA256.Create()) using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static object Value(Dictionary<string, object> data, string key) { data.TryGetValue(key, out var value); return value; }
        static string Text(Dictionary<string, object> data, string key) { return Value(data, key) as string; }
        static bool Is(Dictionary<string, object> data, string key, bool expected) { return Value(data, key) is bool actual && actual == expected; }
        static void Require(bool value) { if (!value) throw new InvalidOperationException("导出回执未满足完整成功条件，未创建平台同步任务。"); }

        public static CompletedPlatformExport RegisterCompleted(string ifcPath, string receiptPath, string expectedExportId, string documentPath, string projectUniqueId)
        {
            try
            {
                var ifc = Absolute(ifcPath); var receipt = Absolute(receiptPath); var document = Absolute(documentPath);
                Require(Guid.TryParse(expectedExportId, out var id) && !string.IsNullOrWhiteSpace(projectUniqueId));
                NoLinks(ifc); NoLinks(receipt);
                Require(File.Exists(ifc) && new FileInfo(ifc).Length > 0 && File.Exists(receipt));
                var receiptHash = Sha256(receipt);
                var data = Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(receipt, Encoding.UTF8));
                Require(data != null);
                Require(new[] { "hifc.native-export-receipt/1.5", "hifc.native-export-receipt/1.6" }.Contains(Text(data, "schema_version")));
                Require(Guid.TryParse(Text(data, "export_id"), out var actualId) && actualId == id);
                Require(string.Equals(Absolute(Text(data, "ifc_path")), ifc, StringComparison.OrdinalIgnoreCase));
                Require(!string.IsNullOrWhiteSpace(Text(data, "source_snapshot_before")) && Text(data, "source_snapshot_before") == Text(data, "source_snapshot_after"));
                Require(Text(data, "model_transaction") == "RolledBack" && Text(data, "export_scope") == "CURRENT_PROJECT");
                Require(Value(data, "doc_export_calls") is int calls && calls == 1);
                Require(Is(data, "ifc_postprocessed", false) && Is(data, "original_export_server_restored", true) && Is(data, "temporary_type_schedules_rolled_back", true));
                var hash = Sha256(ifc);
                Require(string.Equals(Text(data, "ifc_sha256"), hash, StringComparison.OrdinalIgnoreCase));
                Require(Sha256(receipt) == receiptHash);
                return new CompletedPlatformExport(id.ToString("D"), ifc, receipt, document, projectUniqueId, hash, receiptHash);
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { throw new InvalidOperationException("成功导出文件或回执无法核验，未创建平台同步任务。", error); }
        }

        public static string Freeze(CompletedPlatformExport completed, string spoolRoot, string currentDocumentPath, string currentProjectUniqueId)
        {
            if (completed == null) throw new InvalidOperationException("请先通过本插件成功导出当前项目。");
            if (!SameDocument(completed.DocumentPath, completed.ProjectUniqueId, currentDocumentPath, currentProjectUniqueId))
                throw new InvalidOperationException("当前项目与导出时的项目不一致。另存为或复制模型后请重新导出并确认平台绑定。");
            var refreshed = RegisterCompleted(completed.IfcPath, completed.ReceiptPath, completed.ExportId, completed.DocumentPath, completed.ProjectUniqueId);
            Require(refreshed.IfcHash == completed.IfcHash && refreshed.ReceiptHash == completed.ReceiptHash);
            var root = Absolute(spoolRoot); NoLinks(root); Directory.CreateDirectory(root);
            var directory = Path.Combine(root, completed.ExportId + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            // Only this newly created folder is ever cleaned. A ready manifest is published last.
            try
            {
                var frozenIfc = Path.Combine(directory, "model.ifc"); var frozenReceipt = Path.Combine(directory, "native.receipt.json");
                File.Copy(completed.IfcPath, frozenIfc, false); File.Copy(completed.ReceiptPath, frozenReceipt, false);
                NoLinks(frozenIfc); NoLinks(frozenReceipt);
                Require(Sha256(frozenIfc) == completed.IfcHash && Sha256(frozenReceipt) == completed.ReceiptHash);
                Require(Sha256(completed.IfcPath) == completed.IfcHash && Sha256(completed.ReceiptPath) == completed.ReceiptHash);
                var manifest = new {
                    schema = "hifc.platform-sync-handoff/1", export_id = completed.ExportId, created_utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    document_path = completed.DocumentPath, project_unique_id = completed.ProjectUniqueId, original_name = Path.GetFileName(completed.IfcPath),
                    ifc_file = "model.ifc", receipt_file = "native.receipt.json", ifc_sha256 = completed.IfcHash, receipt_sha256 = completed.ReceiptHash
                };
                var ready = Path.Combine(directory, "handoff.json"); var temporary = ready + ".tmp";
                var bytes = new UTF8Encoding(false).GetBytes(Serializer().Serialize(manifest));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                File.Move(temporary, ready);
                return ready;
            }
            catch
            {
                // Keep forensic files if publication may have succeeded; never remove a ready handoff.
                if (!File.Exists(Path.Combine(directory, "handoff.json")))
                    foreach (var name in new[] { "model.ifc", "native.receipt.json", "handoff.json.tmp" })
                        try { File.Delete(Path.Combine(directory, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }

    /// <summary>Noncritical sync registration must never change the native export result.</summary>
    public sealed class PlatformSyncExportSession
    {
        public CompletedPlatformExport Completed { get; private set; }
        public string Status { get; private set; } = "请先成功导出当前项目 IFC。";
        public void BeginExport() { Completed = null; Status = "本轮导出尚未完成，不能用于平台同步。"; }
        public void Register(string ifc, string receipt, string exportId, string document, string uniqueId)
        {
            Completed = null;
            try { Completed = PlatformSyncHandoff.RegisterCompleted(ifc, receipt, exportId, document, uniqueId); Status = "IFC 导出成功；可创建平台同步预览。"; }
            catch (Exception) { Status = "IFC 导出成功；平台交接登记失败，尚未创建同步任务。请重新导出已保存的模型后重试。"; }
        }
    }
}
