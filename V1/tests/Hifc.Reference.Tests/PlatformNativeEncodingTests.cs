using System;using System.Linq;using System.Reflection;using Hifc.Reference;
internal static class PlatformNativeEncodingTests{
 const string Profile="hubei-420626-planning-20261020";
 static void Assert(bool b,string message){if(!b)throw new Exception(message);}
 static ReferenceProperty P(string set,string property,string entity){return CaseExportPolicy.SourceProperties(Profile).Single(p=>p.PropertySet==set&&p.Property==property&&p.Entity==entity);}
 // Fall back to the current unadapted native path until the formatter is implemented.
 static Type Policy=>typeof(CaseExportPolicy).Assembly.GetType("Hifc.Reference.PlatformNativeEncoding");
 static ReferenceProperty Config(ReferenceProperty p,string profile=Profile)=>Policy==null?p:(ReferenceProperty)Policy.GetMethod("ConfigurationProperty").Invoke(null,new object[]{p,profile});
 static string Value(ReferenceProperty p,string input,string profile=Profile){var source=CaseExportPolicy.ValueForExport(p,input,profile);return Policy==null?source:(string)Policy.GetMethod("Value").Invoke(null,new object[]{p,source,profile});}
 public static void Run(Action<string,Action> check){
  check("native_base_boolean_matches_platform_literal_yes",()=>{var p=P("Pset_建筑楼层信息属性集","建筑基底","IfcBuildingStorey");Assert(Config(p).ExportType=="Label","native export is not a localized label");Assert(Value(p,"1")=="是","platform base selector requires the literal 是");Assert(Value(p,"0")=="否","non-base floors must remain no");});
  check("native_civil_boolean_matches_current_dropdown",()=>{var p=P("Pset_建筑技术信息属性集","民用建筑","IfcBuilding");Assert(Config(p).ExportType=="Label","native civil flag is not a dropdown label");Assert(Value(p,"0")=="否","False/0 is not a legal platform option");Assert(Value(p,"1")=="是","true must retain its business meaning");});
  check("native_localization_preserves_boolean_source_and_bridge_identity",()=>{foreach(var p in new[]{P("Pset_建筑技术信息属性集","民用建筑","IfcBuilding"),P("Pset_建筑楼层信息属性集","建筑基底","IfcBuildingStorey")}){var native=Config(p);Assert(p.ExportType=="Boolean","source checkbox contract changed");Assert(ReferencePolicy.BridgeGuid(p)==ReferencePolicy.BridgeGuid(native)&&p.SourceParameter==native.SourceParameter&&p.Binding==native.Binding,"source identity changed");}});
  check("native_localization_is_limited_to_evidenced_fields_and_profile",()=>{var p=P("Pset_建筑技术信息属性集","民用建筑","IfcBuilding");Assert(Config(p,"other-profile").ExportType=="Boolean"&&Value(p,"0","other-profile")=="0","unrelated profile changed");var other=P("Pset_墙信息属性集","是否为女儿墙","IfcWall");Assert(Config(other).ExportType=="Boolean"&&Value(other,"0")=="0","boolean without a platform label choice changed");});
  check("native_parking_boolean_selects_real_calculation_branch",()=>{
   var p=P("Pset_停车位信息属性集","是否为室外停车场","IfcSpace");var rules=PlatformRules.Rules(p.PropertySet);
   Assert(Config(p).ExportType=="Label","Boolean cannot match the platform literal branch selector");
   Assert(rules.SingleOrDefault(r=>r.MatchValue==Value(p,"0"))?.EntityCode=="104","indoor parking is omitted from the calculation");
   Assert(rules.SingleOrDefault(r=>r.MatchValue==Value(p,"1"))?.EntityCode=="203","outdoor parking is omitted from the calculation");
  });
  check("native_parking_business_flags_preserve_true_false",()=>{
   foreach(var field in new[]{"充电车位","公建车位"}){var p=P("Pset_停车位信息属性集",field,"IfcSpace");Assert(Config(p).ExportType=="Label"&&Value(p,"1")=="是"&&Value(p,"0")=="否","parking flag is not a platform choice: "+field);Assert(p.ExportType=="Boolean"&&ReferencePolicy.BridgeGuid(p)==ReferencePolicy.BridgeGuid(Config(p)),"checkbox source or bridge identity changed");}
  });
  check("native_other_evidenced_yes_no_gates_are_consistent",()=>{
   foreach(var p in new[]{P("Pset_阳台信息属性集","是否有顶","IfcSlab"),P("Pset_楼梯段信息属性集","是否为室外楼梯","IfcStairFlight"),P("Pset_停车场信息属性集","是否为充电车场","IfcSpace")})
    Assert(Config(p).ExportType=="Label"&&Value(p,"1")=="是"&&Value(p,"0")=="否","known platform yes/no gate is inconsistent: "+p.Property);
  });
  check("native_boolean_encoding_never_fills_missing_input",()=>{var p=P("Pset_建筑技术信息属性集","民用建筑","IfcBuilding");Assert(Value(p,null)==null&&Value(p,"  ")==null,"missing value invented");bool rejected=false;try{Value(p,"unknown");}catch(InvalidOperationException){rejected=true;}Assert(rejected,"invalid boolean was silently converted");});
  check("native_tsv_uses_label_without_changing_frozen_catalog",()=>{var cat=ReferenceCatalog.FromEmbeddedBaseline();var p=P("Pset_建筑技术信息属性集","民用建筑","IfcBuilding");var native=cat.BuildSelectedExportConfiguration(new[]{Config(p)});Assert(native.Contains("民用建筑\tLabel\t"+ReferencePolicy.BridgeName(p)),"native config type/value bridge mismatch");Assert(cat.BuildExportConfiguration().Contains("民用建筑\tBoolean\t"),"frozen source contract changed");});
 }
}
