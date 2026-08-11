using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Hifc;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class OfficialHifcWriteService
  {
    public static bool Enqueue(
      OfficialHifcWriteRequest request,
      Action<OfficialHifcWriteResult> completed,
      out string error)
    {
      return RevitHost.EnqueueAction(uiapp =>
      {
        OfficialHifcWriteResult result;
        try
        {
          result = Execute(uiapp, request);
        }
        catch (Exception exception)
        {
          result = Failure("写入失败：" + exception.Message);
        }
        completed?.Invoke(result);
      }, out error);
    }

    private static OfficialHifcWriteResult Execute(
      UIApplication uiapp,
      OfficialHifcWriteRequest request)
    {
      Document document = uiapp.ActiveUIDocument?.Document;
      if (document == null) return Failure("Revit 当前没有活动项目文档。");
      if (uiapp.Application.VersionNumber != "2020")
        return Failure("本组件仅支持 Revit 2020。");
      if (document.IsFamilyDocument)
        return Failure("族文件不能执行 H-IFC 属性写入。");
      if (document.IsReadOnly)
        return Failure("当前文档为只读状态。");
      if (request == null)
        return Failure("写入请求为空。");
      if (request.PropertyKeys == null || request.PropertyKeys.Count == 0)
        return Failure("至少需要一个属性。");
      if (request.Values == null || request.Values.Count == 0)
        return Failure("至少需要一个值。");
      if (request.Values.Count != 1
        && request.Values.Count != request.PropertyKeys.Count)
        return Failure("值数量必须为 1，或与属性数量一致。");

      var mappings = new List<OfficialHifcMapping>();
      foreach (string key in request.PropertyKeys)
      {
        if (!OfficialHifcMappingCatalog.Instance.TryResolve(
          key,
          out OfficialHifcMapping mapping))
          return Failure("未找到 H-IFC 属性规则映射：" + key);
        mappings.Add(mapping);
      }

      var workItems = new List<OfficialParameterWriteItem>();
      for (int index = 0; index < mappings.Count; index++)
      {
        OfficialHifcMapping mapping = mappings[index];
        OfficialPluginEntityPolicy policy = mapping.EntityPolicy;
        if (policy.IsBlocked)
          return Failure(
            policy.WritePolicy
            + "："
            + mapping.IfcEntity
            + " 尚未取得官方插件 Revit 写入/导出协议证据。" );

        IReadOnlyList<Element> sourceTargets = ResolveTargetsForMapping(
          document,
          mapping,
          request.ElementIds);
        string rawValue = request.Values.Count == 1
          ? request.Values[0]
          : request.Values[index];
        foreach (Element sourceTarget in sourceTargets)
        {
          Element target = mapping.IsTypeBinding
            ? ResolveTypeTarget(document, sourceTarget)
            : sourceTarget;
          if (target == null)
            return Failure("无法解析类型对象：" + sourceTarget.Id.IntegerValue);
          workItems.Add(new OfficialParameterWriteItem
          {
            Mapping = mapping,
            Target = target,
            RawValue = rawValue
          });
        }
      }

      if (workItems.Count == 0)
        return Failure("没有符合属性实体和类别要求的 Revit 写入目标。");

      OfficialParameterProjectionResult projectionResult;
      using (var group = new TransactionGroup(
        document,
        "湖北BIM报规｜官方插件源参数双写"))
      {
        if (group.Start() != TransactionStatus.Started)
          return Failure("无法启动 Revit 事务组。");
        try
        {
          using (var transaction = new Transaction(
            document,
            "写入内部唯一参数与官方精确源参数"))
          {
            if (transaction.Start() != TransactionStatus.Started)
              throw new InvalidOperationException("无法启动 Revit 事务。");

            projectionResult = OfficialParameterProjectionService.WriteAndVerify(
              document,
              workItems);
            if (transaction.Commit() != TransactionStatus.Committed)
              throw new InvalidOperationException("Revit 写入事务未成功提交。");
          }

          if (group.Assimilate() != TransactionStatus.Committed)
            return Failure("事务组未能合并为一次可撤销操作。");
        }
        catch (Exception exception)
        {
          try { group.RollBack(); } catch { }
          return Failure("写入或回读失败，已整体回滚：" + exception.Message);
        }
      }

      var messages = new List<string>(
        projectionResult.Messages ?? Array.Empty<string>());
      messages.Add(
        "当前完成的是 Revit 双写与回读；仍需由官方 H-IFC 插件重新导出 IFC，"
        + "并由官方检查软件核对实体、属性集、类型、单位和值。" );
      return new OfficialHifcWriteResult
      {
        Success = true,
        Status = "Revit 双写与回读通过｜待官方重新导出验收",
        WriteCount = projectionResult.PropertyValueCount,
        OfficialCompatibilityVerified = false,
        Messages = messages
      };
    }

    private static IReadOnlyList<Element> ResolveTargetsForMapping(
      Document document,
      OfficialHifcMapping mapping,
      IReadOnlyList<int> ids)
    {
      OfficialPluginEntityPolicy policy = mapping.EntityPolicy;
      if (ids == null || ids.Count == 0)
      {
        if (policy.AllowsProjectInformationDefault)
          return new Element[] { document.ProjectInformation };
        throw new InvalidOperationException(
          "仅 IfcProject/IfcBuilding 属性允许在未提供元素时使用 ProjectInformation；"
          + mapping.IfcEntity
          + " 必须提供符合官方对象映射的明确 ElementId。" );
      }

      if (!Enum.TryParse(mapping.Category, out BuiltInCategory categoryId))
        throw new InvalidOperationException(
          "不支持的 Revit 类别：" + mapping.Category);

      var targets = new List<Element>();
      foreach (int id in ids.Distinct())
      {
        Element element = document.GetElement(new ElementId(id));
        if (element == null)
          throw new InvalidOperationException("找不到 Revit 元素 Id=" + id);
        if (element.Category == null
          || element.Category.Id.IntegerValue != (int)categoryId)
          continue;
        targets.Add(element);
      }

      if (targets.Count == 0)
        throw new InvalidOperationException(
          mapping.IfcEntity
          + " 属性要求类别 "
          + mapping.Category
          + "，所提供 ElementId 中没有匹配对象。" );
      return targets;
    }

    private static Element ResolveTypeTarget(Document document, Element element)
    {
      ElementId typeId = element?.GetTypeId();
      return typeId == null || typeId == ElementId.InvalidElementId
        ? null
        : document.GetElement(typeId);
    }

    private static OfficialHifcWriteResult Failure(params string[] messages)
    {
      return new OfficialHifcWriteResult
      {
        Success = false,
        Status = "H-IFC 属性写入失败",
        WriteCount = 0,
        OfficialCompatibilityVerified = false,
        Messages = messages ?? Array.Empty<string>()
      };
    }
  }
}
