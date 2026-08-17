using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;

namespace BIMBaoGui.RevitAddin.Acceptance
{
  internal static class NativeOfficialCarrierProbeService
  {
    private const string SeedFileName = "official-carrier-probe-seed.json";

    internal static bool CanRegisterAtStartup()
    {
      try
      {
        string contextPath = Environment.GetEnvironmentVariable(
          NativeOfficialCarrierProbePolicy.ContextEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(contextPath)) return false;
        NativeOfficialCarrierProbeContext context = LoadContext(contextPath);
        string probeSha = NativeOfficialCarrierProbePolicy.ComputeSha256(
          context.ProbeCopyPath);
        return NativeOfficialCarrierProbePolicy.Authorize(
          contextPath,
          context.ProbeCopyPath,
          probeSha,
          context).Authorized;
      }
      catch
      {
        return false;
      }
    }

    internal static string Execute(UIApplication application)
    {
      if (application == null)
        throw new ArgumentNullException(nameof(application));
      Document document = application.ActiveUIDocument?.Document
        ?? throw new InvalidOperationException("PROBE_ACTIVE_DOCUMENT_MISSING");
      string contextPath = Environment.GetEnvironmentVariable(
        NativeOfficialCarrierProbePolicy.ContextEnvironmentVariable)
        ?? string.Empty;
      NativeOfficialCarrierProbeContext context = LoadContext(contextPath);
      string activeDocumentPreSeedSha256 =
        NativeOfficialCarrierProbePolicy.ComputeSha256(document.PathName);

      NativeOfficialCarrierProbeAuthorization authorization =
        NativeOfficialCarrierProbePolicy.Authorize(
          contextPath,
          document.PathName,
          activeDocumentPreSeedSha256,
          context);
      if (!authorization.Authorized)
        throw new InvalidOperationException(authorization.ErrorCode);
      if (document.IsReadOnly || document.IsFamilyDocument)
        throw new InvalidOperationException("PROBE_DOCUMENT_NOT_WRITABLE");

      IReadOnlyList<NativeStage02BMetricDefinition> metrics =
        NativeStage02BMetricCatalog.Current.MetricsFor("总平模型");
      IReadOnlyList<NativeOfficialCarrierProbeSentinel> sentinels =
        NativeOfficialCarrierProbePolicy.BuildSentinels(context, metrics);
      NativeOfficialCarrierProbeAuthorization parameterValidation =
        NativeOfficialCarrierProbePolicy.ValidateExistingSourceParameters(
          sentinels,
          ReadExistingSourceParameters(document, sentinels));
      if (!parameterValidation.Authorized)
      {
        if (parameterValidation.ErrorCode
          == "OFFICIAL_SOURCE_NAME_AMBIGUOUS")
          throw new InvalidOperationException("OFFICIAL_SOURCE_NAME_AMBIGUOUS");
        throw new InvalidOperationException(
          "OFFICIAL_SOURCE_NAME_CONTRACT_MISMATCH");
      }

      var seedItems = new List<NativeOfficialCarrierProbeSeedItem>();
      using (var group = new TransactionGroup(
        document,
        "HBR Official Carrier Probe"))
      {
        group.Start();
        try
        {
          using (var transaction = new Transaction(
            document,
            "HBR Official Carrier Probe Seed"))
          {
            transaction.Start();
            foreach (IGrouping<string, NativeOfficialCarrierProbeSentinel>
              metricGroup in sentinels.GroupBy(
                value => value.PropertyId,
                StringComparer.Ordinal))
            {
              NativeOfficialCarrierProbeSentinel definition =
                metricGroup.First();
              NativeStage02BMetricDefinition metric = metrics.Single(value =>
                string.Equals(
                  value.PropertyId,
                  definition.PropertyId,
                  StringComparison.Ordinal));
              NativeStage02PropertyDefinition sourceProperty =
                CreateSourceProperty(metric.Property, definition.ExactSourceName);
              string[] categories = metricGroup
                .Select(value => value.CategoryBuiltInId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
              NativeStage02ParameterBindingService.Ensure(
                document,
                sourceProperty,
                categories);
              foreach (NativeOfficialCarrierProbeSentinel sentinel in
                metricGroup.OrderBy(
                  value => value.CandidateIndex))
              {
                Element candidate = ResolveCandidate(document, sentinel);
                Parameter parameter = candidate.get_Parameter(
                  sentinel.ParameterGuid);
                NativeStage02ValueCodec.WriteAndVerify(
                  parameter,
                  sourceProperty,
                  sentinel.CanonicalValue);
                string readback = NativeStage02ValueCodec.Read(
                  parameter,
                  sourceProperty);
                if (!string.Equals(
                  readback,
                  sentinel.CanonicalValue,
                  StringComparison.Ordinal))
                  throw new InvalidOperationException(
                    "PROBE_SENTINEL_READBACK_MISMATCH");
                seedItems.Add(CreateSeedItem(sentinel, readback));
              }
            }
            document.Regenerate();
            transaction.Commit();
          }
          group.Assimilate();
        }
        catch
        {
          group.RollBack();
          throw;
        }
      }

      // Authorization proves PathName is the probe copy and never the source.
      document.Save();
      string manifestPath = Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(contextPath)),
        SeedFileName);
      WriteSeedManifest(
        manifestPath,
        contextPath,
        context,
        seedItems);
      return manifestPath;
    }

    internal static NativeOfficialCarrierProbeContext LoadContext(
      string contextPath)
    {
      if (string.IsNullOrWhiteSpace(contextPath)
        || !File.Exists(contextPath))
        throw new InvalidDataException("PROBE_CONTEXT_NOT_FOUND");
      object raw = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 128
      }.DeserializeObject(File.ReadAllText(contextPath, Encoding.UTF8));
      IDictionary<string, object> root = Map(raw, "PROBE_CONTEXT_INVALID");
      return new NativeOfficialCarrierProbeContext
      {
        SchemaVersion = Text(root, "schemaVersion"),
        SourceGoldenRvtPath = Text(root, "sourceGoldenRvtPath"),
        SourceGoldenRvtSha256 = Text(root, "sourceGoldenRvtSha256"),
        ProbeCopyPath = Text(root, "probeCopyPath"),
        ProbeCopyPreSeedSha256 = Text(root, "probeCopyPreSeedSha256"),
        AcceptanceRoot = Text(root, "acceptanceRoot"),
        AcceptanceRunId = Text(root, "acceptanceRunId"),
        Nonce = Text(root, "nonce"),
        CommitSha = Text(root, "commitSha"),
        RulePackageSha256 = Text(root, "rulePackageSha256"),
        Metrics = new ReadOnlyCollection<NativeOfficialCarrierProbeMetric>(
          Values(root, "metrics").Select(value =>
          {
            IDictionary<string, object> item = Map(
              value,
              "PROBE_METRIC_CONTEXT_INVALID");
            return new NativeOfficialCarrierProbeMetric
            {
              PropertyId = Text(item, "propertyId"),
              Sequence = Integer(item, "sequence"),
              IfcEntity = Text(item, "ifcEntity"),
              IfcPropertySet = Text(item, "ifcPropertySet"),
              IfcProperty = Text(item, "ifcProperty"),
              ExactOfficialSourceName = Text(
                item,
                "exactOfficialSourceName"),
              DeclaredIfcType = Text(item, "declaredIfcType"),
              CanonicalUnit = Text(item, "canonicalUnit"),
              StorageType = Text(item, "storageType")
            };
          }).ToArray()),
        Candidates = new ReadOnlyCollection<NativeOfficialCarrierProbeCandidate>(
          Values(root, "candidates").Select(value =>
          {
            IDictionary<string, object> item = Map(
              value,
              "PROBE_CANDIDATE_INVALID");
            return new NativeOfficialCarrierProbeCandidate
            {
              PropertyId = Text(item, "propertyId"),
              UniqueId = Text(item, "uniqueId"),
              CategoryBuiltInId = Text(item, "categoryBuiltInId"),
              ElementClass = Text(item, "elementClass")
            };
          }).ToArray())
      };
    }

    private static IReadOnlyList<NativeOfficialCarrierProbeExistingParameter>
      ReadExistingSourceParameters(
        Document document,
        IReadOnlyList<NativeOfficialCarrierProbeSentinel> sentinels)
    {
      var names = new HashSet<string>(sentinels
        .Where(value => value != null)
        .Select(value => value.ExactSourceName), StringComparer.Ordinal);
      var result = new List<NativeOfficialCarrierProbeExistingParameter>();
      DefinitionBindingMapIterator iterator =
        document.ParameterBindings.ForwardIterator();
      iterator.Reset();
      while (iterator.MoveNext())
      {
        InternalDefinition definition = iterator.Key as InternalDefinition;
        if (definition == null || !names.Contains(definition.Name)) continue;
        SharedParameterElement shared = document.GetElement(definition.Id)
          as SharedParameterElement;
        result.Add(new NativeOfficialCarrierProbeExistingParameter
        {
          ExactSourceName = definition.Name,
          ParameterGuid = shared?.GuidValue ?? Guid.Empty,
          StorageType = StorageTypeFor(definition.ParameterType)
        });
      }
      return result;
    }

    private static string StorageTypeFor(ParameterType parameterType)
    {
      switch (parameterType)
      {
        case ParameterType.Text: return "String";
        case ParameterType.Integer:
        case ParameterType.YesNo: return "Integer";
        default: return "Double";
      }
    }

    private static NativeStage02PropertyDefinition CreateSourceProperty(
      NativeStage02PropertyDefinition source,
      string exactSourceName)
    {
      return new NativeStage02PropertyDefinition
      {
        PropertyId = source.PropertyId,
        ContractKind = source.ContractKind,
        IfcEntity = source.IfcEntity,
        IfcPropertySet = source.IfcPropertySet,
        IfcProperty = source.IfcProperty,
        DeclaredIfcType = source.DeclaredIfcType,
        CanonicalUnit = source.CanonicalUnit,
        ParameterGuid = source.ParameterGuid,
        ParameterName = exactSourceName,
        BindingScope = "INSTANCE",
        StorageType = source.StorageType,
        ParameterType = source.ParameterType,
        Visible = true,
        UserModifiable = true
      };
    }

    private static Element ResolveCandidate(
      Document document,
      NativeOfficialCarrierProbeSentinel sentinel)
    {
      Element element = string.Equals(
        sentinel.CandidateUniqueId,
        NativeOfficialCarrierProbeCandidate.ProjectInformationToken,
        StringComparison.Ordinal)
          ? (Element)document.ProjectInformation
          : document.GetElement(sentinel.CandidateUniqueId);
      if (element == null)
        throw new InvalidOperationException("PROBE_CANDIDATE_NOT_FOUND");
      if (!Enum.TryParse(
        sentinel.CategoryBuiltInId,
        false,
        out BuiltInCategory category)
        || element.Category == null
        || element.Category.Id.IntegerValue != (int)category)
        throw new InvalidOperationException("PROBE_CANDIDATE_CATEGORY_MISMATCH");
      if (!string.Equals(
        element.GetType().FullName,
        sentinel.ElementClass,
        StringComparison.Ordinal))
        throw new InvalidOperationException("PROBE_CANDIDATE_CLASS_MISMATCH");
      return element;
    }

    private static NativeOfficialCarrierProbeSeedItem CreateSeedItem(
      NativeOfficialCarrierProbeSentinel sentinel,
      string readback)
    {
      return new NativeOfficialCarrierProbeSeedItem
      {
        PropertyId = sentinel.PropertyId,
        IfcEntity = sentinel.IfcEntity,
        IfcPropertySet = sentinel.IfcPropertySet,
        IfcProperty = sentinel.IfcProperty,
        ExactSourceName = sentinel.ExactSourceName,
        DeclaredIfcType = sentinel.DeclaredIfcType,
        CanonicalUnit = sentinel.CanonicalUnit,
        CandidateUniqueId = sentinel.CandidateUniqueId,
        CategoryBuiltInId = sentinel.CategoryBuiltInId,
        ElementClass = sentinel.ElementClass,
        ParameterGuid = sentinel.ParameterGuid.ToString("D"),
        Sentinel = sentinel.CanonicalValue,
        Readback = readback ?? string.Empty
      };
    }

    private static void WriteSeedManifest(
      string path,
      string contextPath,
      NativeOfficialCarrierProbeContext context,
      IReadOnlyList<NativeOfficialCarrierProbeSeedItem> items)
    {
      if (File.Exists(path))
        throw new IOException("PROBE_SEED_MANIFEST_EXISTS");
      var payload = new
      {
        schemaVersion = "HBR_OFFICIAL_CARRIER_PROBE_SEED_V1",
        contextPath = Path.GetFullPath(contextPath),
        contextSha256 = NativeOfficialCarrierProbePolicy.ComputeSha256(
          contextPath),
        sourceGoldenRvtSha256 = context.SourceGoldenRvtSha256,
        probeRvtPath = Path.GetFullPath(context.ProbeCopyPath),
        probeRvtSha256 = NativeOfficialCarrierProbePolicy.ComputeSha256(
          context.ProbeCopyPath),
        commitSha = context.CommitSha,
        rulePackageSha256 = context.RulePackageSha256,
        items = items.OrderBy(value => value.PropertyId, StringComparer.Ordinal)
          .ThenBy(value => value.CandidateUniqueId, StringComparer.Ordinal)
          .Select(value => new
          {
            propertyId = value.PropertyId,
            ifcEntity = value.IfcEntity,
            ifcPropertySet = value.IfcPropertySet,
            ifcProperty = value.IfcProperty,
            exactSourceName = value.ExactSourceName,
            declaredIfcType = value.DeclaredIfcType,
            canonicalUnit = value.CanonicalUnit,
            candidateUniqueId = value.CandidateUniqueId,
            candidateCategoryBuiltInId = value.CategoryBuiltInId,
            candidateElementClass = value.ElementClass,
            parameterGuid = value.ParameterGuid,
            sentinel = value.Sentinel,
            readback = value.Readback
          }).ToArray()
      };
      string json = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 128
      }.Serialize(payload);
      using (var stream = new FileStream(
        path,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None))
      using (var writer = new StreamWriter(
        stream,
        new UTF8Encoding(false)))
        writer.Write(json);
    }

    private static IDictionary<string, object> Map(
      object value,
      string errorCode)
    {
      IDictionary<string, object> result = value
        as IDictionary<string, object>;
      if (result == null) throw new InvalidDataException(errorCode);
      return result;
    }

    private static string Text(
      IDictionary<string, object> value,
      string key)
    {
      return value.TryGetValue(key, out object item)
        ? Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty
        : string.Empty;
    }

    private static int Integer(
      IDictionary<string, object> value,
      string key)
    {
      return value.TryGetValue(key, out object item)
        ? Convert.ToInt32(item, CultureInfo.InvariantCulture)
        : 0;
    }

    private static IEnumerable<object> Values(
      IDictionary<string, object> value,
      string key)
    {
      if (!value.TryGetValue(key, out object item) || !(item is IEnumerable list))
        return Array.Empty<object>();
      return list.Cast<object>();
    }
  }
}
