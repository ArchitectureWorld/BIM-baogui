using System;

namespace Hifc.Reference
{
    public sealed class ReferenceProperty
    {
        public string PropertySet { get; set; }
        public string Binding { get; set; }
        public string Entity { get; set; }
        public string Property { get; set; }
        public string RawType { get; set; }
        // Preserve the frozen source evidence; correct only this proven vendor typo at runtime.
        public string ExportType => PropertySet == "Pset_建筑技术信息属性集" && Property == "地下建筑层数" && RawType == "Integerl" ? "Integer" : RawType;
        public string SourceParameter { get; set; }
        public bool NativeGroup { get; set; }
    }

    public sealed class ParameterRead
    {
        public string Name { get; set; }
        public string Origin { get; set; }
        public bool HasValue { get; set; }
        public object Value { get; set; }
        public string StorageType { get; set; }
        public string DefinitionType { get; set; }
        public string ParameterGuid { get; set; }
        public string DisplayValue { get; set; }
        public bool ReadOnly { get; set; }
    }

}
