using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public sealed class NativeWriteInput
    {
        public string DocumentSessionId { get; set; }
        public CheckResult Basis { get; set; }
        public CapturePlan Capture { get; set; }
        public EditPlan Plan { get; set; }
        public WritePermit Permit { get; set; }
        public IDictionary<string, string> AssignedSlots { get; set; }
    }
    public static class NativeWriter
    {
        public static WriteReceipt Apply(Document document, NativeWriteInput input, PublicSigningKey key, DateTime serverNowUtc) => new WriteReceipt();
    }
}
