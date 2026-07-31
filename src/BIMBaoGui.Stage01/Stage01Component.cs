using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Context;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.GrasshopperTypes;
using BIMBaoGui.Stage01.Infrastructure;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.UI;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace BIMBaoGui.Stage01
{
  public sealed class Stage01Component : GH_Component
  {
    private const string PayloadKeyV05 = "HBR.Stage01.PayloadV05";
    private readonly Stage01RegistryProvider _registry = Stage01RegistryProvider.Instance;
    private Stage01Model _model;
    private RevitDocumentSnapshot _snapshot = new RevitDocumentSnapshot();
    private ValidationResult _validation = new ValidationResult(Array.Empty<ValidationMessage>());
    private IReadOnlyList<string> _operationMessages = Array.Empty<string>();
    private CommitResult _lastCommit;
    private bool _isCommitting;
    private bool _allowAutomaticStoredPayloadLoad = true;
    private string _loadedStoredPayloadIdentity = string.Empty;

    public Stage01Component()
      : base(
        "湖北BIM报规｜文件初始化",
        "报规初始化",
        "在 Rhino.Inside.Revit 中填写、校验并写入 Revit 2020 单文件初始化数据，并输出 HBR_FileContext。",
        "湖北BIM报规",
        "报规工作流")
    {
      _model = _registry.CreateDefaultModel();
    }

    public override Guid ComponentGuid => new Guid("84a95cc7-2020-4c2e-9e1b-bdfc2b02bb70");
    protected override Bitmap Icon => IconFactory.CreateComponentIcon();
    public override GH_Exposure Exposure => GH_Exposure.primary;

    internal Stage01Model Model => _model;
    internal Stage01RegistryProvider Registry => _registry;
    internal RevitDocumentSnapshot Snapshot => _snapshot;
    internal ValidationResult Validation => _validation;
    internal bool IsCommitting => _isCommitting;
    internal string CurrentStatus => ResolveStatus();
    internal IReadOnlyList<string> CurrentMessages => ResolveMessages();

    public override void CreateAttributes()
    {
      m_attributes = new Stage01ComponentAttributes(this);
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddParameter(
        new HBRFileContextParam(),
        "文件上下文",
        "Context",
        "供 02 模型任务与骨架分流使用的强类型 HBR_FileContext。",
        GH_ParamAccess.item);
      pManager.AddBooleanParameter("初始化通过", "OK", "写入与回读均通过时为 True。", GH_ParamAccess.item);
      pManager.AddTextParameter("状态", "S", "当前文件初始化状态。", GH_ParamAccess.item);
      pManager.AddTextParameter("消息", "M", "环境检查、字段校验、写入和回读消息。", GH_ParamAccess.list);
      pManager.AddTextParameter("上下文JSON", "JSON", "HBR_FileContext 的确定性 JSON，仅用于调试和外部检查。", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
      EnsureSystemValues();
      _snapshot = Stage01RevitService.ReadSnapshot(_model);
      TryAutomaticallyLoadStoredPayload();
      _validation = Stage01Validator.Validate(_model, _registry.Fields);

      bool initialized = IsInitializationPassed();
      HBRFileContext context = HBRFileContextFactory.Create(_model, _snapshot, initialized);
      dataAccess.SetData(0, new HBRFileContextGoo(context));
      dataAccess.SetData(1, initialized);
      dataAccess.SetData(2, ResolveStatus());
      dataAccess.SetDataList(3, ResolveMessages());
      dataAccess.SetData(4, HBRFileContextCanonicalizer.ToJson(context));
    }

    internal IReadOnlyList<string> GetVisibleGroups()
    {
      return Stage01UiPolicy.BuildDirectoryGroups(_registry.Groups);
    }

    internal IReadOnlyList<ConditionDefinition> GetVisibleConditions()
    {
      string modelFileType = _model.GetValue(Stage01Keys.ModelFileType);
      if (string.Equals(modelFileType, PlanningTargetRequirementPolicy.AboveGroundModel, StringComparison.Ordinal))
      {
        return _registry.Conditions
          .Where(condition => condition.Key == "building.roof"
            || condition.Key == "building.balcony"
            || condition.Key == "building.canopy")
          .ToArray();
      }
      if (string.Equals(modelFileType, PlanningTargetRequirementPolicy.UndergroundModel, StringComparison.Ordinal))
      {
        return _registry.Conditions
          .Where(condition => condition.Key == "underground.parking"
            || condition.Key == "site.civil_defense")
          .ToArray();
      }
      return _registry.Conditions.Where(condition => condition.Key.StartsWith("site.", StringComparison.Ordinal)).ToArray();
    }

    internal string GetGroupDisplayName(string group)
    {
      if (string.IsNullOrWhiteSpace(group)) return "文件初始化";
      int separator = group.IndexOf('_');
      return separator >= 0 && separator + 1 < group.Length ? group.Substring(separator + 1) : group;
    }

    internal IReadOnlyList<FieldDefinition> GetFieldsForGroup(string group)
    {
      if (string.IsNullOrWhiteSpace(group) || group == "10_项目条件" || group == "11_提交与校验")
        return Array.Empty<FieldDefinition>();

      var fields = new List<FieldDefinition>();
      fields.AddRange(_registry.Fields.Where(field => string.Equals(field.Group, group, StringComparison.Ordinal)));
      if (group == "01_文件与项目身份")
        fields.AddRange(_registry.Fields.Where(field => string.Equals(field.Group, "01_文件与阶段", StringComparison.Ordinal)));

      return fields
        .Where(field => _model.ShowAllFields || !field.Deferred)
        .GroupBy(field => field.Key, StringComparer.Ordinal)
        .Select(grouping => grouping.First())
        .ToList();
    }

    internal IReadOnlyList<FieldDefinition> GetFieldsForActiveGroup()
    {
      return GetFieldsForGroup(_model.ActiveGroup);
    }

    internal bool GroupHasRequiredFields(string group)
    {
      return GetFieldsForGroup(group).Any(IsFieldRequired);
    }

    internal int GetMissingRequiredCount(string group)
    {
      return GetFieldsForGroup(group)
        .Where(IsFieldRequired)
        .Count(field => string.IsNullOrWhiteSpace(GetFieldValue(field)));
    }

    internal bool IsFieldRequired(FieldDefinition definition)
    {
      PlanningTargetDefinition targetDefinition = definition == null
        ? null
        : PlanningTargetCatalog.GetByMvdFieldKey(definition.Key);
      if (targetDefinition != null)
      {
        PlanningTargetRequirement requirement = GetPlanningTargetRequirement(targetDefinition.MetricCode);
        return requirement == PlanningTargetRequirement.Required
          || requirement == PlanningTargetRequirement.Conditional;
      }
      return FieldInputRules.IsRequired(definition);
    }

    internal bool IsFieldEditable(FieldDefinition definition)
    {
      if (definition == null || definition.ReadOnly || definition.Deferred) return false;
      PlanningTargetDefinition targetDefinition = PlanningTargetCatalog.GetByMvdFieldKey(definition.Key);
      if (targetDefinition == null) return true;
      PlanningTargetRequirement requirement = GetPlanningTargetRequirement(targetDefinition.MetricCode);
      return requirement == PlanningTargetRequirement.Required
        || requirement == PlanningTargetRequirement.Conditional
        || requirement == PlanningTargetRequirement.Optional;
    }

    internal PlanningTargetRequirement GetPlanningTargetRequirement(string metricCode)
    {
      return PlanningTargetRequirementPolicy.GetRequirement(
        _model.GetValue(Stage01Keys.ModelFileType),
        metricCode);
    }

    internal PlanningTargetDefinition GetPlanningTargetDefinition(FieldDefinition field)
    {
      return field == null ? null : PlanningTargetCatalog.GetByMvdFieldKey(field.Key);
    }

    internal PlanningTargetValue GetPlanningTarget(string metricCode)
    {
      return _model.GetPlanningTarget(metricCode);
    }

    internal void SetPlanningTarget(PlanningTargetValue target)
    {
      if (target == null) return;
      _model.SetPlanningTarget(target);
      NotifyModelEdited();
    }

    internal void RemovePlanningTarget(string metricCode)
    {
      _model.RemovePlanningTarget(metricCode);
      NotifyModelEdited();
    }

    internal string GetFieldValue(FieldDefinition definition)
    {
      if (definition == null) return string.Empty;
      PlanningTargetDefinition targetDefinition = PlanningTargetCatalog.GetByMvdFieldKey(definition.Key);
      if (targetDefinition != null)
      {
        PlanningTargetValue target = _model.GetPlanningTarget(targetDefinition.MetricCode);
        return target?.ToMvdText() ?? string.Empty;
      }
      return definition.Entity == "IfcOrganization"
        ? _model.GetOrganizationValue(definition.Key)
        : _model.GetValue(definition.Key);
    }

    internal void SetFieldValue(FieldDefinition definition, string value)
    {
      if (!IsFieldEditable(definition)) return;
      PlanningTargetDefinition targetDefinition = PlanningTargetCatalog.GetByMvdFieldKey(definition.Key);
      if (targetDefinition != null)
      {
        if (string.IsNullOrWhiteSpace(value))
        {
          RemovePlanningTarget(targetDefinition.MetricCode);
          return;
        }
        if (PlanningTargetValue.TryParseMvdText(
          targetDefinition.MetricCode,
          value,
          targetDefinition.Unit,
          "项目初始化",
          out PlanningTargetValue target,
          out _))
          SetPlanningTarget(target);
        return;
      }

      if (definition.Entity == "IfcOrganization")
        _model.SetOrganizationValue(definition.Key, value);
      else
      {
        string previous = _model.GetValue(definition.Key);
        _model.SetValue(definition.Key, value);
        if (definition.Key == Stage01Keys.ModelFileType
          && !string.Equals(previous, value, StringComparison.Ordinal))
        {
          foreach (PlanningTargetDefinition planningTarget in PlanningTargetCatalog.All)
            _model.RemovePlanningTarget(planningTarget.MetricCode);
        }
      }
      NotifyModelEdited();
    }

    internal void ToggleBooleanField(FieldDefinition definition)
    {
      if (!IsFieldEditable(definition)) return;
      bool current = bool.TryParse(GetFieldValue(definition), out bool parsed) && parsed;
      SetFieldValue(definition, (!current).ToString());
    }

    internal void SetActiveGroup(string group)
    {
      if (string.IsNullOrWhiteSpace(group)) return;
      _model.ActiveGroup = group;
      _model.ScrollOffset = 0;
      ExpireDisplay();
    }

    internal void MoveGroup(int delta)
    {
      IReadOnlyList<string> groups = GetVisibleGroups();
      if (groups.Count == 0) return;
      int index = groups.IndexOf(_model.ActiveGroup);
      if (index < 0) index = 0;
      index = (index + delta + groups.Count) % groups.Count;
      SetActiveGroup(groups[index]);
    }

    internal void SetScrollOffset(int offset, int visibleCount)
    {
      int count = GetFieldsForActiveGroup().Count;
      _model.ScrollOffset = Stage01UiPolicy.ClampScrollOffset(offset, count, visibleCount);
      ExpireDisplay();
    }

    internal void ScrollFieldsByWheel(int delta, int visibleCount)
    {
      int count = GetFieldsForActiveGroup().Count;
      int next = Stage01UiPolicy.ScrollByWheel(_model.ScrollOffset, delta, count, visibleCount);
      if (next == _model.ScrollOffset) return;
      _model.ScrollOffset = next;
      ExpireDisplay();
    }

    internal void ToggleCondition(string key)
    {
      _model.SetCondition(key, !_model.GetCondition(key));
      NotifyModelEdited();
    }

    internal void ToggleConfirmBlank()
    {
      _model.ConfirmBlankProject = !_model.ConfirmBlankProject;
      NotifyModelEdited();
    }

    internal void ToggleAllowReinitialize()
    {
      _model.AllowReinitialize = !_model.AllowReinitialize;
      ExpireDisplay();
    }

    internal void ToggleShowAllFields()
    {
      _model.ShowAllFields = !_model.ShowAllFields;
      _model.ScrollOffset = 0;
      IReadOnlyList<string> groups = GetVisibleGroups();
      if (!groups.Contains(_model.ActiveGroup))
        _model.ActiveGroup = groups.FirstOrDefault() ?? "01_文件与项目身份";
      ExpireDisplay();
    }

    internal void AddOrganization()
    {
      _model.Organizations.Add(new Dictionary<string, string>(StringComparer.Ordinal));
      _model.OrganizationIndex = _model.Organizations.Count - 1;
      _model.ScrollOffset = 0;
      NotifyModelEdited();
    }

    internal void RemoveCurrentOrganization()
    {
      if (_model.Organizations.Count <= 1)
      {
        _model.CurrentOrganization.Clear();
        NotifyModelEdited();
        return;
      }
      _model.Organizations.RemoveAt(_model.OrganizationIndex);
      _model.OrganizationIndex = Math.Max(0, Math.Min(_model.OrganizationIndex, _model.Organizations.Count - 1));
      _model.ScrollOffset = 0;
      NotifyModelEdited();
    }

    internal void MoveOrganization(int delta)
    {
      if (_model.Organizations.Count == 0) return;
      _model.OrganizationIndex = (_model.OrganizationIndex + delta + _model.Organizations.Count) % _model.Organizations.Count;
      _model.ScrollOffset = 0;
      ExpireDisplay();
    }

    internal void ReadCurrentRevitFile()
    {
      _operationMessages = Stage01RevitService.PopulateModelFromDocument(_model);
      _allowAutomaticStoredPayloadLoad = false;
      EnsureSystemValues();
      ExpireSolution(true);
    }

    internal void ValidateNow()
    {
      _operationMessages = new[] { "已重新执行文件环境、规划目标和字段校验。" };
      ExpireSolution(true);
    }

    internal void CommitInitialization()
    {
      if (_isCommitting) return;
      EnsureSystemValues();
      _validation = Stage01Validator.Validate(_model, _registry.Fields);
      _snapshot = Stage01RevitService.ReadSnapshot(_model);
      var blockers = new List<string>();
      blockers.AddRange(_validation.Messages.Where(message => message.Severity == ValidationSeverity.Error).Select(message => message.Message));
      blockers.AddRange(_snapshot.Messages);
      if (blockers.Count > 0)
      {
        _operationMessages = blockers.Distinct().ToArray();
        ExpireSolution(true);
        return;
      }

      _isCommitting = true;
      _operationMessages = new[] { "正在向 Revit 写入，并执行回读验证……" };
      ExpireDisplay();
      bool queued = Stage01RevitService.EnqueueCommit(_model, result =>
      {
        _lastCommit = result;
        _operationMessages = result.Messages;
        _isCommitting = false;
        Rhino.RhinoApp.InvokeOnUiThread((Action) (() => ExpireSolution(true)));
      }, out string error);
      if (!queued)
      {
        _isCommitting = false;
        _operationMessages = new[] { error };
        ExpireSolution(true);
      }
    }

    internal void ResetForm()
    {
      _model = _registry.CreateDefaultModel();
      _snapshot = new RevitDocumentSnapshot();
      _validation = new ValidationResult(Array.Empty<ValidationMessage>());
      _operationMessages = new[] { "表单已恢复默认值，尚未写入 Revit。" };
      _lastCommit = null;
      _allowAutomaticStoredPayloadLoad = false;
      _loadedStoredPayloadIdentity = string.Empty;
      ExpireSolution(true);
    }

    public override bool Write(GH_IWriter writer)
    {
      var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
      writer.SetString(PayloadKeyV05, CanonicalPayload.Build(_model));
      writer.SetString("HBR.Stage01.Values", serializer.Serialize(_model.Values));
      writer.SetString("HBR.Stage01.Conditions", serializer.Serialize(_model.Conditions));
      writer.SetString("HBR.Stage01.Organizations", serializer.Serialize(_model.Organizations));
      writer.SetBoolean("HBR.Stage01.ConfirmBlank", _model.ConfirmBlankProject);
      writer.SetBoolean("HBR.Stage01.AllowReinitialize", _model.AllowReinitialize);
      writer.SetBoolean("HBR.Stage01.ShowAll", _model.ShowAllFields);
      writer.SetString("HBR.Stage01.ActiveGroup", _model.ActiveGroup ?? string.Empty);
      writer.SetInt32("HBR.Stage01.ScrollOffset", _model.ScrollOffset);
      writer.SetInt32("HBR.Stage01.OrganizationIndex", _model.OrganizationIndex);
      return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
      bool result = base.Read(reader);
      var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
      try
      {
        bool loadedPayload = reader.ItemExists(PayloadKeyV05)
          && Stage01PayloadCodec.TryApply(reader.GetString(PayloadKeyV05), _model, out string payloadError);
        if (!loadedPayload)
        {
          if (reader.ItemExists(PayloadKeyV05) && !string.IsNullOrWhiteSpace(payloadError))
            _operationMessages = new[] { payloadError };
          ReadLegacyForm(reader, serializer);
        }

        if (reader.ItemExists("HBR.Stage01.ConfirmBlank")) _model.ConfirmBlankProject = reader.GetBoolean("HBR.Stage01.ConfirmBlank");
        if (reader.ItemExists("HBR.Stage01.AllowReinitialize")) _model.AllowReinitialize = reader.GetBoolean("HBR.Stage01.AllowReinitialize");
        if (reader.ItemExists("HBR.Stage01.ShowAll")) _model.ShowAllFields = reader.GetBoolean("HBR.Stage01.ShowAll");
        if (reader.ItemExists("HBR.Stage01.ActiveGroup")) _model.ActiveGroup = reader.GetString("HBR.Stage01.ActiveGroup");
        if (reader.ItemExists("HBR.Stage01.ScrollOffset")) _model.ScrollOffset = reader.GetInt32("HBR.Stage01.ScrollOffset");
        if (reader.ItemExists("HBR.Stage01.OrganizationIndex")) _model.OrganizationIndex = reader.GetInt32("HBR.Stage01.OrganizationIndex");
        _allowAutomaticStoredPayloadLoad = false;
      }
      catch (Exception exception)
      {
        _operationMessages = new[] { "读取 GH 文件中的初始化表单失败：" + exception.Message };
      }
      EnsureSystemValues();
      return result;
    }

    private void ReadLegacyForm(GH_IReader reader, JavaScriptSerializer serializer)
    {
      if (reader.ItemExists("HBR.Stage01.Values"))
      {
        Dictionary<string, string> values = serializer.Deserialize<Dictionary<string, string>>(reader.GetString("HBR.Stage01.Values"));
        _model.Values.Clear();
        foreach (KeyValuePair<string, string> pair in values ?? new Dictionary<string, string>())
          _model.Values[pair.Key] = pair.Value;
      }
      if (reader.ItemExists("HBR.Stage01.Conditions"))
      {
        Dictionary<string, bool> conditions = serializer.Deserialize<Dictionary<string, bool>>(reader.GetString("HBR.Stage01.Conditions"));
        _model.Conditions.Clear();
        foreach (KeyValuePair<string, bool> pair in conditions ?? new Dictionary<string, bool>())
          _model.Conditions[pair.Key] = pair.Value;
      }
      if (reader.ItemExists("HBR.Stage01.Organizations"))
      {
        List<Dictionary<string, string>> organizations = serializer.Deserialize<List<Dictionary<string, string>>>(reader.GetString("HBR.Stage01.Organizations"));
        _model.Organizations.Clear();
        foreach (Dictionary<string, string> organization in organizations ?? new List<Dictionary<string, string>>())
          _model.Organizations.Add(new Dictionary<string, string>(organization, StringComparer.Ordinal));
        if (_model.Organizations.Count == 0)
          _model.Organizations.Add(new Dictionary<string, string>(StringComparer.Ordinal));
      }
      foreach (PlanningTargetDefinition definition in PlanningTargetCatalog.All)
      {
        string legacy = _model.GetValue(definition.MvdFieldKey);
        if (string.IsNullOrWhiteSpace(legacy)) continue;
        if (PlanningTargetValue.TryParseMvdText(
          definition.MetricCode,
          legacy,
          definition.Unit,
          "兼容旧版 GH 文件",
          out PlanningTargetValue target,
          out _))
          _model.SetPlanningTarget(target);
      }
    }

    private void TryAutomaticallyLoadStoredPayload()
    {
      if (!_allowAutomaticStoredPayloadLoad || !_snapshot.IsInitialized || string.IsNullOrWhiteSpace(_snapshot.StoredPayloadJson)) return;
      string identity = (_snapshot.DocumentPath ?? string.Empty) + "|" + _snapshot.StoredPayloadHash;
      if (string.Equals(identity, _loadedStoredPayloadIdentity, StringComparison.Ordinal)) return;
      if (Stage01PayloadCodec.TryApply(_snapshot.StoredPayloadJson, _model, out string error))
      {
        _loadedStoredPayloadIdentity = identity;
        _operationMessages = new[] { "已从当前 Revit 文件读取既有初始化记录。" };
        EnsureSystemValues();
        _snapshot = Stage01RevitService.ReadSnapshot(_model);
      }
      else
      {
        _operationMessages = new[] { error };
      }
      _allowAutomaticStoredPayloadLoad = false;
    }

    private void EnsureSystemValues()
    {
      if (string.IsNullOrWhiteSpace(_model.GetValue(Stage01Keys.FileGuid)))
        _model.SetValue(Stage01Keys.FileGuid, Guid.NewGuid().ToString("D"));
      _model.SetValue(Stage01Keys.WorkflowVersion, HBRContextVersions.FileContextSchema);
      _model.SetValue(Stage01Keys.LengthUnit, "m");
      _model.SetValue(Stage01Keys.AreaUnit, "m²");
      _model.SetValue(Stage01Keys.AngleUnit, "°");
      _model.SetValue(Stage01Keys.InitializationStatus, ResolveStatus());
    }

    private bool IsInitializationPassed()
    {
      return _snapshot != null
        && _snapshot.IsInitialized
        && _snapshot.PayloadMatches
        && (_snapshot.Messages == null || _snapshot.Messages.Count == 0)
        && _validation != null
        && _validation.IsValid
        && !_isCommitting;
    }

    private string ResolveStatus()
    {
      if (_isCommitting) return "提交中";
      if (_lastCommit != null && !_lastCommit.Success) return _lastCommit.Status;
      if (_snapshot != null && _snapshot.IsInitialized)
        return _snapshot.PayloadMatches ? "初始化通过" : "已修改待重新提交";
      if (_snapshot != null && _snapshot.HostAvailable && _snapshot.Messages.Count > 0)
        return "环境检查未通过";
      if (_validation != null && !_validation.IsValid)
        return "输入未完成";
      return "待提交";
    }

    private IReadOnlyList<string> ResolveMessages()
    {
      var messages = new List<string>();
      if (_snapshot?.Messages != null) messages.AddRange(_snapshot.Messages);
      if (_snapshot?.BlockingElements != null && !_snapshot.IsBlank && !_snapshot.IsInitialized)
        messages.AddRange(_snapshot.BlockingElements.Take(8).Select(value => "阻断对象：" + value));
      if (_validation?.Messages != null)
        messages.AddRange(_validation.Messages.Select(message => message.Message));
      if (_operationMessages != null) messages.AddRange(_operationMessages);
      if (messages.Count == 0) messages.Add("环境、规划目标与输入未发现阻断问题。可执行写入并回读。");
      return messages.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
    }

    private void NotifyModelEdited()
    {
      _allowAutomaticStoredPayloadLoad = false;
      _lastCommit = null;
      _operationMessages = Array.Empty<string>();
      ExpireSolution(true);
    }

    private void ExpireDisplay()
    {
      Attributes?.ExpireLayout();
      Rhino.RhinoApp.InvokeOnUiThread((Action) (() => Grasshopper.Instances.ActiveCanvas?.Invalidate()));
    }
  }

  internal static class ListExtensions
  {
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
      for (int index = 0; index < values.Count; ++index)
        if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
      return -1;
    }
  }
}
