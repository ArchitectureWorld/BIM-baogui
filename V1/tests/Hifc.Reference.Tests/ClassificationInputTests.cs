using System;
using Hifc.Reference;

internal static class ClassificationInputTests
{
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static ReferenceProperty Field(string set,string name,string type="Label")=>new ReferenceProperty{PropertySet=set,Property=name,RawType=type};
    static void Reject(ReferenceProperty field,string value){try{CaseExportPolicy.ValueForExport(field,value,PlatformRules.ProfileId);}catch(InvalidOperationException){return;}throw new Exception("Strict non-classification input was accepted");}
    internal static void Run(Action<string,Action> check)
    {
        check("custom_building_classification_exports_exact_text",()=>{
            foreach(var name in new[]{"建筑类型名称","建筑类别","建筑用途名称"}) {
                const string text="  自定义学校用途  ";
                Assert(CaseExportPolicy.ValueForExport(Field("Pset_建筑技术信息属性集",name),text,PlatformRules.ProfileId)==text,"Custom classification changed or was rejected: "+name);
            }
        });
        check("custom_parent_child_pair_is_preserved_for_explicit_manual_input",()=>{
            Assert(CaseExportPolicy.ValueForExport(Field("Pset_建筑区域信息属性集","主功能类别"),"自定义教学功能",PlatformRules.ProfileId)=="自定义教学功能","Custom parent rejected");
            Assert(CaseExportPolicy.ValueForExport(Field("Pset_建筑区域信息属性集","子功能类别"),"教室",PlatformRules.ProfileId,"办公")=="教室","Old child was erased or rejected after a parent change");
            Assert(CaseExportPolicy.ValueForExport(Field("Pset_雨篷信息属性集","公建小类"),"自定义设施",PlatformRules.ProfileId,"自定义主类")=="自定义设施","Alias classification rejected");
        });
        check("custom_classification_model_value_remains_writable_without_weakening_snapshot",()=>{
            var property=Field("Pset_建筑技术信息属性集","建筑类别");
            var result=ModelChecks.Evaluate(property,new[]{new ParameterRead{Name=property.SourceParameter,Origin="instance",StorageType="String",HasValue=true,Value="教学配套",ReadOnly=false}},PlatformRules.ProfileId);
            Assert(result.Valid&&result.Editable&&result.Value=="教学配套","Custom model classification is still invalid");
            Assert(PlatformRules.ValueError(property.PropertySet,property.Property,"教学配套")!=null,"Strict reference dictionary was weakened");
        });
        check("custom_classification_exception_does_not_accept_bad_types_or_redline",()=>{
            Reject(Field("Pset_道路边界线信息属性集","道路宽度","Real"),"四米");
            Reject(Field("Pset_规划控制线信息属性集","红线类型"),"自定义红线");
            Reject(Field("Pset_建筑技术信息属性集","建筑类别","Real"),"12.5");
            Assert(CaseExportPolicy.ValueForExport(Field("建筑技术","建筑用途名称")," ",PlatformRules.ProfileId)==null,"Empty required input was filled by a default");
        });
        check("custom_common_pair_applies_only_within_its_confirmed_parent_scope",()=>{
            Func<int,string,ModelElementCheck> row=(id,parent)=>new ModelElementCheck {Id=id,UniqueId="custom-common-"+id,Entity="IfcSlab",Slot="建筑区域",Classified=true,
                Fields=new[]{new FieldCheck {PropertySet="Pset_建筑区域信息属性集",Property="主功能类别",Source="主功能类别",RawType="Label",Value=parent,State="有效",RuleProfileId=PlatformRules.ProfileId},
                    new FieldCheck {PropertySet="Pset_建筑区域信息属性集",Property="子功能类别",Source="子功能类别",RawType="Label",State="未填写",RuleProfileId=PlatformRules.ProfileId}}};
            var own=row(1,"自定义主功能");var other=row(2,"学校");var settings=new PlanningSettings {RuleProfileId=PlatformRules.ProfileId};
            settings.CommonValues["建筑区域|主功能类别"]="自定义主功能";settings.CommonValues["建筑区域|子功能类别"]="自定义子功能";
            PlanningInputs.Populate(new[]{own,other},settings,null);
            Assert(own.Fields[1].SuggestedValue=="自定义子功能"&&own.Fields[1].Value==null,"Explicit custom common pair was lost or written without preview");
            Assert(other.Fields[0].Value=="学校"&&other.Fields[1].SuggestedValue==null,"Custom child crossed into a different existing parent");
        });
    }
}
