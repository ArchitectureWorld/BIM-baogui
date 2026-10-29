using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Hifc.Web.Revit
{
    internal static class NativeParameterDependencies
    {
        internal static string[] Read(Document document, ElementId parameterId, string source, Element[] owners)
        {
            var dependencies = new List<string>();
            if (parameterId == null || parameterId.IntegerValue <= 0) return new[] { "Unknown:参数定义没有可核验的项目标识" };
            foreach (ViewSchedule schedule in new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)))
            {
                try {
                    var definitions = new List<ScheduleDefinition> { schedule.Definition };
                    if (schedule.Definition.HasEmbeddedSchedule) definitions.Add(schedule.Definition.EmbeddedDefinition);
                    foreach (var definition in definitions)
                    foreach (var fieldId in definition.GetFieldOrder())
                    {
                        var field = definition.GetField(fieldId);
                        if (field.ParameterId == parameterId) dependencies.Add("Schedule:" + schedule.UniqueId + ":" + schedule.Name);
                        else if (field.IsCalculatedField) dependencies.Add("Unknown:明细表计算字段引用无法完整核验:" + schedule.UniqueId + ":" + field.GetName());
                        if (field.IsCombinedParameterField && field.GetCombinedParameters().Any(p => p.ParamId == parameterId)) dependencies.Add("ScheduleCombined:" + schedule.UniqueId + ":" + schedule.Name);
                    }
                } catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException) { dependencies.Add("Unknown:明细表依赖读取失败:" + schedule.UniqueId); }
            }
            foreach (ParameterFilterElement filter in new FilteredElementCollector(document).OfClass(typeof(ParameterFilterElement)))
            {
                try { if (Parameters(filter.GetElementFilter()).Contains(parameterId.IntegerValue)) dependencies.Add("Filter:" + filter.UniqueId + ":" + filter.Name); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException) { dependencies.Add("Unknown:过滤器依赖读取失败:" + filter.UniqueId); }
            }
            foreach (var owner in owners)
            {
                try { foreach (var parameter in owner.GetParameters(source)) { var global = parameter.GetAssociatedGlobalParameter(); if (global != ElementId.InvalidElementId) dependencies.Add("GlobalAssociation:" + owner.UniqueId + ":" + global.IntegerValue); } }
                catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException) { dependencies.Add("Unknown:全局参数关联读取失败:" + owner.UniqueId); }
            }
            foreach (GlobalParameter global in new FilteredElementCollector(document).OfClass(typeof(GlobalParameter)))
            {
                try { if ((global.GetFormula() ?? "").IndexOf(source, StringComparison.Ordinal) >= 0) dependencies.Add("GlobalFormula:" + global.UniqueId + ":" + global.Name); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException) { dependencies.Add("Unknown:全局参数公式读取失败:" + global.UniqueId); }
            }
            return dependencies.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
        private static IEnumerable<int> Parameters(ElementFilter filter)
        {
            if (filter is ElementParameterFilter parameters) return parameters.GetRules().Select(rule => rule.GetRuleParameter().IntegerValue);
            if (filter is LogicalAndFilter and) return and.GetFilters().SelectMany(Parameters);
            if (filter is LogicalOrFilter or) return or.GetFilters().SelectMany(Parameters);
            throw new InvalidOperationException("无法识别过滤器的参数依赖。");
        }
    }
}
