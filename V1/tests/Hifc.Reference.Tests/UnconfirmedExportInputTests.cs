using System;
using System.Linq;
using Hifc.Reference;

internal static class UnconfirmedExportInputTests
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static ModelElementCheck BalconyCandidate(int id)
    {
        return new ModelElementCheck
        {
            Id = id,
            UniqueId = "unconfirmed-" + id,
            Name = "设备平台",
            Entity = "IfcSlab",
            Classified = false,
            Candidates = new[] { new ClassificationCandidate { Slot = "阳台", CanPrepare = true } },
            SuggestedSlots = new[] { "阳台" },
            Fields = new[] { "间距审查", "退让审查", "是否有顶", "是否为露台", "是否封闭" }
                .Select(name => InputReviewTests.Field("阳台", name, "0", "Boolean")).ToArray()
        };
    }

    public static void Run(Action<string, Action> check)
    {
        check("unconfirmed_balcony_candidates_do_not_impose_nine_missing_fields_each", () =>
        {
            var rows = new[] { 400469, 401296, 408676, 414999 }.Select(BalconyCandidate).ToArray();
            var review = PlatformInputReview.Build(rows, PlatformRules.ProfileId, "单体建筑");
            Assert(!review.Any(item => item.BlocksExport), "Unconfirmed balcony candidates still block export");
            Assert(review.Count(item => item.Code == "HIFC_UNCONFIRMED_CLASSIFICATION") == 4,
                "Each pending classification must remain visible");
            Assert(rows.All(row => row.Slot == null && !row.Classified && row.Fields[0].Value == "0"),
                "Review changed source values or assigned a class");
        });

        check("confirmed_balcony_keeps_its_required_input_checks", () =>
        {
            var row = BalconyCandidate(1);
            row.Slot = "阳台";
            row.Slots = new[] { "阳台" };
            row.Classified = true;
            var review = PlatformInputReview.Build(new[] { row }, PlatformRules.ProfileId);
            Assert(review.Count(item => item.BlocksExport) == 9,
                "Explicit balcony classification lost mandatory field checks");
        });

        check("sparse_export_rows_without_candidates_keep_required_input_checks", () =>
        {
            var row = BalconyCandidate(1);
            row.Candidates = new ClassificationCandidate[0];
            row.SuggestedSlots = new string[0];
            Assert(PlatformInputReview.Build(new[] { row }, PlatformRules.ProfileId).Any(item => item.BlocksExport),
                "ProjectExportScope rows were mistaken for unconfirmed candidates");
        });

        check("applied_type_or_business_slots_remain_subject_to_required_checks", () =>
        {
            var row = BalconyCandidate(1);
            row.Slots = new[] { "阳台" };
            Assert(PlatformInputReview.Build(new[] { row }, PlatformRules.ProfileId).Any(item => item.BlocksExport),
                "Applied slot was ignored because Classified was false");
        });

        check("native_building_metadata_cannot_be_exempted_by_candidate_flags", () =>
        {
            var row = BalconyCandidate(1);
            row.Entity = "IfcBuilding";
            row.Fields = new[] { InputReviewTests.Field("建筑技术", "建筑编号") };
            Assert(PlatformInputReview.Build(new[] { row }, PlatformRules.ProfileId)
                .Any(item => item.Property == "建筑编号" && item.BlocksExport),
                "Building metadata requirements disappeared");
        });

        check("confirmed_source_conflicts_still_block_export", () =>
        {
            var row = BalconyCandidate(1);
            row.Slot = "阳台";
            row.Classified = true;
            row.Fields = new[] { InputReviewTests.Field("阳台", "投影面积", "20", "Real") };
            row.Fields[0].State = "有歧义";
            row.Fields[0].Detail = "同名来源值冲突";
            Assert(PlatformInputReview.Build(new[] { row }, PlatformRules.ProfileId)
                .Any(item => item.Code == "HIFC_PLATFORM_SOURCE_CONFLICT" && item.BlocksExport),
                "Confirmed source ambiguity became advisory");
        });
    }
}
