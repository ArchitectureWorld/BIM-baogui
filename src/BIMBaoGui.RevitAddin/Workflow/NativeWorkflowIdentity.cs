using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMBaoGui.RevitAddin.Workflow
{
  internal static class NativeWorkflowIdentityFactory
  {
    internal static NativeWorkflowIdentity Create(
      UIApplication application,
      string modelFileType,
      string stage01FileGuid,
      string stage01PayloadHash,
      RulePackageIdentity rulePackage)
    {
      if (application == null) throw new ArgumentNullException(nameof(application));
      UIDocument uiDocument = application.ActiveUIDocument;
      Document document = uiDocument?.Document;
      if (document == null)
        throw new InvalidOperationException("当前没有活动 Revit 文档。");
      if (rulePackage == null)
        throw new ArgumentNullException(nameof(rulePackage));
      Require(modelFileType, nameof(modelFileType));
      Require(stage01FileGuid, nameof(stage01FileGuid));
      Require(stage01PayloadHash, nameof(stage01PayloadHash));
      Require(rulePackage.PackageId, "rulePackage.PackageId");
      Require(rulePackage.PackageVersion, "rulePackage.PackageVersion");
      Require(rulePackage.RulePackageSha256, "rulePackage.RulePackageSha256");

      return new NativeWorkflowIdentity
      {
        DocumentFingerprint = ComputeDocumentFingerprint(
          document.PathName,
          document.Title,
          application.Application.VersionNumber,
          stage01FileGuid,
          stage01PayloadHash),
        ModelFileType = modelFileType.Trim(),
        RulePackageId = rulePackage.PackageId.Trim(),
        RulePackageVersion = rulePackage.PackageVersion.Trim(),
        RulePackageSha256 = rulePackage.RulePackageSha256.Trim()
      };
    }

    internal static string ComputeDocumentFingerprint(
      string documentPath,
      string documentTitle,
      string revitVersion,
      string stage01FileGuid,
      string stage01PayloadHash)
    {
      Require(documentPath, nameof(documentPath));
      Require(documentTitle, nameof(documentTitle));
      Require(revitVersion, nameof(revitVersion));
      Require(stage01FileGuid, nameof(stage01FileGuid));
      Require(stage01PayloadHash, nameof(stage01PayloadHash));
      return Sha256(string.Join("|", new[]
      {
        documentPath,
        documentTitle,
        revitVersion,
        stage01FileGuid,
        stage01PayloadHash
      }));
    }

    private static void Require(string value, string name)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException("Workflow identity value is required.", name);
    }

    internal static string Sha256(string value)
    {
      using (SHA256 algorithm = SHA256.Create())
      {
        return string.Concat(algorithm.ComputeHash(
          Encoding.UTF8.GetBytes(value ?? string.Empty))
          .Select(valueByte => valueByte.ToString("x2")));
      }
    }
  }
}
