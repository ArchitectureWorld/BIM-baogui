using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal static class SelectionService
    {
        internal static SelectionReadResult Read(Document doc, int[] ids, string stage, string kind)
        {
            if (stage != "报规" && stage != "施工图") throw new InvalidOperationException("请先人工确认阶段。");
            if (kind != "总图" && kind != "单体建筑") throw new InvalidOperationException("请先确认模型类型。");
            var rules=CaseWorkflow.ForModel(stage,kind);var rows=new List<ModelElementCheck>();var excluded=new List<string>();string[] common=null;
            foreach(var id in ids.Distinct())
            {
                var e=doc.GetElement(new ElementId(id));
                if(e==null){excluded.Add(id+"：已删除，请重新选择");continue;}
                var row=CaseModelReader.Inventory(doc,e);
                var slots=rules.Where(r=>r.Binding=="实例" && r.Entities.Contains(row.Entity)
                    && !(e is ElementType) && !(e is ProjectInfo) && !(e is Level) && !e.ViewSpecific
                    && (!CaseWorkflow.AreaSlots.Contains(r.Slot) || e is Area)).Select(r=>r.Slot).Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();
                if(e is Area && !string.IsNullOrEmpty(row.CoverageNote))slots=new string[0];
                if(slots.Length==0){excluded.Add(id+" / "+e.Name+"："+(row.CoverageNote ?? (e.Category?.Id.IntegerValue==(int)BuiltInCategory.OST_Parking?"停车属性请选择对应的面积（Area）":"当前阶段或模型类型没有适用的业务载体映射")));continue;}
                CaseModelReader.ReadFields(doc,e,row,rules,kind);rows.Add(row);common=common==null?slots:common.Intersect(slots).ToArray();
            }
            return new SelectionReadResult {SelectedCount=ids.Distinct().Count(),Excluded=excluded.ToArray(),Slots=common??new string[0],
                Snapshot=new ModelSnapshot {Scope="所选构件",DocumentKey=ModelReader.Key(doc),Title=doc.Title,Stage=stage,RequestedKind=kind,ModelKind=kind,
                    Complete=true,ScanId=Guid.NewGuid().ToString("N"),CompletedUtc=DateTime.UtcNow.ToString("O"),Elements=rows.ToArray()}};
        }
        internal static EditPreview Classify(Document doc,ModelSnapshot snapshot,string slot)
        {
            var current=Read(doc,snapshot.Elements.Select(e=>e.Id).ToArray(),snapshot.Stage,snapshot.ModelKind);
            if(!current.Slots.Contains(slot))throw new InvalidOperationException("所选构件不共同支持该业务类目；停车信息需要面积（Area）载体。");
            var draft=new SelectionDraft(snapshot);draft.SetClassification(slot,snapshot.Elements);
            return CaseEdits.PreviewSelection(doc,snapshot,draft.BuildRequests());
        }
        internal static EditPreview Write(Document doc,ModelSnapshot snapshot,EditRequest[] requests)
        {
            var preview=CaseEdits.PreviewSelection(doc,snapshot,requests);
            if(preview.Conflicts.Length>0)throw new InvalidOperationException(string.Join("\n",preview.Conflicts));
            if(preview.Changes.Any(c=>c.Binding!="实例" || !snapshot.Elements.Any(e=>e.Id==c.Id && e.UniqueId==c.OwnerUniqueId)))
                throw new InvalidOperationException("写入范围超出了本次选中的实例。");
            if(preview.Changes.Length>0)CaseEdits.Apply(doc,preview);
            return preview;
        }
    }
}
