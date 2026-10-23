using System;
using System.IO;
using System.Linq;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class ControlLineTests
{
    static void Assert(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static ModelElementCheck Row(string name)
    {
        var row=new ModelElementCheck {Id=73,UniqueId="net-control-ui",Name="用地红线",TypeName="用地红线",Slot="规划控制线",Entity="IfcBuildingElementProxy",Classified=true};
        row.Fields=new[]{new FieldCheck {Property="名称",Source="规划控制线名称",Value=name},new FieldCheck {Property="红线类型",Source="红线类型"}};
        foreach(var f in row.Fields){f.PropertySet="Pset_规划控制线信息属性集";f.RawType="Label";f.Binding="实例";f.OwnerId=row.Id;f.OwnerUniqueId=row.UniqueId;f.Origin="instance";f.Editable=true;f.State=f.Value==null?"未填写":"有效";AutoFillPolicy.Populate(f,row.Slot,row.Name,null);}
        PlanningInputs.ValidateFields(new[]{row});return row;
    }
    internal static void Run(Action<string,Action> check,string output)
    {
        check("selection_control_assignment_displays_both_rule_values",()=>{
            var row=Row(null);var snapshot=new ModelSnapshot {Scope="所选构件",Stage="报规",ModelKind="总图",Complete=true,Elements=new[]{row}};
            var draft=new SelectionDraft(snapshot);draft.SetClassification("规划控制线",new[]{row});
            Assert(draft.Fields.All(f=>f.Value=="净用地控制红线"),"Selection panel still shows native name or empty type");
        });
        check("selection_control_assignment_displays_correction_over_old_value",()=>{
            var row=Row("用地红线");var snapshot=new ModelSnapshot {Scope="所选构件",Stage="报规",ModelKind="总图",Complete=true,Elements=new[]{row}};
            var draft=new SelectionDraft(snapshot);draft.SetClassification("规划控制线",new[]{row});
            Assert(draft.Fields.All(f=>f.Value=="净用地控制红线"),"Old populated value hides pending rule correction");
            Assert(row.Fields[0].Value=="用地红线","UI mutated source baseline");
        });
        check("manager_control_fields_show_same_rule_values_as_selection_panel",()=>{
            var row=Row("用地红线");var snapshot=ManagerImprovementsTests.Sample();snapshot.Elements=new[]{row};
            var fake=new FakeSession {Snapshot=snapshot};var window=new ManagerWindow(fake,Path.Combine(output,"control-rule-"+Guid.NewGuid().ToString("N")));fake.Window=window;
            try{window.Show();window.StageSelector.SelectedItem="报规";window.KindSelector.SelectedItem="总图";window.StartFullCheck();window.ElementGrid.SelectedItem=row;
                Assert(window.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().All(f=>f.Value=="净用地控制红线"),"Manager differs from quick assignment");
                Assert(row.Fields.All(f=>window.FieldMark(f)=="规则待写入"),"Correction not visibly pending");
            }finally{window.Close();}
        });
    }
}
