using System;
using System.Linq;
using Hifc.Reference;

internal static class ControlLineTests
{
    const string Label = "净用地控制红线", Set = "Pset_规划控制线信息属性集";
    static void Assert(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static ModelElementCheck Row(string native = "用地红线", string name = null, string type = null, string profile = "hubei-420626-planning-20261020")
    {
        var row = new ModelElementCheck { Id=17, UniqueId="control-test", Name=native, TypeName=native, Slot="规划控制线", Entity="IfcBuildingElementProxy", Classified=true };
        row.Fields = new[] { new FieldCheck { Property="名称", Source="规划控制线名称", Value=name }, new FieldCheck { Property="红线类型", Source="红线类型", Value=type } };
        foreach (var f in row.Fields) { f.PropertySet=Set; f.RawType="Label"; f.Binding="实例"; f.Origin="instance"; f.OwnerUniqueId=row.UniqueId; f.OwnerId=row.Id; f.Editable=true; f.RuleProfileId=profile; f.State=f.Value==null?"未填写":"有效"; AutoFillPolicy.Populate(f,row.Slot,row.Name,null); }
        PlanningInputs.ValidateFields(new[] { row });
        return row;
    }
    public static void Run(Action<string,Action> check)
    {
        check("manual_net_land_control_prepares_both_confirmed_values",()=>{
            var row=Row();
            Assert(row.Fields.All(f=>f.SuggestedValue==Label),"Manual net-land control still copies native name or leaves type empty");
            Assert(row.Fields.All(f=>f.Value==null),"Preview changed source values");
        });
        check("net_land_control_corrects_old_defaults_without_mutating_baselines",()=>{
            var row=Row(name:"用地红线",type:"净用地规划控制红线");
            Assert(row.Fields.All(f=>f.SuggestedValue==Label&&!f.Acceptable),"Legacy defaults are not pending correction");
            Assert(row.Fields[0].Value=="用地红线"&&row.Fields[1].Value=="净用地规划控制红线","Original values lost");
        });
        check("net_land_control_cannot_be_cleared_or_reverted_to_native_name",()=>{
            foreach(var field in Row().Fields) {
                Assert(ModelChecks.InputError(field,Label)==null,"Confirmed value rejected");
                Assert(ModelChecks.InputError(field,"")!=null&&ModelChecks.InputError(field,"用地红线")!=null,"Rule allows empty or old value");
            }
        });
        check("correct_net_land_control_is_idempotent",()=>{
            Assert(Row(name:Label,type:Label).Fields.All(f=>f.Valid&&f.SuggestedValue==null),"Already correct values get repeat suggestions");
        });
        check("control_rule_applies_in_reference_and_platform_profiles",()=>{
            Assert(Row(profile:"").Fields.All(f=>f.SuggestedValue==Label),"Reference profile missed confirmed control defaults");
        });
        check("other_control_line_kinds_keep_their_values",()=>{
            foreach(var name in new[]{"建筑控制线","道路红线","控制线实体"}) {
                var row=Row(name,name,"建筑控制线");
                Assert(row.Fields.All(f=>f.SuggestedValue==null)&&row.Fields[0].Value==name&&row.Fields[1].Value=="建筑控制线","Unrelated control line rewritten");
            }
        });
        check("net_land_carrier_is_not_reclassified_as_control_line",()=>{
            var row=Row(); row.Slot="规划净用地"; row.Fields=new[]{new FieldCheck {PropertySet="Pset_规划净用地信息属性集",Property="名称",Value="用地红线",State="有效",RawType="Text"}};
            PlanningInputs.ValidateFields(new[]{row}); Assert(row.Fields[0].Value=="用地红线"&&row.Fields[0].SuggestedValue==null,"Net-land area confused with control line");
        });
        check("net_land_control_draft_clear_remains_invalid",()=>{
            var row=Row(name:Label,type:Label); var field=row.Fields[1];
            var draft=PlanningInputs.WithDraftValues(new[]{row},new[]{new PlanningValueOverride {OwnerUniqueId=field.OwnerUniqueId,PropertySet=field.PropertySet,Source=field.Source,Binding=field.Binding,Value=""}}).Single().Fields[1];
            Assert(!PlanningInputs.AcceptableForRules(draft,new[]{draft},field.RuleProfileId)&&ModelChecks.InputError(draft,"")!=null,"Draft clearing bypassed rule");
        });
    }
}
