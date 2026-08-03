using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.GrasshopperTypes;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.TaskPlanning;
using GH_IO.Serialization;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrRuleIdentityPropagationTests
  {
    [Fact]
    public void File_context_and_task_plan_hashes_include_package_identity()
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      HBRFileContext factoryContext = HBRFileContextFactory.Create(
        new Stage01Model(),
        new RevitDocumentSnapshot
        {
          DocumentPath = @"C:\Models\identity-test.rvt",
          DocumentTitle = "identity-test.rvt",
          RevitVersion = "2020"
        },
        initializationPassed: true);
      Assert.Equal(package.PackageId, factoryContext.RulePackageId);
      Assert.Equal(package.PackageVersion, factoryContext.RulePackageVersion);
      Assert.Equal(package.RulePackageSha256, factoryContext.RulePackageSha256);

      HBRFileContext baselineContext = BuildCurrentContext(
        package.PackageId,
        package.PackageVersion,
        package.RulePackageSha256);

      Assert.NotEqual(
        baselineContext.FileContextHash,
        BuildCurrentContext("different-package", package.PackageVersion, package.RulePackageSha256).FileContextHash);
      Assert.NotEqual(
        baselineContext.FileContextHash,
        BuildCurrentContext(package.PackageId, "different-version", package.RulePackageSha256).FileContextHash);
      Assert.NotEqual(
        baselineContext.FileContextHash,
        BuildCurrentContext(package.PackageId, package.PackageVersion, new string('0', 64)).FileContextHash);

      HBRTaskPlan baselinePlan = BuildTaskPlan(
        package.PackageId,
        package.PackageVersion,
        package.RulePackageSha256);
      Assert.NotEqual(
        baselinePlan.TaskPlanHash,
        BuildTaskPlan("different-package", package.PackageVersion, package.RulePackageSha256).TaskPlanHash);
      Assert.NotEqual(
        baselinePlan.TaskPlanHash,
        BuildTaskPlan(package.PackageId, "different-version", package.RulePackageSha256).TaskPlanHash);
      Assert.NotEqual(
        baselinePlan.TaskPlanHash,
        BuildTaskPlan(package.PackageId, package.PackageVersion, new string('0', 64)).TaskPlanHash);

      TaskPlanCompilationResult compilation = TaskPlanCompiler.Compile(baselineContext);
      Assert.True(compilation.Success, string.Join("; ", compilation.Blockers));
      Assert.Equal(package.PackageId, compilation.Plan.RulePackageId);
      Assert.Equal(package.PackageVersion, compilation.Plan.RulePackageVersion);
      Assert.Equal(package.RulePackageSha256, compilation.Plan.RulePackageSha256);

      string contextJson = HBRFileContextCanonicalizer.ToJson(baselineContext);
      AssertOrderedIdentity(contextJson);
      string planJson = HBRTaskPlanCanonicalizer.ToJson(baselinePlan);
      AssertOrderedIdentity(planJson);
      Assert.True(HBRFileContextCanonicalizer.TryParse(
        contextJson,
        out HBRFileContext restoredContext,
        out string contextError), contextError);
      Assert.Equal(package.PackageId, restoredContext.RulePackageId);
      Assert.Equal(package.PackageVersion, restoredContext.RulePackageVersion);
      Assert.Equal(package.RulePackageSha256, restoredContext.RulePackageSha256);
      Assert.True(HBRTaskPlanCanonicalizer.TryParse(
        planJson,
        out HBRTaskPlan restoredPlan,
        out string planError), planError);
      Assert.Equal(package.PackageId, restoredPlan.RulePackageId);
      Assert.Equal(package.PackageVersion, restoredPlan.RulePackageVersion);
      Assert.Equal(package.RulePackageSha256, restoredPlan.RulePackageSha256);

      var serializer = new JavaScriptSerializer();
      Dictionary<string, object> missingIdentity =
        serializer.Deserialize<Dictionary<string, object>>(contextJson);
      missingIdentity.Remove("rulePackageSha256");
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        serializer.Serialize(missingIdentity),
        out _,
        out string missingIdentityError));
      Assert.Contains("缺少完整规则数据库身份", missingIdentityError);

      Dictionary<string, object> missingHash =
        serializer.Deserialize<Dictionary<string, object>>(planJson);
      missingHash.Remove("taskPlanHash");
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        serializer.Serialize(missingHash),
        out _,
        out string missingHashError));
      Assert.Contains("缺少哈希", missingHashError);

      AssertStrictCurrentFileContextParsing(contextJson, serializer);
      AssertStrictCurrentTaskPlanParsing(planJson, serializer);

      var staleFileGoo = new HBRFileContextGoo(BuildCurrentContext(
        "different-package",
        package.PackageVersion,
        package.RulePackageSha256));
      Assert.False(staleFileGoo.IsValid);
      Assert.Contains("重新运行 Stage01", staleFileGoo.IsValidWhyNot);

      var staleTaskGoo = new HBRTaskPlanGoo(BuildTaskPlan(
        package.PackageId,
        "different-version",
        package.RulePackageSha256));
      Assert.False(staleTaskGoo.IsValid);
      Assert.Contains("重新运行任务规划", staleTaskGoo.IsValidWhyNot);

      var invalidHashFileGoo = new HBRFileContextGoo(
        baselineContext.WithHash("invalid-file-context-hash"));
      Assert.False(invalidHashFileGoo.IsValid);
      Assert.Contains("哈希无效", invalidHashFileGoo.IsValidWhyNot);

      var invalidHashTaskGoo = new HBRTaskPlanGoo(
        baselinePlan.WithHash("invalid-task-plan-hash"));
      Assert.False(invalidHashTaskGoo.IsValid);
      Assert.Contains("哈希无效", invalidHashTaskGoo.IsValidWhyNot);
    }

    [Fact]
    public void ValidateContext_rejects_rule_package_hash_mismatch()
    {
      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      HBRFileContext mismatch = BuildCurrentContext(
        package.PackageId,
        package.PackageVersion,
        new string('0', 64));

      IReadOnlyList<string> blockers = TaskPlanCompiler.ValidateContext(mismatch);

      Assert.Contains(blockers, message => message.Contains("规则包 SHA-256 不匹配"));
      Assert.DoesNotContain(blockers, message => message.Contains("文件上下文哈希无效"));

      IReadOnlyList<string> idBlockers = TaskPlanCompiler.ValidateContext(
        BuildCurrentContext(
          "different-package",
          package.PackageVersion,
          package.RulePackageSha256));
      Assert.Contains(idBlockers, message => message.Contains("规则包 ID 不匹配"));

      IReadOnlyList<string> versionBlockers = TaskPlanCompiler.ValidateContext(
        BuildCurrentContext(
          package.PackageId,
          "different-version",
          package.RulePackageSha256));
      Assert.Contains(versionBlockers, message => message.Contains("规则包版本不匹配"));
    }

    [Fact]
    public void Legacy_goo_with_valid_old_hash_requires_rerun()
    {
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        BuildLegacyFileContextJson(validHash: true),
        out HBRFileContext fileContext,
        out string fileError));
      Assert.Null(fileContext);
      Assert.Contains("规则数据库已升级，请重新运行 Stage01", fileError);

      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        BuildLegacyTaskPlanJson(validHash: true),
        out HBRTaskPlan taskPlan,
        out string taskError));
      Assert.Null(taskPlan);
      Assert.Contains("规则数据库已升级，请重新运行任务规划", taskError);

      var fileChunk = new GH_LooseChunk("file");
      fileChunk.SetString(
        "HBR.FileContext.Json",
        BuildLegacyFileContextJson(validHash: true));
      var fileGoo = new HBRFileContextGoo();
      Assert.True(fileGoo.Read(fileChunk));
      Assert.False(fileGoo.IsValid);
      Assert.Null(fileGoo.Value);
      Assert.Contains("规则数据库已升级，请重新运行 Stage01", fileGoo.IsValidWhyNot);
      var rewrittenFileChunk = new GH_LooseChunk("rewritten-file");
      Assert.True(fileGoo.Write(rewrittenFileChunk));
      string rewrittenFileJson = rewrittenFileChunk.GetString("HBR.FileContext.Json");
      Assert.DoesNotContain("rulePackageId", rewrittenFileJson);
      var reloadedFileGoo = new HBRFileContextGoo();
      Assert.True(reloadedFileGoo.Read(rewrittenFileChunk));
      Assert.Contains(
        "规则数据库已升级，请重新运行 Stage01",
        reloadedFileGoo.IsValidWhyNot);
      var castFileGoo = new HBRFileContextGoo();
      Assert.True(castFileGoo.CastFrom(BuildLegacyFileContextJson(validHash: true)));
      Assert.False(castFileGoo.IsValid);
      Assert.Contains("重新运行 Stage01", castFileGoo.IsValidWhyNot);
      var duplicateFileGoo = (HBRFileContextGoo)castFileGoo.Duplicate();
      Assert.False(duplicateFileGoo.IsValid);
      Assert.Equal(castFileGoo.IsValidWhyNot, duplicateFileGoo.IsValidWhyNot);

      var taskChunk = new GH_LooseChunk("task");
      taskChunk.SetString(
        "HBR.TaskPlan.Json",
        BuildLegacyTaskPlanJson(validHash: true));
      var taskGoo = new HBRTaskPlanGoo();
      Assert.True(taskGoo.Read(taskChunk));
      Assert.False(taskGoo.IsValid);
      Assert.Null(taskGoo.Value);
      Assert.Contains("规则数据库已升级，请重新运行任务规划", taskGoo.IsValidWhyNot);
      var rewrittenTaskChunk = new GH_LooseChunk("rewritten-task");
      Assert.True(taskGoo.Write(rewrittenTaskChunk));
      string rewrittenTaskJson = rewrittenTaskChunk.GetString("HBR.TaskPlan.Json");
      Assert.DoesNotContain("rulePackageId", rewrittenTaskJson);
      var reloadedTaskGoo = new HBRTaskPlanGoo();
      Assert.True(reloadedTaskGoo.Read(rewrittenTaskChunk));
      Assert.Contains(
        "规则数据库已升级，请重新运行任务规划",
        reloadedTaskGoo.IsValidWhyNot);
      var castTaskGoo = new HBRTaskPlanGoo();
      Assert.True(castTaskGoo.CastFrom(BuildLegacyTaskPlanJson(validHash: true)));
      Assert.False(castTaskGoo.IsValid);
      Assert.Contains("重新运行任务规划", castTaskGoo.IsValidWhyNot);
      var duplicateTaskGoo = (HBRTaskPlanGoo)castTaskGoo.Duplicate();
      Assert.False(duplicateTaskGoo.IsValid);
      Assert.Equal(castTaskGoo.IsValidWhyNot, duplicateTaskGoo.IsValidWhyNot);

      HbrRulePackage package = HbrRuleDatabase.Current.Package;
      var currentFileChunk = new GH_LooseChunk("current-file");
      currentFileChunk.SetString(
        "HBR.FileContext.Json",
        HBRFileContextCanonicalizer.ToJson(BuildCurrentContext(
          package.PackageId,
          package.PackageVersion,
          package.RulePackageSha256)));
      Assert.True(fileGoo.Read(currentFileChunk));
      Assert.True(fileGoo.IsValid);
      Assert.Equal(string.Empty, fileGoo.IsValidWhyNot);

      var currentTaskChunk = new GH_LooseChunk("current-task");
      currentTaskChunk.SetString(
        "HBR.TaskPlan.Json",
        HBRTaskPlanCanonicalizer.ToJson(BuildTaskPlan(
          package.PackageId,
          package.PackageVersion,
          package.RulePackageSha256)));
      Assert.True(taskGoo.Read(currentTaskChunk));
      Assert.True(taskGoo.IsValid);
      Assert.Equal(string.Empty, taskGoo.IsValidWhyNot);
    }

    [Fact]
    public void Legacy_goo_with_invalid_old_hash_is_rejected()
    {
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        BuildLegacyFileContextJson(validHash: false),
        out HBRFileContext fileContext,
        out string fileError));
      Assert.Null(fileContext);
      Assert.Contains("数据损坏", fileError);
      Assert.DoesNotContain("规则数据库已升级", fileError);

      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        BuildLegacyTaskPlanJson(validHash: false),
        out HBRTaskPlan taskPlan,
        out string taskError));
      Assert.Null(taskPlan);
      Assert.Contains("数据损坏", taskError);
      Assert.DoesNotContain("规则数据库已升级", taskError);

      string tamperedFileJson = BuildLegacyFileContextJson(validHash: true)
        .Replace("旧项目", "篡改项目");
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        tamperedFileJson, out _, out string tamperedFileError));
      Assert.Contains("数据损坏", tamperedFileError);

      string tamperedTaskJson = BuildLegacyTaskPlanJson(validHash: true)
        .Replace("总平基础", "篡改任务");
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        tamperedTaskJson, out _, out string tamperedTaskError));
      Assert.Contains("数据损坏", tamperedTaskError);

      var fileChunk = new GH_LooseChunk("file");
      fileChunk.SetString(
        "HBR.FileContext.Json",
        BuildLegacyFileContextJson(validHash: false));
      var fileGoo = new HBRFileContextGoo();
      Assert.False(fileGoo.Read(fileChunk));
      Assert.False(fileGoo.IsValid);
      Assert.Contains("数据损坏", fileGoo.IsValidWhyNot);

      var taskChunk = new GH_LooseChunk("task");
      taskChunk.SetString(
        "HBR.TaskPlan.Json",
        BuildLegacyTaskPlanJson(validHash: false));
      var taskGoo = new HBRTaskPlanGoo();
      Assert.False(taskGoo.Read(taskChunk));
      Assert.False(taskGoo.IsValid);
      Assert.Contains("数据损坏", taskGoo.IsValidWhyNot);
    }

    private static HBRFileContext BuildCurrentContext(
      string packageId,
      string packageVersion,
      string packageSha256)
    {
      var provisional = new HBRFileContext(
        HBRContextVersions.FileContextSchema,
        HBRContextVersions.FileContextSchema,
        "file-guid",
        "document-fingerprint",
        "测试模型.rvt",
        "P-001",
        "测试项目",
        "S-01",
        "测试子项",
        PlanningTargetRequirementPolicy.SiteModel,
        "报规模型",
        new HBRSpatialReference(
          "CGCS2000",
          "1985国家高程基准",
          0m,
          0m,
          0m,
          0m,
          "m",
          "m²",
          "°"),
        new Dictionary<string, PlanningTargetValue>(StringComparer.Ordinal),
        new Dictionary<string, bool>(StringComparer.Ordinal),
        Array.Empty<string>(),
        Array.Empty<string>(),
        true,
        true,
        string.Empty,
        packageId,
        packageVersion,
        packageSha256,
        "source-payload-hash",
        string.Empty);
      return provisional.WithHash(HBRFileContextCanonicalizer.ComputeHash(provisional));
    }

    private static HBRTaskPlan BuildTaskPlan(
      string packageId,
      string packageVersion,
      string packageSha256)
    {
      var provisional = new HBRTaskPlan(
        HBRContextVersions.TaskPlanSchema,
        "file-context-hash",
        packageId,
        packageVersion,
        packageSha256,
        PlanningTargetRequirementPolicy.SiteModel,
        "总平",
        new[] { BuildTaskItem() },
        Array.Empty<HBRTaskPlanItem>(),
        string.Empty);
      return provisional.WithHash(HBRTaskPlanCanonicalizer.ComputeHash(provisional));
    }

    private static HBRTaskPlanItem BuildTaskItem()
    {
      return new HBRTaskPlanItem(
        "SITE.BASE",
        "总平基础",
        "SITE",
        HBRTaskRequirement.Required,
        string.Empty,
        1,
        false,
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>());
    }

    private static void AssertOrderedIdentity(string json)
    {
      int packageId = json.IndexOf("\"rulePackageId\"", StringComparison.Ordinal);
      int packageVersion = json.IndexOf("\"rulePackageVersion\"", StringComparison.Ordinal);
      int packageSha256 = json.IndexOf("\"rulePackageSha256\"", StringComparison.Ordinal);
      Assert.True(packageId >= 0);
      Assert.True(packageVersion > packageId);
      Assert.True(packageSha256 > packageVersion);
    }

    private static void AssertStrictCurrentFileContextParsing(
      string json,
      JavaScriptSerializer serializer)
    {
      Dictionary<string, object> root =
        serializer.Deserialize<Dictionary<string, object>>(json);

      var missingHash = new Dictionary<string, object>(root, StringComparer.Ordinal);
      missingHash.Remove("fileContextHash");
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        serializer.Serialize(missingHash), out _, out string missingHashError));
      Assert.Contains("缺少哈希", missingHashError);

      var missingAllIdentity = new Dictionary<string, object>(root, StringComparer.Ordinal);
      missingAllIdentity.Remove("rulePackageId");
      missingAllIdentity.Remove("rulePackageVersion");
      missingAllIdentity.Remove("rulePackageSha256");
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        serializer.Serialize(missingAllIdentity), out _, out string missingAllError));
      Assert.Contains("数据损坏", missingAllError);

      var missingPartIdentity = new Dictionary<string, object>(root, StringComparer.Ordinal);
      missingPartIdentity.Remove("rulePackageVersion");
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        serializer.Serialize(missingPartIdentity), out _, out string missingPartError));
      Assert.Contains("缺少完整规则数据库身份", missingPartError);

      var emptyIdentity = new Dictionary<string, object>(root, StringComparer.Ordinal)
      {
        ["rulePackageId"] = string.Empty
      };
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        serializer.Serialize(emptyIdentity), out _, out string emptyError));
      Assert.Contains("缺少完整规则数据库身份", emptyError);

      var nullIdentity = new Dictionary<string, object>(root, StringComparer.Ordinal)
      {
        ["rulePackageSha256"] = null
      };
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        serializer.Serialize(nullIdentity), out _, out string nullIdentityError));
      Assert.Contains("缺少完整规则数据库身份", nullIdentityError);

      Assert.False(HBRFileContextCanonicalizer.TryParse(
        "{}", out _, out string emptyObjectError));
      Assert.Contains("数据损坏", emptyObjectError);
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        "null", out _, out string nullError));
      Assert.Contains("数据损坏", nullError);
    }

    private static void AssertStrictCurrentTaskPlanParsing(
      string json,
      JavaScriptSerializer serializer)
    {
      Dictionary<string, object> root =
        serializer.Deserialize<Dictionary<string, object>>(json);

      var missingHash = new Dictionary<string, object>(root, StringComparer.Ordinal);
      missingHash.Remove("taskPlanHash");
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        serializer.Serialize(missingHash), out _, out string missingHashError));
      Assert.Contains("缺少哈希", missingHashError);

      var missingAllIdentity = new Dictionary<string, object>(root, StringComparer.Ordinal);
      missingAllIdentity.Remove("rulePackageId");
      missingAllIdentity.Remove("rulePackageVersion");
      missingAllIdentity.Remove("rulePackageSha256");
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        serializer.Serialize(missingAllIdentity), out _, out string missingAllError));
      Assert.Contains("数据损坏", missingAllError);

      var missingPartIdentity = new Dictionary<string, object>(root, StringComparer.Ordinal);
      missingPartIdentity.Remove("rulePackageSha256");
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        serializer.Serialize(missingPartIdentity), out _, out string missingPartError));
      Assert.Contains("缺少完整规则数据库身份", missingPartError);

      var emptyIdentity = new Dictionary<string, object>(root, StringComparer.Ordinal)
      {
        ["rulePackageVersion"] = string.Empty
      };
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        serializer.Serialize(emptyIdentity), out _, out string emptyError));
      Assert.Contains("缺少完整规则数据库身份", emptyError);

      var nullIdentity = new Dictionary<string, object>(root, StringComparer.Ordinal)
      {
        ["rulePackageId"] = null
      };
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        serializer.Serialize(nullIdentity), out _, out string nullIdentityError));
      Assert.Contains("缺少完整规则数据库身份", nullIdentityError);

      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        "{}", out _, out string emptyObjectError));
      Assert.Contains("数据损坏", emptyObjectError);
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        "null", out _, out string nullError));
      Assert.Contains("数据损坏", nullError);
    }

    private static string BuildLegacyFileContextJson(bool validHash)
    {
      string hash = validHash
        ? "01bc3093056647764d7ac06052bd110a2df527d555a8e84b8586e2a8db2c311d"
        : new string('0', 64);
      return "{ \"fileContextHash\": \"" + hash + "\", "
        + "\"sourcePayloadHash\": \"legacy-source\", "
        + "\"rulePackVersion\": \"0.1.0\", "
        + "\"officialProtocolCompatible\": true, "
        + "\"initializationPassed\": true, "
        + "\"notApplicableRuleIds\": [], \"activatedRuleIds\": [], "
        + "\"projectConditions\": {}, \"planningTargets\": {}, "
        + "\"spatialReference\": {\"angleUnit\":\"°\",\"areaUnit\":\"m²\",\"lengthUnit\":\"m\",\"trueNorthAngleDegrees\":\"0\",\"baseElevation\":\"0\",\"baseY\":\"0\",\"baseX\":\"0\",\"elevationSystem\":\"1985国家高程基准\",\"coordinateSystem\":\"CGCS2000\"}, "
        + "\"modelScope\": \"报规模型\", \"modelFileType\": \"总平\", "
        + "\"subitemName\": \"旧子项\", \"subitemCode\": \"S-01\", "
        + "\"projectName\": \"旧项目\", \"projectNumber\": \"P-001\", "
        + "\"revitDocumentTitle\": \"legacy.rvt\", "
        + "\"revitDocumentFingerprint\": \"legacy-fingerprint\", "
        + "\"fileGuid\": \"legacy-file\", \"workflowVersion\": \"0.9.0\", "
        + "\"schemaVersion\": \"0.9.0\" }";
    }

    private static string BuildLegacyTaskPlanJson(bool validHash)
    {
      string hash = validHash
        ? "068bea1d2debe3febebf078b9ccc3634095d0175267eab815dbf1e10f0d2247e"
        : new string('0', 64);
      return "{ \"taskPlanHash\": \"" + hash + "\", "
        + "\"notApplicableTasks\": [], "
        + "\"activeTasks\": [{\"targetComparisons\":[],\"propertyChecks\":[],\"geometryChecks\":[],\"dependencies\":[],\"attributeRequirements\":[],\"skeletonTask\":false,\"sequence\":\"1\",\"conditionKey\":\"\",\"requirement\":\"Required\",\"objectCode\":\"SITE\",\"name\":\"总平基础\",\"taskId\":\"SITE.BASE\"}], "
        + "\"skeletonPath\": \"总平\", \"modelFileType\": \"总平\", "
        + "\"fileContextHash\": \"legacy-context-hash\", "
        + "\"schemaVersion\": \"0.5.0\" }";
    }
  }
}
