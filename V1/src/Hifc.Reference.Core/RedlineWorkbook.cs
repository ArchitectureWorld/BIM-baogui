using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace Hifc.Reference
{
    public static class RedlineWorkbook
    {
        const string Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string Rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string PackageRel="http://schemas.openxmlformats.org/package/2006/relationships";
        const string Title="规划净用地红线点";
        sealed class Cell { internal string Text;internal bool Formula; }
        public static RedlineData Read(string path)
        {
            path=Path.GetFullPath(path);
            var info=new FileInfo(path);
            if(!info.Exists||info.Length>20*1024*1024)throw new InvalidOperationException("红线表不存在或超过 20 MB。");
            var bytes=File.ReadAllBytes(path);
            var result=new RedlineData{SourcePath=path};
            using(var sha=SHA256.Create())result.SourceHash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            try {
                using(var memory=new MemoryStream(bytes))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)) {
                    XNamespace ns=Main;XNamespace relation=Rel;
                    var workbook=Xml(zip,"xl/workbook.xml");
                    var sheets=workbook.Descendants(ns+"sheet").Where(s=>(string)s.Attribute("name")=="Sheet1").ToArray();
                    if(sheets.Length!=1)throw new InvalidOperationException("红线模板必须包含唯一的 Sheet1。");
                    var id=(string)sheets[0].Attribute(relation+"id");
                    var links=Xml(zip,"xl/_rels/workbook.xml.rels").Descendants(XName.Get("Relationship",PackageRel)).Where(r=>(string)r.Attribute("Id")==id).ToArray();
                    if(links.Length!=1||(string)links[0].Attribute("TargetMode")=="External")throw new InvalidOperationException("Sheet1 工作表关系无效。");
                    var target=(string)links[0].Attribute("Target");
                    if(string.IsNullOrEmpty(target)||target.Contains("..")||target.Contains(":"))throw new InvalidOperationException("工作表路径不受支持。");
                    target=target.StartsWith("/",StringComparison.Ordinal)?target.TrimStart('/'):("xl/"+target);
                    var sheet=Xml(zip,target.Replace('\\','/'));
                    var strings=zip.GetEntry("xl/sharedStrings.xml")==null?new string[0]:Xml(zip,"xl/sharedStrings.xml").Descendants(ns+"si").Select(s=>string.Concat(s.Descendants(ns+"t").Select(t=>t.Value))).ToArray();
                    var cells=new Dictionary<string,Cell>(StringComparer.Ordinal);
                    foreach(var cell in sheet.Descendants(ns+"c")) {
                        var address=(string)cell.Attribute("r");
                        if(string.IsNullOrEmpty(address)||!Regex.IsMatch(address,"^[A-Z]+[1-9][0-9]*$"))throw new InvalidOperationException("工作表存在无效单元格地址。");
                        string text;var type=(string)cell.Attribute("t");
                        if(type=="inlineStr")text=string.Concat(cell.Descendants(ns+"t").Select(t=>t.Value));
                        else {
                            text=(string)cell.Element(ns+"v")??"";
                            if(type=="s") {
                                int index;
                                if(!int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out index)||index<0||index>=strings.Length)throw new InvalidOperationException("共享文本索引无效："+address);
                                text=strings[index];
                            }else if(type=="e"||type=="b")text="无效坐标值 "+text;
                        }
                        if(cells.ContainsKey(address))throw new InvalidOperationException("存在重复单元格："+address);
                        cells.Add(address,new Cell{Text=text.Trim(),Formula=cell.Element(ns+"f")!=null});
                    }
                    Func<string,string> get=address=>cells.TryGetValue(address,out var cell)?cell.Text:"";
                    if(get("A1")!=Title||get("A2")!="序号"||get("B2")!="图纸的X坐标"||get("C2")!="图纸的Y坐标"||
                        !sheet.Descendants(ns+"mergeCell").Any(m=>(string)m.Attribute("ref")=="A1:C1"))
                        throw new InvalidOperationException("红线模板标题或表头不匹配。请保留 Sheet1 / A1:C1 合并标题“规划净用地红线点”及原 A2:C2 表头。");
                    var issues=new List<string>();var points=new List<RedlinePoint>();
                    foreach(var row in cells.Keys.Where(k=>Regex.IsMatch(k,"^[ABC][0-9]+$")).Select(k=>int.Parse(k.Substring(1),CultureInfo.InvariantCulture)).Where(r=>r>=3).Distinct().OrderBy(r=>r)) {
                        var addresses=new[]{"A"+row,"B"+row,"C"+row};var values=addresses.Select(get).ToArray();
                        if(values.All(string.IsNullOrWhiteSpace))continue;
                        if(addresses.Any(a=>cells.TryGetValue(a,out var cell)&&cell.Formula)){issues.Add("第 "+row+" 行含公式，不能作为人工审核坐标；请填写确认后的数值。");continue;}
                        int sequence;double x,y;
                        if(!int.TryParse(values[0],NumberStyles.None,CultureInfo.InvariantCulture,out sequence)||sequence<1){issues.Add("A"+row+" 序号无效或缺失。");continue;}
                        if(!double.TryParse(values[1],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!double.TryParse(values[2],NumberStyles.Float,CultureInfo.InvariantCulture,out y)||!RedlineMath.Finite(x)||!RedlineMath.Finite(y)) {
                            issues.Add("B"+row+" / C"+row+" 缺少完整的有限 X/Y 数值，不将空格当作 0。");continue;
                        }
                        points.Add(new RedlinePoint{Sequence=sequence,X=x,Y=y,SourceCellX="B"+row,SourceCellY="C"+row});
                    }
                    result.Points=points.ToArray();result.ImportIssues=issues.ToArray();return result;
                }
            }catch(InvalidOperationException){throw;}catch(Exception ex)when(ex is InvalidDataException||ex is XmlException||ex is ArgumentException||ex is OverflowException){throw new InvalidOperationException("无法读取人工红线 XLSX："+ex.Message,ex);}
        }
        static XDocument Xml(ZipArchive zip,string path)
        {
            var entry=zip.GetEntry(path);
            if(entry==null||entry.Length>16*1024*1024)throw new InvalidOperationException("XLSX 部件缺失或过大："+path);
            using(var stream=entry.Open())using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024}))return XDocument.Load(reader);
        }
        public static void Write(string path, RedlineData data)
        {
            var check=RedlineDataPolicy.Validate(data);
            if(!check.ValidCoordinates||!data.Accepted)throw new InvalidOperationException("仅可导出已确认的有效 MANUAL 人工坐标。");
            path=Path.GetFullPath(path);
            if(!string.IsNullOrEmpty(data.SourcePath)&&string.Equals(path,Path.GetFullPath(data.SourcePath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("不能覆盖原始人工坐标表，请另存为新文件。");
            // CreateNew also protects another existing workbook. This service never
            // copies coordinates from model geometry into the authoritative template.
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create)) {
                XNamespace ns=Main;
                var sheetData=new XElement(ns+"sheetData",new XElement(ns+"row",new XAttribute("r",1),TextCell("A1",Title)),
                    new XElement(ns+"row",new XAttribute("r",2),TextCell("A2","序号"),TextCell("B2","图纸的X坐标"),TextCell("C2","图纸的Y坐标")));
                for(int i=0;i<data.Points.Length;i++) {
                    var point=data.Points[i];int row=i+3;
                    sheetData.Add(new XElement(ns+"row",new XAttribute("r",row),NumberCell("A"+row,point.Sequence),NumberCell("B"+row,point.X),NumberCell("C"+row,point.Y)));
                }
                Add(zip,"[Content_Types].xml",new XDocument(new XElement(XName.Get("Types","http://schemas.openxmlformats.org/package/2006/content-types"),
                    new XElement(XName.Get("Default","http://schemas.openxmlformats.org/package/2006/content-types"),new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(XName.Get("Default","http://schemas.openxmlformats.org/package/2006/content-types"),new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),
                    new XElement(XName.Get("Override","http://schemas.openxmlformats.org/package/2006/content-types"),new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(XName.Get("Override","http://schemas.openxmlformats.org/package/2006/content-types"),new XAttribute("PartName","/xl/worksheets/sheet1.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
                Add(zip,"_rels/.rels",Relationships("rId1","xl/workbook.xml","officeDocument"));
                Add(zip,"xl/_rels/workbook.xml.rels",Relationships("rId1","worksheets/sheet1.xml","worksheet"));
                Add(zip,"xl/workbook.xml",new XDocument(new XElement(ns+"workbook",new XAttribute(XNamespace.Xmlns+"r",Rel),new XElement(ns+"sheets",new XElement(ns+"sheet",new XAttribute("name","Sheet1"),new XAttribute("sheetId",1),new XAttribute(XName.Get("id",Rel),"rId1"))))));
                Add(zip,"xl/worksheets/sheet1.xml",new XDocument(new XElement(ns+"worksheet",new XElement(ns+"cols",new XElement(ns+"col",new XAttribute("min",1),new XAttribute("max",1),new XAttribute("width",12),new XAttribute("customWidth",1)),new XElement(ns+"col",new XAttribute("min",2),new XAttribute("max",3),new XAttribute("width",25),new XAttribute("customWidth",1))),sheetData,new XElement(ns+"mergeCells",new XAttribute("count",1),new XElement(ns+"mergeCell",new XAttribute("ref","A1:C1"))))));
            }
        }
        static XDocument Relationships(string id,string target,string type)=>new XDocument(new XElement(XName.Get("Relationships",PackageRel),new XElement(XName.Get("Relationship",PackageRel),new XAttribute("Id",id),new XAttribute("Type",Rel+"/"+type),new XAttribute("Target",target))));
        static XElement TextCell(string address,string value)=>new XElement(XName.Get("c",Main),new XAttribute("r",address),new XAttribute("t","inlineStr"),new XElement(XName.Get("is",Main),new XElement(XName.Get("t",Main),value)));
        static XElement NumberCell(string address,double value)=>new XElement(XName.Get("c",Main),new XAttribute("r",address),new XElement(XName.Get("v",Main),value.ToString("R",CultureInfo.InvariantCulture)));
        static void Add(ZipArchive zip,string name,XDocument xml){using(var stream=zip.CreateEntry(name).Open())using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))xml.Save(writer);}
    }
}
