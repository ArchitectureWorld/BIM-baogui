using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Diagnostics;
using BIMBaoGui.Stage01.GrasshopperTypes;
using BIMBaoGui.Stage01.Rules;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.Stage03;
using BIMBaoGui.Stage01.TaskPlanning;
using GH_IO.Serialization;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrRuleIdentityPropagationTests
  {
    [Fact]
    public async Task Current_package_identity_reaches_real_stage03_field_report()
    {
      string directory = Path.Combine(
        Path.GetTempPath(),
        "hbr-rule-identity-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      try
      {
        string documentPath = Path.Combine(directory, "identity-test.rvt");
        File.WriteAllText(documentPath, "identity fixture");
        HbrRulePackage package = HbrRuleDatabase.Current.Package;
        var model = new Stage01Model();
        model.SetValue(Stage01Keys.FileGuid, "identity-file-guid");
        model.SetValue(
          Stage01Keys.ModelFileType,
          PlanningTargetRequirementPolicy.SiteModel);
        model.SetValue(
          Stage01Keys.WorkflowVersion,
          HBRContextVersions.FileContextSchema);
        HBRFileContext fileContext = HBRFileContextFactory.Create(
          model,
          new RevitDocumentSnapshot
          {
            DocumentPath = documentPath,
            DocumentTitle = "identity-test.rvt",
            RevitVersion = "2020"
          },
          initializationPassed: true);
        AssertIdentity(package, fileContext);

        TaskPlanCompilationResult compilation =
          TaskPlanCompiler.Compile(fileContext);
        Assert.True(compilation.Success, string.Join("; ", compilation.Blockers));
        AssertIdentity(package, compilation.Plan);

        var scanRequest = new Stage03RevitScanRequest(fileContext);
        AssertIdentity(package, scanRequest);

        Stage03FieldReportContext captured = null;
        var coordinator = new Stage03WorkflowCoordinator(
          new Stage03WorkflowServices
          {
            ScanAsync = _ => Task.FromResult(new Stage03WorkflowScanResult
            {
              FileGuid = fileContext.FileGuid,
              DocumentFingerprint = fileContext.RevitDocumentFingerprint,
              DocumentTitle = fileContext.RevitDocumentTitle,
              DocumentPath = documentPath,
              RevitVersion = "2020",
              RulePackageId = scanRequest.RulePackageId,
              RulePackageVersion = scanRequest.RulePackageVersion,
              RulePackageSha256 = scanRequest.RulePackageSha256,
              TechnicalFatalCodes = new[]
              {
                Stage03TechnicalFatalCodes.DocumentUnavailable
              }
            }),
            ExportRawAsync = _ => throw new InvalidOperationException(
              "identity propagation test must remain gate-blocked"),
            TranslateAsync = _ => throw new InvalidOperationException(
              "identity propagation test must remain gate-blocked"),
            WriteFieldReport = context =>
            {
              captured = context;
              return Stage03FieldReportWriter.Write(context);
            },
            WriteFailureReport = Stage03FailureReportWriter.TryWrite,
            UtcNow = () => new DateTimeOffset(
              2026, 8, 10, 12, 0, 0, TimeSpan.Zero)
          });

        await coordinator.RunAsync(new Stage03WorkflowRequest
        {
          Context = fileContext,
          OutputDirectory = directory,
          RvtStem = "identity-test",
          RunId = "20260810T120000Z-identity",
          DocumentPath = documentPath,
          PluginVersion = "1.0.0",
          Mode = Stage03GateMode.Strict
        });

        Assert.NotNull(captured);
        AssertIdentity(package, captured);
      }
      finally
      {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
      }
    }

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

      HbrProductionAssemblyIdentityHarness.AssertHashesIncludePackageIdentity();
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

      HbrProductionAssemblyIdentityHarness.AssertValidateContextRejectsAllIdentityMismatches();
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

      HbrProductionAssemblyIdentityHarness.AssertValidLegacyGooRequiresRerun(
        BuildLegacyFileContextJson(validHash: true),
        BuildLegacyTaskPlanJson(validHash: true));
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

      HbrProductionAssemblyIdentityHarness.AssertInvalidLegacyGooIsRejected(
        BuildLegacyFileContextJson(validHash: false),
        BuildLegacyTaskPlanJson(validHash: false));
    }

    [Fact]
    public void Legacy_canonicalizers_are_dedicated_frozen_types()
    {
      HbrProductionAssemblyIdentityHarness.AssertDedicatedLegacyCanonicalizerTypes();

      Assembly linkedAssembly = typeof(HBRFileContextCanonicalizer).Assembly;
      Assert.NotNull(linkedAssembly.GetType(
        "BIMBaoGui.Stage01.Context.HBRFileContextLegacyCanonicalizer",
        false));
      Assert.NotNull(linkedAssembly.GetType(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanLegacyCanonicalizer",
        false));
    }

    [Fact]
    public void Legacy_complex_golden_vectors_are_stable()
    {
      string fileJson = BuildComplexLegacyFileContextJson(validHash: true);
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        fileJson, out _, out string fileError));
      Assert.Contains("规则数据库已升级，请重新运行 Stage01", fileError);

      string taskJson = BuildComplexLegacyTaskPlanJson(validHash: true);
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        taskJson, out _, out string taskError));
      Assert.Contains("规则数据库已升级，请重新运行任务规划", taskError);

      string tamperedFile = fileJson.Replace("复杂项目", "篡改项目");
      Assert.False(HBRFileContextCanonicalizer.TryParse(
        tamperedFile, out _, out string tamperedFileError));
      Assert.Contains("数据损坏", tamperedFileError);

      string tamperedTask = taskJson.Replace("任务\\\"A", "篡改任务A");
      Assert.False(HBRTaskPlanCanonicalizer.TryParse(
        tamperedTask, out _, out string tamperedTaskError));
      Assert.Contains("数据损坏", tamperedTaskError);

      HbrProductionAssemblyIdentityHarness.AssertValidLegacyGooRequiresRerun(
        fileJson,
        taskJson);
      HbrProductionAssemblyIdentityHarness.AssertInvalidLegacyGooIsRejected(
        tamperedFile,
        tamperedTask);
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

    private static void AssertIdentity(HbrRulePackage expected, object actual)
    {
      Type type = actual.GetType();
      const BindingFlags flags = BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance;
      Assert.Equal(
        expected.PackageId,
        type.GetProperty("RulePackageId", flags).GetValue(actual, null));
      Assert.Equal(
        expected.PackageVersion,
        type.GetProperty("RulePackageVersion", flags).GetValue(actual, null));
      Assert.Equal(
        expected.RulePackageSha256,
        type.GetProperty("RulePackageSha256", flags).GetValue(actual, null));
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

    private static string BuildComplexLegacyFileContextJson(bool validHash)
    {
      string hash = validHash
        ? "29dd8aeacbd20589d38ac910f313be4bb9b80cefb4c914eed9a53d916e57a324"
        : new string('0', 64);
      return "{\n"
        + "  \"fileContextHash\":" + Json(hash) + ",\n"
        + "  \"sourcePayloadHash\":" + Json("legacy\"source\\hash") + ",\n"
        + "  \"rulePackVersion\":\"0.1.0\",\n"
        + "  \"officialProtocolCompatible\":true,\n"
        + "  \"initializationPassed\":true,\n"
        + "  \"notApplicableRuleIds\":[\"HBR.Y\",\"HBR.X\"],\n"
        + "  \"activatedRuleIds\":[\"HBR.C\\\\D\",\"HBR.B\\\"Q\",\"HBR.A\",\"HBR.A\"],\n"
        + "  \"projectConditions\":{\"site.quote\\\"key\":true,\"site.green\":true,\"site.alpha\":false},\n"
        + "  \"planningTargets\":{\n"
        + "    \"planning.green_rate\":{\"mvdText\":\"ignored\",\"source\":" + Json("审查\n意见") + ",\"unit\":\"Percent\",\"value2\":\"\",\"value1\":\"35.0\",\"operator\":\"GreaterOrEqual\"},\n"
        + "    \"planning.floor_area_ratio\":{\"mvdText\":\"ignored\",\"source\":" + Json("规则\"来源") + ",\"unit\":\"Ratio\",\"value2\":\"\",\"value1\":\"2.00\",\"operator\":\"LessOrEqual\"}\n"
        + "  },\n"
        + "  \"spatialReference\":{\"angleUnit\":\"°\",\"areaUnit\":\"m²\",\"lengthUnit\":\"m\",\"trueNorthAngleDegrees\":\"5.500\",\"baseElevation\":\"12.30\",\"baseY\":\"-67.890\",\"baseX\":\"123.450\",\"elevationSystem\":\"1985国家高程基准\",\"coordinateSystem\":\"CGCS2000\"},\n"
        + "  \"modelScope\":" + Json("报规\\模型") + ",\n"
        + "  \"modelFileType\":\"总平\",\n"
        + "  \"subitemName\":\"复杂子项\",\n"
        + "  \"subitemCode\":\"S-02\",\n"
        + "  \"projectName\":\"复杂项目\",\n"
        + "  \"projectNumber\":" + Json("P-\n001") + ",\n"
        + "  \"revitDocumentTitle\":" + Json("复杂\"模型.rvt") + ",\n"
        + "  \"revitDocumentFingerprint\":\"legacy-complex-fingerprint\",\n"
        + "  \"fileGuid\":\"legacy-complex-file\",\n"
        + "  \"workflowVersion\":\"0.9.0\",\n"
        + "  \"schemaVersion\":\"0.9.0\"\n"
        + "}";
    }

    private static string BuildComplexLegacyTaskPlanJson(bool validHash)
    {
      string hash = validHash
        ? "c1999411ef6f8891dcf72c85110ab171912d7396eb412fe0e9c6918e7f15517c"
        : new string('0', 64);
      return "{\n"
        + "  \"taskPlanHash\":" + Json(hash) + ",\n"
        + "  \"notApplicableTasks\":[{\"targetComparisons\":[],\"propertyChecks\":[],\"geometryChecks\":[],\"dependencies\":[],\"attributeRequirements\":[\"attr.x\"],\"skeletonTask\":false,\"sequence\":\"30\",\"conditionKey\":\"site.alpha\",\"requirement\":\"Conditional\",\"objectCode\":\"OBJ.X\",\"name\":\"不适用任务\",\"taskId\":\"SITE.X\"}],\n"
        + "  \"activeTasks\":[\n"
        + "    {\"targetComparisons\":[\"target.c\"],\"propertyChecks\":[\"prop.c\"],\"geometryChecks\":[\"geo.c\"],\"dependencies\":[\"SITE.A\"],\"attributeRequirements\":[\"attr.c\"],\"skeletonTask\":true,\"sequence\":\"20\",\"conditionKey\":\"site.green\",\"requirement\":\"Conditional\",\"objectCode\":\"OBJ.B\",\"name\":" + Json("任务\nB") + ",\"taskId\":\"SITE.B\"},\n"
        + "    {\"targetComparisons\":[\"target.b\",\"target.a\"],\"propertyChecks\":[\"prop.b\",\"prop.a\"],\"geometryChecks\":[\"geo.b\",\"geo.a\"],\"dependencies\":[\"dep.b\",\"dep.a\"],\"attributeRequirements\":[\"attr.z\",\"attr.a\",\"attr.a\"],\"skeletonTask\":false,\"sequence\":\"10\",\"conditionKey\":\"\",\"requirement\":\"Required\",\"objectCode\":\"OBJ.A\",\"name\":" + Json("任务\"A") + ",\"taskId\":\"SITE.A\"}\n"
        + "  ],\n"
        + "  \"skeletonPath\":" + Json("总平\\复杂") + ",\n"
        + "  \"modelFileType\":\"总平\",\n"
        + "  \"fileContextHash\":\"legacy-complex-context-hash\",\n"
        + "  \"schemaVersion\":\"0.5.0\"\n"
        + "}";
    }

    private static string Json(string value)
    {
      return new JavaScriptSerializer().Serialize(value);
    }
  }
}
