using System;
using System.Linq;
using Hifc.Reference;

internal static class RequiredSubmissionTests
{
    static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
    static FieldCheck Field(string slot,string name,string value=null,string type="Label") => new FieldCheck {
        RuleProfileId=PlatformRules.ProfileId,PropertySet=PlatformRules.CanonicalPropertySet(slot),Property=name,Source=name,
        RawType=type,Value=value,State=value==null?"未填写":"有效",Binding="实例",OwnerId=17,OwnerUniqueId="required-17"};
    static ModelElementCheck Row(string slot,params FieldCheck[] fields)=>new ModelElementCheck {Id=17,UniqueId="required-17",Name="必填检查样本",Slot=slot,Classified=true,
        Entity=slot=="建筑技术"?"IfcBuilding":"IfcSlab",Fields=fields};
    static bool Blocks(InputReviewItem item)=>typeof(InputReviewItem).GetProperty("BlocksExport")?.GetValue(item) as bool? == true;
    internal static void Run(Action<string,Action> check)
    {
        check("required_20261104_building_fields_match_current_ui",()=>{
            var required=PlatformRules.RequiredFields("建筑技术");
            Assert(required.Contains("安全等级")&&required.Contains("耐火等级")&&required.Contains("建筑编号")&&required.Contains("山墙开窗"),"Current mandatory building inputs omitted");
            Assert(!required.Contains("屋脊高度")&&!required.Contains("户数"),"Current optional fields were made universally mandatory");
        });
        check("required_building_shape_enum_does_not_accept_function_words",()=>{
            foreach(var value in new[]{"办公用房","养老服务设施","中学"}) {
                var field=Field("建筑技术","建筑用途名称",value);
                Assert(ModelChecks.InputError(field,value)!=null,"Unsupported building-use enum accepted: "+value);
            }
            Assert(ModelChecks.InputError(Field("建筑技术","建筑用途名称","塔楼"),"塔楼")==null,"Current enum rejected");
        });
        check("delivery_guide_permanence_and_shape_are_checked_even_when_ui_optional",()=>{
            foreach(var name in new[]{"临时永久","结构类型","形状"}) {
                var field=Field("建筑技术",name);
                Assert(PlatformRules.RequiredFields("建筑技术").Contains(name),"Delivery guide mandatory field omitted: "+name);
                var items=PlatformInputReview.Build(new[]{Row("建筑技术",field)},PlatformRules.ProfileId);
                Assert(Blocks(items.Single(i=>i.Property==name)),"Missing delivery field does not block export: "+name);
                Assert(field.Value==null&&field.SuggestedValue==null,"Missing delivery field was filled from a default");
            }
        });
        check("required_green_missing_type_and_factor_block_without_default_fill",()=>{
            var type=Field("绿地","绿地类型");var factor=Field("绿地","折算系数",null,"Real");
            var items=PlatformInputReview.Build(new[]{Row("绿地",Field("绿地","投影面积","20","Real"),type,factor)},PlatformRules.ProfileId);
            Assert(items.Where(i=>i.Property=="绿地类型"||i.Property=="折算系数").Count(Blocks)==2,"Mandatory green inputs are only advisory");
            Assert(type.Value==null&&factor.Value==null&&type.SuggestedValue==null&&factor.SuggestedValue==null,"Platform defaults were written or suggested as facts");
        });
        check("required_missing_values_mark_the_whole_business_role",()=>{
            var row=Row("绿地",Field("绿地","投影面积","20","Real"),Field("绿地","绿地类型"),Field("绿地","折算系数",null,"Real"));
            var role=CaseWorkflow.Summarize("报规","总图",new[]{row},PlatformRules.ProfileId).Roles.Single(r=>r.Slot=="绿地");
            Assert(role.NeedsAttention&&role.State.Contains("必填"),"Mandatory gaps do not mark the role");
        });
        check("required_optional_road_width_does_not_block_but_missing_type_does",()=>{
            var row=Row("道路红线",Field("道路红线","道路类型"),Field("道路红线","红线宽度",null,"Real"));
            var items=PlatformInputReview.Build(new[]{row},PlatformRules.ProfileId);
            Assert(Blocks(items.Single(i=>i.Property=="道路类型")),"Required road type can pass export");
            Assert(!Blocks(items.Single(i=>i.Property=="红线宽度")),"Optional road width became mandatory");
        });
        check("required_explicit_zero_and_false_are_values",()=>{
            var row=Row("建筑技术",Field("建筑技术","地下建筑层数","0","Integer"),Field("建筑技术","民用建筑","0","Boolean"));
            Assert(!PlatformInputReview.Build(new[]{row},PlatformRules.ProfileId).Any(i=>(i.Property=="地下建筑层数"||i.Property=="民用建筑")&&Blocks(i)),"Zero/false treated as missing");
        });
        check("required_new_choices_are_separate_from_frozen_rule_snapshot",()=>{
            Assert(PlatformRules.Choices("绿地","绿地类型").Contains("庭院绿地"),"Current green choice missing");
            Assert(PlatformRules.Choices("建筑技术","建筑类别").Contains("门诊楼"),"Current building category missing");
            Assert(!PlatformRules.Rules("绿地").SelectMany(r=>r.Fields).Single(f=>f.Name=="绿地类型").Items.Contains("庭院绿地"),"Frozen snapshot was rewritten");
        });
    }
}
