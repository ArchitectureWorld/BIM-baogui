using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class SelectionDraftTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static void Assert(bool value) { if (!value) throw new Exception("selection draft assertion failed"); }
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Flags).Invoke(value, args);
    static object Get(object value, string name) => value.GetType().GetProperty(name, Flags).GetValue(value);
    static ModelElementCheck Row(int id, string value, string suggestion = null, string raw = "Real", string binding = "实例") => new ModelElementCheck {
        Id=id, UniqueId="selected-"+id, Entity="IfcSlab", Name="不规则命名"+id,
        Fields=new[] {new FieldCheck {PropertySet="Pset_绿地信息属性集",Property="投影面积",Source="绿地投影面积",RawType=raw,Binding=binding,
            OwnerId=id,OwnerUniqueId="selected-"+id,SourceSignature="source-"+id,Value=value,SuggestedValue=suggestion,
            State=value==null?"未填写":"有效",Editable=binding=="实例",Origin=binding=="类型"?"type":"instance"}} };
    static object Draft(ModelElementCheck[] original, ModelElementCheck[] proposed)
    {
        var scope=typeof(ModelSnapshot).GetProperty("Scope"); Assert(scope!=null);
        var snapshot=new ModelSnapshot {DocumentKey="selection-document",Stage="报规",ModelKind="总图",Complete=true,ScanId="selection-1",Elements=original};
        scope.SetValue(snapshot,"所选构件");
        var type=typeof(ModelSnapshot).Assembly.GetType("Hifc.Revit.Management.SelectionDraft"); Assert(type!=null);
        var draft=Activator.CreateInstance(type,Flags,null,new object[]{snapshot},null);
        Call(draft,"SetClassification","绿地",proposed); return draft;
    }
    static object[] Fields(object draft) => ((IEnumerable)Get(draft,"Fields")).Cast<object>().ToArray();
    static EditRequest[] Requests(object draft) => (EditRequest[])Call(draft,"BuildRequests");
    internal static void Run(Action<string,Action> check)
    {
        check("selection_multiple_values_are_not_written_as_placeholder",()=>{
            var rows=new[]{Row(1,"10"),Row(2,"20")};var d=Draft(rows,rows);var f=Fields(d).Single();
            Assert((string)Get(f,"Value")=="多个值"); f.GetType().GetProperty("Value").SetValue(f,"多个值");
            Assert(Requests(d).Length==1 && Requests(d)[0].Kind=="分类");
        });
        check("selection_manual_edit_targets_only_captured_instances",()=>{
            var rows=new[]{Row(1,"10"),Row(2,"20")};var d=Draft(rows,rows);var f=Fields(d).Single();f.GetType().GetProperty("Value").SetValue(f,"32.5");
            var r=Requests(d).Single(x=>x.Kind=="赋值");Assert(r.Ids.SequenceEqual(new[]{1,2})&&r.Value=="32.5"&&r.Baselines[1].Value=="20"&&r.Baselines[1].ElementUniqueId=="selected-2");
        });
        check("selection_different_automatic_areas_remain_per_instance",()=>{
            var rows=new[]{Row(1,null,"10"),Row(2,null,"20")};var d=Draft(rows,rows);
            Assert((string)Get(Fields(d).Single(),"Value")=="多个值" && Requests(d).All(r=>r.Kind=="分类"));
        });
        check("selection_boolean_false_is_an_explicit_batch_value",()=>{
            var rows=new[]{Row(1,"1",raw:"Boolean"),Row(2,"0",raw:"Boolean")};var d=Draft(rows,rows);var f=Fields(d).Single();
            f.GetType().GetProperty("Value").SetValue(f,"否");Assert(Requests(d).Single(r=>r.Kind=="赋值").Value=="0");
        });
        check("selection_type_values_cannot_modify_unselected_instances",()=>{
            var rows=new[]{Row(1,"10",binding:"类型")};var d=Draft(rows,rows);var f=Fields(d).Single();Assert(!(bool)Get(f,"Editable"));
            bool rejected=false;try{f.GetType().GetProperty("Value").SetValue(f,"20");}catch(TargetInvocationException){rejected=true;}Assert(rejected&&Requests(d).Length==1);
        });
        check("selection_read_cannot_be_presented_as_full_project_check",()=>{
            var rows=new[]{Row(1,"10")};var d=Draft(rows,rows);var snapshot=(ModelSnapshot)Get(d,"Snapshot");
            var fake=new FakeSession {Snapshot=snapshot};var w=new ManagerWindow(fake,System.IO.Path.Combine(System.IO.Path.GetTempPath(),"hifc-selection-tests"));fake.Window=w;
            bool rejected=false;try{w.Accept(snapshot);}catch(InvalidOperationException){rejected=true;}finally{w.Close();}Assert(rejected);
        });
    }
}
