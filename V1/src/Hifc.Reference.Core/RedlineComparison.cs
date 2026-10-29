using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hifc.Reference
{
    public sealed class RedlineOutline
    {
        public RedlinePoint[] Vertices { get; set; } = new RedlinePoint[0];
        public string UnsupportedReason { get; set; }
    }
    public sealed class RedlineCarrier
    {
        public int ElementId { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
        public RedlineOutline Outline { get; set; }
        public bool SolidVerified { get; set; }
        public double BottomM { get; set; }
        public double TopM { get; set; }
    }
    public sealed class RedlineCheck
    {
        public bool Passed { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public double? MaximumDeviationM { get; set; }
        public double? GapM { get; set; }
        public double? IntersectionVolumeM3 { get; set; }
    }
    public sealed class RedlineReport
    {
        public RedlineCheck Pair { get; set; }
        public RedlineCheck Manual { get; set; }
        public string[] Issues { get; set; } = new string[0];
    }
    public static class RedlineRoles
    {
        public const string NetLand = "规划净用地";
        public const string Control = "净用地控制红线";
        public static FieldCheck[] ControlFields(Func<string,string> readSource)
        {
            if(readSource==null)throw new ArgumentNullException(nameof(readSource));
            // Entity variants in the contract intentionally share source definitions.
            // Read the source once; never turn those declarations into duplicate values.
            return ReferenceCatalog.FromEmbeddedBaseline().Properties.Where(p=>p.PropertySet==ControlLinePolicy.PropertySet&&(p.Property=="名称"||p.Property=="红线类型"))
                .GroupBy(p=>p.Property).Select(group=>{
                    var sources=group.Select(p=>p.SourceParameter).Distinct(StringComparer.Ordinal).ToArray();
                    if(sources.Length!=1)throw new InvalidOperationException("规划控制线“"+group.Key+"”的属性来源不唯一。");
                    return new FieldCheck{PropertySet=ControlLinePolicy.PropertySet,Property=group.Key,Source=sources[0],Value=readSource(sources[0])};
                }).ToArray();
        }
        public static string Resolve(ModelElementCheck row)
        {
            if(row==null||!row.Classified||!string.IsNullOrEmpty(row.Issue))return null;
            if(ReferenceCatalog.LegacySlot(row.Slot??"")==NetLand)return NetLand;
            if(ReferenceCatalog.LegacySlot(row.Slot??"")!="规划控制线")return null;
            var fields=(row.Fields??new FieldCheck[0]).Where(f=>f.PropertySet==ControlLinePolicy.PropertySet).ToArray();
            return new[]{"名称","红线类型"}.All(name=>fields.Count(f=>f.Property==name)==1&&fields.Single(f=>f.Property==name).Value==Control)?Control:null;
        }
    }
    public static class RedlineComparison
    {
        public static RedlineCheck Outlines(RedlineOutline left, RedlineOutline right, double toleranceM)
        {
            Tolerance(toleranceM);
            foreach(var outline in new[]{left,right}) {
                var issue=outline==null?"缺少外轮廓。":outline.UnsupportedReason??RedlineMath.PolygonIssue(outline.Vertices);
                if(issue!=null)return Pending(issue);
            }
            var a=RedlineMath.Ring(left.Vertices);var b=RedlineMath.Ring(right.Vertices);
            bool passed=Within(a,b,toleranceM)&&Within(b,a,toleranceM);
            // The capsule coverage test checks every point along every edge, not only
            // area, a projection hash, vertices, or a fixed tessellation sample.
            double hi=0;
            foreach(var p in a)foreach(var q in b)hi=Math.Max(hi,Math.Sqrt((p.X-q.X)*(p.X-q.X)+(p.Y-q.Y)*(p.Y-q.Y)));
            double lo=0;
            for(int i=0;i<36&&hi-lo>1e-8;i++){double mid=(lo+hi)/2;if(Within(a,b,mid)&&Within(b,a,mid))hi=mid;else lo=mid;}
            return new RedlineCheck{Passed=passed,Status=passed?"通过":"不一致",MaximumDeviationM=hi,
                Detail="外轮廓双向最大偏差 "+N(hi)+" m；水平容差 "+N(toleranceM)+" m。按完整边界形状及位置比较。"};
        }
        public static RedlineCheck Manual(RedlineData data, RedlineOutline model, double toleranceM)
        {
            Tolerance(toleranceM);var validation=RedlineDataPolicy.Validate(data);
            if(!validation.CanCompare)return Pending(string.Join("；",validation.Issues));
            double factor=data.Unit==RedlineUnit.Millimeters?.001:1;
            return Outlines(new RedlineOutline{Vertices=data.Points.Select(p=>new RedlinePoint{Sequence=p.Sequence,X=p.X*factor,Y=p.Y*factor}).ToArray()},model,toleranceM);
        }
        public static RedlineCheck Pair(RedlineCarrier land, RedlineCarrier control, double? intersectionVolumeM3, double toleranceM, double minimumGapM)
        {
            Tolerance(toleranceM);
            if(!RedlineMath.Finite(minimumGapM)||minimumGapM<0)throw new InvalidOperationException("实体最小净距必须为非负有限数值（米）。");
            if(land==null||control==null||land.Role!=RedlineRoles.NetLand||control.Role!=RedlineRoles.Control||land.ElementId==control.ElementId)
                return Pending("需要各一个明确分类的规划净用地及净用地控制红线载体，不能以名称猜测或同一对象代替。");
            var outline=Outlines(land.Outline,control.Outline,toleranceM);
            if(!land.SolidVerified||!control.SolidVerified||!intersectionVolumeM3.HasValue||!RedlineMath.Finite(intersectionVolumeM3.Value)||intersectionVolumeM3<0||
                !RedlineMath.Finite(land.BottomM)||!RedlineMath.Finite(land.TopM)||!RedlineMath.Finite(control.BottomM)||!RedlineMath.Finite(control.TopM)||
                land.TopM<=land.BottomM||control.TopM<=control.BottomM)
                return Pending("实体碰撞/厚度未可靠核对，不能通过。"+(outline.Detail??""));
            double gap=Math.Max(land.BottomM-control.TopM,control.BottomM-land.TopM);
            bool separated=gap>minimumGapM+1e-9&&intersectionVolumeM3.Value<=1e-9;
            var below=control.TopM<land.BottomM?"控制红线在净用地下方":land.TopM<control.BottomM?"控制红线在净用地上方":"两实体竖向范围重叠或接触";
            return new RedlineCheck{Passed=outline.Passed&&separated,Status=outline.Status=="待核对"?"待核对":outline.Passed&&separated?"通过":"不通过",
                MaximumDeviationM=outline.MaximumDeviationM,GapM=gap,IntersectionVolumeM3=intersectionVolumeM3,
                Detail=outline.Detail+"\n规划净用地厚度 "+N(land.TopM-land.BottomM)+" m；控制红线厚度 "+N(control.TopM-control.BottomM)+" m；竖向净距 "+N(gap)+" m（要求大于 "+N(minimumGapM)+" m）；实算相交体积 "+N(intersectionVolumeM3.Value)+" m³。"+below+"。同轮廓的这两个不同业务载体不作为重复净用地。"};
        }
        public static RedlineCheck Pending(string detail)=>new RedlineCheck{Status="待核对",Detail=detail};
        static string N(double value)=>value.ToString("0.#########",CultureInfo.InvariantCulture);
        static void Tolerance(double value){if(!RedlineMath.Finite(value)||value<=0)throw new InvalidOperationException("水平容差必须是大于零的有限数值（米）。");}
        sealed class Interval { internal double Lo,Hi; }
        static bool Within(RedlinePoint[] a,RedlinePoint[] b,double tolerance)
        {
            for(int i=0;i<a.Length;i++) {
                var intervals=new List<Interval>();var p=a[i];var q=a[(i+1)%a.Length];
                for(int j=0;j<b.Length;j++)Capsule(intervals,p,q,b[j],b[(j+1)%b.Length],tolerance);
                double covered=0;bool starts=false;
                foreach(var interval in intervals.OrderBy(x=>x.Lo)) {
                    if(interval.Hi<0||interval.Lo>1)continue;
                    if(!starts){if(interval.Lo>1e-12)return false;starts=true;}
                    if(interval.Lo>covered+1e-12)return false;
                    covered=Math.Max(covered,interval.Hi);if(covered>=1-1e-12)break;
                }
                if(!starts||covered<1-1e-12)return false;
            }
            return true;
        }
        static void Capsule(List<Interval> list,RedlinePoint p,RedlinePoint q,RedlinePoint c,RedlinePoint d,double radius)
        {
            double dx=q.X-p.X,dy=q.Y-p.Y,vx=d.X-c.X,vy=d.Y-c.Y,wx=p.X-c.X,wy=p.Y-c.Y;
            Circle(list,dx,dy,wx,wy,radius);Circle(list,dx,dy,p.X-d.X,p.Y-d.Y,radius);
            double v2=vx*vx+vy*vy;
            if(v2<=1e-24)return;
            double lo=0,hi=1;
            if(Clip(ref lo,ref hi,wx*vx+wy*vy,dx*vx+dy*vy,0,v2)&&
                Clip(ref lo,ref hi,RedlineMath.Cross(wx,wy,vx,vy),RedlineMath.Cross(dx,dy,vx,vy),-radius*Math.Sqrt(v2),radius*Math.Sqrt(v2)))
                list.Add(new Interval{Lo=lo,Hi=hi});
        }
        static bool Clip(ref double lo,ref double hi,double offset,double slope,double minimum,double maximum)
        {
            if(Math.Abs(slope)<1e-24)return offset>=minimum-1e-14&&offset<=maximum+1e-14;
            double a=(minimum-offset)/slope,b=(maximum-offset)/slope;
            lo=Math.Max(lo,Math.Min(a,b));hi=Math.Min(hi,Math.Max(a,b));return lo<=hi+1e-14;
        }
        static void Circle(List<Interval> list,double dx,double dy,double wx,double wy,double radius)
        {
            double a=dx*dx+dy*dy,b=2*(dx*wx+dy*wy),c=wx*wx+wy*wy-radius*radius;
            if(a<=1e-24){if(c<=0)list.Add(new Interval{Lo=0,Hi=1});return;}
            double discriminant=b*b-4*a*c;
            if(discriminant<0)return;
            double root=Math.Sqrt(discriminant);list.Add(new Interval{Lo=Math.Max(0,(-b-root)/(2*a)),Hi=Math.Min(1,(-b+root)/(2*a))});
        }
    }
}
