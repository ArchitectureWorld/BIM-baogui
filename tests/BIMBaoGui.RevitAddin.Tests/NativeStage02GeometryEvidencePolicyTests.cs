using System;
using System.Collections.Generic;
using System.Linq;
using BIMBaoGui.RevitAddin.Rules;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Workflow;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02GeometryEvidencePolicyTests
  {
    [Fact]
    public void Every_source_geometry_rule_has_one_policy_and_no_unsupported_phase1()
    {
      NativeStage02ElementSnapshot element = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "subject",
        ElementId = 1,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        AssignedRoleId = "SITE_GREEN_OBJECT",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          BoundingBox = new NativeStage02BoundingBoxEvidence
          {
            Available = true,
            MinXFeet = 0,
            MinYFeet = 0,
            MinZFeet = 0,
            MaxXFeet = 10,
            MaxYFeet = 10,
            MaxZFeet = 1
          },
          LocationKind = "LocationPoint",
          LocationCoordinatesFeet = new[] { 1.0, 1.0, 0.0 },
          ApprovedProjectedAreaSquareMetres = 100,
          ProjectedAreaSource = "PLANAR_FACE",
          PlanarLoopsMetres = new IReadOnlyList<double>[]
          {
            new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 0, 0 }
          },
          CurveChainsMetres = new IReadOnlyList<double>[]
          {
            new double[] { 0, 0, 10, 0, 10, 10 }
          },
          ShortCurveToleranceMetres = 0.01,
          TopologySource = "PLANAR_FACE",
          EvidenceHash = new string('b', 64)
        }
      };
      NativeStage02ElementSnapshot reference = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "reference",
        ElementId = 2,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        AssignedRoleId = "SITE_TOTAL_LAND",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          PlanarLoopsMetres = new IReadOnlyList<double>[]
          {
            new double[] { -100, -100, 100, -100, 100, 100, -100, 100, -100, -100 }
          },
          EvidenceHash = new string('c', 64)
        }
      };
      NativeStage02GeometryEvaluationContext context =
        new NativeStage02GeometryEvaluationContext
        {
          Identity = new NativeWorkflowIdentity
          {
            DocumentFingerprint = "doc",
            ModelFileType = "总平模型",
            RulePackageId = "HBR-WUHAN-PLANNING",
            RulePackageVersion = "1.0.0",
            RulePackageSha256 = new string('a', 64)
          },
          ConfirmedElements = new[] { element, reference },
          ManualReviews = Array.Empty<NativeStage02ManualReviewRecord>()
        };

      NativeStage02GeometryCheckEvidence[] checks = NativeRuleCatalog.Current.Tasks
        .Where(task => task.ModelFileType == "总平模型")
        .SelectMany(task => NativeStage02GeometryEvidencePolicy.Evaluate(
          task,
          element,
          element.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(),
          context).Checks)
        .ToArray();
      int expected = NativeRuleCatalog.Current.Tasks
        .Where(task => task.ModelFileType == "总平模型")
        .Sum(task => task.GeometryChecks.Count + task.PropertyChecks.Count);

      Assert.Equal(expected, checks.Length);
      Assert.Equal(34, checks.Length);
      Assert.DoesNotContain(checks, value =>
        value.Code == "GEOMETRY_CHECK_UNSUPPORTED_PHASE1");
      Assert.Equal(expected, checks.Select(value => value.RuleText).Count());
    }

    [Fact]
    public void Reliable_supported_element_topologies_have_positive_area()
    {
      string[] kinds = { "BuildingPad", "Floor", "DirectShape", "FamilyInstance" };
      foreach (string kind in kinds)
      {
        NativeStage02ElementSnapshot element = new NativeStage02ElementSnapshot
        {
          DocumentFingerprint = "doc",
          UniqueId = kind,
          ElementId = 1,
          Category = "OST_BuildingPad",
          ElementKind = kind,
          AssignedRoleId = "SITE_TOTAL_LAND",
          IsModelElement = true,
          Geometry = new NativeStage02GeometryEvidence
          {
            ApprovedProjectedAreaSquareMetres = 25,
            ProjectedAreaSource = kind == "FamilyInstance"
              ? "INSTANCE_PLANAR_FACE"
              : "PLANAR_FACE",
            PlanarLoopsMetres = new IReadOnlyList<double>[]
            {
              new double[] { 0, 0, 5, 0, 5, 5, 0, 5, 0, 0 }
            },
            TopologySource = "PLANAR_FACE",
            EvidenceHash = new string('d', 64)
          }
        };
        NativeStage02TaskGeometryEvaluation evaluation =
          NativeStage02GeometryEvidencePolicy.Evaluate(
            new NativeTaskDefinition
            {
              TaskId = "SITE.TOTAL_LAND",
              GeometryChecks = new[] { "面积大于零" }
            },
            element,
            element.Geometry,
            new Dictionary<Guid, NativeStage02ParameterEvidence>(),
            new NativeStage02GeometryEvaluationContext
            {
              Identity = new NativeWorkflowIdentity
              {
                DocumentFingerprint = "doc",
                ModelFileType = "总平模型",
                RulePackageId = "HBR-WUHAN-PLANNING",
                RulePackageVersion = "1.0.0",
                RulePackageSha256 = new string('a', 64)
              },
              ConfirmedElements = new[] { element }
            });

        Assert.Equal(NativeStage02GeometryCheckState.Passed,
          Assert.Single(evaluation.Checks).State);
      }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void Missing_zero_or_non_finite_area_is_blocked_and_bbox_is_not_area(
      double? area)
    {
      NativeStage02ElementSnapshot element = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "A",
        ElementId = 1,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        AssignedRoleId = "SITE_TOTAL_LAND",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          BoundingBox = new NativeStage02BoundingBoxEvidence
          {
            Available = true,
            MinXFeet = 0,
            MinYFeet = 0,
            MinZFeet = 0,
            MaxXFeet = 100,
            MaxYFeet = 100,
            MaxZFeet = 10
          },
          ApprovedProjectedAreaSquareMetres = area,
          EvidenceHash = new string('e', 64)
        }
      };
      NativeStage02TaskGeometryEvaluation evaluation =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.TOTAL_LAND",
            GeometryChecks = new[] { "面积大于零" }
          },
          element,
          element.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(),
          new NativeStage02GeometryEvaluationContext
          {
            Identity = new NativeWorkflowIdentity
            {
              DocumentFingerprint = "doc",
              ModelFileType = "总平模型",
              RulePackageId = "HBR-WUHAN-PLANNING",
              RulePackageVersion = "1.0.0",
              RulePackageSha256 = new string('a', 64)
            },
            ConfirmedElements = new[] { element }
          });

      NativeStage02GeometryCheckEvidence check = Assert.Single(evaluation.Checks);
      Assert.Equal(NativeStage02GeometryCheckState.Failed, check.State);
      Assert.NotEqual("BBOX", check.Basis);
    }

    [Fact]
    public void Closed_self_intersection_and_area_evaluators_each_have_green_and_red()
    {
      NativeStage02ElementSnapshot element = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "A",
        ElementId = 1,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        AssignedRoleId = "SITE_TOTAL_LAND",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          ApprovedProjectedAreaSquareMetres = 100,
          PlanarLoopsMetres = new IReadOnlyList<double>[]
          {
            new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 0, 0 }
          },
          EvidenceHash = new string('f', 64)
        }
      };
      NativeStage02GeometryEvaluationContext context =
        new NativeStage02GeometryEvaluationContext
        {
          Identity = new NativeWorkflowIdentity
          {
            DocumentFingerprint = "doc",
            ModelFileType = "总平模型",
            RulePackageId = "HBR-WUHAN-PLANNING",
            RulePackageVersion = "1.0.0",
            RulePackageSha256 = new string('a', 64)
          },
          ConfirmedElements = new[] { element }
        };
      NativeStage02TaskGeometryEvaluation green =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.TOTAL_LAND",
            GeometryChecks = new[] { "边界闭合", "无自交", "面积大于零" }
          }, element, element.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(), context);

      element.Geometry.PlanarLoopsMetres = new IReadOnlyList<double>[]
      {
        new double[] { 0, 0, 10, 10, 0, 10, 10, 0, 1, 0 }
      };
      element.Geometry.ApprovedProjectedAreaSquareMetres = 0;
      NativeStage02TaskGeometryEvaluation red =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.TOTAL_LAND",
            GeometryChecks = new[] { "边界闭合", "无自交", "面积大于零" }
          }, element, element.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(), context);

      Assert.All(green.Checks, value => Assert.Equal(
        NativeStage02GeometryCheckState.Passed, value.State));
      Assert.All(red.Checks, value => Assert.Equal(
        NativeStage02GeometryCheckState.Failed, value.State));
    }

    [Fact]
    public void Contains_duplicate_continuity_and_short_curve_each_have_green_and_red()
    {
      NativeStage02ElementSnapshot subject = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "subject",
        ElementId = 1,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        AssignedRoleId = "SITE_GREEN_OBJECT",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          PlanarLoopsMetres = new IReadOnlyList<double>[]
          {
            new double[] { 1, 1, 2, 1, 2, 2, 1, 2, 1, 1 }
          },
          CurveChainsMetres = new IReadOnlyList<double>[]
          {
            new double[] { 0, 0, 1, 0, 2, 0 }
          },
          ShortCurveToleranceMetres = 0.1,
          EvidenceHash = new string('1', 64)
        }
      };
      NativeStage02ElementSnapshot reference = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "reference",
        ElementId = 2,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        AssignedRoleId = "SITE_NET_LAND",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          PlanarLoopsMetres = new IReadOnlyList<double>[]
          {
            new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 0, 0 }
          },
          EvidenceHash = new string('2', 64)
        }
      };
      NativeStage02GeometryEvaluationContext context =
        new NativeStage02GeometryEvaluationContext
        {
          Identity = new NativeWorkflowIdentity
          {
            DocumentFingerprint = "doc",
            ModelFileType = "总平模型",
            RulePackageId = "HBR-WUHAN-PLANNING",
            RulePackageVersion = "1.0.0",
            RulePackageSha256 = new string('a', 64)
          },
          ConfirmedElements = new[] { subject, reference },
          ManualReviews = Array.Empty<NativeStage02ManualReviewRecord>()
        };
      NativeStage02TaskGeometryEvaluation inside =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.GREEN",
            GeometryChecks = new[] { "绿地不越界" }
          }, subject, subject.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(), context);
      NativeStage02ElementSnapshot road = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "road",
        ElementId = 4,
        Category = "OST_Lines",
        ElementKind = "CurveElement",
        AssignedRoleId = "SITE_ROAD_REDLINE",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          CurveChainsMetres = new IReadOnlyList<double>[]
          {
            new double[] { 0, 0, 1, 0, 2, 0 }
          },
          ShortCurveToleranceMetres = 0.1,
          EvidenceHash = new string('5', 64)
        }
      };
      NativeStage02TaskGeometryEvaluation continuousLong =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.ROAD_REDLINE",
            GeometryChecks = new[] { "曲线连续", "无无效短线" }
          }, road, road.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(), context);
      NativeStage02TaskGeometryEvaluation unique =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.GREEN",
            GeometryChecks = new[] { "绿地不重复统计" }
          }, subject, subject.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(), context);

      subject.Geometry.PlanarLoopsMetres = new IReadOnlyList<double>[]
      {
        new double[] { 9, 9, 11, 9, 11, 11, 9, 11, 9, 9 }
      };
      road.Geometry.CurveChainsMetres = new IReadOnlyList<double>[]
      {
        new double[] { 0, 0, 0.01, 0 },
        new double[] { 2, 0, 3, 0 }
      };
      NativeStage02ElementSnapshot duplicate = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "duplicate",
        ElementId = 3,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        AssignedRoleId = "SITE_GREEN_OBJECT",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          PlanarLoopsMetres = new IReadOnlyList<double>[]
          {
            new double[] { 9, 9, 11, 9, 11, 11, 9, 11, 9, 9 }
          },
          EvidenceHash = new string('3', 64)
        }
      };
      context.ConfirmedElements = new[] { subject, reference, duplicate };
      NativeStage02TaskGeometryEvaluation outside =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.GREEN",
            GeometryChecks = new[] { "绿地不越界" }
          }, road, road.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(), context);
      NativeStage02TaskGeometryEvaluation discontinuousShort =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.ROAD_REDLINE",
            GeometryChecks = new[] { "曲线连续", "无无效短线" }
          }, subject, subject.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(), context);
      NativeStage02TaskGeometryEvaluation duplicated =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.GREEN",
            GeometryChecks = new[] { "绿地不重复统计" }
          }, subject, subject.Geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(), context);

      Assert.Equal(NativeStage02GeometryCheckState.Passed, Assert.Single(inside.Checks).State);
      Assert.All(continuousLong.Checks, value => Assert.Equal(NativeStage02GeometryCheckState.Passed, value.State));
      Assert.Equal(NativeStage02GeometryCheckState.Passed, Assert.Single(unique.Checks).State);
      Assert.Equal(NativeStage02GeometryCheckState.Failed, Assert.Single(outside.Checks).State);
      Assert.All(discontinuousShort.Checks, value => Assert.Equal(NativeStage02GeometryCheckState.Failed, value.State));
      Assert.Equal(NativeStage02GeometryCheckState.Failed, Assert.Single(duplicated.Checks).State);
    }

    [Fact]
    public void Property_evaluators_use_exact_area_tolerance_and_finite_green_formula()
    {
      Guid areaGuid = new Guid("6cc053e3-891d-51b1-b861-af498733f73a");
      Guid factorGuid = new Guid("a99a0961-05fe-56fd-b8a0-865410bfe72f");
      NativeStage02ElementSnapshot element = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "A",
        ElementId = 1,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        AssignedRoleId = "SITE_GREEN_OBJECT",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          ApprovedProjectedAreaSquareMetres = 100,
          EvidenceHash = new string('4', 64)
        }
      };
      var parameters = new Dictionary<Guid, NativeStage02ParameterEvidence>
      {
        [areaGuid] = new NativeStage02ParameterEvidence
        {
          ParameterGuid = areaGuid,
          Exists = true,
          CurrentCanonicalValue = "100.05"
        },
        [factorGuid] = new NativeStage02ParameterEvidence
        {
          ParameterGuid = factorGuid,
          Exists = true,
          CurrentCanonicalValue = "0.8"
        }
      };
      NativeStage02TaskGeometryEvaluation green =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.GREEN",
            PropertyChecks = new[] { "折算面积计算有效" }
          }, element, element.Geometry, parameters,
          new NativeStage02GeometryEvaluationContext
          {
            Identity = new NativeWorkflowIdentity
            {
              DocumentFingerprint = "doc",
              ModelFileType = "总平模型",
              RulePackageId = "HBR-WUHAN-PLANNING",
              RulePackageVersion = "1.0.0",
              RulePackageSha256 = new string('a', 64)
            },
            ConfirmedElements = new[] { element }
          });
      parameters[factorGuid].CurrentCanonicalValue = "NaN";
      NativeStage02TaskGeometryEvaluation red =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.GREEN",
            PropertyChecks = new[] { "折算面积计算有效" }
          }, element, element.Geometry, parameters,
          new NativeStage02GeometryEvaluationContext
          {
            Identity = new NativeWorkflowIdentity
            {
              DocumentFingerprint = "doc",
              ModelFileType = "总平模型",
              RulePackageId = "HBR-WUHAN-PLANNING",
              RulePackageVersion = "1.0.0",
              RulePackageSha256 = new string('a', 64)
            },
            ConfirmedElements = new[] { element }
          });

      Assert.Equal(NativeStage02GeometryCheckState.Passed, Assert.Single(green.Checks).State);
      Assert.Equal(NativeStage02GeometryCheckState.Failed, Assert.Single(red.Checks).State);
    }

    [Fact]
    public void Host_area_only_keeps_area_check_usable_while_topology_stays_blocked()
    {
      var geometry = new NativeStage02GeometryEvidence
      {
        ApprovedProjectedAreaSquareMetres = 12.5,
        ProjectedAreaSource = "HOST_AREA_COMPUTED"
      };
      NativeStage02RevitGeometryEvidenceService.ApplyCaptureStatus(
        geometry,
        supportedSurface: true,
        hasAcceptedPlanarFace: false,
        hasApprovedHostArea: true,
        hasCurveTopology: false);
      var element = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "A",
        ElementId = 1,
        Category = "OST_BuildingPad",
        ElementKind = "BuildingPad",
        AssignedRoleId = "SITE_TOTAL_LAND",
        IsModelElement = true,
        Geometry = geometry
      };

      NativeStage02TaskGeometryEvaluation evaluation =
        NativeStage02GeometryEvidencePolicy.Evaluate(
          new NativeTaskDefinition
          {
            TaskId = "SITE.TOTAL_LAND",
            GeometryChecks = new[] { "边界闭合", "面积大于零" }
          },
          element,
          geometry,
          new Dictionary<Guid, NativeStage02ParameterEvidence>(),
          new NativeStage02GeometryEvaluationContext());

      Assert.Equal(
        "GEOMETRY_TOPOLOGY_MISSING",
        evaluation.Checks.Single(value => value.RuleText == "边界闭合").Code);
      Assert.Equal(
        NativeStage02GeometryCheckState.Passed,
        evaluation.Checks.Single(value => value.RuleText == "面积大于零").State);
      Assert.Equal(string.Empty, geometry.CaptureCode);
    }

    [Fact]
    public void Manual_review_seals_the_full_subject_and_reference_set_for_its_check()
    {
      var subjectOne = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "subject-1",
        ElementId = 1,
        Category = "OST_Lines",
        ElementKind = "CurveElement",
        AssignedRoleId = "SITE_ROAD_CENTERLINE",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          EvidenceHash = new string('1', 64)
        }
      };
      var subjectTwo = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "subject-2",
        ElementId = 2,
        Category = "OST_Lines",
        ElementKind = "CurveElement",
        AssignedRoleId = "SITE_ROAD_CENTERLINE",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          EvidenceHash = new string('2', 64)
        }
      };
      var reference = new NativeStage02ElementSnapshot
      {
        DocumentFingerprint = "doc",
        UniqueId = "reference",
        ElementId = 3,
        Category = "OST_Lines",
        ElementKind = "CurveElement",
        AssignedRoleId = "SITE_ROAD_REDLINE",
        IsModelElement = true,
        Geometry = new NativeStage02GeometryEvidence
        {
          EvidenceHash = new string('3', 64)
        }
      };
      var task = new NativeTaskDefinition
      {
        TaskId = "SITE.ROAD_CENTERLINE",
        GeometryChecks = new[] { "与道路红线关系有效" }
      };
      var context = new NativeStage02GeometryEvaluationContext
      {
        Identity = new NativeWorkflowIdentity
        {
          DocumentFingerprint = "doc",
          ModelFileType = "总平模型",
          RulePackageId = "HBR-WUHAN-PLANNING",
          RulePackageVersion = "1.0.0",
          RulePackageSha256 = new string('a', 64)
        },
        ConfirmedElements = new[] { subjectOne, subjectTwo, reference },
        ScopeComplete = true
      };
      string checkId = NativeReportingRuleCatalog.Current
        .GetChecks("总平模型")
        .Single(value => value.TaskId == task.TaskId
          && value.RuleText == "与道路红线关系有效")
        .CheckId;

      NativeStage02ManualReviewRecord record =
        NativeStage02GeometryEvidencePolicy.SealManualReview(
          task,
          subjectOne,
          context,
          new NativeStage02ManualReviewCommand
          {
            CheckId = checkId,
            Decision = "APPROVED",
            Reviewer = "reviewer",
            Basis = "current full-model review"
          },
          "2026-08-17T00:00:00.0000000Z");
      context.ManualReviews = new[] { record };

      Assert.Equal(
        new[] { "reference", "subject-1", "subject-2" },
        record.ElementUniqueIds);
      Assert.All(
        new[] { subjectOne, subjectTwo },
        subject => Assert.Equal(
          NativeStage02GeometryCheckState.ManualReviewApproved,
          Assert.Single(NativeStage02GeometryEvidencePolicy.Evaluate(
            task,
            subject,
            subject.Geometry,
            new Dictionary<Guid, NativeStage02ParameterEvidence>(),
            context).Checks).State));
    }

    [Fact]
    public void Geometry_evidence_contains_no_project_metric_outcomes()
    {
      string members = string.Join("|", typeof(NativeStage02GeometryEvidence)
        .GetProperties(System.Reflection.BindingFlags.Instance
          | System.Reflection.BindingFlags.NonPublic)
        .Select(value => value.Name));

      Assert.DoesNotContain("GrossFloorArea", members);
      Assert.DoesNotContain("BuildingDensity", members);
      Assert.DoesNotContain("FloorAreaRatio", members);
      Assert.DoesNotContain("GreenRate", members);
      Assert.DoesNotContain("ParkingCount", members);
    }
  }
}
