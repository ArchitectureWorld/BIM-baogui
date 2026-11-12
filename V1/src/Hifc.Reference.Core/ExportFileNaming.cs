using System;
using System.Collections.Generic;
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
        public string SourceStage { get; set; }
        public string SuggestionMessage { get; set; }
    }

    public static class ExportFileNaming
    {
        static readonly Regex Prefix = new Regex(@"^(?:3D|OUT|标准化)_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex MetadataSuffix = new Regex(@"(?:^|_)(?:[0-9]{8}|V[0-9]+)$|\.[0-9]{4}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex StageMarker = new Regex(@"_(报规|施工图)(?=_|$)", RegexOptions.CultureInvariant);

        static string RawModelName(string modelPath, string title)
        {
            var name = string.IsNullOrWhiteSpace(modelPath) ? title : Path.GetFileName(modelPath);
            if (string.IsNullOrWhiteSpace(name)) name = "未命名项目";
            name = Regex.Replace(name.Trim(), @"\.(?:rvt|ifc)$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            while (Prefix.IsMatch(name)) name = Prefix.Replace(name, "", 1);
            return name.Trim();
        }

        static string WithoutMetadata(string name)
        {
            name = name.Trim();
            while (MetadataSuffix.IsMatch(name)) name = MetadataSuffix.Replace(name, "").Trim();
            return name;
        }

        static string CleanToken(string value)
            => new string((value ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');

        public static string ModelName(string modelPath, string title) => CleanToken(RawModelName(modelPath, title));

        public static ExportNames SuggestNames(string modelPath, string title, string kind)
        {
            var names = ParseNames(RawModelName(modelPath, title));
            if (kind == "总图") names.BuildingName = "总图";
            return names;
        }

        static ExportNames ParseNames(string name)
        {
            name = WithoutMetadata(name);
            var markers = StageMarker.Matches(name).Cast<Match>().ToArray();
            if (markers.Length == 0) return ParseLegacyName(name);

            var stage = markers[0].Groups[1].Value;
            if (markers.Any(marker => marker.Groups[1].Value != stage)) return AmbiguousNames(stage);

            var head = name.Substring(0, markers[0].Index).Trim();
            var subjects = new List<string>();
            var firstFragmentWasMetadata = false;
            for (var index = 0; index < markers.Length; index++)
            {
                var start = markers[index].Index + markers[index].Length;
                var end = index + 1 < markers.Length ? markers[index + 1].Index : name.Length;
                if (start < end && name[start] == '_') start++;
                var fragment = name.Substring(start, end - start).Trim();
                var subject = WithoutMetadata(fragment);
                if (index == 0) firstFragmentWasMetadata = fragment.Length > 0 && subject.Length == 0;
                if (subject.Length > 0) subjects.Add(subject);
            }

            // In the old order, the stage separates complete project and subject
            // fields. Repeated export wrappers must agree on the subject.
            if (subjects.Count > 0)
            {
                var distinct = subjects.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (distinct.Length != 1 || string.IsNullOrWhiteSpace(head) || IsMetadataLabel(distinct[0])) return AmbiguousNames(stage);
                if (firstFragmentWasMetadata)
                {
                    var nested = ParseSubjectFirstName(head, stage);
                    return nested.StandardName && string.Equals(nested.BuildingName, distinct[0], StringComparison.OrdinalIgnoreCase)
                        ? nested : AmbiguousNames(stage);
                }
                return ResolvedNames(head, distinct[0], stage, true);
            }
            return ParseSubjectFirstName(head, stage);
        }

        static ExportNames ParseSubjectFirstName(string head, string stage)
        {
            // In the subject-first order, additional underscores could belong to
            // either identity. A fixed site subject supplies an explicit boundary.
            if (head.StartsWith("总图_", StringComparison.Ordinal) && head.Length > 3)
                return ResolvedNames(head.Substring(3), "总图", stage, true);
            var split = head.IndexOf('_');
            if (split <= 0 || split != head.LastIndexOf('_') || split == head.Length - 1 || IsMetadataLabel(head.Substring(0, split)))
                return AmbiguousNames(stage);
            return ResolvedNames(head.Substring(split + 1), head.Substring(0, split), stage, true);
        }

        static ExportNames ParseLegacyName(string name)
        {
            var split = name.LastIndexOf('_');
            var standard = split > 0 && split < name.Length - 1;
            if (split < 0) split = name.LastIndexOfAny(new[] { '-', '－', '—' });
            if (split <= 0 || split == name.Length - 1 || IsMetadataLabel(name.Substring(split + 1)))
                return ResolvedNames(name, "", null, false);
            return ResolvedNames(name.Substring(0, split), name.Substring(split + 1), null, standard);
        }

        static bool IsMetadataLabel(string value)
            => Regex.IsMatch(value.Trim(), @"^(?:[0-9]+|V[0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static ExportNames ResolvedNames(string project, string subject, string stage, bool standard)
            => new ExportNames { ProjectName = project.Trim(), BuildingName = subject.Trim(), SourceStage = stage, StandardName = standard };

        static ExportNames AmbiguousNames(string stage)
            => new ExportNames {
                ProjectName = "", BuildingName = "", SourceStage = stage,
                SuggestionMessage = "文件名中的项目与主体边界或重复片段存在歧义，请依据原文件名填写完整项目名称和具体单体名称。"
            };

        public static string FileKind(string modelPath, string title)
        {
            var names = ParseNames(RawModelName(modelPath, title));
            if (!names.StandardName || string.IsNullOrWhiteSpace(names.BuildingName)) return null;
            return names.BuildingName == "总图" ? "总图" : "单体建筑";
        }

        public static string Stem(string modelPath, string title, string stage, string kind, DateTime date)
        {
            var names = SuggestNames(modelPath, title, kind);
            return StemForNames(names.ProjectName, names.BuildingName, stage, kind, date);
        }

        public static string StemForNames(string projectName, string buildingName, string stage, string kind, DateTime date)
        {
            if (stage != "报规" && stage != "施工图") throw new InvalidOperationException("请先在模型属性管理中人工确认报规或施工图阶段。");
            if (kind != "总图" && kind != "单体建筑" && kind != "单体") throw new InvalidOperationException("模型类型尚未确定，请确认本次按单体还是总图导出。");
            var project = CleanToken(projectName);
            var building = kind == "总图" ? "总图" : CleanToken(buildingName);
            if (string.IsNullOrWhiteSpace(project)) throw new InvalidOperationException("请填写项目名称。");
            if (string.IsNullOrWhiteSpace(building) || building == "单体" || building == "单体建筑")
                throw new InvalidOperationException("请填写具体单体名称，例如综合楼或大门。");
            return "OUT_" + building + "_" + project + "_" + stage + "_" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        public static string InitialDirectory(string modelPath, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(modelPath) && Path.IsPathRooted(modelPath))
            {
                var folder = Path.GetDirectoryName(modelPath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) return folder;
            }
            return fallback;
        }

        public static string NextPathForNames(string directory, string projectName, string buildingName, string stage, string kind, DateTime date)
        {
            var stem = StemForNames(projectName, buildingName, stage, kind, date);
            var project = CleanToken(projectName);
            var building = kind == "总图" ? "总图" : CleanToken(buildingName);
            var legacyStem = "OUT_" + project + "_" + stage + "_" + building + "_" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return NextVersionPath(directory, stem, new[] { stem, legacyStem });
        }

        public static string NextPath(string directory, string stem)
        {
            ValidateStem(stem);
            var dated = Regex.Match(stem, @"^OUT_.+_([0-9]{8})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (dated.Success && DateTime.TryParseExact(dated.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var names = ParseNames(RawModelName("", stem));
                if (names.StandardName && names.SourceStage != null)
                    return NextPathForNames(directory, names.ProjectName, names.BuildingName, names.SourceStage, names.BuildingName == "总图" ? "总图" : "单体建筑", date);
            }
            return NextVersionPath(directory, stem, new[] { stem });
        }

        static void ValidateStem(string stem)
        {
            if (string.IsNullOrWhiteSpace(stem) || Path.GetFileName(stem) != stem)
                throw new InvalidOperationException("导出文件名不应包含文件夹路径。");
        }

        static string NextVersionPath(string directory, string stem, string[] compatibleStems)
        {
            ValidateStem(stem);
            long maximum = 0;
            var alternatives = string.Join("|", compatibleStems.Distinct(StringComparer.OrdinalIgnoreCase).Select(Regex.Escape));
            var pattern = new Regex("^(?:" + alternatives + @")_V([0-9]+)\.(?:ifc(?:\.log)?|psets\.tsv|receipt\.json)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var match = pattern.Match(Path.GetFileName(path));
                if (!match.Success) continue;
                if (!long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == long.MaxValue)
                    throw new InvalidOperationException("已有导出版本号过大，请调整项目名称或保存目录。");
                maximum = Math.Max(maximum, number);
            }
            return Path.Combine(directory, stem + "_V" + (maximum + 1).ToString(CultureInfo.InvariantCulture) + ".ifc");
        }

        public static string ResolveSelection(string selectedPath)
        {
            var path = Path.GetFullPath(selectedPath);
            if (!string.Equals(Path.GetExtension(path), ".ifc", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("导出文件扩展名应为 .ifc。");
            var stem = Regex.Replace(Path.GetFileNameWithoutExtension(path), @"_V[0-9]+$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return NextPath(Path.GetDirectoryName(path), stem);
        }
    }
}
