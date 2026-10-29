using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class WorkbookStagingTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Stage(ManagerWindow window,ReportingWorkbookDecision decision)
    {
        var method=typeof(ManagerWindow).GetMethod("StageWorkbook",Flags);
        if(method==null)throw new Exception("Workbook staging not integrated into management workflow");
        try{method.Invoke(window,new object[]{decision});}catch(TargetInvocationException error){throw error.InnerException;}
    }
    static ReportingWorkbookDecision Decision(ModelElementCheck row,string value="新用地名称")
    {
        var f=row.Fields[0];
        return new ReportingWorkbookDecision {TableId="test-table",Changes=new[]{new ReportingWorkbookChange {ElementId=row.Id,ElementUniqueId=row.UniqueId,OwnerUniqueId=f.OwnerUniqueId,
            PropertySet=f.PropertySet,Property=f.Property,Source=f.Source,Binding=f.Binding,RawType=f.RawType,CurrentValue=f.Value,CurrentState=f.State,SourceSignature=f.SourceSignature,Value=value,CanSelect=true,Selected=true}},
            References=new[]{new ReportingWorkbookReference {Field="项目资料",Value="原表记录",Status="仅保存资料"}}};
    }
    public static void Run(Action<string,Action> check,string output)
    {
        check("workbook_numeric_area_keeps_metric_unit_until_revit_write",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var row=fake.Snapshot.Elements[0];var field=row.Fields[0];field.Property="投影面积";field.RawType="Real";field.Value="100";
            var w=new ManagerWindow(fake,Path.Combine(output,"wb-units"));fake.Window=w;
            try{w.StageSelector.SelectedItem="报规";w.Accept(fake.Snapshot);Stage(w,Decision(row,"123.45"));var request=w.DraftRequests.Single();
                Assert((string)request.GetType().GetProperty("ValueUnit",Flags)?.GetValue(request)=="m²","Excel square meters became an unqualified project-unit number");
            }finally{w.Close();}
        });
        check("workbook_restage_deselection_removes_only_import_owned_drafts",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var row=fake.Snapshot.Elements[0];var w=new ManagerWindow(fake,Path.Combine(output,"wb-restage"));fake.Window=w;
            try{w.StageSelector.SelectedItem="报规";w.Accept(fake.Snapshot);Stage(w,Decision(row));Stage(w,new ReportingWorkbookDecision{TableId="test-table",References=new[]{new ReportingWorkbookReference{Field="资料",Value="保留"}}});
                Assert(w.DraftRequests.Length==0,"Deselected Excel field remains as a hidden write request");
                Stage(w,Decision(row));w.ElementGrid.SelectedItem=row;w.StageValue(row.Fields[0],"人工修改");
                Stage(w,new ReportingWorkbookDecision{TableId="test-table",References=new[]{new ReportingWorkbookReference{Field="资料",Value="保留"}}});
                Assert(w.DraftRequests.Single().Value=="人工修改","Restaging erased a later manual edit");
            }finally{w.Close();}
        });
        check("workbook_matching_manual_draft_keeps_manual_ownership",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var row=fake.Snapshot.Elements[0];var w=new ManagerWindow(fake,Path.Combine(output,"wb-manual-owner"));fake.Window=w;
            try{w.StageSelector.SelectedItem="报规";w.Accept(fake.Snapshot);w.ElementGrid.SelectedItem=row;w.StageValue(row.Fields[0],"新用地名称");Stage(w,Decision(row));
                Stage(w,new ReportingWorkbookDecision{TableId="test-table",References=new[]{new ReportingWorkbookReference{Field="资料",Value="保留"}}});
                Assert(w.DraftRequests.Single().Value=="新用地名称","Identical Excel selection stole and removed manual draft");
            }finally{w.Close();}
        });
        check("workbook_shared_same_value_retains_other_import_intent",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var row=fake.Snapshot.Elements[0];var w=new ManagerWindow(fake,Path.Combine(output,"wb-shared-owner"));fake.Window=w;
            try{w.StageSelector.SelectedItem="报规";w.Accept(fake.Snapshot);Stage(w,Decision(row));var second=Decision(row);second.TableId="second-table";Stage(w,second);
                Stage(w,new ReportingWorkbookDecision{TableId="second-table",References=new[]{new ReportingWorkbookReference{Field="资料",Value="保留"}}});
                Assert(w.DraftRequests.Single().Value=="新用地名称","Second import cancellation removed first import intent");
                Stage(w,second);
                Stage(w,new ReportingWorkbookDecision{TableId="test-table",References=new[]{new ReportingWorkbookReference{Field="资料",Value="保留"}}});
                Assert(w.DraftRequests.Single().Value=="新用地名称","First import cancellation removed second import intent");
                Stage(w,new ReportingWorkbookDecision{TableId="second-table",References=new[]{new ReportingWorkbookReference{Field="资料",Value="保留"}}});Assert(w.DraftRequests.Length==0,"Last import cancellation retained hidden value");
            }finally{w.Close();}
        });
        check("bulk_area_edit_uses_one_unit_for_imported_and_unimported_targets",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var row=fake.Snapshot.Elements[0];var f=row.Fields[0];f.Property="投影面积";f.RawType="Real";f.Value=f.DisplayValue="100";
            var other=new ModelElementCheck{Id=200,UniqueId="other-area",Name="另一面积",Entity=row.Entity,Slot=row.Slot,Classified=true,Fields=new[]{new FieldCheck{PropertySet=f.PropertySet,Property=f.Property,Source=f.Source,RawType=f.RawType,OwnerId=200,OwnerUniqueId="other-area",Binding=f.Binding,State=f.State,Value="200",Editable=true}}};
            fake.Snapshot.Elements=new[]{row,other};var w=new ManagerWindow(fake,Path.Combine(output,"wb-bulk-unit"));fake.Window=w;
            try{w.StageSelector.SelectedItem="报规";w.Accept(fake.Snapshot);Stage(w,Decision(row,"123.45"));w.ElementGrid.SelectedItems.Add(other);w.ElementGrid.SelectedItems.Add(row);w.StageValue(f,"50");
                Assert(w.DraftRequests.Length==2&&w.DraftRequests.All(r=>(string)r.GetType().GetProperty("ValueUnit",Flags).GetValue(r)=="m²"),"One bulk edit uses different physical unit conventions");
                w.StageValue(f,"100");Assert(w.DraftRequests.Length==2&&w.DraftRequests.All(r=>r.Value=="100"),"Canonical metric edit was canceled because raw display text matched");
            }finally{w.Close();}
        });
        check("workbook_selection_stages_in_existing_property_drafts",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var w=new ManagerWindow(fake,Path.Combine(output,"wb-stage"));fake.Window=w;
            try{w.StageSelector.SelectedItem="报规";w.Accept(fake.Snapshot);Stage(w,Decision(fake.Snapshot.Elements[0]));
                Assert(w.DraftRequests.Single().Value=="新用地名称"&&w.CanApply&&fake.Written==null,"Import bypassed stage/write workflow");
                bool blocked=false;try{w.EnsureExportReady();}catch(InvalidOperationException){blocked=true;}Assert(blocked,"Pending workbook did not block export");
            }finally{w.Close();}
        });
        check("workbook_metadata_only_import_is_writable_and_persisted",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var directory=Path.Combine(output,"wb-meta");var w=new ManagerWindow(fake,directory);fake.Window=w;
            try{w.StageSelector.SelectedItem="报规";w.Accept(fake.Snapshot);Stage(w,new ReportingWorkbookDecision {TableId="references-only",References=new[]{new ReportingWorkbookReference{Field="建设单位",Value="资料单位"}}});
                Assert(w.CanApply&&w.DraftRequests.Length==0,"Reference-only imports cannot be saved");w.WriteChanges();Assert(fake.Written!=null&&fake.Written.Length==0,"Reference-only import requires fabricated property edit");
                var state=new ManagementStateStore(directory,fake.Snapshot.DocumentKey).Load();
                var property=state.GetType().GetProperty("PendingWorkbooks",Flags);Assert(property!=null&&((Array)property.GetValue(state)).Length==1,"Workbook draft lost on close/reopen");
            }finally{w.Close();}
        });
        check("workbook_stale_or_conflicting_selection_cannot_partially_stage",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var w=new ManagerWindow(fake,Path.Combine(output,"wb-stale"));fake.Window=w;
            try{w.StageSelector.SelectedItem="报规";w.Accept(fake.Snapshot);var decision=Decision(fake.Snapshot.Elements[0]);decision.Changes[0].SourceSignature="stale";
                bool rejected=false;try{Stage(w,decision);}catch(InvalidOperationException){rejected=true;}Assert(rejected&&w.DraftRequests.Length==0,"Stale workbook overwrote a model field");
            }finally{w.Close();}
        });
        check("workbook_blank_never_erases_existing_value",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var w=new ManagerWindow(fake,Path.Combine(output,"wb-blank"));fake.Window=w;
            try{w.StageSelector.SelectedItem="报规";w.Accept(fake.Snapshot);Stage(w,Decision(fake.Snapshot.Elements[0],""));Assert(w.DraftRequests.Length==0,"Blank import erased model value");}finally{w.Close();}
        });
    }
}
