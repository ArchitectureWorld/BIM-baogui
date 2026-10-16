using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Hifc.Web.Client
{
    public static class JsonWire
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None, DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateFormatHandling = DateFormatHandling.IsoDateFormat, Culture = CultureInfo.InvariantCulture, MaxDepth = 64
        };
        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);
        public static T Deserialize<T>(string value)
        {
            if (value == null || value.Length > 32 * 1024 * 1024) throw new InvalidOperationException("服务器响应为空或超出允许大小。");
            return JsonConvert.DeserializeObject<T>(value, Settings);
        }
    }
}
