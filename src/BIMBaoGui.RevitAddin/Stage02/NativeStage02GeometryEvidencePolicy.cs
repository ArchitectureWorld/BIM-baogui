using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal static class NativeStage02GeometryEvidencePolicy
  {
    private const double PointTolerance = 1e-6;
    private static readonly IReadOnlyDictionary<string, Policy> Policies =
      BuildPolicies();

    internal static NativeStage02TaskGeometryEvaluation Evaluate(
      NativeTaskDefinition task,
      NativeStage02ElementSnapshot element,
      NativeStage02GeometryEvidence geometry,
      IReadOnlyDictionary<Guid, NativeStage02ParameterEvidence> parameters,
      NativeStage02GeometryEvaluationContext context)
    {
      if (task == null) throw new ArgumentNullException(nameof(task));
      if (element == null) throw new ArgumentNullException(nameof(element));
      geometry = geometry ?? new NativeStage02GeometryEvidence();
      parameters = parameters
        ?? new Dictionary<Guid, NativeStage02ParameterEvidence>();
      context = context ?? new NativeStage02GeometryEvaluationContext();
      var checks = new List<NativeStage02GeometryCheckEvidence>();
      foreach (string ruleText in (task.GeometryChecks ?? Array.Empty<string>())
        .Concat(task.PropertyChecks ?? Array.Empty<string>()))
      {
        string key = Key(task.TaskId, ruleText);
        Policy policy;
        if (!Policies.TryGetValue(key, out policy))
        {
          checks.Add(Check(
            task.TaskId,
            ruleText,
            NativeStage02GeometryCheckState.Failed,
            "GEOMETRY_CHECK_UNSUPPORTED_PHASE1",
            key));
          continue;
        }
        checks.Add(EvaluatePolicy(
          task.TaskId,
          ruleText,
          policy,
          element,
          geometry,
          parameters,
          context));
      }
      string evaluationCanonical = string.Join("\u001f", checks.Select(value =>
        value.CheckId + "\u001e" + value.State + "\u001e" + value.Code
        + "\u001e" + value.Basis + "\u001e"
        + value.ManualReviewRecordHash));
      return new NativeStage02TaskGeometryEvaluation
      {
        TaskId = task.TaskId ?? string.Empty,
        ElementUniqueId = element.UniqueId ?? string.Empty,
        Checks = checks.ToArray(),
        EvaluationHash = NativeStage02SemanticAssignmentCanonicalizer.Sha256(
          evaluationCanonical)
      };
    }

    internal static NativeStage02ManualReviewRecord SealManualReview(
      NativeTaskDefinition task,
      NativeStage02ElementSnapshot element,
      NativeStage02GeometryEvaluationContext context,
      NativeStage02ManualReviewCommand command,
      string reviewedUtc)
    {
      if (task == null || element == null || context?.Identity == null
        || command == null)
        return null;
      string ruleText = (task.GeometryChecks ?? Array.Empty<string>())
        .FirstOrDefault(value => string.Equals(
          CheckId(task.TaskId, value),
          command.CheckId,
          StringComparison.Ordinal));
      Policy policy;
      if (ruleText == null
        || !Policies.TryGetValue(Key(task.TaskId, ruleText), out policy)
        || policy.Mode != "MANUAL_CURRENT_SNAPSHOT_REVIEW")
        return null;
      NativeStage02ElementSnapshot[] elements = ManualElements(
        element,
        policy,
        context);
      return NativeStage02ManualReviewPolicy.Seal(
        new NativeStage02ManualReviewRecord
        {
          CheckId = command.CheckId,
          RuleText = ruleText,
          DocumentFingerprint = context.Identity.DocumentFingerprint,
          RulePackageSha256 = context.Identity.RulePackageSha256,
          ElementUniqueIds = elements.Select(value => value.UniqueId).ToArray(),
          ElementSnapshotHashes = elements
            .Select(NativeStage02ElementSnapshotCanonicalizer.Sha256)
            .ToArray(),
          GeometryEvidenceHashes = elements.Select(value =>
            value.Geometry?.EvidenceHash ?? string.Empty).ToArray(),
          Decision = command.Decision,
          Reviewer = command.Reviewer,
          Basis = command.Basis,
          ReviewedUtc = reviewedUtc
        });
    }

    private static NativeStage02GeometryCheckEvidence EvaluatePolicy(
      string taskId,
      string ruleText,
      Policy policy,
      NativeStage02ElementSnapshot element,
      NativeStage02GeometryEvidence geometry,
      IReadOnlyDictionary<Guid, NativeStage02ParameterEvidence> parameters,
      NativeStage02GeometryEvaluationContext context)
    {
      if (!string.IsNullOrWhiteSpace(policy.SubjectRoleId)
        && !string.Equals(
          element.AssignedRoleId,
          policy.SubjectRoleId,
          StringComparison.Ordinal))
      {
        return Fail(taskId, ruleText, "GEOMETRY_SUBJECT_ROLE_MISMATCH");
      }
      if (!string.IsNullOrWhiteSpace(geometry.CaptureCode))
        return Fail(taskId, ruleText, geometry.CaptureCode);

      switch (policy.Mode)
      {
        case "AUTO_SHARED_COORDINATE":
          return geometry.BoundingBox?.Available == true
            || (geometry.LocationCoordinatesFeet?.Count ?? 0) > 0
            ? Pass(taskId, ruleText, "CURRENT_REVIT_COORDINATES")
            : Fail(taskId, ruleText, "SHARED_COORDINATE_EVIDENCE_MISSING");
        case "AUTO_PLANAR_REFERENCE":
          return HasTopology(geometry)
            || FinitePositive(geometry.ApprovedProjectedAreaSquareMetres)
            ? Pass(taskId, ruleText, "CURRENT_STAGE01_PLANE")
            : Fail(taskId, ruleText, "PLANAR_REFERENCE_EVIDENCE_MISSING");
        case "AUTO_CLOSED_BOUNDARY":
          return EvaluateClosed(taskId, ruleText, geometry);
        case "AUTO_NO_SELF_INTERSECTION":
          return EvaluateSelfIntersection(taskId, ruleText, geometry);
        case "AUTO_POSITIVE_AREA":
          return FinitePositive(geometry.ApprovedProjectedAreaSquareMetres)
            ? Pass(taskId, ruleText, geometry.ProjectedAreaSource)
            : Fail(taskId, ruleText, "GEOMETRY_APPROVED_AREA_INVALID");
        case "AUTO_CONTAINED_BY_ROLE":
          return EvaluateContains(
            taskId, ruleText, policy.ReferenceRoleId, element, geometry, context);
        case "AUTO_NO_DUPLICATE_GEOMETRY":
          return EvaluateDuplicate(taskId, ruleText, element, geometry, context);
        case "AUTO_CONTINUOUS_CURVE_CHAIN":
          return EvaluateContinuity(taskId, ruleText, geometry);
        case "AUTO_MIN_SEGMENT_LENGTH":
          return EvaluateShortCurves(taskId, ruleText, geometry);
        case "MANUAL_CURRENT_SNAPSHOT_REVIEW":
          return EvaluateManual(taskId, ruleText, policy, element, context);
        case "AUTO_PROJECTED_AREA_MATCH":
          return EvaluateAreaMatch(
            taskId, ruleText, policy.PropertyGuid, geometry, parameters);
        case "AUTO_GREEN_CONVERTED_AREA_FINITE":
          return EvaluateGreenFormula(
            taskId,
            ruleText,
            policy.AreaPropertyGuid,
            policy.FactorPropertyGuid,
            parameters);
        default:
          return Fail(taskId, ruleText, "GEOMETRY_CHECK_UNSUPPORTED_PHASE1");
      }
    }

    private static NativeStage02GeometryCheckEvidence EvaluateClosed(
      string taskId,
      string ruleText,
      NativeStage02GeometryEvidence geometry)
    {
      bool planarLoops = (geometry.PlanarLoopsMetres?.Count ?? 0) > 0;
      IReadOnlyList<IReadOnlyList<double>> sources = planarLoops
        ? geometry.PlanarLoopsMetres
        : geometry.CurveChainsMetres;
      Point[][] chains = ParseChains(sources);
      if (chains.Length == 0
        || chains.Any(value => value.Length < (planarLoops ? 3 : 2)))
        return Fail(taskId, ruleText, "GEOMETRY_TOPOLOGY_MISSING");
      if (planarLoops)
      {
        return chains.Any(value => !Equal(value[0], value[value.Length - 1]))
          ? Fail(taskId, ruleText, "GEOMETRY_BOUNDARY_OPEN")
          : Pass(taskId, ruleText, geometry.TopologySource);
      }
      for (int index = 1; index < chains.Length; index++)
      {
        if (!Equal(chains[index - 1][chains[index - 1].Length - 1], chains[index][0]))
          return Fail(taskId, ruleText, "GEOMETRY_CHAIN_DISCONTINUITY");
      }
      if (!Equal(chains[0][0], chains[chains.Length - 1][
        chains[chains.Length - 1].Length - 1]))
        return Fail(taskId, ruleText, "GEOMETRY_BOUNDARY_OPEN");
      return Pass(taskId, ruleText, geometry.TopologySource);
    }

    private static NativeStage02GeometryCheckEvidence EvaluateSelfIntersection(
      string taskId,
      string ruleText,
      NativeStage02GeometryEvidence geometry)
    {
      Point[][] loops = ParseChains(geometry.PlanarLoopsMetres);
      if (loops.Length == 0)
        return Fail(taskId, ruleText, "GEOMETRY_TOPOLOGY_MISSING");
      foreach (Point[] loop in loops)
      {
        if (loop.Length < 4)
          return Fail(taskId, ruleText, "GEOMETRY_TOPOLOGY_AMBIGUOUS");
        int segmentCount = loop.Length - 1;
        for (int left = 0; left < segmentCount; left++)
        {
          for (int right = left + 1; right < segmentCount; right++)
          {
            if (Math.Abs(left - right) <= 1
              || (left == 0 && right == segmentCount - 1))
              continue;
            if (SegmentsIntersect(
              loop[left], loop[left + 1], loop[right], loop[right + 1]))
              return Fail(taskId, ruleText, "GEOMETRY_SELF_INTERSECTION");
          }
        }
      }
      return Pass(taskId, ruleText, geometry.TopologySource);
    }

    private static NativeStage02GeometryCheckEvidence EvaluateContains(
      string taskId,
      string ruleText,
      string referenceRoleId,
      NativeStage02ElementSnapshot element,
      NativeStage02GeometryEvidence geometry,
      NativeStage02GeometryEvaluationContext context)
    {
      if (!context.ScopeComplete)
        return Fail(taskId, ruleText, "FULL_MODEL_RECHECK_REQUIRED");
      NativeStage02ElementSnapshot[] references = (context.ConfirmedElements
          ?? Array.Empty<NativeStage02ElementSnapshot>())
        .Where(value => value != null
          && value.UniqueId != element.UniqueId
          && value.AssignedRoleId == referenceRoleId)
        .ToArray();
      if (references.Length == 0)
        return Fail(taskId, ruleText, "GEOMETRY_REFERENCE_ROLE_MISSING");
      if (references.Length > 1)
        return Fail(taskId, ruleText, "GEOMETRY_REFERENCE_ROLE_MULTIPLE");
      Point[] subject = ParseChains(geometry.PlanarLoopsMetres).FirstOrDefault();
      Point[] reference = ParseChains(
        references[0].Geometry?.PlanarLoopsMetres).FirstOrDefault();
      if (subject == null || reference == null)
        return Fail(taskId, ruleText, "GEOMETRY_TOPOLOGY_MISSING");
      if (subject.Any(point => !InsideOrBoundary(point, reference)))
        return Fail(taskId, ruleText, "GEOMETRY_NOT_CONTAINED");
      if (BoundariesProperlyCross(subject, reference))
        return Fail(taskId, ruleText, "GEOMETRY_BOUNDARY_CROSSING");
      return Pass(taskId, ruleText, "REFERENCE_ROLE:" + referenceRoleId);
    }

    private static NativeStage02GeometryCheckEvidence EvaluateDuplicate(
      string taskId,
      string ruleText,
      NativeStage02ElementSnapshot element,
      NativeStage02GeometryEvidence geometry,
      NativeStage02GeometryEvaluationContext context)
    {
      if (!context.ScopeComplete)
        return Fail(taskId, ruleText, "FULL_MODEL_RECHECK_REQUIRED");
      Point[] subject = ParseChains(geometry.PlanarLoopsMetres).FirstOrDefault();
      if (subject == null)
        return Fail(taskId, ruleText, "GEOMETRY_TOPOLOGY_MISSING");
      string subjectHash = RingHash(subject);
      bool duplicate = (context.ConfirmedElements
          ?? Array.Empty<NativeStage02ElementSnapshot>())
        .Where(value => value != null
          && value.UniqueId != element.UniqueId
          && value.AssignedRoleId == element.AssignedRoleId)
        .Select(value => ParseChains(value.Geometry?.PlanarLoopsMetres)
          .FirstOrDefault())
        .Where(value => value != null)
        .Any(value => RingHash(value) == subjectHash);
      return duplicate
        ? Fail(taskId, ruleText, "GEOMETRY_DUPLICATE")
        : Pass(taskId, ruleText, "CANONICAL_RING_SHA256:" + subjectHash);
    }

    private static NativeStage02GeometryCheckEvidence EvaluateContinuity(
      string taskId,
      string ruleText,
      NativeStage02GeometryEvidence geometry)
    {
      Point[][] chains = ParseChains(geometry.CurveChainsMetres);
      if (chains.Length == 0)
        return Fail(taskId, ruleText, "GEOMETRY_CURVE_CHAIN_MISSING");
      var degrees = new Dictionary<string, int>(StringComparer.Ordinal);
      var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
      foreach (Point[] chain in chains)
      {
        for (int index = 0; index + 1 < chain.Length; index++)
        {
          string a = PointKey(chain[index]);
          string b = PointKey(chain[index + 1]);
          AddDegree(degrees, a);
          AddDegree(degrees, b);
          AddEdge(adjacency, a, b);
        }
      }
      int odd = degrees.Values.Count(value => value % 2 != 0);
      if (degrees.Count == 0 || (odd != 0 && odd != 2)
        || ConnectedCount(adjacency) != 1)
        return Fail(taskId, ruleText, "GEOMETRY_CURVE_CHAIN_DISCONTINUOUS");
      return Pass(taskId, ruleText, "SORTED_ENDPOINT_DEGREES");
    }

    private static NativeStage02GeometryCheckEvidence EvaluateShortCurves(
      string taskId,
      string ruleText,
      NativeStage02GeometryEvidence geometry)
    {
      double tolerance = geometry.ShortCurveToleranceMetres;
      if (!Finite(tolerance) || tolerance <= 0)
        return Fail(taskId, ruleText, "GEOMETRY_SHORT_CURVE_TOLERANCE_MISSING");
      Point[][] chains = ParseChains(geometry.CurveChainsMetres);
      if (chains.Length == 0)
        return Fail(taskId, ruleText, "GEOMETRY_CURVE_CHAIN_MISSING");
      bool tooShort = chains.Any(chain => chain.Zip(
        chain.Skip(1),
        (left, right) => Distance(left, right)).Any(length =>
          !Finite(length) || length < tolerance));
      return tooShort
        ? Fail(taskId, ruleText, "GEOMETRY_SEGMENT_BELOW_REVIT_TOLERANCE")
        : Pass(taskId, ruleText, "REVIT_APPLICATION_SHORT_CURVE_TOLERANCE");
    }

    private static NativeStage02GeometryCheckEvidence EvaluateManual(
      string taskId,
      string ruleText,
      Policy policy,
      NativeStage02ElementSnapshot element,
      NativeStage02GeometryEvaluationContext context)
    {
      if (!string.IsNullOrWhiteSpace(policy.ReferenceRoleId)
        && !context.ScopeComplete)
        return Fail(taskId, ruleText, "FULL_MODEL_RECHECK_REQUIRED");
      if (!string.IsNullOrWhiteSpace(policy.ReferenceRoleId)
        && !(context.ConfirmedElements
          ?? Array.Empty<NativeStage02ElementSnapshot>()).Any(value =>
            value != null
            && value.UniqueId != element.UniqueId
            && value.AssignedRoleId == policy.ReferenceRoleId))
        return Fail(taskId, ruleText, "GEOMETRY_REFERENCE_ROLE_MISSING");
      string checkId = CheckId(taskId, ruleText);
      NativeStage02ElementSnapshot[] elements = ManualElements(
        element,
        policy,
        context);
      NativeStage02ManualReviewRecord record = (context.ManualReviews
          ?? Array.Empty<NativeStage02ManualReviewRecord>())
        .Where(value => value != null && value.CheckId == checkId)
        .OrderByDescending(value => value.ReviewedUtc, StringComparer.Ordinal)
        .FirstOrDefault();
      return NativeStage02ManualReviewPolicy.VerifyCurrent(
        record,
        checkId,
        ruleText,
        context.Identity,
        elements.Select(value => value.UniqueId).ToArray(),
        elements.Select(NativeStage02ElementSnapshotCanonicalizer.Sha256).ToArray(),
        elements.Select(value => value.Geometry?.EvidenceHash ?? string.Empty).ToArray());
    }

    private static NativeStage02ElementSnapshot[] ManualElements(
      NativeStage02ElementSnapshot element,
      Policy policy,
      NativeStage02GeometryEvaluationContext context)
    {
      var reviewRoles = new HashSet<string>(StringComparer.Ordinal)
      {
        policy.SubjectRoleId
      };
      if (!string.IsNullOrWhiteSpace(policy.ReferenceRoleId))
        reviewRoles.Add(policy.ReferenceRoleId);
      return new[] { element }
        .Concat((context.ConfirmedElements
          ?? Array.Empty<NativeStage02ElementSnapshot>()).Where(value =>
            value != null
            && reviewRoles.Contains(value.AssignedRoleId)))
        .GroupBy(value => value.UniqueId, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(value => value.UniqueId, StringComparer.Ordinal)
        .ToArray();
    }

    private static NativeStage02GeometryCheckEvidence EvaluateAreaMatch(
      string taskId,
      string ruleText,
      Guid propertyGuid,
      NativeStage02GeometryEvidence geometry,
      IReadOnlyDictionary<Guid, NativeStage02ParameterEvidence> parameters)
    {
      NativeStage02ParameterEvidence parameter;
      double fieldValue;
      double geometryValue = geometry.ApprovedProjectedAreaSquareMetres
        .GetValueOrDefault(double.NaN);
      if (!parameters.TryGetValue(propertyGuid, out parameter)
        || parameter == null
        || !double.TryParse(
          parameter.CurrentCanonicalValue,
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out fieldValue)
        || !Finite(fieldValue)
        || !Finite(geometryValue))
        return Fail(taskId, ruleText, "PROJECTED_AREA_EVIDENCE_MISSING");
      double tolerance = Math.Max(0.01, geometryValue * 0.001);
      return Math.Abs(fieldValue - geometryValue) <= tolerance
        ? Pass(taskId, ruleText, "MAX_0.01M2_OR_0.1_PERCENT")
        : Fail(taskId, ruleText, "PROJECTED_AREA_MISMATCH");
    }

    private static NativeStage02GeometryCheckEvidence EvaluateGreenFormula(
      string taskId,
      string ruleText,
      Guid areaGuid,
      Guid factorGuid,
      IReadOnlyDictionary<Guid, NativeStage02ParameterEvidence> parameters)
    {
      double area;
      double factor;
      if (!TryParameter(parameters, areaGuid, out area)
        || !TryParameter(parameters, factorGuid, out factor)
        || area < 0 || factor < 0 || !Finite(area * factor))
        return Fail(taskId, ruleText, "GREEN_CONVERTED_AREA_INVALID");
      return Pass(taskId, ruleText, "AREA_MULTIPLIED_BY_FACTOR_FINITE");
    }

    private static bool TryParameter(
      IReadOnlyDictionary<Guid, NativeStage02ParameterEvidence> parameters,
      Guid guid,
      out double value)
    {
      value = double.NaN;
      NativeStage02ParameterEvidence parameter;
      return parameters.TryGetValue(guid, out parameter)
        && parameter != null
        && double.TryParse(
          parameter.CurrentCanonicalValue,
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out value)
        && Finite(value);
    }

    private static NativeStage02GeometryCheckEvidence Pass(
      string taskId,
      string ruleText,
      string basis)
    {
      return Check(
        taskId,
        ruleText,
        NativeStage02GeometryCheckState.Passed,
        "GEOMETRY_CHECK_PASSED",
        basis);
    }

    private static NativeStage02GeometryCheckEvidence Fail(
      string taskId,
      string ruleText,
      string code)
    {
      return Check(
        taskId,
        ruleText,
        NativeStage02GeometryCheckState.Failed,
        code,
        code);
    }

    private static NativeStage02GeometryCheckEvidence Check(
      string taskId,
      string ruleText,
      NativeStage02GeometryCheckState state,
      string code,
      string basis)
    {
      return new NativeStage02GeometryCheckEvidence
      {
        CheckId = CheckId(taskId, ruleText),
        RuleText = ruleText ?? string.Empty,
        State = state,
        Code = code ?? string.Empty,
        Basis = basis ?? string.Empty
      };
    }

    private static string CheckId(string taskId, string ruleText)
    {
      NativeReportingCheckDefinition definition =
        NativeReportingRuleCatalog.Current.GetChecks("总平模型")
          .FirstOrDefault(value => value.TaskId == taskId
            && value.RuleText == ruleText
            && (value.CheckKind == NativeReportingCheckKind.Geometry
              || value.CheckKind == NativeReportingCheckKind.PropertyConsistency));
      return definition?.CheckId
        ?? "STAGE02A.GEOMETRY." + taskId + "."
          + NativeStage02SemanticAssignmentCanonicalizer.Sha256(
            ruleText ?? string.Empty).Substring(0, 16);
    }

    private static bool HasTopology(NativeStage02GeometryEvidence geometry)
    {
      return (geometry.PlanarLoopsMetres?.Count ?? 0) > 0
        || (geometry.CurveChainsMetres?.Count ?? 0) > 0;
    }

    private static Point[][] ParseChains(
      IReadOnlyList<IReadOnlyList<double>> chains)
    {
      return (chains ?? Array.Empty<IReadOnlyList<double>>())
        .Where(value => value != null && value.Count >= 4 && value.Count % 2 == 0)
        .Select(value => Enumerable.Range(0, value.Count / 2)
          .Select(index => new Point(value[index * 2], value[index * 2 + 1]))
          .ToArray())
        .Where(value => value.All(point => Finite(point.X) && Finite(point.Y)))
        .ToArray();
    }

    private static bool InsideOrBoundary(Point point, Point[] polygon)
    {
      bool inside = false;
      for (int left = 0, right = polygon.Length - 1;
        left < polygon.Length;
        right = left++)
      {
        Point a = polygon[right];
        Point b = polygon[left];
        if (OnSegment(a, point, b)) return true;
        bool crosses = (a.Y > point.Y) != (b.Y > point.Y)
          && point.X < (b.X - a.X) * (point.Y - a.Y)
            / (b.Y - a.Y) + a.X;
        if (crosses) inside = !inside;
      }
      return inside;
    }

    private static bool SegmentsIntersect(Point a, Point b, Point c, Point d)
    {
      double o1 = Cross(a, b, c);
      double o2 = Cross(a, b, d);
      double o3 = Cross(c, d, a);
      double o4 = Cross(c, d, b);
      if (((o1 > PointTolerance && o2 < -PointTolerance)
        || (o1 < -PointTolerance && o2 > PointTolerance))
        && ((o3 > PointTolerance && o4 < -PointTolerance)
          || (o3 < -PointTolerance && o4 > PointTolerance)))
        return true;
      return Math.Abs(o1) <= PointTolerance && OnSegment(a, c, b)
        || Math.Abs(o2) <= PointTolerance && OnSegment(a, d, b)
        || Math.Abs(o3) <= PointTolerance && OnSegment(c, a, d)
        || Math.Abs(o4) <= PointTolerance && OnSegment(c, b, d);
    }

    private static bool BoundariesProperlyCross(Point[] subject, Point[] reference)
    {
      int subjectSegments = Math.Max(0, subject.Length - 1);
      int referenceSegments = Math.Max(0, reference.Length - 1);
      for (int left = 0; left < subjectSegments; left++)
      {
        for (int right = 0; right < referenceSegments; right++)
        {
          if (SegmentsProperlyIntersect(
            subject[left],
            subject[left + 1],
            reference[right],
            reference[right + 1]))
            return true;
        }
      }
      return false;
    }

    private static bool SegmentsProperlyIntersect(
      Point a,
      Point b,
      Point c,
      Point d)
    {
      double o1 = Cross(a, b, c);
      double o2 = Cross(a, b, d);
      double o3 = Cross(c, d, a);
      double o4 = Cross(c, d, b);
      return ((o1 > PointTolerance && o2 < -PointTolerance)
          || (o1 < -PointTolerance && o2 > PointTolerance))
        && ((o3 > PointTolerance && o4 < -PointTolerance)
          || (o3 < -PointTolerance && o4 > PointTolerance));
    }

    private static bool OnSegment(Point a, Point point, Point b)
    {
      return Math.Abs(Cross(a, b, point)) <= PointTolerance
        && point.X >= Math.Min(a.X, b.X) - PointTolerance
        && point.X <= Math.Max(a.X, b.X) + PointTolerance
        && point.Y >= Math.Min(a.Y, b.Y) - PointTolerance
        && point.Y <= Math.Max(a.Y, b.Y) + PointTolerance;
    }

    private static double Cross(Point a, Point b, Point c)
    {
      return (b.X - a.X) * (c.Y - a.Y)
        - (b.Y - a.Y) * (c.X - a.X);
    }

    private static string RingHash(Point[] raw)
    {
      Point[] ring = raw.Length > 1 && Equal(raw[0], raw[raw.Length - 1])
        ? raw.Take(raw.Length - 1).ToArray()
        : raw.ToArray();
      if (ring.Length == 0) return string.Empty;
      string forward = CanonicalRotation(ring);
      string reverse = CanonicalRotation(ring.Reverse().ToArray());
      return NativeStage02SemanticAssignmentCanonicalizer.Sha256(
        string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse);
    }

    private static string CanonicalRotation(Point[] ring)
    {
      return Enumerable.Range(0, ring.Length)
        .Select(start => string.Join(";", Enumerable.Range(0, ring.Length)
          .Select(offset => PointKey(ring[(start + offset) % ring.Length]))))
        .OrderBy(value => value, StringComparer.Ordinal)
        .First();
    }

    private static string PointKey(Point point)
    {
      return Math.Round(point.X / PointTolerance).ToString(
          CultureInfo.InvariantCulture)
        + "," + Math.Round(point.Y / PointTolerance).ToString(
          CultureInfo.InvariantCulture);
    }

    private static bool Equal(Point left, Point right)
    {
      return Distance(left, right) <= PointTolerance;
    }

    private static double Distance(Point left, Point right)
    {
      double x = left.X - right.X;
      double y = left.Y - right.Y;
      return Math.Sqrt(x * x + y * y);
    }

    private static void AddDegree(IDictionary<string, int> degrees, string key)
    {
      int value;
      degrees.TryGetValue(key, out value);
      degrees[key] = value + 1;
    }

    private static void AddEdge(
      IDictionary<string, HashSet<string>> adjacency,
      string left,
      string right)
    {
      HashSet<string> values;
      if (!adjacency.TryGetValue(left, out values))
        adjacency[left] = values = new HashSet<string>(StringComparer.Ordinal);
      values.Add(right);
      if (!adjacency.TryGetValue(right, out values))
        adjacency[right] = values = new HashSet<string>(StringComparer.Ordinal);
      values.Add(left);
    }

    private static int ConnectedCount(
      IDictionary<string, HashSet<string>> adjacency)
    {
      var remaining = new HashSet<string>(adjacency.Keys, StringComparer.Ordinal);
      int count = 0;
      while (remaining.Count > 0)
      {
        count++;
        string start = remaining.First();
        var pending = new Stack<string>();
        pending.Push(start);
        remaining.Remove(start);
        while (pending.Count > 0)
        {
          string current = pending.Pop();
          HashSet<string> next;
          if (!adjacency.TryGetValue(current, out next)) continue;
          foreach (string value in next.Where(remaining.Contains).ToArray())
          {
            remaining.Remove(value);
            pending.Push(value);
          }
        }
      }
      return count;
    }

    private static bool FinitePositive(double? value)
    {
      return value.HasValue && Finite(value.Value) && value.Value > 0;
    }

    private static bool Finite(double value)
    {
      return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string Key(string taskId, string ruleText)
    {
      return (taskId ?? string.Empty) + "\u001f" + (ruleText ?? string.Empty);
    }

    private static IReadOnlyDictionary<string, Policy> BuildPolicies()
    {
      var result = new Dictionary<string, Policy>(StringComparer.Ordinal);
      Add(result, "SITE.SKELETON", "项目基点与共享坐标有效", "AUTO_SHARED_COORDINATE");
      Add(result, "SITE.SKELETON", "总平计算平面有效", "AUTO_PLANAR_REFERENCE");
      Add(result, "SITE.TOTAL_LAND", "边界闭合", "AUTO_CLOSED_BOUNDARY", "SITE_TOTAL_LAND");
      Add(result, "SITE.TOTAL_LAND", "无自交", "AUTO_NO_SELF_INTERSECTION", "SITE_TOTAL_LAND");
      Add(result, "SITE.TOTAL_LAND", "面积大于零", "AUTO_POSITIVE_AREA", "SITE_TOTAL_LAND");
      Add(result, "SITE.NET_LAND", "边界闭合", "AUTO_CLOSED_BOUNDARY", "SITE_NET_LAND");
      Add(result, "SITE.NET_LAND", "位于规划总用地内", "AUTO_CONTAINED_BY_ROLE", "SITE_NET_LAND", "SITE_TOTAL_LAND");
      Add(result, "SITE.NET_LAND", "面积大于零", "AUTO_POSITIVE_AREA", "SITE_NET_LAND");
      Add(result, "SITE.BUILDING_FOOTPRINT", "轮廓闭合", "AUTO_CLOSED_BOUNDARY", "SITE_BUILDING_FOOTPRINT");
      Add(result, "SITE.BUILDING_FOOTPRINT", "位于规划净用地内", "AUTO_CONTAINED_BY_ROLE", "SITE_BUILDING_FOOTPRINT", "SITE_NET_LAND");
      Add(result, "SITE.BUILDING_FOOTPRINT", "建筑轮廓不重复", "AUTO_NO_DUPLICATE_GEOMETRY", "SITE_BUILDING_FOOTPRINT");
      Add(result, "SITE.OTHER_LAND", "边界闭合", "AUTO_CLOSED_BOUNDARY", "SITE_OTHER_LAND");
      Add(result, "SITE.OTHER_LAND", "用地关系有效", "MANUAL_CURRENT_SNAPSHOT_REVIEW", "SITE_OTHER_LAND");
      Add(result, "SITE.ROAD_REDLINE", "曲线连续", "AUTO_CONTINUOUS_CURVE_CHAIN", "SITE_ROAD_REDLINE");
      Add(result, "SITE.ROAD_REDLINE", "无无效短线", "AUTO_MIN_SEGMENT_LENGTH", "SITE_ROAD_REDLINE");
      Add(result, "SITE.ROAD_CENTERLINE", "中心线连续", "AUTO_CONTINUOUS_CURVE_CHAIN", "SITE_ROAD_CENTERLINE");
      Add(result, "SITE.ROAD_CENTERLINE", "与道路红线关系有效", "MANUAL_CURRENT_SNAPSHOT_REVIEW", "SITE_ROAD_CENTERLINE", "SITE_ROAD_REDLINE");
      Add(result, "SITE.INTERNAL_ROADS", "道路范围闭合", "AUTO_CLOSED_BOUNDARY", "SITE_INTERNAL_ROADS");
      Add(result, "SITE.INTERNAL_ROADS", "位于规划净用地内", "AUTO_CONTAINED_BY_ROLE", "SITE_INTERNAL_ROADS", "SITE_NET_LAND");
      Add(result, "SITE.FIRE_LANE", "消防道路连续", "AUTO_CONTINUOUS_CURVE_CHAIN", "SITE_FIRE_LANE");
      Add(result, "SITE.FIRE_LANE", "消防道路范围有效", "MANUAL_CURRENT_SNAPSHOT_REVIEW", "SITE_FIRE_LANE");
      Add(result, "SITE.FIRE_FIELD", "场地边界闭合", "AUTO_CLOSED_BOUNDARY", "SITE_FIRE_FIELD");
      Add(result, "SITE.FIRE_FIELD", "与服务建筑关系有效", "MANUAL_CURRENT_SNAPSHOT_REVIEW", "SITE_FIRE_FIELD", "SITE_BUILDING_FOOTPRINT");
      Add(result, "SITE.GREEN", "绿地边界闭合", "AUTO_CLOSED_BOUNDARY", "SITE_GREEN_OBJECT");
      Add(result, "SITE.GREEN", "绿地不越界", "AUTO_CONTAINED_BY_ROLE", "SITE_GREEN_OBJECT", "SITE_NET_LAND");
      Add(result, "SITE.GREEN", "绿地不重复统计", "AUTO_NO_DUPLICATE_GEOMETRY", "SITE_GREEN_OBJECT");
      Add(result, "SITE.OUTDOOR_PARKING", "停车范围有效", "MANUAL_CURRENT_SNAPSHOT_REVIEW", "SITE_OUTDOOR_PARKING");
      Add(result, "SITE.OUTDOOR_PARKING", "车位不重复", "AUTO_NO_DUPLICATE_GEOMETRY", "SITE_OUTDOOR_PARKING");
      Add(result, "SITE.CIVIL_DEFENSE", "人防范围闭合", "AUTO_CLOSED_BOUNDARY", "SITE_CIVIL_DEFENSE");
      Add(result, "SITE.CIVIL_DEFENSE", "范围关系有效", "MANUAL_CURRENT_SNAPSHOT_REVIEW", "SITE_CIVIL_DEFENSE");
      Add(result, "SITE.STRUCTURES", "设施位置位于项目范围内", "AUTO_CONTAINED_BY_ROLE", "SITE_STRUCTURES", "SITE_TOTAL_LAND");
      AddArea(result, "SITE.TOTAL_LAND", "投影面积与几何一致", "b970d6b1-92c9-51d2-8fac-187808a07801");
      AddArea(result, "SITE.NET_LAND", "投影面积与几何一致", "c42ea80f-4a12-5d4b-8bba-2374135d9d2a");
      result.Add(Key("SITE.GREEN", "折算面积计算有效"), new Policy
      {
        Mode = "AUTO_GREEN_CONVERTED_AREA_FINITE",
        AreaPropertyGuid = new Guid("6cc053e3-891d-51b1-b861-af498733f73a"),
        FactorPropertyGuid = new Guid("a99a0961-05fe-56fd-b8a0-865410bfe72f")
      });
      return result;
    }

    private static void Add(
      IDictionary<string, Policy> policies,
      string taskId,
      string ruleText,
      string mode,
      string subjectRoleId = "",
      string referenceRoleId = "")
    {
      policies.Add(Key(taskId, ruleText), new Policy
      {
        Mode = mode,
        SubjectRoleId = subjectRoleId,
        ReferenceRoleId = referenceRoleId
      });
    }

    private static void AddArea(
      IDictionary<string, Policy> policies,
      string taskId,
      string ruleText,
      string propertyGuid)
    {
      policies.Add(Key(taskId, ruleText), new Policy
      {
        Mode = "AUTO_PROJECTED_AREA_MATCH",
        PropertyGuid = new Guid(propertyGuid)
      });
    }

    private sealed class Policy
    {
      internal string Mode { get; set; } = string.Empty;
      internal string SubjectRoleId { get; set; } = string.Empty;
      internal string ReferenceRoleId { get; set; } = string.Empty;
      internal Guid PropertyGuid { get; set; }
      internal Guid AreaPropertyGuid { get; set; }
      internal Guid FactorPropertyGuid { get; set; }
    }

    private struct Point
    {
      internal Point(double x, double y)
      {
        X = x;
        Y = y;
      }

      internal double X { get; }
      internal double Y { get; }
    }
  }
}
