using System;
using System.IO;
using System.Reflection;
using Hifc.Reference;

internal static class ExportNamingTests
{
    static object Call(string method,params object[] args)
    {
        var type=typeof(ExportFileNaming);
        if(type==null)throw new Exception("Export naming policy is missing");
        try{return type.GetMethod(method).Invoke(null,args);}
        catch(TargetInvocationException e){throw e.InnerException;}
    }
    static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
    static string Stem(string title,string kind="单体建筑",string stage="报规")=>(string)Call("Stem","",title,stage,kind,new DateTime(2026,10,23));
    static void WithDirectory(Action<string> test)
    {
        var path=Path.Combine(Path.GetTempPath(),"hifc-export-naming-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);
        try{test(path);}finally{foreach(var file in Directory.GetFiles(path))File.Delete(file);Directory.Delete(path);}
    }
    internal static void Run(Action<string,Action> check)
    {
        check("export_name_accepts_optional_3d_prefix_and_preserves_building",()=>{
            Assert(Stem("3D_学校_综合楼")=="OUT_综合楼_学校_报规_20261023","Unexpected normalized building filename");
            Assert(Stem("学校_综合楼")==Stem("3D_学校_综合楼"),"Optional 3D prefix changes output identity");
            Assert(Stem("学校_大门")!=Stem("学校_综合楼"),"Different buildings share a version family");
        });
        check("site_name_contains_single_site_token_and_actual_stage_date",()=>{
            Assert(Stem("学校_总图","总图")=="OUT_总图_学校_报规_20261023","Site name duplicated or lost");
            Assert(Stem("3d_学校_总图","总图","施工图")=="OUT_总图_学校_施工图_20261023","Stage/date not reflected");
        });
        check("saved_rvt_filename_overrides_document_title",()=>{
            Assert((string)Call("Stem",@"D:\模型\学校_综合楼.rvt","错误标题","报规","单体建筑",new DateTime(2026,10,23))=="OUT_综合楼_学校_报规_20261023","RVT filename not used");
        });
        check("export_defaults_to_model_sibling_directory_and_unsaved_fallback",()=>WithDirectory(dir=>{
            Assert((string)Call("InitialDirectory",Path.Combine(dir,"学校_总图.rvt"),Path.GetTempPath())==dir,"Default folder is not beside RVT");
            Assert((string)Call("InitialDirectory","",dir)==dir,"Unsaved project cannot select a destination");
        }));
        check("version_uses_maximum_existing_number_and_preserves_old_files",()=>WithDirectory(dir=>{
            var stem=Stem("学校_综合楼");File.WriteAllText(Path.Combine(dir,stem+"_V2.ifc"),"keep-v2");File.WriteAllText(Path.Combine(dir,stem+"_V10.ifc"),"keep-v10");
            Assert((string)Call("NextPath",dir,stem)==Path.Combine(dir,stem+"_V11.ifc"),"Version is not max plus one");
            Assert(File.ReadAllText(Path.Combine(dir,stem+"_V10.ifc"))=="keep-v10","Existing IFC changed");
            Assert((string)Call("NextPath",dir,Stem("学校_大门"))==Path.Combine(dir,Stem("学校_大门")+"_V1.ifc"),"Other building affected version");
        }));
        check("new_day_and_final_custom_directory_have_independent_versions",()=>WithDirectory(dir=>{
            File.WriteAllText(Path.Combine(dir,"OUT_学校_报规_总图_20261022_V18.ifc"),"old day");
            var stem=Stem("学校_总图","总图");
            Assert((string)Call("ResolveSelection",Path.Combine(dir,stem+"_V99.ifc"))==Path.Combine(dir,stem+"_V1.ifc"),"Destination version was inherited from another folder/day");
        }));
        check("orphan_receipt_configuration_log_and_folder_reserve_version",()=>WithDirectory(dir=>{
            var stem=Stem("学校_总图","总图");
            File.WriteAllText(Path.Combine(dir,stem+"_V2.psets.tsv"),"old config");
            File.WriteAllText(Path.Combine(dir,stem+"_V4.receipt.json"),"old receipt");
            File.WriteAllText(Path.Combine(dir,stem+"_V6.ifc.log"),"old log");
            Assert((string)Call("NextPath",dir,stem)==Path.Combine(dir,stem+"_V7.ifc"),"Existing companion file would collide");
        }));
        check("unresolved_stage_or_type_is_not_invented",()=>{
            foreach(var args in new[]{new[]{"","总图"},new[]{"报规","待确认"},new[]{"报规","混合模型"}}){bool rejected=false;try{Stem("学校",args[1],args[0]);}catch(InvalidOperationException){rejected=true;}Assert(rejected,"Unknown context became an export label");}
        });
        check("filename_cleanup_is_scoped_and_keeps_chinese",()=>{
            Assert(Stem("学校_3D实验楼")=="OUT_3D实验楼_学校_报规_20261023","Interior 3D name lost");
            Assert(Stem("学校_楼/栋?")=="OUT_楼_栋__学校_报规_20261023","Invalid filename characters remain");
        });
        check("legacy_name_suggests_project_and_specific_building",()=>{
            Assert(Stem("3D_歇马镇中心学校-综合楼")=="OUT_综合楼_歇马镇中心学校_报规_20261023","Legacy building suggestion not split");
            Assert(Stem("3D_学校-总图","总图")=="OUT_总图_学校_报规_20261023","Legacy site repeated in project name");
        });
        check("project_underscores_preserve_identity_and_missing_building_requires_input",()=>{
            Assert(Stem("3D_学校_一期_综合楼")=="OUT_综合楼_学校_一期_报规_20261023","Project identity lost");
            bool rejected=false;try{Stem("学校");}catch(InvalidOperationException){rejected=true;}
            Assert(rejected,"Missing building silently became generic single-building name");
        });
        check("confirmed_names_override_suggestion_without_model_changes",()=>{
            Assert((string)Call("StemForNames","学校","大门","报规","单体建筑",new DateTime(2026,10,23))=="OUT_大门_学校_报规_20261023","Confirmed names not applied");
            Assert((string)Call("StemForNames","学校","误填单体","报规","总图",new DateTime(2026,10,23))=="OUT_总图_学校_报规_20261023","Site identity not fixed");
        });
        check("standardized_rvt_old_order_discards_source_date_and_version",()=>{
            var names=ExportFileNaming.SuggestNames("","3D_项目_报规_1号楼_20261109_V2.rvt","单体建筑");
            Assert(names.ProjectName=="项目"&&names.BuildingName=="1号楼","Date or version became an identity field");
            Assert(Stem("3D_项目_报规_1号楼_20261109_V2.rvt")=="OUT_1号楼_项目_报规_20261023","Source date/version leaked into new export");
        });
        check("standardized_subject_first_and_site_detection_use_identity",()=>{
            Assert(Stem("3D_综合楼_学校_报规_20261112_V1.rvt")=="OUT_综合楼_学校_报规_20261023","Subject-first fields were inverted");
            foreach(var title in new[]{"3D_总图_项目_报规_20261112_V1.rvt","3D_项目_报规_总图_20261109_V2.rvt","标准化_项目_报规_总图_20261109_V2.0001.rvt"})
            {
                Assert(ExportFileNaming.FileKind("",title)=="总图","Standardized site classified as building: "+title);
                Assert(Stem(title,"总图")=="OUT_总图_项目_报规_20261023","Site output retains source wrappers: "+title);
            }
        });
        check("repeated_prefixes_stages_and_revit_backup_suffix_are_removed",()=>{
            Assert(Stem("OUT_标准化_3D_学校_报规_报规_1号楼_20261109_V2_20261110_V3.0001.rvt")=="OUT_1号楼_学校_报规_20261023","Repeated source wrappers survived");
            Assert(Stem("3D_1号楼_学校_报规_报规_20261109_V2.rvt")=="OUT_1号楼_学校_报规_20261023","Repeated terminal stage inverted fields");
        });
        check("previous_parser_pollution_recovers_single_subject_and_stage",()=>{
            const string project="保康县保南片区特困人员供养服务中心";
            var pollutedSite="OUT_"+project+"_报规_总图_20261109_报规_总图_20261112_V1.ifc";
            Assert(Stem(pollutedSite,"总图")=="OUT_总图_"+project+"_报规_20261023","Repeated site or stage still pollutes project");
            var pollutedBuilding="OUT_"+project+"_报规_1号楼_20261109_报规_V2_20261112_V1.ifc";
            Assert(Stem(pollutedBuilding)=="OUT_1号楼_"+project+"_报规_20261023","V2 pollution became a building");
        });
        check("new_output_can_be_parsed_again_without_adding_wrappers",()=>{
            var first=Stem("3D_学校_报规_综合楼_20261109_V2.rvt");
            Assert(Stem(first+"_V8.ifc")==first,"Second parse duplicated identity fields");
            Assert(Stem("OUT_综合楼_学校_报规_20261109_V2.ifc")==first,"IFC prefix was retained in project");
        });
        check("unambiguous_stage_boundary_preserves_real_underscores_and_numbers",()=>{
            Assert(Stem("3D_学校_一期_2026园区_报规_1号楼_东区_20261109_V2.rvt")=="OUT_1号楼_东区_学校_一期_2026园区_报规_20261023","Real identity underscores or phase numbers were removed");
            Assert(Stem("3D_总图_学校_一期_报规_20261112_V1.rvt","总图")=="OUT_总图_学校_一期_报规_20261023","Fixed site subject did not resolve project underscores");
        });
        check("project_number_before_explicit_stage_is_preserved",()=>{
            Assert(Stem("3D_学校_2026_V2_报规_1号楼_20261109_V3.rvt")=="OUT_1号楼_学校_2026_V2_报规_20261023","Project revision-like text was mistaken for a filename tail");
            Assert(Stem("3D_总图_学校_20261109_报规_20261112_V1.rvt","总图")=="OUT_总图_学校_20261109_报规_20261023","Project numeric identity was removed before the stage boundary");
        });
        check("repeated_export_wrapper_around_subject_first_source_keeps_one_subject",()=>{
            Assert(Stem("OUT_总图_学校_报规_20261109_报规_总图_20261112_V1.ifc","总图")=="OUT_总图_学校_报规_20261023","Subject-first source was retained inside the project");
            Assert(Stem("OUT_综合楼_学校_报规_20261109_报规_综合楼_20261112_V1.ifc")=="OUT_综合楼_学校_报规_20261023","Repeated new-order subject survived an old-order wrapper");
        });
        check("ambiguous_new_underscore_boundaries_require_confirmed_fields",()=>{
            const string title="3D_综合楼_东区_学校_一期_报规_20261112_V1.rvt";
            var names=ExportFileNaming.SuggestNames("",title,"单体建筑");
            Assert(string.IsNullOrEmpty(names.ProjectName)&&string.IsNullOrEmpty(names.BuildingName),"Ambiguous identity was guessed");
            Assert(ExportFileNaming.FileKind("",title)==null,"Ambiguous identity asserted a model type");
            bool rejected=false;try{Stem(title);}catch(InvalidOperationException){rejected=true;}
            Assert(rejected,"Ambiguous names bypassed manual confirmation");
            Assert((string)Call("StemForNames","学校_一期","综合楼_东区","报规","单体建筑",new DateTime(2026,10,23))=="OUT_综合楼_东区_学校_一期_报规_20261023","Confirmed complete names lost underscores");
        });
        check("conflicting_repeated_subjects_are_not_silently_discarded",()=>{
            var names=ExportFileNaming.SuggestNames("","OUT_学校_报规_综合楼_20261109_报规_大门_20261112_V1.ifc","单体建筑");
            Assert(string.IsNullOrEmpty(names.ProjectName)&&string.IsNullOrEmpty(names.BuildingName),"Conflicting subjects were resolved without input");
        });
        check("date_version_and_generic_building_labels_cannot_be_subjects",()=>{
            foreach(var title in new[]{"学校_20261109_V2.rvt","学校_01.rvt"})
                Assert(string.IsNullOrEmpty(ExportFileNaming.SuggestNames("",title,"单体建筑").BuildingName),"Numeric suffix became building: "+title);
            foreach(var label in new[]{"单体","单体建筑"})
            {
                bool rejected=false;try{Call("StemForNames","学校",label,"报规","单体建筑",new DateTime(2026,10,23));}catch(InvalidOperationException){rejected=true;}
                Assert(rejected,"Generic subject was accepted: "+label);
            }
        });
        check("version_counts_new_and_legacy_order_for_same_identity_day_stage",()=>WithDirectory(dir=>{
            File.WriteAllText(Path.Combine(dir,"OUT_综合楼_学校_报规_20261023_V2.ifc"),"keep-new");
            File.WriteAllText(Path.Combine(dir,"OUT_学校_报规_综合楼_20261023_V10.ifc"),"keep-old");
            File.WriteAllText(Path.Combine(dir,"OUT_学校_报规_综合楼_20261022_V90.ifc"),"other-day");
            File.WriteAllText(Path.Combine(dir,"OUT_学校_施工图_综合楼_20261023_V90.ifc"),"other-stage");
            File.WriteAllText(Path.Combine(dir,"OUT_学校_报规_大门_20261023_V90.ifc"),"other-building");
            Assert((string)Call("NextPath",dir,"OUT_综合楼_学校_报规_20261023")==Path.Combine(dir,"OUT_综合楼_学校_报规_20261023_V11.ifc"),"Mixed-order version family was split");
            Assert(File.ReadAllText(Path.Combine(dir,"OUT_学校_报规_综合楼_20261023_V10.ifc"))=="keep-old","Legacy IFC changed");
            Assert((string)Call("ResolveSelection",Path.Combine(dir,"OUT_学校_报规_综合楼_20261023_V99.ifc"))==Path.Combine(dir,"OUT_综合楼_学校_报规_20261023_V11.ifc"),"Legacy selection produced another legacy filename");
        }));
        check("confirmed_underscore_names_share_versions_with_exact_legacy_identity",()=>WithDirectory(dir=>{
            File.WriteAllText(Path.Combine(dir,"OUT_学校_一期_报规_综合楼_东区_20261023_V12.ifc"),"same-identity");
            File.WriteAllText(Path.Combine(dir,"OUT_东区_学校_一期_报规_综合楼_20261023_V99.ifc"),"different-identity");
            Assert((string)Call("NextPathForNames",dir,"学校_一期","综合楼_东区","报规","单体建筑",new DateTime(2026,10,23))==Path.Combine(dir,"OUT_综合楼_东区_学校_一期_报规_20261023_V13.ifc"),"Confirmed underscores were split to count versions");
        }));
        check("legacy_companions_and_matching_directories_reserve_versions",()=>WithDirectory(dir=>{
            File.WriteAllText(Path.Combine(dir,"OUT_学校_报规_总图_20261023_V8.ifc.log"),"old-log");
            var collision=Path.Combine(dir,"OUT_总图_学校_报规_20261023_V10.ifc");Directory.CreateDirectory(collision);
            try{Assert((string)Call("NextPath",dir,"OUT_总图_学校_报规_20261023")==Path.Combine(dir,"OUT_总图_学校_报规_20261023_V11.ifc"),"Reserved old/folder output would collide");}
            finally{Directory.Delete(collision);}
        }));
    }
}
