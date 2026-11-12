using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Hifc.Reference;
using Hifc.Revit.Management;
using Hifc.Web.Client;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    internal sealed class NativeStatisticsSource : IDisposable
    {
        internal Document Document;
        internal Document Host;
        internal ElementId LinkId;
        internal Transform Placement;
        internal SiteStatisticsSource Info;
        internal string PlacementIdentity;
        internal void Validate()
        {
            if (Document == null || !Document.IsValidObject || Document.PathName != Info.Path)
                throw new InvalidOperationException("统计源文档已关闭或另存，请刷新。");
            if (LinkId == null) return;
            var link = Host.GetElement(LinkId) as RevitLinkInstance;
            if (link == null || !Document.Equals(link.GetLinkDocument())) throw new InvalidOperationException("链接来源已变化，请刷新。");
            using (var transform = link.GetTotalTransform())
                if (Identity(Document, transform) != PlacementIdentity) throw new InvalidOperationException("链接位置已变化，请刷新。");
        }
        internal static string Identity(Document document, Transform transform)
        {
            var vectors = new[] { transform.Origin, transform.BasisX, transform.BasisY, transform.BasisZ };
            return Canonical.Hash((document.PathName ?? "").ToLowerInvariant() + "|" + document.ProjectInformation.UniqueId + "|" +
                string.Join("|", vectors.SelectMany(v => new[] { v.X, v.Y, v.Z }).Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
        }
        public void Dispose() { Placement?.Dispose(); }
    }

    internal sealed class NativeStatisticsCapture : IDisposable
    {
        internal StatisticsRequest Request { get; }
        internal NativeStatisticsSource[] Sources { get; }
        private readonly Dictionary<string, Solid[]> projections = new Dictionary<string, Solid[]>(StringComparer.Ordinal);

        internal static NativeStatisticsSource[] ListSources(Document host, string key, bool links)
        {
            var result = new List<NativeStatisticsSource>();
            var identity = Transform.Identity;
            result.Add(new NativeStatisticsSource { Document = host, Host = host, Placement = identity, PlacementIdentity = NativeStatisticsSource.Identity(host, identity),
                Info = new SiteStatisticsSource { Key = key, Name = host.Title, Path = host.PathName, IsHost = true, Selected = true } });
            if (links)
                foreach (var link in new FilteredElementCollector(host).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    var document = link.GetLinkDocument(); var transform = link.GetTotalTransform();
                    result.Add(new NativeStatisticsSource { Document = document, Host = host, LinkId = link.Id, Placement = transform,
                        PlacementIdentity = document == null ? null : NativeStatisticsSource.Identity(document, transform),
                        Info = new SiteStatisticsSource { Key = key + "/link/" + link.UniqueId, Name = link.Name, Path = document?.PathName,
                            Available = document != null, Notes = document == null ? "链接尚未加载" : "所选直接链接实例" } });
                }
            return result.ToArray();
        }

        internal NativeStatisticsCapture(string kind, string ruleVersion, NativeStatisticsSource[] sources, string[] selected, string fallbackProfile)
        {
            Sources = sources;
            var host = sources.Single(s => s.Info.IsHost);
            var keys = new HashSet<string>(selected ?? new string[0], StringComparer.Ordinal); keys.Add(host.Info.Key);
            Request = new StatisticsRequest { RuleVersion = ruleVersion, Kind = kind, DocumentKey = host.Info.Key, DocumentTitle = host.Info.Name,
                SourcePath = host.Info.Path, Sources = sources.Select(source => {
                    var metadata = JsonWire.Deserialize<SiteStatisticsSource>(JsonWire.Serialize(source.Info));
                    metadata.Selected = metadata.Available && keys.Contains(metadata.Key);
                    return new StatisticsSourceCapture { Source = metadata, PlacementIdentity = source.PlacementIdentity,
                        Settings = new ModelSettings { RuleProfileId = fallbackProfile ?? "" } };
                }).ToArray() };
        }

        internal void ReadInventory(string sourceKey)
        {
            var source = Sources.Single(s => s.Info.Key == sourceKey); source.Validate();
            var target = Request.Sources.Single(s => s.Source.Key == sourceKey);
            var data = NativeProjectData.Read(source.Document);
            var saved = NativeSettingsState.Record(data);
            if (saved.Json != null) target.Settings = NativeSettingsState.Decode(saved);
            target.Elements = NativeInventory.Read(source.Document, null, false);
            if (Request.Kind != "site") return;
            foreach (var element in target.Elements)
            {
                if (element.LevelElevationM.HasValue)
                    element.LevelElevationM = source.Placement.OfPoint(new XYZ(0, 0, element.LevelElevationM.Value / .3048)).Z * .3048;
                if (element.ProjectedAreaM2.HasValue || !string.IsNullOrEmpty(element.DeclaredSlot))
                {
                    try { element.ProjectedAreaM2 = (double)SiteStatisticsGeometryReader.UnionArea(Projection(source, element.UniqueId)); }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
                    { element.ProjectedAreaM2 = null; element.AreaReason = "几何投影尚待核对。" + ex.Message; }
                }
            }
        }

        internal void ReadValues(StatisticsSourcePlan plan)
        {
            var source = Sources.Single(s => s.Info.Key == plan.SourceKey); source.Validate();
            Request.Sources.Single(s => s.Source.Key == plan.SourceKey).Values = NativeInventory.ReadValues(source.Document, plan.Targets);
        }

        private Solid[] Projection(NativeStatisticsSource source, string uniqueId)
        {
            var key = source.Info.Key + "|" + uniqueId;
            if (projections.TryGetValue(key, out var existing)) return existing;
            source.Validate();
            var element = source.Document.GetElement(uniqueId) ?? throw new InvalidOperationException("统计构件已删除。");
            var solids = Request.Kind == "building" ? NativeGeometry.ProjectionSolids(element) : SiteStatisticsGeometryReader.Project(element, source.Placement);
            if (solids == null || solids.Length == 0) throw new InvalidOperationException("构件 " + element.Id + " 无可靠水平投影。");
            projections.Add(key, solids); return solids;
        }

        internal StatisticsGeometryResult Measure(StatisticsGeometryGroup group)
        {
            var result = new StatisticsGeometryResult { Key = group.Key,
                ComponentAreaSum = group.Members.All(m => m.GeometricArea.HasValue) ? group.Members.Sum(m => m.GeometricArea.Value) : (decimal?)null };
            try
            {
                if (!result.ComponentAreaSum.HasValue) throw new InvalidOperationException("部分构件的几何投影未取得。");
                decimal area = 0;
                foreach (var plane in group.Members.GroupBy(m => Math.Round(m.ElevationM ?? 0, 6)))
                {
                    var shapes = plane.SelectMany(member => {
                        var source = Sources.Single(s => s.Info.Key == member.SourceKey);
                        var element = source.Document.GetElement(new ElementId(member.ElementId));
                        if (element?.UniqueId != member.UniqueId) throw new InvalidOperationException("统计构件身份已变化。");
                        return Projection(source, member.UniqueId);
                    }).ToArray();
                    area += SiteStatisticsGeometryReader.UnionArea(shapes);
                }
                result.UnionArea = area; result.OverlapArea = Math.Max(result.ComponentAreaSum.Value - area, 0); result.Verified = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                result.Verified = false; result.UnionArea = result.ComponentAreaSum;
                result.Notes = "分类投影合并未完成，当前值为逐构件几何小计，重叠尚待核对。" + ex.Message;
            }
            return result;
        }

        internal void Validate()
        {
            foreach (var source in Sources.Where(s => Request.Sources.Any(c => c.Source.Key == s.Info.Key && c.Source.Selected))) source.Validate();
        }
        public void Dispose()
        {
            foreach (var shapes in projections.Values) foreach (var shape in shapes) shape.Dispose();
            projections.Clear(); foreach (var source in Sources) source.Dispose();
        }
    }
}
