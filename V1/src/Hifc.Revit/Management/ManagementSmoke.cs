using System;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal static class ManagementSmoke
    {
        internal static object Run(Document doc, Element land, Element balcony, Element unknown)
        {
            var partial = new ScanJob(doc); partial.ReadNext(doc, 1);
            bool partialBlocked = false;
            try { partial.Snapshot(doc); } catch (InvalidOperationException) { partialBlocked = true; }
            while (!partial.Complete) partial.ReadNext(doc, 1);
            var before = partial.Snapshot(doc);
            if (before.Elements.Length != 3 || before.Elements.Count(e => !e.Classified) != 1) throw new InvalidOperationException("Whole-project scan did not include the unselected specimen.");
            var edit = new EditRequest { Kind = "赋值", Ids = new[] { land.Id.IntegerValue }, Source = "规划净用地投影面积", Value = "99.75" };
            var preview = ModelEdits.Preview(doc, edit); ModelEdits.Apply(doc, preview);
            bool writePassed = land.GetParameters(edit.Source).Single().AsString() == "99.75";
            edit = new EditRequest { Kind = "赋值", Ids = new[] { land.Id.IntegerValue }, Source = edit.Source, Value = "77" };
            var stale = ModelEdits.Preview(doc, edit);
            using (var tx = new Transaction(doc, "HIFC management stale-preview specimen")) { tx.Start(); land.GetParameters(edit.Source).Single().Set("64"); tx.Commit(); }
            bool staleBlocked = false;
            try { ModelEdits.Apply(doc, stale); } catch (InvalidOperationException) { staleBlocked = true; }
            if (land.GetParameters(edit.Source).Single().AsString() != "64") throw new InvalidOperationException("Stale preview changed the source value.");
            var classify = ModelEdits.Preview(doc, new EditRequest { Kind = "分类", Ids = new[] { unknown.Id.IntegerValue }, Slot = "阳台" });
            ModelEdits.Apply(doc, classify);
            bool classificationPassed = unknown.GetParameters("类目").Single().AsString() == "阳台";
            var absentBoolean = ModelReader.Read(doc, unknown).Fields.Single(f => f.Property == "是否封闭");
            bool unsetPreserved = absentBoolean.State == "未填写";
            bool falsePreserved = ModelReader.Read(doc, balcony).Fields.Single(f => f.Property == "是否封闭").Valid;
            using (var tx = new Transaction(doc, "HIFC management restore IFC probe")) { tx.Start(); land.GetParameters(edit.Source).Single().Set("40.5"); tx.Commit(); }
            if (!partialBlocked || !writePassed || !staleBlocked || !classificationPassed || !unsetPreserved || !falsePreserved)
                throw new InvalidOperationException("Management native host checks failed.");
            return new { allPassed = true, scan_unselected_instances = before.Elements.Length, partial_scan_blocked = partialBlocked,
                instance_write_verified = writePassed, stale_preview_blocked = staleBlocked, classification_prepared = classificationPassed,
                unset_boolean_preserved = unsetPreserved, false_preserved = falsePreserved,
                scope = "Real Revit API services in fresh unsaved specimen; ExternalEvent/UI workflow verified separately" };
        }
    }
}
