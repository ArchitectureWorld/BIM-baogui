using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace Hifc.Reference
{
    public sealed class ClassificationCandidate
    {
        public string Slot { get; set; }
        public string Evidence { get; set; }
        public bool CanPrepare { get; set; }
        public bool RequiresReview { get; set; }
    }
    internal sealed class CaseCorrespondence
    {
        public string Scope { get; set; }
        public string SourceIfc { get; set; }
        public int StepId { get; set; }
        public int RevitTag { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string Category { get; set; }
        public string Entity { get; set; }
        public string IfcEntity { get; set; }
        public string Slot { get; set; }
        public string Caution { get; set; }
    }
    internal static class RecognitionEvidence
    {
        public sealed class Catalog { public CaseCorrespondence[] Correspondences { get; set; } }
        private static readonly CaseCorrespondence[] Entries = Load();
        private static CaseCorrespondence[] Load()
        {
            using (var source = typeof(RecognitionEvidence).Assembly.GetManifestResourceStream("Hifc.Reference.RecognitionEvidence.json"))
            using (var buffer = new MemoryStream())
            {
                if (source == null) throw new InvalidOperationException("构件对应证据缺失。");
                source.CopyTo(buffer); var bytes = buffer.ToArray();
                using (var sha = SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != "5cafb3d308b521bc85ec08f88d987d01251b81a3b4b441dda0f95b1a12660840")
                        throw new InvalidOperationException("构件对应证据哈希不匹配。");
                return new JavaScriptSerializer().Deserialize<Catalog>(System.Text.Encoding.UTF8.GetString(bytes)).Correspondences;
            }
        }
        internal static CaseCorrespondence[] Match(ModelElementCheck row, string kind) => string.IsNullOrEmpty(row.UniqueId)
            ? new CaseCorrespondence[0] : Entries.Where(e => e.Scope == kind && e.UniqueId == row.UniqueId).ToArray();
    }
}
