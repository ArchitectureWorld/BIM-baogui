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
