using System.Collections.Generic;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class Stage03SavedRoleAuditPolicyTests
  {
    [Fact]
    public void Select_UsesOnlyAuditFromCurrentRulePackage()
    {
      var audits = new[]
      {
        Audit(
          elementId: 10,
          roleId: "ORGANIZATION",
          packageId: "current-package",
          packageVersion: "0.9.0",
          packageSha256: "ABCDEF",
          auditUtc: "2026-08-04T09:00:00.0000000Z"),
        Audit(
          elementId: 20,
          roleId: "FORGED-ROLE",
          packageId: "old-package",
          packageVersion: "0.8.0",
          packageSha256: "OLD",
          auditUtc: "2026-08-04T10:00:00.0000000Z")
      };

      IReadOnlyDictionary<string, string> selected =
        Stage03SavedRoleAuditPolicy.Select(
          audits,
          "current-package",
          "0.9.0",
          "abcdef");

      Assert.Equal("ORGANIZATION", selected["element-uid"]);
    }

    [Fact]
    public void Select_UsesHighestStorageElementIdForEqualTimestamp()
    {
      var audits = new[]
      {
        Audit(10, "FIRST", "package", "0.9.0", "SHA", "2026-08-04T10:00:00Z"),
        Audit(20, "SECOND", "package", "0.9.0", "SHA", "2026-08-04T10:00:00Z")
      };

      IReadOnlyDictionary<string, string> selected =
        Stage03SavedRoleAuditPolicy.Select(
          audits,
          "package",
          "0.9.0",
          "SHA");

      Assert.Equal("SECOND", selected["element-uid"]);
    }

    [Fact]
    public void Select_RejectsAuditWhenExpectedPackageIdentityIsMissing()
    {
      IReadOnlyDictionary<string, string> selected =
        Stage03SavedRoleAuditPolicy.Select(
          new[]
          {
            Audit(10, "ORGANIZATION", "package", "0.9.0", "SHA", "2026-08-04T10:00:00Z")
          },
          string.Empty,
          "0.9.0",
          "SHA");

      Assert.Empty(selected);
    }

    private static Stage03SavedRoleAuditSnapshot Audit(
      int elementId,
      string roleId,
      string packageId,
      string packageVersion,
      string packageSha256,
      string auditUtc)
    {
      return new Stage03SavedRoleAuditSnapshot
      {
        StorageElementId = elementId,
        UniqueId = "element-uid",
        RoleId = roleId,
        RulePackageId = packageId,
        RulePackageVersion = packageVersion,
        RulePackageSha256 = packageSha256,
        AuditUtc = auditUtc
      };
    }
  }
}
