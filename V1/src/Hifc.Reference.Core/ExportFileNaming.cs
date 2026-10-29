using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hifc.Reference
{
    public sealed class ExportNames
    {
        public string ProjectName { get; set; }
        public string BuildingName { get; set; }
        public bool StandardName { get; set; }
    }
    public static class ExportFileNaming
    {
        static string RawModelName(string modelPath,string title)
        {
            var name=string.IsNullOrWhiteSpace(modelPath)?title:Path.GetFileNameWithoutExtension(modelPath);
            if(string.IsNullOrWhiteSpace(name))name="未命名项目";
            if(name.EndsWith(".rvt",StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.Length-4);
            if(name.StartsWith("3D_",StringComparison.OrdinalIgnoreCase))name=name.Substring(3);
            return name.Trim();
        }
        static string CleanToken(string value)=>new string((value??"").Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray()).Trim().TrimEnd('.');
        public static string ModelName(string modelPath,string title)=>CleanToken(RawModelName(modelPath,title));
        public static ExportNames SuggestNames(string modelPath,string title,string kind)
        {
            var name=RawModelName(modelPath,title);
            var split=name.LastIndexOf('_');var standard=split>0&&split<name.Length-1;
            // Legacy hyphens are suggestions only; the export window always asks
            // for confirmation. Do not turn a trailing date/revision into a building.
            if(split<0) {
                split=name.LastIndexOfAny(new[]{'-','－','—'});
                if(split>=0&&Regex.IsMatch(name.Substring(split+1),@"^\d+$"))split=-1;
            }
            return new ExportNames {ProjectName=split>=0?name.Substring(0,split).Trim():name,
                BuildingName=kind=="总图"?"总图":split>=0?name.Substring(split+1).Trim():"",StandardName=standard};
        }
        public static string FileKind(string modelPath,string title)
        {
            var parts=RawModelName(modelPath,title).Split('_');
            return parts.Length<2?null:parts.Last()=="总图"?"总图":"单体建筑";
        }
        public static string Stem(string modelPath,string title,string stage,string kind,DateTime date)
        {
            var names=SuggestNames(modelPath,title,kind);
            return StemForNames(names.ProjectName,names.BuildingName,stage,kind,date);
        }
        public static string StemForNames(string projectName,string buildingName,string stage,string kind,DateTime date)
        {
            if(stage!="报规"&&stage!="施工图")throw new InvalidOperationException("请先在模型属性管理中人工确认报规或施工图阶段。");
            if(kind!="总图"&&kind!="单体建筑"&&kind!="单体")throw new InvalidOperationException("模型类型尚未确定，请确认本次按单体还是总图导出。");
            var project=CleanToken(projectName);var building=kind=="总图"?"总图":CleanToken(buildingName);
            if(string.IsNullOrWhiteSpace(project))throw new InvalidOperationException("请填写项目名称。");
            if(string.IsNullOrWhiteSpace(building))throw new InvalidOperationException("请填写具体单体名称，例如综合楼或大门。");
            return "OUT_"+project+"_"+stage+"_"+building+"_"+date.ToString("yyyyMMdd",CultureInfo.InvariantCulture);
        }
        public static string InitialDirectory(string modelPath,string fallback)
        {
            if(!string.IsNullOrWhiteSpace(modelPath)&&Path.IsPathRooted(modelPath))
            {
                var folder=Path.GetDirectoryName(modelPath);
                if(!string.IsNullOrEmpty(folder)&&Directory.Exists(folder))return folder;
            }
            return fallback;
        }
        public static string NextPath(string directory,string stem)
        {
            if(string.IsNullOrWhiteSpace(stem)||Path.GetFileName(stem)!=stem)throw new InvalidOperationException("导出文件名不应包含文件夹路径。");
            long maximum=0;
            var pattern=new Regex("^"+Regex.Escape(stem)+@"_V([0-9]+)\.(?:ifc(?:\.log)?|psets\.tsv|receipt\.json)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            foreach(var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var match=pattern.Match(Path.GetFileName(path));if(!match.Success)continue;
                if(!long.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var number)||number==long.MaxValue)
                    throw new InvalidOperationException("已有导出版本号过大，请调整项目名称或保存目录。");
                maximum=Math.Max(maximum,number);
            }
            return Path.Combine(directory,stem+"_V"+(maximum+1).ToString(CultureInfo.InvariantCulture)+".ifc");
        }
        public static string ResolveSelection(string selectedPath)
        {
            var path=Path.GetFullPath(selectedPath);
            if(!string.Equals(Path.GetExtension(path),".ifc",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("导出文件扩展名应为 .ifc。");
            var stem=Regex.Replace(Path.GetFileNameWithoutExtension(path),@"_V[0-9]+$","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            return NextPath(Path.GetDirectoryName(path),stem);
        }
    }
}
