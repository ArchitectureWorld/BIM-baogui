using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    internal static class RedlineGeometry
    {
        sealed class Candidate {internal Element Element;internal string Role;}
        sealed class GeometryRead:IDisposable
        {
            internal RedlineCarrier Carrier;
            internal Solid Solid;
            internal XYZ[] Vertices;
            public void Dispose(){Solid?.Dispose();}
        }
        internal static RedlineReport Compare(Document doc,RedlineData data,double toleranceM,double minimumGapM)
        {
            var issues=new List<string>();var candidates=new List<Candidate>();
            foreach(var element in new FilteredElementCollector(doc).WhereElementIsNotElementType()) {
                if(element.ViewSpecific||element.Category?.CategoryType!=CategoryType.Model||element is RevitLinkInstance)continue;
                try {
                    var slot=Text(doc,element,"类目");
                    if(string.IsNullOrWhiteSpace(slot))continue;
                    slot=ReferenceCatalog.LegacySlot(slot.Trim());if(slot!=RedlineRoles.NetLand&&slot!="规划控制线")continue;
                    var row=new ModelElementCheck{Id=element.Id.IntegerValue,Name=element.Name,Classified=true,Slot=slot,
                        Fields=slot=="规划控制线"?RedlineRoles.ControlFields(name=>Text(doc,element,name)):new FieldCheck[0]};
                    var role=RedlineRoles.Resolve(row);
                    if(role!=null)candidates.Add(new Candidate{Element=element,Role=role});
                    else if((element.Name??"").Contains("用地红线")||(element.Name??"").Contains("净用地")||row.Fields.Any(f=>f.Value==RedlineRoles.Control))
                        issues.Add(element.Id.IntegerValue+" / "+element.Name+"：规划控制线的名称、红线类型需同时精确为“净用地控制红线”，当前未作为已确认载体。");
                }catch(Exception ex)when(ex is InvalidOperationException||ex is Autodesk.Revit.Exceptions.ApplicationException) {
                    // Do not skip an unreadable classification and then claim a unique pair.
                    issues.Add(element.Id.IntegerValue+" / "+element.Name+"：分类或属性来源无法可靠读取："+ex.Message);
                }
            }
            var lands=candidates.Where(c=>c.Role==RedlineRoles.NetLand).ToArray();var controls=candidates.Where(c=>c.Role==RedlineRoles.Control).ToArray();
            if(lands.Length!=1||controls.Length!=1||issues.Count>0) {
                var reason="明确分类的规划净用地 "+lands.Length+" 个；净用地控制红线 "+controls.Length+" 个。需各一个可核对载体；多载体、标签未确认或读取歧义不能自动配对。";
                if(candidates.Count>0)reason+="\n候选："+string.Join("；",candidates.Select(c=>c.Role+" / "+c.Element.Id.IntegerValue+" / "+c.Element.Name));
                return new RedlineReport{Pair=RedlineComparison.Pending(reason),Manual=RedlineComparison.Pending("模型双载体尚未唯一确定，保留人工资料，未作模型对照。"),Issues=issues.ToArray()};
            }
            using(var land=Read(lands[0]))using(var control=Read(controls[0])) {
                foreach(var carrier in new[]{land.Carrier,control.Carrier})if(!string.IsNullOrEmpty(carrier.Outline.UnsupportedReason))issues.Add(carrier.Outline.UnsupportedReason);
                double? intersection=null;
                if(land.Solid!=null&&control.Solid!=null) {
                    try {using(var common=BooleanOperationsUtils.ExecuteBooleanOperation(land.Solid,control.Solid,BooleanOperationsType.Intersect))intersection=common.Volume*.3048*.3048*.3048;}
                    catch(Exception ex)when(ex is InvalidOperationException||ex is Autodesk.Revit.Exceptions.ApplicationException){issues.Add("原生实体布尔相交检查失败："+ex.Message);}
                }
                var pair=RedlineComparison.Pair(land.Carrier,control.Carrier,intersection,toleranceM,minimumGapM);
                RedlineCheck manual;
                var validation=RedlineDataPolicy.Validate(data);
                if(!validation.CanCompare)manual=RedlineComparison.Pending(string.Join("；",validation.Issues));
                else if(land.Vertices==null)manual=RedlineComparison.Pending(land.Carrier.Outline.UnsupportedReason??"无法可靠取得规划净用地外轮廓。");
                else {
                    try {
                        var outline=new RedlineOutline{Vertices=land.Vertices.Select((p,i)=>ConvertInternal(doc,p,data.Frame,data.Axes,i+1)).ToArray()};
                        manual=RedlineComparison.Manual(data,outline,toleranceM);
                        manual.Detail+="\n采用"+(data.Frame==RedlineFrame.Shared?"当前项目位置共享坐标（"+doc.ActiveProjectLocation.Name+"）":"Revit 内部坐标")+"；"+(data.Axes==RedlineAxes.XEastYNorth?"图纸 X东/Y北":"图纸 X北/Y东")+"。";
                    }catch(Exception ex)when(ex is InvalidOperationException||ex is Autodesk.Revit.Exceptions.ApplicationException){manual=RedlineComparison.Pending("坐标转换未完成："+ex.Message);}
                }
                pair.Detail="规划净用地 ID "+land.Carrier.ElementId+"；净用地控制红线 ID "+control.Carrier.ElementId+"。\n"+pair.Detail;
                return new RedlineReport{Pair=pair,Manual=manual,Issues=issues.ToArray()};
            }
        }
        // GetProjectPosition returns the shared coordinates of this specific internal
        // point, including site rotation and translation. No GetTotalTransform direction
        // is guessed. Native acceptance must exercise a non-origin transformed point.
        internal static RedlinePoint ConvertInternal(Document doc,XYZ internalPoint,RedlineFrame frame,RedlineAxes axes,int sequence=1)
        {
            if(frame==RedlineFrame.Unknown||axes==RedlineAxes.Unknown)throw new InvalidOperationException("坐标框架和 X/Y 方向尚未确认。");
            double east,north;
            if(frame==RedlineFrame.Shared) {
                if(doc.ActiveProjectLocation==null)throw new InvalidOperationException("没有当前项目位置。");
                using(var position=doc.ActiveProjectLocation.GetProjectPosition(internalPoint)){east=position.EastWest*.3048;north=position.NorthSouth*.3048;}
            }else if(frame==RedlineFrame.Internal){east=internalPoint.X*.3048;north=internalPoint.Y*.3048;}
            else throw new InvalidOperationException("不支持的坐标框架。");
            if(axes!=RedlineAxes.XEastYNorth&&axes!=RedlineAxes.XNorthYEast)throw new InvalidOperationException("不支持的 X/Y 对应方向。");
            return new RedlinePoint{Sequence=sequence,X=axes==RedlineAxes.XEastYNorth?east:north,Y=axes==RedlineAxes.XEastYNorth?north:east};
        }
        static string Text(Document doc,Element element,string name)
        {
            var instance=OwnerText(element,name);
            return instance??OwnerText(doc.GetElement(element.GetTypeId()),name);
        }
        static string OwnerText(Element element,string name)
        {
            if(element==null)return null;
            var parameters=element.GetParameters(name);
            if(parameters.Count>1)throw new InvalidOperationException(name+" 存在重复来源。");
            var parameter=parameters.FirstOrDefault();if(parameter==null||!parameter.HasValue)return null;
            if(parameter.StorageType!=StorageType.String)throw new InvalidOperationException(name+" 不是文字来源，不能猜测分类。");
            var text=parameter.AsString();return string.IsNullOrWhiteSpace(text)?null:text;
        }
        static GeometryRead Read(Candidate candidate)
        {
            var output=new GeometryRead{Carrier=new RedlineCarrier{ElementId=candidate.Element.Id.IntegerValue,Name=candidate.Element.Name,Role=candidate.Role,Outline=new RedlineOutline()}};
            var solids=new List<Solid>();
            try {
                using(var options=new Options{DetailLevel=ViewDetailLevel.Fine,IncludeNonVisibleObjects=false,ComputeReferences=false})
                using(var geometry=candidate.Element.get_Geometry(options)) {
                    if(geometry==null)throw new InvalidOperationException("没有可核对的物理实体。");
                    Solids(geometry,solids);
                }
                if(solids.Count!=1)throw new InvalidOperationException("需单一明确实体，当前 "+solids.Count+" 个有效实体；多实体/歧义载体未自动配对。");
                output.Solid=solids[0];solids.Clear();
                var top=new List<PlanarFace>();var bottom=new List<PlanarFace>();
                foreach(Face face in output.Solid.Faces) {
                    if(!(face is PlanarFace planar))throw new InvalidOperationException("含曲面；当前检查只支持单外环的水平直线棱柱实体。");
                    var z=planar.FaceNormal.Z;
                    if(z>1-1e-8)top.Add(planar);else if(z<-1+1e-8)bottom.Add(planar);
                    else if(Math.Abs(z)>1e-8)throw new InvalidOperationException("含斜面/非水平厚度，不能以标高差代替实际实体核对。");
                }
                if(top.Count!=1||bottom.Count!=1)throw new InvalidOperationException("顶/底面不唯一，不能确定单一水平载体厚度。");
                var topPoints=Points(top[0]);var bottomPoints=Points(bottom[0]);
                var outline=new RedlineOutline{Vertices=topPoints.Select((p,i)=>new RedlinePoint{Sequence=i+1,X=p.X*.3048,Y=p.Y*.3048}).ToArray()};
                var lower=new RedlineOutline{Vertices=bottomPoints.Select((p,i)=>new RedlinePoint{Sequence=i+1,X=p.X*.3048,Y=p.Y*.3048}).ToArray()};
                var same=RedlineComparison.Outlines(outline,lower,.000001);
                if(!same.Passed)throw new InvalidOperationException("顶/底外轮廓不一致，不能确认直立棱柱实体。");
                output.Carrier.Outline=outline;output.Vertices=topPoints;
                output.Carrier.BottomM=bottom[0].Origin.Z*.3048;output.Carrier.TopM=top[0].Origin.Z*.3048;
                output.Carrier.SolidVerified=output.Carrier.TopM>output.Carrier.BottomM;
            }catch(Exception ex)when(ex is InvalidOperationException||ex is Autodesk.Revit.Exceptions.ApplicationException) {
                output.Carrier.SolidVerified=false;output.Carrier.Outline.UnsupportedReason=candidate.Element.Id.IntegerValue+" / "+candidate.Element.Name+"："+ex.Message;
                output.Vertices=null;
            }finally{foreach(var solid in solids)solid.Dispose();}
            return output;
        }
        static void Solids(GeometryElement geometry,List<Solid> solids)
        {
            if(geometry==null)throw new InvalidOperationException("几何实例为空，不能确认物理实体。");
            foreach(var item in geometry) {
                if(item is Mesh)throw new InvalidOperationException("含网格物理几何，不能只核对其中的实体而忽略网格。");
                if(item is Solid solid&&solid.Faces.Size>0) {
                    if(solid.Volume<=1e-12)throw new InvalidOperationException("含零体积或退化实体，物理厚度无法可靠核对。");
                    solids.Add(SolidUtils.CreateTransformed(solid,Transform.Identity));
                }
                else if(item is GeometryInstance instance)using(var transformed=instance.GetInstanceGeometry())Solids(transformed,solids);
            }
        }
        static XYZ[] Points(PlanarFace face)
        {
            var loops=face.GetEdgesAsCurveLoops();if(loops.Count!=1)throw new InvalidOperationException("存在多环/孔洞，尚未支持可靠单外轮廓比对。");
            var curves=loops[0].ToArray();if(curves.Length<3||curves.Any(c=>!(c is Line)||!c.IsBound))throw new InvalidOperationException("存在曲线或未闭合的边，尚未支持可靠外轮廓比对。");
            for(int i=0;i<curves.Length;i++)if(curves[i].GetEndPoint(1).DistanceTo(curves[(i+1)%curves.Length].GetEndPoint(0))>1e-7)throw new InvalidOperationException("几何外轮廓不连续/不闭合。");
            return curves.Select(c=>c.GetEndPoint(0)).ToArray();
        }
    }
}
