using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Hifc;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class Stage01OfficialHifcProjectionService
  {
    public static IReadOnlyList<string> WriteAndVerify(
      Document document,
      string payloadJson)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      PayloadEnvelope payload = ReadPayload(payloadJson);
      var messages = new List<string>();
      var items = new List<OfficialParameterWriteItem>();

      foreach (KeyValuePair<string, string> field in payload.values
        .OrderBy(item => item.Key, StringComparer.Ordinal))
      {
        if (string.IsNullOrWhiteSpace(field.Value)) continue;
        if (!field.Key.StartsWith("IfcProject|", StringComparison.Ordinal)) continue;
        if (PlanningTargetCatalog.IsManagedMvdField(field.Key)) continue;

        if (!OfficialHifcMappingCatalog.Instance.TryResolveStage01FieldKey(
          field.Key,
          out OfficialHifcMapping mapping))
        {
          if (OfficialPluginCompatibilityCatalog.Instance
            .IsStage01ProjectFieldException(field.Key))
          {
            messages.Add(
              "Stage 01 标准字段按登记例外暂不投影：" + field.Key);
            continue;
          }
          throw new InvalidOperationException(
            "Stage 01 标准字段缺少规则对应参数映射：" + field.Key);
        }

        if (mapping.EntityPolicy.IsBlocked)
          throw new InvalidOperationException(
            mapping.EntityPolicy.WritePolicy
            + "："
            + mapping.IfcEntity
            + " / "
            + mapping.ParameterName);

        items.Add(new OfficialParameterWriteItem
        {
          Mapping = mapping,
          Target = document.ProjectInformation,
          RawValue = field.Value
        });
      }

      Stage01OfficialCompatibilityDecision compatibility =
        Stage01OfficialCompatibilityPolicy.Evaluate(payload.organizations);
      messages.AddRange(compatibility.Blockers);

      if (items.Count == 0)
      {
        messages.Add("Stage 01 没有需要投影的非空 IfcProject 标准字段。");
        return messages;
      }

      OfficialParameterProjectionResult result =
        OfficialParameterProjectionService.WriteAndVerify(document, items);
      messages.AddRange(result.Messages);
      messages.Add(
        "Stage 01 已双写内部唯一参数与官方精确源参数；"
        + "可进入 02 构件与属性准备，后续由 03 执行标准 IFC4 导出与 HIFC-MVD 转译。" );
      return messages;
    }

    private static PayloadEnvelope ReadPayload(string payloadJson)
    {
      if (string.IsNullOrWhiteSpace(payloadJson))
        throw new InvalidDataException("Stage 01 初始化载荷为空。");

      var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
      PayloadEnvelope payload = serializer.Deserialize<PayloadEnvelope>(payloadJson);
      if (payload == null)
        throw new InvalidDataException("Stage 01 初始化载荷无法解析。");
      payload.values = payload.values
        ?? new Dictionary<string, string>(StringComparer.Ordinal);
      payload.organizations = payload.organizations
        ?? new List<Dictionary<string, string>>();
      return payload;
    }

    private sealed class PayloadEnvelope
    {
      public Dictionary<string, string> values { get; set; }
      public List<Dictionary<string, string>> organizations { get; set; }
    }
  }
}
