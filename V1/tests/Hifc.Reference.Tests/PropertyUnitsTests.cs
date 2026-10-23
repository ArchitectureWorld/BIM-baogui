using System;
using System.Globalization;
using Hifc.Reference;

internal static class PropertyUnitsTests
{
    static string Export(string name,double value,string unitType)
    {
        var type=typeof(ReferencePolicy).Assembly.GetType("Hifc.Reference.PropertyUnits");
        if(type==null)throw new Exception("Metric quantity export policy missing");
        return (string)type.GetMethod("ExportValue").Invoke(null,new object[]{new ReferenceProperty{Property=name,RawType="Real"},new ParameterRead{StorageType="Double",HasValue=true,Value=value},unitType});
    }
    static void Near(double expected,string value){if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var actual)||Math.Abs(expected-actual)>1e-8)throw new Exception("quantity unit differs: "+value);}
    public static void Run(Action<string,Action> check)
    {
        check("native_height_and_road_width_export_meters_from_internal_feet",()=>{Near(3,Export("建筑高度",3/.3048,"UT_Length"));Near(4,Export("道路宽度",4/.3048,"UT_Length"));});
        check("native_floor_and_building_areas_export_square_meters",()=>{Near(123.45,Export("楼层面积",123.45/.3048/.3048,"UT_Area"));Near(37.6,Export("建筑占地面积",37.6/.3048/.3048,"UT_Area"));});
        check("dimensionless_number_is_not_reinterpreted_as_physical_quantity",()=>{Near(3,Export("建筑高度",3,"UT_Number"));Near(.75,Export("容积率",.75,"UT_Number"));});
        check("contract_length_depth_and_height_difference_export_meters",()=>{
            foreach(var name in new[]{"长度","阳台长度","进深","阳台进深","室内外高差最大值","窗台高"})
                Near(3,Export(name,3/.3048,"UT_Length"));
        });
    }
}
