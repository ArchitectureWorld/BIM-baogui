using System;
using System.Globalization;

namespace Hifc.Web.Contracts
{
    public sealed class ExportNotice
    {
        public int ElementId { get; set; }
        public string Property { get; set; }
        public string Code { get; set; }
        public string Detail { get; set; }
        public bool BlocksExport { get; set; }
    }

    public sealed class RequiredInputProof
    {
        public string schema { get; set; }
        public string rule_profile_id { get; set; }
        public string rule_revision { get; set; }
        public string rule_evidence_sha256 { get; set; }
        public bool complete { get; set; }
        public string model_kind { get; set; }
        public int checked_objects { get; set; }
        public int checked_fields { get; set; }
        public int blocking_count { get; set; }

        public static string Identity(RequiredInputProof proof)
        {
            if (proof == null) return Canonical.Hash("");
            return Canonical.Hash(string.Join("\n", proof.schema, proof.rule_profile_id, proof.rule_revision,
                proof.rule_evidence_sha256, proof.complete ? "1" : "0", proof.model_kind,
                proof.checked_objects.ToString(CultureInfo.InvariantCulture),
                proof.checked_fields.ToString(CultureInfo.InvariantCulture),
                proof.blocking_count.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
