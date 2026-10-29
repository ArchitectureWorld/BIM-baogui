using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace Hifc.Reference
{
    public enum RedlineUnit { Unknown, Meters, Millimeters }
    public enum RedlineFrame { Unknown, Internal, Shared }
    public enum RedlineAxes { Unknown, XEastYNorth, XNorthYEast }
    public sealed class RedlinePoint
    {
        public int Sequence { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public string SourceCellX { get; set; }
        public string SourceCellY { get; set; }
    }
    public sealed class RedlineData
    {
        public int Version { get; set; } = 1;
        public string Source { get; set; } = "MANUAL";
        public string SourcePath { get; set; }
        public string SourceHash { get; set; }
        public string SheetName { get; set; } = "Sheet1";
        public RedlinePoint[] Points { get; set; } = new RedlinePoint[0];
        public string[] ImportIssues { get; set; } = new string[0];
        public bool Accepted { get; set; }
        public RedlineUnit Unit { get; set; }
        public RedlineFrame Frame { get; set; }
        public RedlineAxes Axes { get; set; }
        public string CoordinateSystemName { get; set; }
    }
    public sealed class RedlineValidation
    {
        public bool ValidCoordinates { get; set; }
        public bool CanCompare { get; set; }
        public string[] Issues { get; set; }
        public bool ExplicitClosure { get; set; }
    }
    public static class RedlineDataPolicy
    {
        public static RedlineValidation Validate(RedlineData data)
        {
            var coordinateIssues=new List<string>();
            if(data==null)return new RedlineValidation{Issues=new[]{"未导入人工红线坐标。"}};
            if(data.Version!=1)coordinateIssues.Add("人工红线资料版本不受支持。");
            if(data.Source!="MANUAL")coordinateIssues.Add("仅接受人工填写/审核的 MANUAL 红线坐标，模型自身不能生成权威对照坐标。");
            coordinateIssues.AddRange(data.ImportIssues??new string[0]);
            var points=data.Points??new RedlinePoint[0];
            if(points.Any(p=>p==null)||!points.Select(p=>p?.Sequence??0).SequenceEqual(Enumerable.Range(1,points.Length)))
                coordinateIssues.Add("序号必须从 1 连续递增，不能缺号或重复；按表中顺序组成外轮廓。");
            var polygonIssue=RedlineMath.PolygonIssue(points);
            if(polygonIssue!=null)coordinateIssues.Add(polygonIssue);
            var valid=coordinateIssues.Count==0;
            var issues=new List<string>(coordinateIssues);
            if(!data.Accepted)issues.Add("尚未确认该表为人工填写/审核的红线坐标。");
            if(!Enum.IsDefined(typeof(RedlineUnit),data.Unit)||data.Unit==RedlineUnit.Unknown)issues.Add("坐标单位未确认：请选择米或毫米。");
            if(!Enum.IsDefined(typeof(RedlineFrame),data.Frame)||data.Frame==RedlineFrame.Unknown)issues.Add("坐标框架未确认：请选择 Revit 内部或当前项目位置的共享坐标。");
            if(!Enum.IsDefined(typeof(RedlineAxes),data.Axes)||data.Axes==RedlineAxes.Unknown)issues.Add("X/Y 对应方向未确认：请选择 X东/Y北 或 X北/Y东。");
            return new RedlineValidation{ValidCoordinates=valid,CanCompare=issues.Count==0,Issues=issues.Distinct().ToArray(),
                ExplicitClosure=points.Length>1&&points[0]!=null&&points[points.Length-1]!=null&&RedlineMath.Same(points[0],points[points.Length-1])};
        }
        public static string Serialize(RedlineData data)
        {
            var check=Validate(data);
            if(!check.ValidCoordinates||!data.Accepted||!Enum.IsDefined(typeof(RedlineUnit),data.Unit)||!Enum.IsDefined(typeof(RedlineFrame),data.Frame)||!Enum.IsDefined(typeof(RedlineAxes),data.Axes))
                throw new InvalidOperationException("不能保存未确认或无效的人工红线资料："+string.Join("；",check.Issues));
            return new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Serialize(data);
        }
        public static RedlineData Deserialize(string json)
        {
            try {
                var value=new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Deserialize<RedlineData>(json);
                if(value==null||value.Version!=1||!value.Accepted||!Validate(value).ValidCoordinates||
                    !Enum.IsDefined(typeof(RedlineUnit),value.Unit)||!Enum.IsDefined(typeof(RedlineFrame),value.Frame)||!Enum.IsDefined(typeof(RedlineAxes),value.Axes))
                    throw new InvalidOperationException("模型内人工红线资料无效或版本不受支持。");
                return value;
            }catch(InvalidOperationException){throw;}catch(Exception ex){throw new InvalidOperationException("无法读取模型内人工红线资料。",ex);}
        }
    }

    internal static class RedlineMath
    {
        internal static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        internal static bool Same(RedlinePoint a,RedlinePoint b)=>Math.Abs(a.X-b.X)<1e-10&&Math.Abs(a.Y-b.Y)<1e-10;
        internal static RedlinePoint[] Ring(RedlinePoint[] points)=>points.Length>1&&Same(points[0],points[points.Length-1])?points.Take(points.Length-1).ToArray():points;
        internal static double Cross(double ax,double ay,double bx,double by)=>ax*by-ay*bx;
        internal static string PolygonIssue(RedlinePoint[] input)
        {
            if(input==null||input.Length<3)return "至少需要 3 个完整人工坐标点。";
            if(input.Length>2000)return "坐标点超过 2000 个，需先人工核对并精简，不执行可能失真的比对。";
            if(input.Any(p=>p==null||!Finite(p.X)||!Finite(p.Y)))return "坐标包含非有限数值。";
            var ring=Ring(input);
            if(ring.Length<3)return "闭合后不足 3 个不同的坐标点。";
            for(int i=0;i<ring.Length;i++)for(int j=i+1;j<ring.Length;j++)if(Same(ring[i],ring[j]))return "除末点闭合外，轮廓中存在重复坐标点。";
            double area=0;var origin=ring[0];
            for(int i=0;i<ring.Length;i++){var a=ring[i];var b=ring[(i+1)%ring.Length];area+=Cross(a.X-origin.X,a.Y-origin.Y,b.X-origin.X,b.Y-origin.Y);}
            if(Math.Abs(area)<1e-12)return "坐标轮廓面积为零或退化。";
            for(int i=0;i<ring.Length;i++)for(int j=i+1;j<ring.Length;j++) {
                if(j==i+1||i==0&&j==ring.Length-1)continue;
                if(Intersects(ring[i],ring[(i+1)%ring.Length],ring[j],ring[(j+1)%ring.Length]))return "坐标轮廓自交或存在相接的非相邻边。";
            }
            return null;
        }
        static bool Intersects(RedlinePoint a,RedlinePoint b,RedlinePoint c,RedlinePoint d)
        {
            double abx=b.X-a.X,aby=b.Y-a.Y,cdx=d.X-c.X,cdy=d.Y-c.Y;
            double acx=c.X-a.X,acy=c.Y-a.Y,den=Cross(abx,aby,cdx,cdy);
            if(Math.Abs(den)>1e-12){double t=Cross(acx,acy,cdx,cdy)/den,u=Cross(acx,acy,abx,aby)/den;return t>=-1e-12&&t<=1+1e-12&&u>=-1e-12&&u<=1+1e-12;}
            if(Math.Abs(Cross(acx,acy,abx,aby))>1e-10)return false;
            return Math.Max(Math.Min(a.X,b.X),Math.Min(c.X,d.X))<=Math.Min(Math.Max(a.X,b.X),Math.Max(c.X,d.X))+1e-10&&
                Math.Max(Math.Min(a.Y,b.Y),Math.Min(c.Y,d.Y))<=Math.Min(Math.Max(a.Y,b.Y),Math.Max(c.Y,d.Y))+1e-10;
        }
    }
}
