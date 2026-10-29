using System;
using System.Linq;

namespace Hifc.Reference
{
    // A text classification may be user-defined. The frozen platform enum
    // remains strict reference evidence for suggestions and advisory warnings.
    public static class ClassificationInputs
    {
        public static bool IsParent(string property)=>property=="主功能类别"||property=="公建大类";
        public static bool IsChild(string property)=>property=="子功能类别"||property=="公建小类";
        public static bool Supports(string propertySet,string property)
        {
            var set=PlatformRules.CanonicalPropertySet(propertySet);
            if(set=="Pset_建筑技术信息属性集"&&new[]{"建筑类型名称","建筑类别","建筑用途名称"}.Contains(property,StringComparer.Ordinal))return true;
            if(!IsParent(property)&&!IsChild(property))return false;
            return PlatformRules.Fields(set).Any(f=>(f.Name==property||f.InternalName==property)&&(IsParent(f.InternalName)||IsChild(f.InternalName)));
        }
        public static string[] Choices(string propertySet,string property,string parent=null)
            => Supports(propertySet,property)?PlatformRules.Choices(propertySet,property,parent):new string[0];
        public static string Hint(string propertySet,string property,string value,string parent=null)
        {
            if(!Supports(propertySet,property)||string.IsNullOrWhiteSpace(value))return "";
            if(IsChild(property)&&string.IsNullOrWhiteSpace(parent))return "请先核对主功能；子功能按主功能提供选项，现有值保留。";
            var error=PlatformRules.ValueError(propertySet,property,value,parent);
            return string.IsNullOrEmpty(error)?"":"自定义分类，平台识别待验证。"+error;
        }
    }
}
