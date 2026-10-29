using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class PlanningWindowTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Assert(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static object Get(object o,string name)=>o.GetType().GetProperty(name,F).GetValue(o);
    static Window Dialog(string kind="总图")
    {
        var type=typeof(ManagerWindow).Assembly.GetType("Hifc.Revit.Management.PlanningWindow");
        Assert(type!=null,"Project calculation settings UI missing");
        var rows=new[]{1,2}.Select(id=>new ModelElementCheck{Id=id,UniqueId="land"+id,Name="用地红线",
            Entity="IfcSlab",Category="楼板",ProjectionKey="same",ProjectedAreaM2=100}).ToArray();
        return (Window)Activator.CreateInstance(type,F,null,new object[]{new ModelSnapshot{ModelKind=kind,Elements=rows},new PlanningSettings()},null);
    }
    public static void Run(Action<string,Action> check,string output)
    {
        check("calculation_settings_do_not_preconfirm_generic_land",()=>{
            var w=Dialog();try {
                w.Show();Program.Screenshot(w,Path.Combine(output,"planning-scope.png"));
                var rows=((IEnumerable)Get(w,"RoleRows")).Cast<object>().ToArray();
                Assert(rows.Length==2 && rows.All(r=>!(bool)Get(r,"Included")),"generic net-land candidates preconfirmed");
            }finally{w.Close();}
        });
        check("calculation_settings_reject_duplicate_projected_net_land",()=>{
            var w=Dialog();try {
                foreach(var row in ((IEnumerable)Get(w,"RoleRows")).Cast<object>())row.GetType().GetProperty("Included").SetValue(row,true);
                bool blocked=false;
                try{w.GetType().GetMethod("BuildSettings",F).Invoke(w,null);}
                catch(TargetInvocationException ex){blocked=ex.InnerException is InvalidOperationException && ex.InnerException.Message.Contains("重复");}
                Assert(blocked,"two identical net-land surfaces accepted");
            }finally{w.Close();}
        });
        check("shared_project_values_have_no_invented_initial_defaults",()=>{
            var w=Dialog("单体建筑");try {
                w.Show();w.UpdateLayout();
                var rows=((IEnumerable)Get(w,"ValueRows")).Cast<object>().ToArray();
                Assert(rows.Any(r=>(string)Get(r,"Property")=="计容系数") && rows.All(r=>string.IsNullOrEmpty((string)Get(r,"Value"))),"template business defaults leaked into model");
            }finally{w.Close();}
        });
        check("calculation_settings_and_shared_values_persist_per_model",()=>{
            var directory=Path.Combine(output,"planning-profiles");
            var state=new ManagementState {DocumentKey="planning-A",Planning=new PlanningSettings()};
            state.Planning.CommonValues["建筑区域|计算系数"]="1";
            state.Planning.Roles=new[]{new PlanningRoleChoice{UniqueId="a",Slot="建筑区域",Identity="evidence"}};
            var store=new ManagementStateStore(directory,state.DocumentKey);store.Save(state);
            Assert(store.Load().Planning.Roles.Single().Identity=="evidence" &&
                store.Load().Planning.CommonValues["建筑区域|计算系数"]=="1","project settings did not persist");
            Assert(new ManagementStateStore(directory,"other-model").Load().Planning.CommonValues.Count==0,"settings leaked between projects");
        });
    }
}
