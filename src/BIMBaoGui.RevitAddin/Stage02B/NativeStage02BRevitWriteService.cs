using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Workflow;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal static class NativeStage02BRevitWriteService
  {
    internal static NativeStage02BWriteResult Execute(
      UIApplication application,
      NativeStage02BWriteRequest request)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      NativeStage02BRevitContext context = NativeStage02BRevitContext.Create(
        application);
      Document document = context.Document;
      string runId = string.IsNullOrWhiteSpace(request.RunId)
        ? Guid.NewGuid().ToString("N") : request.RunId.Trim();
      NativeStage02BMetricDefinition[] catalog = NativeStage02BMetricCatalog
        .Current.MetricsFor(context.Identity.ModelFileType).ToArray();
      var metricById = catalog.ToDictionary(
        value => value.PropertyId, value => value, StringComparer.Ordinal);
      NativeStage02BMetricInput[] inputs = (request.Metrics
          ?? Array.Empty<NativeStage02BMetricInput>())
        .Where(value => value != null).Select(value =>
          new NativeStage02BMetricInput
          {
            PropertyId = (value.PropertyId ?? string.Empty).Trim(),
            RawValue = value.RawValue ?? string.Empty
          }).ToArray();
      if (inputs.Length == 0
        || inputs.Any(value => !metricById.ContainsKey(value.PropertyId))
        || inputs.GroupBy(value => value.PropertyId, StringComparer.Ordinal)
          .Any(group => group.Count() > 1))
        throw new InvalidOperationException("STAGE02B_INPUT_SET_INVALID");

      NativeStage02SemanticAssignmentSnapshot assignments =
        ReadCurrentAssignments(document, context.Identity);
      var outcomes = new List<NativeStage02BMetricOutcome>();
      foreach (NativeStage02BMetricInput input in inputs)
      {
        NativeStage02BMetricDefinition metric = metricById[input.PropertyId];
        NativeStage02BStorageSnapshot before = NativeStage02BStorage.Read(document);
        NativeStage02BMetricRecord previous = before.Records.FirstOrDefault(
          value => string.Equals(value.PropertyId, metric.PropertyId,
            StringComparison.Ordinal));
        NativeStage02BValueDecision validation = NativeStage02BValuePolicy
          .Validate(metric, input.RawValue);
        NativeStage02BOwnerDecision owner = ResolveOwner(metric);
        string projectionStatus = ProjectionStatus(owner);
        if (!validation.Accepted || !owner.InternalSaveAllowed)
        {
          string code = validation.Accepted
            ? owner.Code : validation.Code;
          NativeStage02BMetricRecord audit = FailureRecord(
            metric, previous, context.Identity, runId,
            validation.Accepted
              ? input.RawValue.Trim() : input.RawValue.Trim(),
            projectionStatus, owner.OfficialCarrierStatus, code);
          outcomes.Add(WriteFailureAudit(document, metric, audit, code));
          continue;
        }

        Transaction transaction = new Transaction(
          document, "HBR Stage02B 指标 " + metric.PropertyId);
        try
        {
          transaction.Start();
          Element projectionCarrier = null;
          if (owner.ProjectionMode == NativeStage02BProjectionMode.ProjectInformation)
          {
            NativeStage02ParameterBindingService.Ensure(
              document, metric.Property, new[] { "OST_ProjectInformation" });
            projectionCarrier = document.ProjectInformation;
          }
          else if (owner.ProjectionMode
            == NativeStage02BProjectionMode.VerifiedElementParameter)
          {
            NativeOfficialProjectionCarrierDefinition definition =
              NativeReportingRuleCatalog.Current.GetProjectionCarrier(
                metric.OfficialProjectionCarrierId);
            NativeStage02BResolvedProjectionCarrier resolved =
              NativeStage02BProjectionCarrierResolver.Resolve(
                document, definition, assignments);
            NativeStage02ParameterBindingService.Ensure(
              document, metric.Property, new[] { definition.CategoryBuiltInId });
            projectionCarrier = resolved.Element;
          }

          if (projectionCarrier != null)
          {
            Parameter parameter = projectionCarrier.get_Parameter(
              metric.Property.ParameterGuid);
            NativeStage02ValueCodec.WriteAndVerify(
              parameter, metric.Property, validation.CanonicalValue);
          }
          NativeStage02BMetricRecord succeeded = SuccessRecord(
            metric, context.Identity, runId, validation.CanonicalValue,
            projectionStatus, owner.OfficialCarrierStatus);
          NativeStage02BStorage.WriteMetric(document, succeeded);
          document.Regenerate();
          NativeStage02BMetricRecord readback = NativeStage02BStorage
            .Read(document).Records.Single(value => string.Equals(
              value.PropertyId, metric.PropertyId, StringComparison.Ordinal));
          if (!NativeStage02BCanonicalizer.VerifyRecord(readback)
            || !string.Equals(readback.LastSuccessfulCanonicalValue,
              validation.CanonicalValue, StringComparison.Ordinal))
            throw new InvalidDataException("READBACK_FAILED");
          if (projectionCarrier != null)
          {
            string parameterReadback = NativeStage02ValueCodec.Read(
              projectionCarrier.get_Parameter(metric.Property.ParameterGuid),
              metric.Property);
            if (!string.Equals(parameterReadback,
              validation.CanonicalValue, StringComparison.Ordinal))
              throw new InvalidDataException("READBACK_FAILED");
          }
          NativeTransactionCommitPolicy.RequireCommitted(
            transaction.Commit().ToString(),
            "STAGE02B_METRIC_TRANSACTION_NOT_COMMITTED");
          outcomes.Add(new NativeStage02BMetricOutcome
          {
            PropertyId = metric.PropertyId,
            Identity = metric.Identity,
            RequestedCanonicalValue = validation.CanonicalValue,
            PersistedCanonicalValue = validation.CanonicalValue,
            Succeeded = true,
            InternalWriteSucceeded = true,
            ParameterWriteSucceeded = projectionCarrier != null,
            ReadbackSucceeded = true,
            ProjectionStatus = projectionStatus,
            OfficialCarrierStatus = owner.OfficialCarrierStatus,
            OfficialProjectionCarrierId = metric.OfficialProjectionCarrierId,
            OfficialEvidenceRef = metric.OfficialEvidenceRef,
            Record = succeeded
          });
        }
        catch (Exception exception)
        {
          if (transaction.GetStatus() == TransactionStatus.Started)
            transaction.RollBack();
          string code = StableErrorCode(exception);
          NativeStage02BMetricRecord audit = FailureRecord(
            metric, previous, context.Identity, runId,
            validation.CanonicalValue, projectionStatus,
            owner.OfficialCarrierStatus, code);
          outcomes.Add(WriteFailureAudit(document, metric, audit, code));
          continue;
        }
        finally
        {
          transaction.Dispose();
        }
      }

      NativeStage02BWriteBatchDecision batch = NativeStage02BWriteBatchPolicy
        .Merge(outcomes);
      var result = new NativeStage02BWriteResult
      {
        RunId = runId,
        MetricOutcomes = new ReadOnlyCollection<NativeStage02BMetricOutcome>(
          outcomes),
        FailedPropertyIds = batch.FailedPropertyIds,
        PartialSuccess = batch.PartialSuccess
      };
      try
      {
        NativeStage02BStorageSnapshot finalReadback =
          NativeStage02BStorage.Read(document);
        NativeWorkflowResultEnvelope envelope =
          NativeStage02BResultCanonicalizer.Build(
            runId,
            context.Identity,
            finalReadback,
            inputs.Select(value => value.PropertyId),
            outcomes,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        using (var envelopeTransaction = new Transaction(
          document, "HBR Stage02B 结果信封"))
        {
          envelopeTransaction.Start();
          try
          {
            NativeWorkflowResultStorage.Write(document, envelope);
            NativeTransactionCommitPolicy.RequireCommitted(
              envelopeTransaction.Commit().ToString(),
              "STAGE02B_ENVELOPE_TRANSACTION_NOT_COMMITTED");
            result.WorkflowResult = envelope;
          }
          catch
          {
            if (envelopeTransaction.GetStatus() == TransactionStatus.Started)
              envelopeTransaction.RollBack();
            throw;
          }
        }
      }
      catch
      {
        result.TechnicalErrorCode = "RESULT_ENVELOPE_WRITE_FAILED";
        result.WorkflowResult = null;
      }
      return result;
    }

    private static NativeStage02BMetricOutcome WriteFailureAudit(
      Document document,
      NativeStage02BMetricDefinition metric,
      NativeStage02BMetricRecord audit,
      string errorCode)
    {
      bool saved = false;
      string message = string.Empty;
      using (var auditTransaction = new Transaction(
        document, "HBR Stage02B 失败审计 " + metric.PropertyId))
      {
        try
        {
          auditTransaction.Start();
          NativeStage02BStorage.WriteMetric(document, audit);
          NativeTransactionCommitPolicy.RequireCommitted(
            auditTransaction.Commit().ToString(),
            "STAGE02B_AUDIT_TRANSACTION_NOT_COMMITTED");
          saved = true;
        }
        catch (Exception exception)
        {
          if (auditTransaction.GetStatus() == TransactionStatus.Started)
            auditTransaction.RollBack();
          message = "FAILURE_AUDIT_WRITE_FAILED: " + exception.Message;
        }
      }
      return new NativeStage02BMetricOutcome
      {
        PropertyId = metric.PropertyId,
        Identity = metric.Identity,
        RequestedCanonicalValue = audit.RequestedCanonicalValue,
        PersistedCanonicalValue = audit.LastSuccessfulCanonicalValue,
        Succeeded = false,
        InternalWriteSucceeded = saved,
        ReadbackSucceeded = false,
        ProjectionStatus = audit.ProjectionStatus,
        OfficialCarrierStatus = audit.OfficialCarrierStatus,
        OfficialProjectionCarrierId = audit.OfficialProjectionCarrierId,
        OfficialEvidenceRef = audit.OfficialEvidenceRef,
        ErrorCode = errorCode,
        Message = message,
        Record = saved ? audit : null
      };
    }

    private static NativeStage02BOwnerDecision ResolveOwner(
      NativeStage02BMetricDefinition metric)
    {
      NativeReportingRuleCatalog reporting = NativeReportingRuleCatalog.Current;
      NativeOfficialCarrierPolicy policy = reporting.GetCarrierPolicy(
        metric.Property.IfcEntity);
      NativeOfficialProjectionCarrierDefinition carrier =
        string.IsNullOrWhiteSpace(metric.OfficialProjectionCarrierId)
          ? null : reporting.GetProjectionCarrier(
            metric.OfficialProjectionCarrierId);
      NativeOfficialCarrierProbeRecord probe = string.IsNullOrWhiteSpace(
          metric.OfficialCarrierProbeRef)
        ? null : reporting.GetCarrierProbe(metric.OfficialCarrierProbeRef);
      NativeOfficialEvidenceRecord evidence = string.IsNullOrWhiteSpace(
          metric.OfficialEvidenceRef)
        ? null : reporting.GetOfficialEvidence(metric.OfficialEvidenceRef);
      return NativeStage02BOwnerPolicy.Resolve(
        metric, policy, carrier, probe, evidence);
    }

    private static NativeStage02SemanticAssignmentSnapshot ReadCurrentAssignments(
      Document document,
      NativeWorkflowIdentity identity)
    {
      NativeStage02SemanticAssignmentStorageSnapshot stored =
        NativeStage02SemanticAssignmentStorage.Read(document);
      NativeStage02SemanticAssignmentRecord[] records =
        (stored?.Payload?.Assignments
          ?? Array.Empty<NativeStage02SemanticAssignmentRecord>())
        .Where(value => value != null).ToArray();
      string[] live = records.Where(value => document.GetElement(
          value.ElementUniqueId) != null)
        .Select(value => value.ElementUniqueId).ToArray();
      NativeStage02SemanticAssignmentStorageDecision decision =
        NativeStage02SemanticAssignmentStoragePolicy.Evaluate(stored, live);
      NativeStage02ElementSnapshot[] liveSnapshots = records
        .Select(value => document.GetElement(value.ElementUniqueId))
        .Where(value => value != null)
        .Select(value => NativeStage02RevitService.CreateSnapshot(
          document,
          value,
          identity.DocumentFingerprint))
        .Where(value => value != null)
        .ToArray();
      return NativeStage02BAssignmentFreshnessPolicy.Evaluate(
        decision,
        identity.DocumentFingerprint,
        identity.RulePackageSha256,
        liveSnapshots);
    }

    private static NativeStage02BMetricRecord SuccessRecord(
      NativeStage02BMetricDefinition metric,
      NativeWorkflowIdentity identity,
      string runId,
      string value,
      string projectionStatus,
      NativeOfficialCarrierEvidenceStatus carrierStatus)
    {
      return NativeStage02BCanonicalizer.SealRecord(
        new NativeStage02BMetricRecord
        {
          PropertyId = metric.PropertyId,
          Identity = metric.Identity,
          Unit = metric.Property.CanonicalUnit,
          Source = "MANUAL_INPUT",
          RequestedCanonicalValue = value,
          LastSuccessfulCanonicalValue = value,
          LastAttemptRunId = runId,
          LastSuccessfulRunId = runId,
          WriteStatus = "SUCCEEDED",
          ReadbackStatus = "SUCCEEDED",
          ProjectionStatus = projectionStatus,
          OfficialCarrierStatus = carrierStatus,
          OfficialProjectionCarrierId = metric.OfficialProjectionCarrierId,
          OfficialCarrierProbeRef = metric.OfficialCarrierProbeRef,
          OfficialEvidenceRef = metric.OfficialEvidenceRef,
          IdentityContext = identity,
          UpdatedUtc = DateTimeOffset.UtcNow.ToString(
            "O", CultureInfo.InvariantCulture)
        });
    }

    private static NativeStage02BMetricRecord FailureRecord(
      NativeStage02BMetricDefinition metric,
      NativeStage02BMetricRecord previous,
      NativeWorkflowIdentity identity,
      string runId,
      string requested,
      string projectionStatus,
      NativeOfficialCarrierEvidenceStatus carrierStatus,
      string errorCode)
    {
      return NativeStage02BCanonicalizer.SealRecord(
        new NativeStage02BMetricRecord
        {
          PropertyId = metric.PropertyId,
          Identity = metric.Identity,
          Unit = metric.Property.CanonicalUnit,
          Source = "MANUAL_INPUT",
          RequestedCanonicalValue = requested ?? string.Empty,
          LastSuccessfulCanonicalValue = previous?.LastSuccessfulCanonicalValue
            ?? string.Empty,
          LastAttemptRunId = runId,
          LastSuccessfulRunId = previous?.LastSuccessfulRunId ?? string.Empty,
          WriteStatus = "FAILED",
          ReadbackStatus = "FAILED",
          ProjectionStatus = projectionStatus,
          OfficialCarrierStatus = carrierStatus,
          OfficialProjectionCarrierId = metric.OfficialProjectionCarrierId,
          OfficialCarrierProbeRef = metric.OfficialCarrierProbeRef,
          OfficialEvidenceRef = metric.OfficialEvidenceRef,
          IdentityContext = identity,
          UpdatedUtc = DateTimeOffset.UtcNow.ToString(
            "O", CultureInfo.InvariantCulture),
          ErrorCode = errorCode ?? string.Empty
        });
    }

    private static string ProjectionStatus(NativeStage02BOwnerDecision owner)
    {
      switch (owner.ProjectionMode)
      {
        case NativeStage02BProjectionMode.ProjectInformation:
          return "PROJECT_INFORMATION";
        case NativeStage02BProjectionMode.VerifiedElementParameter:
          return "VERIFIED_OFFICIAL_CARRIER";
        default:
          return "BLOCKED_PENDING_GOLDEN_RVT";
      }
    }

    private static string StableErrorCode(Exception exception)
    {
      string message = (exception?.Message ?? string.Empty).Trim();
      string[] stable =
      {
        "OFFICIAL_CARRIER_NOT_FOUND",
        "OFFICIAL_CARRIER_AMBIGUOUS",
        "OFFICIAL_CARRIER_TYPE_MISMATCH",
        "OFFICIAL_CARRIER_CONTRACT_MISMATCH",
        "STAGE02B_METRIC_TRANSACTION_NOT_COMMITTED",
        "READBACK_FAILED"
      };
      return stable.Contains(message, StringComparer.Ordinal)
        ? message : "STAGE02B_METRIC_WRITE_FAILED";
    }
  }
}
