using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Context;
using GH_IO.Serialization;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  internal static class HbrProductionAssemblyIdentityHarness
  {
    private const string ProductionAssemblyFixtureName =
      "BIMBaoGui.Stage01.production.dll";

    private static readonly Lazy<Assembly> LazyProductionAssembly =
      new Lazy<Assembly>(LoadProductionAssembly);

    public static void AssertHashesIncludePackageIdentity()
    {
      Assembly assembly = ProductionAssembly;
      RuleIdentity identity = CurrentIdentity();

      object factoryContext = BuildFileContextFromFactory();
      AssertProductionObject(factoryContext);
      Assert.Equal(
        identity.PackageId,
        StringProperty(factoryContext, "RulePackageId"));
      Assert.Equal(
        identity.PackageVersion,
        StringProperty(factoryContext, "RulePackageVersion"));
      Assert.Equal(
        identity.PackageSha256,
        StringProperty(factoryContext, "RulePackageSha256"));

      object baselineContext = BuildFileContext(
        identity.PackageId,
        identity.PackageVersion,
        identity.PackageSha256);
      AssertProductionObject(baselineContext);
      string baselineContextHash = StringProperty(
        baselineContext,
        "FileContextHash");
      Assert.NotEqual(
        baselineContextHash,
        StringProperty(BuildFileContext(
          "different-package",
          identity.PackageVersion,
          identity.PackageSha256), "FileContextHash"));
      Assert.NotEqual(
        baselineContextHash,
        StringProperty(BuildFileContext(
          identity.PackageId,
          "different-version",
          identity.PackageSha256), "FileContextHash"));
      Assert.NotEqual(
        baselineContextHash,
        StringProperty(BuildFileContext(
          identity.PackageId,
          identity.PackageVersion,
          new string('0', 64)), "FileContextHash"));

      object baselinePlan = BuildTaskPlan(
        identity.PackageId,
        identity.PackageVersion,
        identity.PackageSha256);
      AssertProductionObject(baselinePlan);
      string baselinePlanHash = StringProperty(baselinePlan, "TaskPlanHash");
      Assert.NotEqual(
        baselinePlanHash,
        StringProperty(BuildTaskPlan(
          "different-package",
          identity.PackageVersion,
          identity.PackageSha256), "TaskPlanHash"));
      Assert.NotEqual(
        baselinePlanHash,
        StringProperty(BuildTaskPlan(
          identity.PackageId,
          "different-version",
          identity.PackageSha256), "TaskPlanHash"));
      Assert.NotEqual(
        baselinePlanHash,
        StringProperty(BuildTaskPlan(
          identity.PackageId,
          identity.PackageVersion,
          new string('0', 64)), "TaskPlanHash"));

      Type compilerType = ProductionType(
        "BIMBaoGui.Stage01.TaskPlanning.TaskPlanCompiler");
      object compilation = compilerType.GetMethod(
        "Compile",
        BindingFlags.Public | BindingFlags.Static).Invoke(
          null,
          new[] { baselineContext });
      AssertProductionObject(compilation);
      Assert.True(
        BooleanProperty(compilation, "Success"),
        "生产 TaskPlanCompiler.Compile 失败："
          + string.Join(" | ", ((IEnumerable)Property(compilation, "Blockers"))
            .Cast<object>()
            .Select(Convert.ToString)));
      object compiledPlan = Property(compilation, "Plan");
      AssertProductionObject(compiledPlan);
      Assert.Equal(identity.PackageId, StringProperty(compiledPlan, "RulePackageId"));
      Assert.Equal(
        identity.PackageVersion,
        StringProperty(compiledPlan, "RulePackageVersion"));
      Assert.Equal(
        identity.PackageSha256,
        StringProperty(compiledPlan, "RulePackageSha256"));

      string contextJson = ToJson(
        "BIMBaoGui.Stage01.Context.HBRFileContextCanonicalizer",
        baselineContext);
      string planJson = ToJson(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanCanonicalizer",
        baselinePlan);
      AssertOrderedIdentity(contextJson);
      AssertOrderedIdentity(planJson);

      object restoredContext = AssertTryParseSuccess(
        "BIMBaoGui.Stage01.Context.HBRFileContextCanonicalizer",
        contextJson);
      AssertIdentity(restoredContext, identity);
      object restoredPlan = AssertTryParseSuccess(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanCanonicalizer",
        planJson);
      AssertIdentity(restoredPlan, identity);

      AssertStrictCurrentFileContextParsing(contextJson);
      AssertStrictCurrentTaskPlanParsing(planJson);
      AssertCurrentGooIdentityAndHashValidation(
        baselineContext,
        baselinePlan,
        identity);

      Assert.Same(assembly, baselineContext.GetType().Assembly);
      Assert.Same(assembly, baselinePlan.GetType().Assembly);
    }

    public static void AssertValidateContextRejectsAllIdentityMismatches()
    {
      RuleIdentity identity = CurrentIdentity();
      string[] idBlockers = ValidateContext(BuildFileContext(
        "different-package",
        identity.PackageVersion,
        identity.PackageSha256));
      Assert.Contains(idBlockers, message => message.Contains("规则包 ID 不匹配"));

      string[] versionBlockers = ValidateContext(BuildFileContext(
        identity.PackageId,
        "different-version",
        identity.PackageSha256));
      Assert.Contains(
        versionBlockers,
        message => message.Contains("规则包版本不匹配"));

      string[] shaBlockers = ValidateContext(BuildFileContext(
        identity.PackageId,
        identity.PackageVersion,
        new string('0', 64)));
      Assert.Contains(
        shaBlockers,
        message => message.Contains("规则包 SHA-256 不匹配"));
      Assert.DoesNotContain(
        shaBlockers,
        message => message.Contains("文件上下文哈希无效"));
    }

    public static void AssertValidLegacyGooRequiresRerun(
      string fileJson,
      string taskJson)
    {
      RuleIdentity identity = CurrentIdentity();
      AssertLegacyCanonicalizer(
        "BIMBaoGui.Stage01.Context.HBRFileContextCanonicalizer",
        fileJson,
        "规则数据库已升级，请重新运行 Stage01");
      AssertLegacyCanonicalizer(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanCanonicalizer",
        taskJson,
        "规则数据库已升级，请重新运行任务规划");
      AssertValidLegacyGooLifecycle(
        "BIMBaoGui.Stage01.GrasshopperTypes.HBRFileContextGoo",
        "BIMBaoGui.Stage01.Context.HBRFileContextCanonicalizer",
        "HBR.FileContext.Json",
        fileJson,
        "规则数据库已升级，请重新运行 Stage01",
        BuildFileContext(
          identity.PackageId,
          identity.PackageVersion,
          identity.PackageSha256));
      AssertValidLegacyGooLifecycle(
        "BIMBaoGui.Stage01.GrasshopperTypes.HBRTaskPlanGoo",
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanCanonicalizer",
        "HBR.TaskPlan.Json",
        taskJson,
        "规则数据库已升级，请重新运行任务规划",
        BuildTaskPlan(
          identity.PackageId,
          identity.PackageVersion,
          identity.PackageSha256));
    }

    public static void AssertInvalidLegacyGooIsRejected(
      string fileJson,
      string taskJson)
    {
      AssertLegacyCanonicalizer(
        "BIMBaoGui.Stage01.Context.HBRFileContextCanonicalizer",
        fileJson,
        "数据损坏");
      AssertLegacyCanonicalizer(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanCanonicalizer",
        taskJson,
        "数据损坏");
      AssertLegacyGoo(
        "BIMBaoGui.Stage01.GrasshopperTypes.HBRFileContextGoo",
        "HBR.FileContext.Json",
        fileJson,
        false,
        "数据损坏");
      AssertLegacyGoo(
        "BIMBaoGui.Stage01.GrasshopperTypes.HBRTaskPlanGoo",
        "HBR.TaskPlan.Json",
        taskJson,
        false,
        "数据损坏");
    }

    public static void AssertDedicatedLegacyCanonicalizerTypes()
    {
      AssertDedicatedLegacyType(
        "BIMBaoGui.Stage01.Context.HBRFileContextLegacyCanonicalizer");
      AssertDedicatedLegacyType(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanLegacyCanonicalizer");
    }

    private static Assembly ProductionAssembly => LazyProductionAssembly.Value;

    private static Assembly LoadProductionAssembly()
    {
      string path = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        ProductionAssemblyFixtureName);
      Assert.True(File.Exists(path), "生产程序集夹具不存在：" + path);
      Assembly assembly = Assembly.LoadFile(Path.GetFullPath(path));
      Assert.NotSame(typeof(HBRFileContext).Assembly, assembly);
      return assembly;
    }

    private static Type ProductionType(string name)
    {
      Type type = ProductionAssembly.GetType(name, false);
      Assert.NotNull(type);
      Assert.Same(ProductionAssembly, type.Assembly);
      return type;
    }

    private static RuleIdentity CurrentIdentity()
    {
      Type databaseType = ProductionType(
        "BIMBaoGui.Stage01.Rules.HbrRuleDatabase");
      object database = databaseType.GetProperty(
        "Current",
        BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
      AssertProductionObject(database);
      object package = Property(database, "Package");
      AssertProductionObject(package);
      return new RuleIdentity(
        StringProperty(package, "PackageId"),
        StringProperty(package, "PackageVersion"),
        StringProperty(package, "RulePackageSha256"));
    }

    private static object BuildFileContextFromFactory()
    {
      Type modelType = ProductionType(
        "BIMBaoGui.Stage01.Core.Stage01Model");
      object model = Activator.CreateInstance(modelType);
      AssertProductionObject(model);

      Type snapshotType = ProductionType(
        "BIMBaoGui.Stage01.Revit.RevitDocumentSnapshot");
      object snapshot = Activator.CreateInstance(snapshotType);
      AssertProductionObject(snapshot);
      SetProperty(snapshot, "DocumentPath", @"C:\Models\production-identity-test.rvt");
      SetProperty(snapshot, "DocumentTitle", "production-identity-test.rvt");
      SetProperty(snapshot, "RevitVersion", "2020");

      Type factoryType = ProductionType(
        "BIMBaoGui.Stage01.Context.HBRFileContextFactory");
      object context = factoryType.GetMethod(
        "Create",
        BindingFlags.Public | BindingFlags.Static).Invoke(
          null,
          new[] { model, snapshot, (object)true });
      AssertProductionObject(context);
      return context;
    }

    private static object BuildFileContext(
      string packageId,
      string packageVersion,
      string packageSha256)
    {
      Type spatialType = ProductionType(
        "BIMBaoGui.Stage01.Context.HBRSpatialReference");
      object spatial = Activator.CreateInstance(spatialType, new object[]
      {
        "CGCS2000",
        "1985国家高程基准",
        0m,
        0m,
        0m,
        0m,
        "m",
        "m²",
        "°"
      });
      AssertProductionObject(spatial);

      Type contextType = ProductionType(
        "BIMBaoGui.Stage01.Context.HBRFileContext");
      ConstructorInfo constructor = contextType.GetConstructors()
        .Single(candidate => candidate.GetParameters()
          .Any(parameter => parameter.Name == "rulePackageSha256"));
      object provisional = constructor.Invoke(new object[]
      {
        "0.9.0",
        "0.9.0",
        "production-file-guid",
        "production-document-fingerprint",
        "production.rvt",
        "P-001",
        "生产程序集项目",
        "S-01",
        "生产程序集子项",
        "总平模型",
        "报规模型",
        spatial,
        null,
        new Dictionary<string, bool>(StringComparer.Ordinal),
        Array.Empty<string>(),
        Array.Empty<string>(),
        true,
        true,
        string.Empty,
        packageId,
        packageVersion,
        packageSha256,
        "production-source-hash",
        string.Empty
      });
      AssertProductionObject(provisional);

      Type canonicalizerType = ProductionType(
        "BIMBaoGui.Stage01.Context.HBRFileContextCanonicalizer");
      string hash = (string)canonicalizerType.GetMethod(
        "ComputeHash",
        BindingFlags.Public | BindingFlags.Static).Invoke(
          null,
          new[] { provisional });
      object context = contextType.GetMethod(
        "WithHash",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
          provisional,
          new object[] { hash });
      AssertProductionObject(context);
      return context;
    }

    private static object BuildTaskPlan(
      string packageId,
      string packageVersion,
      string packageSha256)
    {
      Type itemType = ProductionType(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanItem");
      Type requirementType = ProductionType(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskRequirement");
      object required = Enum.Parse(requirementType, "Required");
      object item = Activator.CreateInstance(itemType, new object[]
      {
        "SITE.BASE",
        "生产程序集任务",
        "SITE",
        required,
        string.Empty,
        1,
        false,
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>()
      });
      AssertProductionObject(item);
      Array active = Array.CreateInstance(itemType, 1);
      active.SetValue(item, 0);
      Array notApplicable = Array.CreateInstance(itemType, 0);

      Type planType = ProductionType(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlan");
      ConstructorInfo constructor = planType.GetConstructors()
        .Single(candidate => candidate.GetParameters()
          .Any(parameter => parameter.Name == "rulePackageSha256"));
      object provisional = constructor.Invoke(new object[]
      {
        "0.5.0",
        "fixed-production-context-hash",
        packageId,
        packageVersion,
        packageSha256,
        "总平",
        "总平",
        active,
        notApplicable,
        string.Empty
      });
      AssertProductionObject(provisional);

      Type canonicalizerType = ProductionType(
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanCanonicalizer");
      string hash = (string)canonicalizerType.GetMethod(
        "ComputeHash",
        BindingFlags.Public | BindingFlags.Static).Invoke(
          null,
          new[] { provisional });
      object plan = planType.GetMethod(
        "WithHash",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
          provisional,
          new object[] { hash });
      AssertProductionObject(plan);
      return plan;
    }

    private static string ToJson(string canonicalizerTypeName, object value)
    {
      AssertProductionObject(value);
      Type canonicalizerType = ProductionType(canonicalizerTypeName);
      return (string)canonicalizerType.GetMethod(
        "ToJson",
        BindingFlags.Public | BindingFlags.Static).Invoke(
          null,
          new[] { value });
    }

    private static object AssertTryParseSuccess(
      string canonicalizerTypeName,
      string json)
    {
      MethodInfo tryParse = ProductionType(canonicalizerTypeName).GetMethod(
        "TryParse",
        BindingFlags.Public | BindingFlags.Static);
      object[] arguments = { json, null, null };
      bool success = (bool)tryParse.Invoke(null, arguments);
      Assert.True(success, Convert.ToString(arguments[2]));
      AssertProductionObject(arguments[1]);
      Assert.Equal(string.Empty, Convert.ToString(arguments[2]));
      return arguments[1];
    }

    private static string AssertTryParseFailure(
      string canonicalizerTypeName,
      string json)
    {
      MethodInfo tryParse = ProductionType(canonicalizerTypeName).GetMethod(
        "TryParse",
        BindingFlags.Public | BindingFlags.Static);
      object[] arguments = { json, null, null };
      bool success = (bool)tryParse.Invoke(null, arguments);
      Assert.False(success);
      Assert.Null(arguments[1]);
      return Convert.ToString(arguments[2]) ?? string.Empty;
    }

    private static void AssertIdentity(object value, RuleIdentity identity)
    {
      AssertProductionObject(value);
      Assert.Equal(identity.PackageId, StringProperty(value, "RulePackageId"));
      Assert.Equal(
        identity.PackageVersion,
        StringProperty(value, "RulePackageVersion"));
      Assert.Equal(
        identity.PackageSha256,
        StringProperty(value, "RulePackageSha256"));
    }

    private static void AssertOrderedIdentity(string json)
    {
      int packageId = json.IndexOf(
        "\"rulePackageId\"",
        StringComparison.Ordinal);
      int packageVersion = json.IndexOf(
        "\"rulePackageVersion\"",
        StringComparison.Ordinal);
      int packageSha256 = json.IndexOf(
        "\"rulePackageSha256\"",
        StringComparison.Ordinal);
      Assert.True(packageId >= 0);
      Assert.True(packageVersion > packageId);
      Assert.True(packageSha256 > packageVersion);
    }

    private static void AssertStrictCurrentFileContextParsing(string json)
    {
      const string canonicalizer =
        "BIMBaoGui.Stage01.Context.HBRFileContextCanonicalizer";
      var serializer = new JavaScriptSerializer();
      Dictionary<string, object> root =
        serializer.Deserialize<Dictionary<string, object>>(json);

      var missingHash = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal);
      missingHash.Remove("fileContextHash");
      Assert.Contains(
        "缺少哈希",
        AssertTryParseFailure(canonicalizer, serializer.Serialize(missingHash)));

      var missingAllIdentity = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal);
      missingAllIdentity.Remove("rulePackageId");
      missingAllIdentity.Remove("rulePackageVersion");
      missingAllIdentity.Remove("rulePackageSha256");
      Assert.Contains(
        "数据损坏",
        AssertTryParseFailure(
          canonicalizer,
          serializer.Serialize(missingAllIdentity)));

      var missingPartIdentity = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal);
      missingPartIdentity.Remove("rulePackageVersion");
      Assert.Contains(
        "缺少完整规则数据库身份",
        AssertTryParseFailure(
          canonicalizer,
          serializer.Serialize(missingPartIdentity)));

      var emptyIdentity = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal)
      {
        ["rulePackageId"] = string.Empty
      };
      Assert.Contains(
        "缺少完整规则数据库身份",
        AssertTryParseFailure(canonicalizer, serializer.Serialize(emptyIdentity)));

      var nullIdentity = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal)
      {
        ["rulePackageSha256"] = null
      };
      Assert.Contains(
        "缺少完整规则数据库身份",
        AssertTryParseFailure(canonicalizer, serializer.Serialize(nullIdentity)));
      Assert.Contains("数据损坏", AssertTryParseFailure(canonicalizer, "{}"));
      Assert.Contains("数据损坏", AssertTryParseFailure(canonicalizer, "null"));
    }

    private static void AssertStrictCurrentTaskPlanParsing(string json)
    {
      const string canonicalizer =
        "BIMBaoGui.Stage01.TaskPlanning.HBRTaskPlanCanonicalizer";
      var serializer = new JavaScriptSerializer();
      Dictionary<string, object> root =
        serializer.Deserialize<Dictionary<string, object>>(json);

      var missingHash = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal);
      missingHash.Remove("taskPlanHash");
      Assert.Contains(
        "缺少哈希",
        AssertTryParseFailure(canonicalizer, serializer.Serialize(missingHash)));

      var missingAllIdentity = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal);
      missingAllIdentity.Remove("rulePackageId");
      missingAllIdentity.Remove("rulePackageVersion");
      missingAllIdentity.Remove("rulePackageSha256");
      Assert.Contains(
        "数据损坏",
        AssertTryParseFailure(
          canonicalizer,
          serializer.Serialize(missingAllIdentity)));

      var missingPartIdentity = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal);
      missingPartIdentity.Remove("rulePackageSha256");
      Assert.Contains(
        "缺少完整规则数据库身份",
        AssertTryParseFailure(
          canonicalizer,
          serializer.Serialize(missingPartIdentity)));

      var emptyIdentity = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal)
      {
        ["rulePackageVersion"] = string.Empty
      };
      Assert.Contains(
        "缺少完整规则数据库身份",
        AssertTryParseFailure(canonicalizer, serializer.Serialize(emptyIdentity)));

      var nullIdentity = new Dictionary<string, object>(
        root,
        StringComparer.Ordinal)
      {
        ["rulePackageId"] = null
      };
      Assert.Contains(
        "缺少完整规则数据库身份",
        AssertTryParseFailure(canonicalizer, serializer.Serialize(nullIdentity)));
      Assert.Contains("数据损坏", AssertTryParseFailure(canonicalizer, "{}"));
      Assert.Contains("数据损坏", AssertTryParseFailure(canonicalizer, "null"));
    }

    private static void AssertCurrentGooIdentityAndHashValidation(
      object baselineContext,
      object baselinePlan,
      RuleIdentity identity)
    {
      AssertInvalidCurrentGoo(
        "BIMBaoGui.Stage01.GrasshopperTypes.HBRFileContextGoo",
        BuildFileContext(
          "different-package",
          identity.PackageVersion,
          identity.PackageSha256),
        "重新运行 Stage01");
      AssertInvalidCurrentGoo(
        "BIMBaoGui.Stage01.GrasshopperTypes.HBRTaskPlanGoo",
        BuildTaskPlan(
          identity.PackageId,
          "different-version",
          identity.PackageSha256),
        "重新运行任务规划");
      AssertInvalidCurrentGoo(
        "BIMBaoGui.Stage01.GrasshopperTypes.HBRFileContextGoo",
        WithHash(baselineContext, "invalid-file-context-hash"),
        "哈希无效");
      AssertInvalidCurrentGoo(
        "BIMBaoGui.Stage01.GrasshopperTypes.HBRTaskPlanGoo",
        WithHash(baselinePlan, "invalid-task-plan-hash"),
        "哈希无效");
    }

    private static object WithHash(object value, string hash)
    {
      AssertProductionObject(value);
      object result = value.GetType().GetMethod(
        "WithHash",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
          value,
          new object[] { hash });
      AssertProductionObject(result);
      return result;
    }

    private static void AssertInvalidCurrentGoo(
      string gooTypeName,
      object value,
      string expectedReason)
    {
      object goo = CreateGooWithValue(gooTypeName, value);
      Assert.False(BooleanProperty(goo, "IsValid"));
      Assert.Contains(expectedReason, StringProperty(goo, "IsValidWhyNot"));
    }

    private static object CreateGooWithValue(string gooTypeName, object value)
    {
      AssertProductionObject(value);
      Type gooType = ProductionType(gooTypeName);
      ConstructorInfo constructor = gooType.GetConstructors()
        .Single(candidate =>
        {
          ParameterInfo[] parameters = candidate.GetParameters();
          return parameters.Length == 1
            && parameters[0].ParameterType == value.GetType();
        });
      object goo = constructor.Invoke(new[] { value });
      AssertProductionObject(goo);
      return goo;
    }

    private static string[] ValidateContext(object context)
    {
      AssertProductionObject(context);
      Type compilerType = ProductionType(
        "BIMBaoGui.Stage01.TaskPlanning.TaskPlanCompiler");
      object result = compilerType.GetMethod(
        "ValidateContext",
        BindingFlags.Public | BindingFlags.Static).Invoke(
          null,
          new[] { context });
      return ((IEnumerable)result)
        .Cast<object>()
        .Select(Convert.ToString)
        .ToArray();
    }

    private static void AssertLegacyCanonicalizer(
      string typeName,
      string json,
      string expectedError)
    {
      Type canonicalizerType = ProductionType(typeName);
      MethodInfo tryParse = canonicalizerType.GetMethod(
        "TryParse",
        BindingFlags.Public | BindingFlags.Static);
      object[] arguments = { json, null, null };
      bool success = (bool)tryParse.Invoke(null, arguments);
      Assert.False(success);
      Assert.Null(arguments[1]);
      Assert.Contains(expectedError, (string)arguments[2]);
    }

    private static void AssertLegacyGoo(
      string typeName,
      string storageKey,
      string json,
      bool expectedRead,
      string expectedReason)
    {
      Type gooType = ProductionType(typeName);
      object goo = Activator.CreateInstance(gooType);
      AssertProductionObject(goo);
      var chunk = new GH_LooseChunk("production-legacy");
      chunk.SetString(storageKey, json);
      bool read = (bool)gooType.GetMethod(
        "Read",
        BindingFlags.Public | BindingFlags.Instance).Invoke(
          goo,
          new object[] { chunk });
      Assert.Equal(expectedRead, read);
      Assert.False(BooleanProperty(goo, "IsValid"));
      Assert.Null(Property(goo, "Value"));
      Assert.Contains(expectedReason, StringProperty(goo, "IsValidWhyNot"));
    }

    private static void AssertValidLegacyGooLifecycle(
      string gooTypeName,
      string canonicalizerTypeName,
      string storageKey,
      string legacyJson,
      string expectedReason,
      object currentValue)
    {
      AssertProductionObject(currentValue);
      Type gooType = ProductionType(gooTypeName);
      object goo = Activator.CreateInstance(gooType);
      AssertProductionObject(goo);

      var legacyChunk = new GH_LooseChunk("production-legacy");
      legacyChunk.SetString(storageKey, legacyJson);
      Assert.True((bool)gooType.GetMethod(
        "Read",
        BindingFlags.Public | BindingFlags.Instance).Invoke(
          goo,
          new object[] { legacyChunk }));
      Assert.False(BooleanProperty(goo, "IsValid"));
      Assert.Null(Property(goo, "Value"));
      Assert.Contains(expectedReason, StringProperty(goo, "IsValidWhyNot"));

      var rewrittenChunk = new GH_LooseChunk("production-rewritten");
      Assert.True((bool)gooType.GetMethod(
        "Write",
        BindingFlags.Public | BindingFlags.Instance).Invoke(
          goo,
          new object[] { rewrittenChunk }));
      string rewrittenJson = rewrittenChunk.GetString(storageKey);
      Assert.Equal(legacyJson, rewrittenJson);
      Assert.DoesNotContain("rulePackageId", rewrittenJson);

      object reloadedGoo = Activator.CreateInstance(gooType);
      AssertProductionObject(reloadedGoo);
      Assert.True((bool)gooType.GetMethod(
        "Read",
        BindingFlags.Public | BindingFlags.Instance).Invoke(
          reloadedGoo,
          new object[] { rewrittenChunk }));
      Assert.False(BooleanProperty(reloadedGoo, "IsValid"));
      Assert.Null(Property(reloadedGoo, "Value"));
      Assert.Contains(
        expectedReason,
        StringProperty(reloadedGoo, "IsValidWhyNot"));

      object castGoo = Activator.CreateInstance(gooType);
      AssertProductionObject(castGoo);
      Assert.True((bool)gooType.GetMethod(
        "CastFrom",
        BindingFlags.Public | BindingFlags.Instance).Invoke(
          castGoo,
          new object[] { legacyJson }));
      Assert.False(BooleanProperty(castGoo, "IsValid"));
      Assert.Null(Property(castGoo, "Value"));
      Assert.Contains(expectedReason, StringProperty(castGoo, "IsValidWhyNot"));

      object duplicate = gooType.GetMethod(
        "Duplicate",
        BindingFlags.Public | BindingFlags.Instance).Invoke(
          castGoo,
          null);
      AssertProductionObject(duplicate);
      Assert.False(BooleanProperty(duplicate, "IsValid"));
      Assert.Null(Property(duplicate, "Value"));
      Assert.Equal(
        StringProperty(castGoo, "IsValidWhyNot"),
        StringProperty(duplicate, "IsValidWhyNot"));

      var currentChunk = new GH_LooseChunk("production-current");
      currentChunk.SetString(
        storageKey,
        ToJson(canonicalizerTypeName, currentValue));
      Assert.True((bool)gooType.GetMethod(
        "Read",
        BindingFlags.Public | BindingFlags.Instance).Invoke(
          goo,
          new object[] { currentChunk }));
      Assert.True(BooleanProperty(goo, "IsValid"));
      Assert.Equal(string.Empty, StringProperty(goo, "IsValidWhyNot"));
      AssertProductionObject(Property(goo, "Value"));
    }

    private static void AssertDedicatedLegacyType(string name)
    {
      Type type = ProductionAssembly.GetType(name, false);
      Assert.True(
        type != null,
        "生产程序集缺少独立 legacy canonicalizer：" + name);
      Assert.Same(ProductionAssembly, type.Assembly);
      MethodInfo computeHash = type.GetMethod(
        "ComputeHash",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
      Assert.NotNull(computeHash);
      Assert.Same(type, computeHash.DeclaringType);
    }

    private static void AssertProductionObject(object value)
    {
      Assert.NotNull(value);
      Assert.Same(ProductionAssembly, value.GetType().Assembly);
    }

    private static object Property(object value, string name)
    {
      Assert.NotNull(value);
      PropertyInfo property = value.GetType().GetProperty(
        name,
        BindingFlags.Public | BindingFlags.Instance);
      Assert.NotNull(property);
      return property.GetValue(value, null);
    }

    private static void SetProperty(object value, string name, object propertyValue)
    {
      AssertProductionObject(value);
      PropertyInfo property = value.GetType().GetProperty(
        name,
        BindingFlags.Public | BindingFlags.Instance);
      Assert.NotNull(property);
      Assert.True(property.CanWrite);
      property.SetValue(value, propertyValue, null);
    }

    private static string StringProperty(object value, string name)
    {
      return (string)Property(value, name);
    }

    private static bool BooleanProperty(object value, string name)
    {
      return (bool)Property(value, name);
    }

    private sealed class RuleIdentity
    {
      public RuleIdentity(
        string packageId,
        string packageVersion,
        string packageSha256)
      {
        PackageId = packageId;
        PackageVersion = packageVersion;
        PackageSha256 = packageSha256;
      }

      public string PackageId { get; }
      public string PackageVersion { get; }
      public string PackageSha256 { get; }
    }
  }
}
