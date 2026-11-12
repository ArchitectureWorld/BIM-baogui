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
        check("county_net_land_defaults_keep_name_and_type_distinct",()=>{
            var row=Row();
            Assert(row.Fields[0].SuggestedValue==Label&&row.Fields[1].SuggestedValue=="红线","Name and enum are still forced to the same value");
            Assert(row.Fields.All(f=>f.Value==null),"Preview changed source values");
        });
        check("county_net_land_keeps_written_names_and_types_without_forced_rewrites",()=>{
            var row=Row(name:"用地红线",type:"净用地规划控制红线");
            Assert(row.Fields.All(f=>f.SuggestedValue==null&&f.RequiredValue==null),"Existing values still get forced rewrites");
            Assert(row.Fields[0].Value=="用地红线"&&row.Fields[1].Value=="净用地规划控制红线","Original values lost");
        });
        check("county_red_type_stays_required_and_validates_its_actual_enum",()=>{
            var row=Row();var fields=row.Fields;
            Assert(ModelChecks.InputError(fields[1],"红线")==null,"Valid platform red enum rejected");
            Assert(ModelChecks.InputError(fields[1],Label)==null,"Legacy compatible value rejected");
            Assert(!PlanningInputs.AcceptableForRules(fields[1],fields,PlatformRules.ProfileId)&&
                PlatformInputReview.Build(new[]{row},PlatformRules.ProfileId).Any(i=>i.Property=="红线类型"&&i.BlocksExport),"Missing required type can be exported");
            Assert(ModelChecks.InputError(fields[1],"不存在的线型")!=null,"Unknown type passed enum validation");
            Assert(ModelChecks.InputError(fields[0],"用地红线")==null,"Name is still forced to the type label");
        });
        check("current_site_redline_value_is_exportable_without_suggestion",()=>{
            var row=Row(name:Label,type:"红线");
            Assert(row.Fields.All(f=>f.Valid&&f.SuggestedValue==null&&f.RequiredValue==null),"Actual site value remains blocked");
            Assert(!PlatformInputReview.Build(new[]{row},PlatformRules.ProfileId).Any(i=>i.BlocksExport),"Export preflight still blocks the actual redline");
        });
        check("redline_pairing_accepts_red_enum_with_confirmed_net_land_name",()=>{
            Assert(RedlineRoles.Resolve(Row(name:Label,type:"红线"))==RedlineRoles.Control,"Current red enum not recognized for net-land pairing");
            Assert(RedlineRoles.Resolve(Row(name:"道路红线",type:"红线"))==null,"An arbitrary red control was paired as net land");
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
            var draftRows=PlanningInputs.WithDraftValues(new[]{row},new[]{new PlanningValueOverride {OwnerUniqueId=field.OwnerUniqueId,PropertySet=field.PropertySet,Source=field.Source,Binding=field.Binding,Value=""}});
            var draft=draftRows.Single().Fields[1];
            Assert(!PlanningInputs.AcceptableForRules(draft,new[]{draft},field.RuleProfileId)&&
                PlatformInputReview.Build(draftRows,PlatformRules.ProfileId).Any(i=>i.Property=="红线类型"&&i.BlocksExport),"Draft clearing bypassed required export validation");
        });
    }
}
