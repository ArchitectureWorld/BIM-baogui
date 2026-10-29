using Hifc.Web.Contracts;

namespace Hifc.Web.Revit.PlatformSync
{
    public sealed class PlatformSyncSource
    {
        public string DocumentPath { get; set; }
        public string ProjectUniqueId { get; set; }
        public string DocumentSessionId { get; set; }
        public ExportReceipt Receipt { get; set; }
    }
}
