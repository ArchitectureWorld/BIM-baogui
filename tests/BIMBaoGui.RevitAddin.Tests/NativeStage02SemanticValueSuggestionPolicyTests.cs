using System;
using BIMBaoGui.RevitAddin.Stage02;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage02SemanticValueSuggestionPolicyTests
  {
    [Fact]
    public void GreenClassificationUsesOnlyApprovedSystemFixedValue()
    {
      NativeStage02SemanticSuggestionDecision decision = Evaluate(
        "SYSTEM_FIXED",
        new[] { "绿地" });

      Assert.Equal(NativeStage02SemanticSuggestionStatus.Suggested, decision.Status);
      Assert.Equal("绿地", decision.CanonicalValue);
      Assert.Equal("SystemFixed", decision.Source);
    }

    [Fact]
    public void ExactGreenTypeNameCanBeSuggested()
    {
      NativeStage02SemanticSuggestionDecision decision = Evaluate(
        "EXACT_ENUM_FROM_TYPE",
        GreenTypes,
        " 集中绿地 ");

      Assert.Equal(NativeStage02SemanticSuggestionStatus.Suggested, decision.Status);
      Assert.Equal("集中绿地", decision.CanonicalValue);
    }

    [Fact]
    public void SimilarButUnapprovedTypeNameIsNeverGuessed()
    {
      NativeStage02SemanticSuggestionDecision decision = Evaluate(
        "EXACT_ENUM_FROM_TYPE",
        GreenTypes,
        "集中绿地-01");

      Assert.Equal(NativeStage02SemanticSuggestionStatus.PendingInput, decision.Status);
      Assert.Empty(decision.CanonicalValue);
    }

    [Fact]
    public void ApprovedPositiveAreaCanBeSuggestedInSquareMetres()
    {
      NativeStage02SemanticSuggestionDecision decision = Evaluate(
        "APPROVED_REVIT_AREA",
        Array.Empty<string>(),
        string.Empty,
        123.456);

      Assert.Equal(NativeStage02SemanticSuggestionStatus.Suggested, decision.Status);
      Assert.Equal("123.456", decision.CanonicalValue);
      Assert.Equal("ApprovedRevitAreaM2", decision.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void MissingOrInvalidAreaStaysPending(double? area)
    {
      NativeStage02SemanticSuggestionDecision decision = Evaluate(
        "APPROVED_REVIT_AREA",
        Array.Empty<string>(),
        string.Empty,
        area);

      Assert.Equal(NativeStage02SemanticSuggestionStatus.PendingInput, decision.Status);
      Assert.Empty(decision.CanonicalValue);
    }

    [Fact]
    public void GreenConversionFactorNeverReceivesInventedDefault()
    {
      NativeStage02SemanticSuggestionDecision decision = Evaluate(
        "PENDING_INPUT",
        Array.Empty<string>());

      Assert.Equal(NativeStage02SemanticSuggestionStatus.PendingInput, decision.Status);
      Assert.Empty(decision.CanonicalValue);
    }

    private static readonly string[] GreenTypes =
    {
      "集中绿地",
      "宅旁绿地",
      "水域",
      "屋顶绿地",
      "口袋公园",
      "道路绿地",
      "附属绿地",
      "其它绿地"
    };

    private static NativeStage02SemanticSuggestionDecision Evaluate(
      string kind,
      string[] approved,
      string typeName = "",
      double? area = null)
    {
      return NativeStage02SemanticValueSuggestionPolicy.Evaluate(
        kind,
        approved,
        typeName,
        area);
    }
  }
}
