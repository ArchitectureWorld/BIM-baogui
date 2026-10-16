using System;

namespace Hifc.Web.Contracts
{
    public sealed class EditDraft
    {
        public string FieldKey { get; set; }
        public string OriginalValue { get; set; }
        public string OriginalSignature { get; set; }
        public string Value { get; set; }
    }
    public sealed class DraftConflict
    {
        public EditDraft Draft { get; set; }
        public string CurrentValue { get; set; }
        public string Reason { get; set; }
    }
    public sealed class DraftRebaseResult
    {
        public EditDraft[] Reusable { get; set; } = new EditDraft[0];
        public DraftConflict[] Conflicts { get; set; } = new DraftConflict[0];
        public EditDraft[] InvalidTargets { get; set; } = new EditDraft[0];
    }
    public sealed class WriteChange
    {
        public string FieldKey { get; set; }
        public int OwnerId { get; set; }
        public string OwnerUniqueId { get; set; }
        public string Source { get; set; }
        public string SourceGuid { get; set; }
        public string Origin { get; set; }
        public string PropertySet { get; set; }
        public string RawType { get; set; }
        public string ExpectedSignature { get; set; }
        public string ExpectedValue { get; set; }
        public string Value { get; set; }
        public string Unit { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
        public int AffectedInstances { get; set; }
        public bool Manual { get; set; }
    }
    public sealed class EditPlan
    {
        public RequestContext Context { get; set; }
        public string Id { get; set; }
        public string CaptureHash { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public WriteChange[] Changes { get; set; } = new WriteChange[0];
        public string Hash { get; set; }
        public string Signature { get; set; }
    }
    public sealed class WritePermit
    {
        public RequestContext Context { get; set; }
        public string Id { get; set; }
        public string PlanId { get; set; }
        public string PlanHash { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public string Signature { get; set; }
    }
    public sealed class PublicSigningKey
    {
        public string Modulus { get; set; }
        public string Exponent { get; set; }
    }
    public sealed class WriteReceipt
    {
        public string PermitId { get; set; }
        public string PlanId { get; set; }
        public string DocumentSessionId { get; set; }
        public bool TransactionCommitted { get; set; }
        public bool ReadbackPassed { get; set; }
        public bool RecheckCompleted { get; set; }
        public bool RvtSaved { get; set; }
        public string Detail { get; set; }
        public DateTime CompletedUtc { get; set; }
    }
    public sealed class PreviewRequest
    {
        public RequestContext Context { get; set; }
        public EditDraft[] Drafts { get; set; } = new EditDraft[0];
        public bool IncludeSuggestions { get; set; } = true;
    }
    public sealed class AuthorizeRequest
    {
        public string PlanHash { get; set; }
        public string IdempotencyKey { get; set; }
    }
}
