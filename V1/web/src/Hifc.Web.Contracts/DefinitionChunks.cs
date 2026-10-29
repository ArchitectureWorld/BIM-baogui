using System;
using System.Collections.Generic;
using System.Linq;

namespace Hifc.Web.Contracts
{
    public sealed class NativeDefinitionChunk
    {
        public NativeDefinitionSnapshot Header { get; set; }
        public int ValueOffset { get; set; }
        public int TotalValues { get; set; }
        public ParameterValue[] Values { get; set; } = new ParameterValue[0];
    }
    public static class DefinitionChunks
    {
        public static NativeDefinitionSnapshot[] Assemble(CapturePage[] pages)
        {
            var complete = new List<NativeDefinitionSnapshot>();
            var direct = pages.SelectMany(p => p.Definitions ?? new NativeDefinitionSnapshot[0]).ToArray();
            if (direct.Any(d => d == null || d.Values == null || d.Values.Length > 256 || string.IsNullOrWhiteSpace(d.Source) || d.Hash != Canonical.DefinitionHash(d)))
                throw new InvalidOperationException("参数定义快照无效；大范围定义必须分片上传。");
            foreach (var group in direct.GroupBy(d => d.Source + "|" + d.ParameterId)) {
                if (group.Select(d => d.Hash).Distinct().Count() != 1) throw new InvalidOperationException("同一参数定义出现不同快照。");
                complete.Add(group.First());
            }
            var chunks = pages.SelectMany(p => p.DefinitionChunks ?? new NativeDefinitionChunk[0]).ToArray();
            if (pages.Any(p => (p.DefinitionChunks?.Length ?? 0) > 8) || chunks.Any(c => c == null || c.Header == null || string.IsNullOrWhiteSpace(c.Header.Source)
                || c.Header.Values == null || c.Header.Values.Length != 0 || c.Values == null || c.Values.Length > 256 || c.ValueOffset < 0 || c.TotalValues < 0 || c.TotalValues > 2000000
                || c.TotalValues > 0 && c.Values.Length == 0 || c.Values.Any(v => v == null)))
                throw new InvalidOperationException("参数定义分片格式或大小无效。");
            foreach (var group in chunks.GroupBy(c => c.Header.Source + "|" + c.Header.ParameterId)) {
                var ordered = group.OrderBy(c => c.ValueOffset).ToArray(); var first = ordered[0];
                if (complete.Any(d => d.Source == first.Header.Source && d.ParameterId == first.Header.ParameterId)
                    || ordered.Select(c => c.ValueOffset).Distinct().Count() != ordered.Length || ordered.Any(c => c.TotalValues != first.TotalValues || c.Header.Hash != first.Header.Hash
                        || Canonical.DefinitionHash(c.Header) != Canonical.DefinitionHash(first.Header)))
                    throw new InvalidOperationException("参数定义分片重复或属于不同快照。");
                var values = new List<ParameterValue>();
                foreach (var chunk in ordered) {
                    if (chunk.ValueOffset != values.Count) throw new InvalidOperationException("参数定义分片缺失或重叠。");
                    values.AddRange(chunk.Values);
                }
                if (values.Count != first.TotalValues) throw new InvalidOperationException("参数定义分片尚未完整收到。");
                var header = first.Header;
                var result = new NativeDefinitionSnapshot { Source = header.Source, Guid = header.Guid, ParameterId = header.ParameterId, DefinitionType = header.DefinitionType,
                    Origin = header.Origin, CategoryIds = header.CategoryIds, Dependencies = header.Dependencies, Values = values.ToArray(), Hash = header.Hash };
                if (result.Hash != Canonical.DefinitionHash(result)) throw new InvalidOperationException("完整参数定义与分片声明的散列不一致。");
                complete.Add(result);
            }
            if (complete.Sum(d => (long)d.Values.Length) > 2000000) throw new InvalidOperationException("参数定义依赖总数超过允许大小。");
            return complete.OrderBy(d => d.Source, StringComparer.Ordinal).ThenBy(d => d.ParameterId).ToArray();
        }
    }
}
