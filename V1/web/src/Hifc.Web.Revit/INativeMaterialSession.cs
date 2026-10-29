using System.Threading.Tasks;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public interface INativeMaterialSession
    {
        CheckResult Result { get; }
        Task<MaterialPreview> PreviewMaterialAsync(MaterialPreviewRequest request);
        Task AcceptMaterialAsync(MaterialPreview preview, MaterialDecision decision, string sourcePath);
    }
}
