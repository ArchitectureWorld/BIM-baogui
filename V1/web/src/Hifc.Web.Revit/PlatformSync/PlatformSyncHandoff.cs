using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit.PlatformSync
{
    public sealed class CompletedPlatformExport
    {
        public string ExportId { get; internal set; }
        public string IfcPath { get; internal set; }
        public string ReceiptPath { get; internal set; }
        public string DocumentPath { get; internal set; }
        public string ProjectUniqueId { get; internal set; }
        public string IfcHash { get; internal set; }
        public string ReceiptHash { get; internal set; }
        internal ExportReceipt AcceptedReceipt;
    }
    public static class PlatformSyncHandoff
    {
        public static bool SameDocument(string left, string leftId, string right, string rightId) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && !string.IsNullOrWhiteSpace(leftId) && Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase) && leftId == rightId;
        public static string Sha256(string path) { NoLinks(path); using (var input = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
        internal static void NoLinks(string path) { for (var part = Path.GetFullPath(path); !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part)) if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Web 平台资料不接受链接或重定向目录。"); }
        private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("请先完成并登记当前文档的原生 IFC 导出；文件存在不能代替成功回执。"); }
        public static CompletedPlatformExport FromAcceptedSource(PlatformSyncSource source)
        {
            var r = source?.Receipt; Require(r != null && r.Exported && r.SourceRestored && r.DocumentSessionId == source.DocumentSessionId && !string.IsNullOrWhiteSpace(source.DocumentPath) && Path.IsPathRooted(source.DocumentPath) && !string.IsNullOrWhiteSpace(source.ProjectUniqueId));
            Require(!string.IsNullOrWhiteSpace(r.SnapshotBefore) && r.SnapshotBefore == r.SnapshotAfter && Guid.TryParse(r.PlanId, out _));
            Require(Path.IsPathRooted(r.IfcPath) && Path.IsPathRooted(r.ReceiptPath)); NoLinks(r.IfcPath); NoLinks(r.ReceiptPath);
            Require(File.Exists(r.IfcPath) && File.Exists(r.ReceiptPath) && new FileInfo(r.ReceiptPath).Length <= 65536);
            var disk = JsonWire.Deserialize<ExportReceipt>(File.ReadAllText(r.ReceiptPath));
            Require(disk != null && PlatformLaunchTrust.ReceiptIdentity(disk) == PlatformLaunchTrust.ReceiptIdentity(r) && disk.SourceRestored && disk.Exported && Sha256(r.IfcPath) == r.IfcHash);
            return new CompletedPlatformExport { ExportId = r.PlanId, IfcPath = Path.GetFullPath(r.IfcPath), ReceiptPath = Path.GetFullPath(r.ReceiptPath), DocumentPath = Path.GetFullPath(source.DocumentPath), ProjectUniqueId = source.ProjectUniqueId, IfcHash = r.IfcHash, ReceiptHash = Sha256(r.ReceiptPath), AcceptedReceipt = r };
        }
        public static string Freeze(CompletedPlatformExport completed, string spoolRoot, string document, string unique)
        {
            Require(completed != null && SameDocument(completed.DocumentPath, completed.ProjectUniqueId, document, unique));
            var current = FromAcceptedSource(new PlatformSyncSource { DocumentPath = document, ProjectUniqueId = unique, DocumentSessionId = completed.AcceptedReceipt.DocumentSessionId, Receipt = completed.AcceptedReceipt });
            Require(current.IfcHash == completed.IfcHash && current.ReceiptHash == completed.ReceiptHash); NoLinks(spoolRoot); Directory.CreateDirectory(spoolRoot);
            var directory = Path.Combine(spoolRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var ifc = Path.Combine(directory, "model.ifc"); var receipt = Path.Combine(directory, "native.receipt.json");
            File.Copy(current.IfcPath, ifc); File.Copy(current.ReceiptPath, receipt);
            Require(Sha256(ifc) == current.IfcHash && Sha256(receipt) == current.ReceiptHash && Sha256(current.IfcPath) == current.IfcHash && Sha256(current.ReceiptPath) == current.ReceiptHash);
            var manifest = new { schema = "hifc.platform-sync-handoff/1", export_id = current.ExportId, created_utc = DateTime.UtcNow.ToString("O"), document_path = current.DocumentPath, project_unique_id = current.ProjectUniqueId, original_name = Path.GetFileName(current.IfcPath), ifc_file = "model.ifc", receipt_file = "native.receipt.json", ifc_sha256 = current.IfcHash, receipt_sha256 = current.ReceiptHash,
                web = new { schema = "hifc.web-platform-export/1", document_session_id = current.AcceptedReceipt.DocumentSessionId, receipt_identity_hash = PlatformLaunchTrust.ReceiptIdentity(current.AcceptedReceipt) } };
            var temporary = Path.Combine(directory, "handoff.pending"); File.WriteAllText(temporary, JsonWire.Serialize(manifest), new UTF8Encoding(false)); var ready = Path.Combine(directory, "handoff.json"); File.Move(temporary, ready); return ready;
        }
    }
}
