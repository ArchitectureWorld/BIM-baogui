using System;
using System.IO;
using System.Reflection;
using Hifc.Reference;

internal static class ExportNamingTests
{
    static object Call(string method,params object[] args)
    {
        var type=typeof(ReferenceCatalog).Assembly.GetType("Hifc.Reference.ExportFileNaming");
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
            Assert(Stem("3D_学校_综合楼")=="OUT_学校_报规_综合楼_20261023","Unexpected normalized building filename");
            Assert(Stem("学校_综合楼")==Stem("3D_学校_综合楼"),"Optional 3D prefix changes output identity");
            Assert(Stem("学校_大门")!=Stem("学校_综合楼"),"Different buildings share a version family");
        });
        check("site_name_contains_single_site_token_and_actual_stage_date",()=>{
            Assert(Stem("学校_总图","总图")=="OUT_学校_报规_总图_20261023","Site name duplicated or lost");
            Assert(Stem("3d_学校_总图","总图","施工图")=="OUT_学校_施工图_总图_20261023","Stage/date not reflected");
        });
        check("saved_rvt_filename_overrides_document_title",()=>{
            Assert((string)Call("Stem",@"D:\模型\学校_综合楼.rvt","错误标题","报规","单体建筑",new DateTime(2026,10,23))=="OUT_学校_报规_综合楼_20261023","RVT filename not used");
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
            Assert(Stem("学校_3D实验楼")=="OUT_学校_报规_3D实验楼_20261023","Interior 3D name lost");
            Assert(Stem("学校_楼/栋?")=="OUT_学校_报规_楼_栋__20261023","Invalid filename characters remain");
        });
        check("legacy_name_suggests_project_and_specific_building",()=>{
            Assert(Stem("3D_歇马镇中心学校-综合楼")=="OUT_歇马镇中心学校_报规_综合楼_20261023","Legacy building suggestion not split");
            Assert(Stem("3D_学校-总图","总图")=="OUT_学校_报规_总图_20261023","Legacy site repeated in project name");
        });
        check("project_underscores_preserve_identity_and_missing_building_requires_input",()=>{
            Assert(Stem("3D_学校_一期_综合楼")=="OUT_学校_一期_报规_综合楼_20261023","Project identity lost");
            bool rejected=false;try{Stem("学校");}catch(InvalidOperationException){rejected=true;}
            Assert(rejected,"Missing building silently became generic single-building name");
        });
        check("confirmed_names_override_suggestion_without_model_changes",()=>{
            Assert((string)Call("StemForNames","学校","大门","报规","单体建筑",new DateTime(2026,10,23))=="OUT_学校_报规_大门_20261023","Confirmed names not applied");
            Assert((string)Call("StemForNames","学校","误填单体","报规","总图",new DateTime(2026,10,23))=="OUT_学校_报规_总图_20261023","Site identity not fixed");
        });
    }
}
