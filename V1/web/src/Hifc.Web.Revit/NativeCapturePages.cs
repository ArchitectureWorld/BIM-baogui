using System;
using System.Linq;
using System.Collections.Generic;
using Hifc.Web.Contracts;

namespace Hifc.Web.Revit
{
    public static class NativeCapturePages
    {
        public static CapturePage[] Create(RequestContext context, ParameterValue[] values, NativeDefinitionSnapshot[] definitions, int pageSize = 1024)
        {
            if (context == null || values == null || definitions == null || pageSize < 1) throw new ArgumentException("字段分页输入不完整。");
            var pages = new List<CapturePage>();
            for (int offset = 0; offset < values.Length; offset += pageSize) pages.Add(new CapturePage { Values = values.Skip(offset).Take(pageSize).ToArray() });
            var chunks = new List<NativeDefinitionChunk>();
            foreach (var definition in definitions)
            {
                var header = new NativeDefinitionSnapshot { Source = definition.Source, Guid = definition.Guid, ParameterId = definition.ParameterId,
                    DefinitionType = definition.DefinitionType, Origin = definition.Origin, CategoryIds = definition.CategoryIds, Dependencies = definition.Dependencies, Hash = definition.Hash };
                for (int offset = 0; offset < Math.Max(1, definition.Values.Length); offset += 256)
                    chunks.Add(new NativeDefinitionChunk { Header = header, ValueOffset = offset, TotalValues = definition.Values.Length, Values = definition.Values.Skip(offset).Take(256).ToArray() });
            }
            for (int offset = 0; offset < chunks.Count; offset += 8) pages.Add(new CapturePage { DefinitionChunks = chunks.Skip(offset).Take(8).ToArray() });
            if (pages.Count == 0) pages.Add(new CapturePage());
            for (int index = 0; index < pages.Count; index++)
            {
                var page = pages[index]; page.Context = context; page.PageIndex = index; page.PageCount = pages.Count;
                page.TotalValues = values.Length; page.PageHash = Canonical.ValuesHash(page.Values);
            }
            return pages.ToArray();
        }
    }
}
