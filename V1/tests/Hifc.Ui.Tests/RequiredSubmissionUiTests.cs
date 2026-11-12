using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class RequiredSubmissionUiTests
{
    static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
    static ModelElementCheck Green()=>new ModelElementCheck{Id=66,UniqueId="required-green-66",Name="待补绿地",Category="楼板",Slot="绿地",Entity="IfcSlab",Classified=true,
        Fields=new[]{"投影面积","绿地类型","折算系数"}.Select((name,i)=>new FieldCheck{PropertySet="Pset_绿地信息属性集",Property=name,Source=name,
            RuleProfileId=PlatformRules.ProfileId,RawType=i==1?"Label":"Real",Value=i==0?"20":null,State=i==0?"有效":"未填写",Binding="实例",Editable=true,OwnerId=66,OwnerUniqueId="required-green-66"}).ToArray()};
    internal static void Run(Action<string,Action> check,string output)
    {
        check("all_required_green_fields_have_markers_and_remain_red_until_readback",()=>{
            var row=Green();var fake=new FakeSession{Snapshot=new ModelSnapshot{Complete=true,DocumentKey=Guid.NewGuid().ToString(),Title="必填属性检查",Stage="报规",ModelKind="总图",ScanId="required-ui",Planning=new PlanningSettings{RuleProfileId=PlatformRules.ProfileId},Elements=new[]{row}}};
            var w=new ManagerWindow(fake,Path.Combine(output,"required-green"));fake.Window=w;
            try {w.Show();w.StageSelector.SelectedItem="报规";w.KindSelector.SelectedItem="总图";w.StartFullCheck();w.ElementGrid.SelectedItem=row;w.UpdateLayout();
                var editors=w.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().ToArray();
                Assert(editors.All(e=>e.DisplayProperty.EndsWith(" *")),"Required marker missing");
                Assert(editors.Where(e=>e.Property!="投影面积").All(e=>((SolidColorBrush)e.Background).Color==(Color)ColorConverter.ConvertFromString("#FFFFDADA")),"Required gaps not red");
                Assert(w.Summary.Roles.Single(r=>r.Slot=="绿地").NeedsAttention,"Green role remains unmarked");
                Program.Screenshot(w,Path.Combine(output,"mandatory-green-fields.png"));
                w.StageValue(row.Fields[1],"附属绿地");w.StageValue(row.Fields[2],"1");
                Assert(w.Summary.Roles.Single(r=>r.Slot=="绿地").NeedsAttention,"Draft satisfied mandatory checks before write/readback");
            } finally {w.Close();}
        });
        check("selection_editor_marks_unwritten_required_fields_even_with_suggestions",()=>{
            var row=Green();row.Fields[2].SuggestedValue="1";
            var draft=new SelectionDraft(new ModelSnapshot{Scope="所选构件",Complete=true,Stage="报规",ModelKind="总图",Elements=new[]{row}});
            draft.SetClassification("绿地",new[]{row});var factor=draft.Fields.Single(f=>f.Property=="折算系数");
            Assert(factor.DisplayProperty.EndsWith(" *")&&factor.Background=="#FFFFE9E7"&&factor.State.Contains("必填"),"Suggested factor falsely satisfies required source");
        });
    }
}
