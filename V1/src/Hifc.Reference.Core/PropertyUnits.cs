using System;
using System.Globalization;

namespace Hifc.Reference
{
    public static class PropertyUnits
    {
        public static string Canonical(string property,string rawType)
        {
            if(rawType!="Real"||property==null)return null;
            if(property.EndsWith("面积",StringComparison.Ordinal))return "m²";
            return property=="室内外高差最大值"||property=="窗台高"
                ||Array.Exists(new[]{"高度","标高","层高","宽度","长度","进深"},suffix=>property.EndsWith(suffix,StringComparison.Ordinal))?"m":null;
        }
        public static string ExportValue(ReferenceProperty property,ParameterRead read,string revitUnitType)
        {
            var raw=ReferencePolicy.ToBridgeValue(read);
            if(read?.StorageType!="Double"||read.HasValue!=true||raw==null)return raw;
            var unit=Canonical(property.Property,property.ExportType);
            if(unit=="m²"&&revitUnitType=="UT_Area")return (Convert.ToDouble(read.Value,CultureInfo.InvariantCulture)*.3048*.3048).ToString("R",CultureInfo.InvariantCulture);
            if(unit=="m"&&revitUnitType=="UT_Length")return (Convert.ToDouble(read.Value,CultureInfo.InvariantCulture)*.3048).ToString("R",CultureInfo.InvariantCulture);
            // Number parameters already store the business quantity; never apply
            // a length scale merely because the displayed value looks numeric.
            return raw;
        }
    }
}
