using System.Text.Json;
using System.Text.Json.Nodes;
using Hifc.Web.Contracts;

namespace Hifc.Web.RuleEngine;

public sealed partial class RuleAdapter
{
    private static NativeDataWrite SettingsWrite(CheckResult basis)
    {
        var json=StableJson(JsonSerializer.SerializeToNode(basis.Settings,MaterialJson)).ToJsonString();
        // Validate the stored identity/hash even when its contents need no change.
        var write=DataWrite(basis,ModelStorageSchemas.WebSettingsId,ModelStorageSchemas.WebSettingsName,json);
        var previous=basis.ProjectData.SingleOrDefault(r=>r.SchemaId==ModelStorageSchemas.WebSettingsId);
        if(!string.IsNullOrEmpty(previous?.Json)) {
            try {
                var stored=JsonNode.Parse(previous.Json);
                if(stored is not JsonObject)throw new InvalidOperationException("模型内 Web 设置不是有效对象，未覆盖原记录。");
                if(StableJson(stored).ToJsonString()==json)return null;
            }
            catch(JsonException ex) { throw new InvalidOperationException("模型内 Web 设置格式损坏，未覆盖原记录。",ex); }
        }
        return write;
    }

    // Preserve the ModelSettings object shape and array order; dictionary/object
    // insertion order must not cause a second write for identical settings.
    private static JsonNode StableJson(JsonNode value) => value switch {
        JsonObject obj => new JsonObject(obj.OrderBy(p=>p.Key,StringComparer.Ordinal)
            .Select(p=>new KeyValuePair<string,JsonNode>(p.Key,StableJson(p.Value)))),
        JsonArray array => new JsonArray(array.Select(StableJson).ToArray()),
        _ => value?.DeepClone()
    };
}
