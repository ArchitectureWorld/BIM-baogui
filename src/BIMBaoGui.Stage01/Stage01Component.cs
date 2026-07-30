using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Core;
using BIMBaoGui.Stage01.Infrastructure;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.UI;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace BIMBaoGui.Stage01
{
  public sealed class Stage01Component : GH_Component
  {
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
        "在 Rhino.Inside.Revit 中填写、校验并写入 Revit 2020 单文件初始化数据。",
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
      pManager.AddBooleanParameter("初始化通过", "OK", "写入与回读均通过时为 True。", GH_ParamAccess.item);
      pManager.AddTextParameter("状态", "S", "当前文件初始化状态。", GH_ParamAccess.item);
      pManager.AddTextParameter("文件上下文", "C", "当前文件初始化载荷 JSON。", GH_ParamAccess.item);
      pManager.AddTextParameter("消息", "M", "环境检查、字段校验、写入和回读消息。", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
      EnsureSystemValues();
      _snapshot = Stage01RevitService.ReadSnapshot(_model);
      TryAutomaticallyLoadStoredPayload();
      _validation = Stage01Validator.Validate(_model, _registry.Fields);

      bool initialized = _snapshot.IsInitialized && _snapshot.PayloadMatches && !_isCommitting;
      string payload = CanonicalPayload.Build(_model);
      dataAccess.SetData(0, initialized);
      dataAccess.SetData(1, ResolveStatus());
      dataAccess.SetData(2, payload);
      dataAccess.SetDataList(3, ResolveMessages());
    }

    internal IReadOnlyList<string> GetVisibleGroups()
    {
      var groups = new List<string>();
      if (_model.ShowAllFields)
      {
        groups.AddRange(_registry.Groups.Where(group => group != "00_当前Revit文件" && group != "09_提交与回读"));
      }
      else
      {
        groups.Add("01_文件与项目身份");
        groups.Add("02_坐标与高程");
        groups.Add("06_参建组织");
      }
      groups.Add("10_项目条件");
      groups.Add("11_提交与校验");
      return groups.Distinct(StringComparer.Ordinal).ToList();
    }

    internal string GetGroupDisplayName(string group)
    {
      if (string.IsNullOrWhiteSpace(group)) return "文件初始化";
      int separator = group.IndexOf('_');
      return separator >= 0 && separator + 1 < group.Length ? group.Substring(separator + 1) : group;
    }

    internal IReadOnlyList<FieldDefinition> GetFieldsForActiveGroup()
    {
      string group = _model.ActiveGroup;
      if (group == "10_项目条件" || group == "11_提交与校验")
        return Array.Empty<FieldDefinition>();

      var fields = new List<FieldDefinition>();
      fields.AddRange(_registry.FieldsForGroup(group, _model.ShowAllFields));
      if (!_model.ShowAllFields && group == "01_文件与项目身份")
        fields.AddRange(_registry.FieldsForGroup("01_文件与阶段", false));
      return fields
        .Where(field => !field.Deferred || _model.ShowAllFields)
        .GroupBy(field => field.Key, StringComparer.Ordinal)
        .Select(grouping => grouping.First())
        .ToList();
    }

    internal string GetFieldValue(FieldDefinition definition)
    {
      return definition.Entity == "IfcOrganization"
        ? _model.GetOrganizationValue(definition.Key)
        : _model.GetValue(definition.Key);
    }

    internal void SetFieldValue(FieldDefinition definition, string value)
    {
      if (definition == null || definition.ReadOnly) return;
      if (definition.Entity == "IfcOrganization")
        _model.SetOrganizationValue(definition.Key, value);
      else
        _model.SetValue(definition.Key, value);
      NotifyModelEdited();
    }

    internal void ToggleBooleanField(FieldDefinition definition)
    {
      if (definition == null || definition.ReadOnly) return;
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

    internal void SetScrollOffset(int offset, int pageSize)
    {
      int count = GetFieldsForActiveGroup().Count;
      int maximum = Math.Max(0, count - Math.Max(1, pageSize));
      _model.ScrollOffset = Math.Max(0, Math.Min(offset, maximum));
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
      if (!groups.Contains(_model.ActiveGroup)) _model.ActiveGroup = groups.FirstOrDefault() ?? "01_文件与项目身份";
      ExpireDisplay();
    }

    internal void AddOrganization()
    {
      _model.Organizations.Add(new Dictionary<string, string>(StringComparer.Ordinal));
      _model.OrganizationIndex = _model.Organizations.Count - 1;
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
      NotifyModelEdited();
    }

    internal void MoveOrganization(int delta)
    {
      if (_model.Organizations.Count == 0) return;
      _model.OrganizationIndex = (_model.OrganizationIndex + delta + _model.Organizations.Count) % _model.Organizations.Count;
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
      _operationMessages = new[] { "已重新执行文件环境与字段校验。" };
      ExpireSolution(true);
    }

    internal void CommitInitialization()
    {
      if (_isCommitting) return;
      EnsureSystemValues();
      _validation = Stage01Validator.Validate(_model, _registry.Fields);
      _snapshot = Stage01RevitService.ReadSnapshot(_model);
      var blockers = new List<string>();
      blockers.AddRange(_validation.Messages.Where(x => x.Severity == ValidationSeverity.Error).Select(x => x.Message));
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
        if (reader.ItemExists("HBR.Stage01.Values"))
        {
          Dictionary<string, string> values = serializer.Deserialize<Dictionary<string, string>>(reader.GetString("HBR.Stage01.Values"));
          _model.Values.Clear();
          foreach (KeyValuePair<string, string> pair in values ?? new Dictionary<string, string>()) _model.Values[pair.Key] = pair.Value;
        }
        if (reader.ItemExists("HBR.Stage01.Conditions"))
        {
          Dictionary<string, bool> conditions = serializer.Deserialize<Dictionary<string, bool>>(reader.GetString("HBR.Stage01.Conditions"));
          _model.Conditions.Clear();
          foreach (KeyValuePair<string, bool> pair in conditions ?? new Dictionary<string, bool>()) _model.Conditions[pair.Key] = pair.Value;
        }
        if (reader.ItemExists("HBR.Stage01.Organizations"))
        {
          List<Dictionary<string, string>> organizations = serializer.Deserialize<List<Dictionary<string, string>>>(reader.GetString("HBR.Stage01.Organizations"));
          _model.Organizations.Clear();
          foreach (Dictionary<string, string> organization in organizations ?? new List<Dictionary<string, string>>())
            _model.Organizations.Add(new Dictionary<string, string>(organization, StringComparer.Ordinal));
          if (_model.Organizations.Count == 0) _model.Organizations.Add(new Dictionary<string, string>(StringComparer.Ordinal));
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
      if (string.IsNullOrWhiteSpace(_model.GetValue(Stage01Keys.WorkflowVersion)))
        _model.SetValue(Stage01Keys.WorkflowVersion, "0.2.0");
      _model.SetValue(Stage01Keys.LengthUnit, "m");
      _model.SetValue(Stage01Keys.AreaUnit, "m²");
      _model.SetValue(Stage01Keys.AngleUnit, "°");
      _model.SetValue(Stage01Keys.InitializationStatus, ResolveStatus());
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
        messages.AddRange(_snapshot.BlockingElements.Take(8).Select(x => "阻断对象：" + x));
      if (_validation?.Messages != null)
        messages.AddRange(_validation.Messages.Select(x => x.Message));
      if (_operationMessages != null) messages.AddRange(_operationMessages);
      if (messages.Count == 0) messages.Add("环境与输入未发现阻断问题。可执行写入并回读。" );
      return messages.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
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
      for (int i = 0; i < values.Count; ++i)
        if (EqualityComparer<T>.Default.Equals(values[i], value)) return i;
      return -1;
    }
  }
}
