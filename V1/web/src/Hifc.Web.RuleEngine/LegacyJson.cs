using System.Collections.Generic;
using System.Text.Json;

// Server-only adapter. Existing Local source is compiled read-only and remains unchanged.
namespace System.Web.Script.Serialization
{
    public sealed class JavaScriptSerializer
    {
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, IncludeFields = true };
        public int MaxJsonLength { get; set; } = 16 * 1024 * 1024;
        public object DeserializeObject(string json)
        {
            if (json.Length > MaxJsonLength) throw new InvalidOperationException("JSON 超出允许大小。");
            using var document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
            return Read(document.RootElement);
        }
        public T Deserialize<T>(string json)
        {
            if (json.Length > MaxJsonLength) throw new InvalidOperationException("JSON 超出允许大小。");
            return JsonSerializer.Deserialize<T>(json.TrimStart('\uFEFF'), Options);
        }
        public string Serialize(object value) => JsonSerializer.Serialize(value, Options);
        private static object Read(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Read(p.Value), StringComparer.Ordinal),
            JsonValueKind.Array => value.EnumerateArray().Select(Read).ToArray(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt32(out var small) ? (object)small : value.TryGetInt64(out var large) ? (object)large : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
