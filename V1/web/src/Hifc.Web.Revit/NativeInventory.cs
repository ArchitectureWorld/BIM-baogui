using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Hifc.Web.Contracts;
using ElementRecord = Hifc.Web.Contracts.ElementRecord;
using ParameterValue = Hifc.Web.Contracts.ParameterValue;

namespace Hifc.Web.Revit
{
    public static class NativeInventory
    {
        public static ElementRecord[] Read(Document document, IDictionary<string, string> assigned = null) => new ElementRecord[0];
        public static ParameterValue[] ReadValues(Document document, CaptureTarget[] targets) => new ParameterValue[0];
    }
}
