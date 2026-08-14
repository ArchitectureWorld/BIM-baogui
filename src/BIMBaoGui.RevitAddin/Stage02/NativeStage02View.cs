using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal sealed class NativeStage02View : UserControl
  {
    private readonly RadioButton _fullModel;
    private readonly RadioButton _currentSelection;
    private readonly RadioButton _automatic;
    private readonly RadioButton _manual;
    private readonly ComboBox _manualRole;
    private readonly TextBlock _manualHint;
    private readonly Button _previewButton;
    private readonly Button _writeButton;
    private readonly CheckBox _problemOnly;
    private readonly TextBlock _summaryText;
    private readonly TextBlock _statusText;
    private readonly ListBox _elementList;
    private readonly StackPanel _fieldPanel;
    private readonly Dictionary<string, string> _roleOverrides =
      new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly IReadOnlyList<ManualRoleChoice> _manualRoleChoices;
    private NativeStage02Preview _preview;
    private NativeStage02PreviewRequest _resolvedRequest;
    private bool _busy;
    private bool _previewStale;

    internal NativeStage02View()
    {
      Background = Brushes.White;
      _manualRoleChoices = LoadManualRoleChoices();

      var root = new Grid();
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition
      {
        Height = new GridLength(1, GridUnitType.Star)
      });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(96) });

      var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
      heading.Children.Add(new TextBlock
      {
        Text = "02 构件与属性准备",
        FontSize = 20,
        FontWeight = FontWeights.SemiBold
      });
      _summaryText = new TextBlock
      {
        Text = "先生成只读预览，再确认写入。当前 Revit 选择支持自动识别或手动指定报规语义类型。",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 5, 0, 0)
      };
      heading.Children.Add(_summaryText);
      Grid.SetRow(heading, 0);
      root.Children.Add(heading);

      var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
      _fullModel = new RadioButton
      {
        Content = "全模型",
        IsChecked = true,
        Margin = new Thickness(0, 8, 14, 0),
        VerticalAlignment = VerticalAlignment.Center
      };
      _currentSelection = new RadioButton
      {
        Content = "当前 Revit 选择",
        Margin = new Thickness(0, 8, 14, 0),
        VerticalAlignment = VerticalAlignment.Center
      };
      _previewButton = ActionButton("生成预览");
      _writeButton = ActionButton("确认写入");
      _writeButton.IsEnabled = false;
      _problemOnly = new CheckBox
      {
        Content = "仅显示问题",
        Margin = new Thickness(8, 8, 0, 0),
        VerticalAlignment = VerticalAlignment.Center
      };
      _previewButton.Click += (_, __) => RequestPreview();
      _writeButton.Click += (_, __) => RequestWrite();
      _problemOnly.Checked += (_, __) => RenderElements();
      _problemOnly.Unchecked += (_, __) => RenderElements();
      _fullModel.Checked += (_, __) => ScopeChanged();
      _currentSelection.Checked += (_, __) => ScopeChanged();
      actions.Children.Add(_fullModel);
      actions.Children.Add(_currentSelection);
      actions.Children.Add(_previewButton);
      actions.Children.Add(_writeButton);
      actions.Children.Add(_problemOnly);
      Grid.SetRow(actions, 1);
      root.Children.Add(actions);

      var semantics = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
      semantics.Children.Add(new TextBlock
      {
        Text = "识别方式：",
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 8, 0),
        VerticalAlignment = VerticalAlignment.Center
      });
      _automatic = new RadioButton
      {
        Content = "自动识别",
        IsChecked = true,
        Margin = new Thickness(0, 8, 12, 0),
        VerticalAlignment = VerticalAlignment.Center
      };
      _manual = new RadioButton
      {
        Content = "手动指定",
        Margin = new Thickness(0, 8, 14, 0),
        VerticalAlignment = VerticalAlignment.Center
      };
      semantics.Children.Add(_automatic);
      semantics.Children.Add(_manual);
      semantics.Children.Add(new TextBlock
      {
        Text = "批量语义类型：",
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 8, 0),
        VerticalAlignment = VerticalAlignment.Center
      });
      _manualRole = new ComboBox
      {
        MinWidth = 190,
        Margin = new Thickness(0, 3, 10, 0),
        ItemsSource = _manualRoleChoices
      };
      if (_manualRoleChoices.Count > 0) _manualRole.SelectedIndex = 0;
      semantics.Children.Add(_manualRole);
      _manualHint = new TextBlock
      {
        Text = "仅“当前 Revit 选择”可用；角色由 HBR 规则包动态提供。",
        Foreground = Brushes.DimGray,
        Margin = new Thickness(0, 8, 0, 0),
        VerticalAlignment = VerticalAlignment.Center
      };
      semantics.Children.Add(_manualHint);
      _automatic.Checked += (_, __) => IdentificationChanged();
      _manual.Checked += (_, __) => IdentificationChanged();
      _manualRole.SelectionChanged += (_, __) => SemanticInputChanged(
        "批量语义类型已变化，请重新生成预览。" );
      Grid.SetRow(semantics, 2);
      root.Children.Add(semantics);

      var workspace = new Grid();
      workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
      workspace.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(1, GridUnitType.Star)
      });
      _elementList = new ListBox
      {
        Margin = new Thickness(0, 0, 14, 0),
        HorizontalContentAlignment = HorizontalAlignment.Stretch
      };
      _elementList.SelectionChanged += (_, __) => RenderSelectedElement();
      Grid.SetColumn(_elementList, 0);
      workspace.Children.Add(_elementList);

      _fieldPanel = new StackPanel { Margin = new Thickness(4, 0, 14, 16) };
      var fieldScroll = new ScrollViewer
      {
        Content = _fieldPanel,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
      };
      Grid.SetColumn(fieldScroll, 1);
      workspace.Children.Add(fieldScroll);
      Grid.SetRow(workspace, 3);
      root.Children.Add(workspace);

      _statusText = new TextBlock
      {
        Text = "状态：等待生成预览",
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(10)
      };
      var statusScroll = new ScrollViewer
      {
        Content = _statusText,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Margin = new Thickness(0, 8, 0, 0),
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250))
      };
      Grid.SetRow(statusScroll, 4);
      root.Children.Add(statusScroll);
      Content = root;
      UpdateSemanticControls();
      RenderElements();
    }

    internal event Action<string> StatusChanged;

    private void ScopeChanged()
    {
      if (_fullModel == null || _automatic == null) return;
      if (_fullModel.IsChecked == true)
      {
        _automatic.IsChecked = true;
        _roleOverrides.Clear();
      }
      UpdateSemanticControls();
      SemanticInputChanged("作用范围已变化，请重新生成预览。" );
    }

    private void IdentificationChanged()
    {
      if (_automatic == null || _manualRole == null) return;
      UpdateSemanticControls();
      SemanticInputChanged("识别方式已变化，请重新生成预览。" );
    }

    private void UpdateSemanticControls()
    {
      if (_automatic == null || _manual == null || _manualRole == null) return;
      bool currentSelection = _currentSelection?.IsChecked == true;
      _automatic.IsEnabled = currentSelection && !_busy;
      _manual.IsEnabled = currentSelection && !_busy;
      _manualRole.IsEnabled = currentSelection
        && _manual.IsChecked == true
        && !_busy
        && _manualRoleChoices.Count > 0;
      _manualHint.Text = currentSelection
        ? _manualRoleChoices.Count == 0
          ? "当前 embedded HBR 规则包没有可用的手动语义角色。"
          : "手动角色来自 embedded HBR 规则目录；具体载体和 Stage01 条件在预览时严格校验。"
        : "全模型保持自动识别；手动指定仅对当前 Revit 选择开放。";
    }

    private void SemanticInputChanged(string message)
    {
      if (_busy || _preview == null) return;
      _previewStale = true;
      _resolvedRequest = null;
      _writeButton.IsEnabled = false;
      SetStatus(message);
      RenderElements();
    }

    private void RequestPreview()
    {
      if (_busy) return;
      bool currentSelection = _currentSelection.IsChecked == true;
      bool manual = currentSelection && _manual.IsChecked == true;
      ManualRoleChoice bulkRole = _manualRole.SelectedItem as ManualRoleChoice;
      if (manual && bulkRole == null)
      {
        SetStatus("手动指定模式需要选择一个批量语义类型。" );
        return;
      }
      _preview = null;
      _resolvedRequest = null;
      _previewStale = false;
      _writeButton.IsEnabled = false;
      var request = new NativeStage02PreviewRequest
      {
        ScopeMode = currentSelection
          ? NativeStage02ScopeMode.CustomSelection
          : NativeStage02ScopeMode.FullModel,
        IdentificationMode = manual
          ? NativeStage02IdentificationMode.Manual
          : NativeStage02IdentificationMode.Automatic,
        CustomUniqueIds = Array.Empty<string>(),
        BulkRoleId = manual ? bulkRole.RoleId : string.Empty,
        RoleOverrides = manual
          ? _roleOverrides
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new NativeStage02RoleOverride
            {
              ElementUniqueId = pair.Key,
              RoleId = pair.Value
            })
            .ToArray()
          : Array.Empty<NativeStage02RoleOverride>()
      };
      SetBusy(true, "正在通过 Revit ExternalEvent 读取构件、语义角色与参数证据……" );
      try
      {
        RevitExternalEventDispatcher.RequestStage02Preview(
          request,
          ApplyPreviewResult,
          ApplyFailure);
      }
      catch (Exception exception) { ApplyFailure(exception); }
    }

    private void RequestWrite()
    {
      if (_busy || _preview == null || _resolvedRequest == null || _previewStale)
        return;
      SetBusy(true, "正在回读预览、准备参数并按构件原子写入角色与属性……" );
      try
      {
        RevitExternalEventDispatcher.RequestStage02Write(
          new NativeStage02WriteRequest
          {
            Preview = _preview,
            ResolvedRequest = _resolvedRequest.Clone()
          },
          ApplyWriteResult,
          ApplyFailure);
      }
      catch (Exception exception) { ApplyFailure(exception); }
    }

    private void ApplyPreviewResult(NativeStage02RevitPreviewResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(
          new Action<NativeStage02RevitPreviewResult>(ApplyPreviewResult),
          result);
        return;
      }
      SetBusy(false, string.Empty);
      if (result == null || !result.Success || result.Preview == null)
      {
        _preview = null;
        _resolvedRequest = null;
        _previewStale = false;
        _writeButton.IsEnabled = false;
        RenderElements();
        SetStatus(result == null
          ? "预览失败：未返回结果。"
          : result.Status + "｜" + string.Join(" ", result.Messages));
        return;
      }
      _preview = result.Preview;
      _resolvedRequest = result.ResolvedRequest;
      _previewStale = false;
      _writeButton.IsEnabled = true;
      RenderElements();
      SetStatus(result.Status + "｜" + string.Join(" ", result.Messages));
    }

    private void ApplyWriteResult(NativeStage02WriteResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action<NativeStage02WriteResult>(ApplyWriteResult), result);
        return;
      }
      SetBusy(false, string.Empty);
      if (result == null)
      {
        SetStatus("写入失败：未返回结果。" );
        return;
      }
      if (result.RefreshedPreview != null)
      {
        _preview = result.RefreshedPreview;
        _resolvedRequest = result.ResolvedRequest;
        _previewStale = false;
      }
      if (result.RequiresNewPreview && result.RefreshedPreview == null)
      {
        _preview = null;
        _resolvedRequest = null;
        _previewStale = false;
      }
      _writeButton.IsEnabled = _preview != null && !_busy && !_previewStale;
      RenderElements();
      SetStatus(result.Status + "｜" + string.Join(" ", result.Messages));
    }

    private void ApplyFailure(Exception exception)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action<Exception>(ApplyFailure), exception);
        return;
      }
      SetBusy(false, string.Empty);
      SetStatus("Revit 请求失败：" + (exception == null ? "未知错误" : exception.Message));
    }

    private void RenderElements()
    {
      NativeStage02ElementPlan selected = (_elementList.SelectedItem as ElementListItem)?.Plan;
      _elementList.Items.Clear();
      if (_preview == null)
      {
        _summaryText.Text = "尚未生成 Stage02 预览。";
        RenderSelectedElement();
        return;
      }
      IEnumerable<NativeStage02ElementPlan> elements = _preview.Elements;
      if (_problemOnly.IsChecked == true)
      {
        elements = elements.Where(value => value.IsBlocked
          || value.Fields.Any(field => field.Status != NativeStage02FieldStatus.Correct
            && field.Status != NativeStage02FieldStatus.NotApplicable));
      }
      foreach (NativeStage02ElementPlan element in elements)
        _elementList.Items.Add(new ElementListItem(element));
      if (selected != null)
      {
        ElementListItem match = _elementList.Items.Cast<ElementListItem>()
          .FirstOrDefault(value => value.Plan.Element.UniqueId == selected.Element.UniqueId);
        if (match != null) _elementList.SelectedItem = match;
      }
      if (_elementList.SelectedItem == null && _elementList.Items.Count > 0)
        _elementList.SelectedIndex = 0;

      _summaryText.Text = (_previewStale ? "【预览已失效，需重新生成】｜" : string.Empty)
        + "构件=" + _preview.Elements.Count
        + "｜阻断=" + _preview.BlockedElementCount
        + "｜可执行=" + _preview.ActionableElementCount
        + "｜待绑定=" + _preview.PendingBindingFieldCount
        + "｜待写入=" + _preview.PendingWriteFieldCount
        + "｜待填写=" + _preview.PendingInputFieldCount
        + "｜SHA-256=" + _preview.PreviewHash;
      RenderSelectedElement();
    }

    private void RenderSelectedElement()
    {
      _fieldPanel.Children.Clear();
      ElementListItem selected = _elementList.SelectedItem as ElementListItem;
      if (selected == null)
      {
        _fieldPanel.Children.Add(new TextBlock
        {
          Text = _preview == null ? "生成预览后在左侧选择构件。" : "当前筛选没有可显示构件。",
          Margin = new Thickness(8)
        });
        return;
      }
      NativeStage02ElementPlan plan = selected.Plan;
      _fieldPanel.Children.Add(new TextBlock
      {
        Text = plan.Element.ElementName + "｜ElementId=" + plan.Element.ElementId
          + "｜Revit 类别=" + plan.Element.Category
          + "｜ElementKind=" + plan.Element.ElementKind,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4)
      });
      _fieldPanel.Children.Add(new TextBlock
      {
        Text = "最终角色=" + (string.IsNullOrWhiteSpace(plan.RoleId) ? "未识别" : plan.RoleId)
          + "｜来源=" + plan.RoleMatchSource
          + (string.IsNullOrWhiteSpace(plan.Message) ? string.Empty : "｜" + plan.Message),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
      });
      if (_currentSelection.IsChecked == true && _manual.IsChecked == true)
        _fieldPanel.Children.Add(ElementOverrideEditor(plan));
      foreach (NativeStage02FieldPlan field in plan.Fields)
        _fieldPanel.Children.Add(FieldCard(field));
      if (plan.Fields.Count == 0)
      {
        _fieldPanel.Children.Add(new TextBlock
        {
          Text = plan.IsBlocked ? "该构件未通过当前识别策略，不会写入。" : "该构件没有 Stage02 字段。",
          Foreground = plan.IsBlocked ? Brushes.DarkRed : Brushes.DimGray,
          TextWrapping = TextWrapping.Wrap
        });
      }
    }

    private FrameworkElement ElementOverrideEditor(NativeStage02ElementPlan plan)
    {
      var panel = new StackPanel();
      panel.Children.Add(new TextBlock
      {
        Text = "逐项语义类型",
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 4)
      });
      var choices = new List<ElementRoleChoice>
      {
        new ElementRoleChoice(string.Empty, "继承批量选择"),
        new ElementRoleChoice(NativeStage02RoleAssignmentPolicy.AutoOverrideRoleId, "恢复自动识别")
      };
      choices.AddRange(_manualRoleChoices.Select(value =>
        new ElementRoleChoice(value.RoleId, value.DisplayName)));
      var combo = new ComboBox
      {
        MinWidth = 220,
        ItemsSource = choices
      };
      string current;
      _roleOverrides.TryGetValue(plan.Element.UniqueId, out current);
      combo.SelectedItem = choices.First(value => string.Equals(
        value.RoleId,
        current ?? string.Empty,
        StringComparison.Ordinal));
      combo.SelectionChanged += (_, __) =>
      {
        ElementRoleChoice choice = combo.SelectedItem as ElementRoleChoice;
        if (choice == null || string.IsNullOrEmpty(choice.RoleId))
          _roleOverrides.Remove(plan.Element.UniqueId);
        else
          _roleOverrides[plan.Element.UniqueId] = choice.RoleId;
        SemanticInputChanged("构件 " + plan.Element.ElementId + " 的逐项语义类型已变化，请重新生成预览。" );
      };
      panel.Children.Add(combo);
      panel.Children.Add(new TextBlock
      {
        Text = "继承批量选择 / 指定其他合法角色 / 恢复自动识别。变更后必须重新生成预览。",
        FontSize = 11,
        Foreground = Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0)
      });
      return new Border
      {
        Child = panel,
        BorderBrush = new SolidColorBrush(Color.FromRgb(205, 214, 225)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10),
        Margin = new Thickness(0, 0, 0, 10),
        Background = new SolidColorBrush(Color.FromRgb(249, 251, 253))
      };
    }

    private static FrameworkElement FieldCard(NativeStage02FieldPlan field)
    {
      var panel = new StackPanel();
      panel.Children.Add(new TextBlock
      {
        Text = field.Property.IfcProperty + " · " + StatusText(field.Status),
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap
      });
      panel.Children.Add(new TextBlock
      {
        Text = field.Property.IfcEntity + " / " + field.Property.IfcPropertySet
          + " / " + field.Property.IfcProperty + "｜" + field.Property.ParameterGuid.ToString("D"),
        FontSize = 11,
        Foreground = Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0)
      });
      panel.Children.Add(new TextBlock
      {
        Text = "参数动作=" + field.BindingAction + "｜值动作=" + field.ValueAction
          + "｜当前=" + Empty(field.CurrentCanonicalValue)
          + "｜建议=" + Empty(field.ProposedCanonicalValue),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0)
      });
      if (!string.IsNullOrWhiteSpace(field.Message))
      {
        panel.Children.Add(new TextBlock
        {
          Text = field.Message,
          Foreground = field.Status == NativeStage02FieldStatus.Blocked
            || field.Status == NativeStage02FieldStatus.RuntimeBlocked
              ? Brushes.DarkRed : Brushes.DarkGoldenrod,
          FontSize = 11,
          TextWrapping = TextWrapping.Wrap,
          Margin = new Thickness(0, 3, 0, 0)
        });
      }
      return new Border
      {
        Child = panel,
        BorderBrush = new SolidColorBrush(Color.FromRgb(224, 227, 232)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10),
        Margin = new Thickness(0, 0, 0, 8),
        Background = Brushes.White
      };
    }

    private void SetBusy(bool busy, string status)
    {
      _busy = busy;
      _previewButton.IsEnabled = !busy;
      _writeButton.IsEnabled = !busy && _preview != null && _resolvedRequest != null && !_previewStale;
      _fullModel.IsEnabled = !busy;
      _currentSelection.IsEnabled = !busy;
      _problemOnly.IsEnabled = !busy;
      UpdateSemanticControls();
      if (!string.IsNullOrWhiteSpace(status)) SetStatus(status);
    }

    private void SetStatus(string status)
    {
      _statusText.Text = "状态：" + (status ?? string.Empty);
      StatusChanged?.Invoke(status ?? string.Empty);
    }

    private static IReadOnlyList<ManualRoleChoice> LoadManualRoleChoices()
    {
      try
      {
        return NativeStage02ManualRoleCatalog.Current.Roles
          .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
          .ThenBy(value => value.RoleId, StringComparer.Ordinal)
          .Select(value => new ManualRoleChoice(value.RoleId, value.DisplayName))
          .ToArray();
      }
      catch
      {
        return Array.Empty<ManualRoleChoice>();
      }
    }

    private static Button ActionButton(string text)
    {
      return new Button
      {
        Content = text,
        Padding = new Thickness(14, 7, 14, 7),
        Margin = new Thickness(0, 0, 8, 0),
        MinWidth = 105
      };
    }

    private static string StatusText(NativeStage02FieldStatus status)
    {
      switch (status)
      {
        case NativeStage02FieldStatus.Correct: return "正确";
        case NativeStage02FieldStatus.PendingBinding: return "待绑定";
        case NativeStage02FieldStatus.PendingWrite: return "待写入";
        case NativeStage02FieldStatus.PendingInput: return "待填写";
        case NativeStage02FieldStatus.NotApplicable: return "不适用";
        case NativeStage02FieldStatus.RuntimeBlocked: return "运行能力阻断";
        default: return "阻断";
      }
    }

    private static string Empty(string value)
    {
      return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private sealed class ManualRoleChoice
    {
      internal ManualRoleChoice(string roleId, string displayName)
      {
        RoleId = roleId ?? string.Empty;
        DisplayName = displayName ?? roleId ?? string.Empty;
      }
      internal string RoleId { get; }
      internal string DisplayName { get; }
      public override string ToString() => DisplayName;
    }

    private sealed class ElementRoleChoice
    {
      internal ElementRoleChoice(string roleId, string label)
      {
        RoleId = roleId ?? string.Empty;
        Label = label ?? string.Empty;
      }
      internal string RoleId { get; }
      internal string Label { get; }
      public override string ToString() => Label;
    }

    private sealed class ElementListItem
    {
      internal ElementListItem(NativeStage02ElementPlan plan) { Plan = plan; }
      internal NativeStage02ElementPlan Plan { get; }
      public override string ToString()
      {
        string name = string.IsNullOrWhiteSpace(Plan.Element.ElementName)
          ? Plan.Element.Category : Plan.Element.ElementName;
        return name + " · Id=" + Plan.Element.ElementId + " · "
          + Plan.Element.Category + " · " + Plan.Element.ElementKind + " · "
          + (Plan.IsBlocked ? "阻断" : Plan.RoleId);
      }
    }
  }
}
