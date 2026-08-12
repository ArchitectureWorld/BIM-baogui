using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal sealed class NativeStage01View : UserControl
  {
    private readonly NativeStage01ViewModel _viewModel;
    private readonly StackPanel _directoryPanel;
    private readonly StackPanel _formPanel;
    private readonly TextBlock _summaryText;
    private readonly TextBlock _statusText;
    private readonly CheckBox _allowReinitialize;
    private readonly Button _readButton;
    private readonly Button _validateButton;
    private readonly Button _writeButton;
    private readonly Dictionary<string, bool> _optionalExpansionByGroup =
      new Dictionary<string, bool>(StringComparer.Ordinal);
    private Expander _activeOptionalExpander;
    private bool _busy;

    internal NativeStage01View()
    {
      _viewModel = new NativeStage01ViewModel(NativeRuleCatalog.Current);
      Background = Brushes.White;

      var root = new Grid();
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition
      {
        Height = new GridLength(1, GridUnitType.Star)
      });
      root.RowDefinitions.Add(new RowDefinition
      {
        Height = new GridLength(96)
      });

      var heading = new StackPanel
      {
        Margin = new Thickness(0, 0, 0, 10)
      };
      heading.Children.Add(new TextBlock
      {
        Text = "01 文件初始化",
        FontSize = 20,
        FontWeight = FontWeights.SemiBold
      });
      _summaryText = new TextBlock
      {
        Text = "依据 HBR 数据库填写项目、子项、坐标、高程、真北、单位、组织和项目条件。",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 5, 0, 0)
      };
      heading.Children.Add(_summaryText);
      Grid.SetRow(heading, 0);
      root.Children.Add(heading);

      var actions = new WrapPanel
      {
        Margin = new Thickness(0, 0, 0, 12)
      };
      _readButton = ActionButton("读取当前文件");
      _validateButton = ActionButton("校验");
      _writeButton = ActionButton("写入并回读");
      _readButton.Click += (_, __) => RequestReadCurrentFile();
      _validateButton.Click += (_, __) => ValidateCurrentModel();
      _writeButton.Click += (_, __) => RequestWrite();
      actions.Children.Add(_readButton);
      actions.Children.Add(_validateButton);
      actions.Children.Add(_writeButton);
      _allowReinitialize = new CheckBox
      {
        Content = "允许重新初始化",
        Margin = new Thickness(14, 8, 0, 0),
        VerticalAlignment = VerticalAlignment.Center
      };
      actions.Children.Add(_allowReinitialize);
      Grid.SetRow(actions, 1);
      root.Children.Add(actions);

      var workspace = new Grid();
      workspace.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(220)
      });
      workspace.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(1, GridUnitType.Star)
      });
      _directoryPanel = new StackPanel
      {
        Margin = new Thickness(0, 0, 14, 0)
      };
      var directoryScroll = new ScrollViewer
      {
        Content = _directoryPanel,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
      };
      Grid.SetColumn(directoryScroll, 0);
      workspace.Children.Add(directoryScroll);

      _formPanel = new StackPanel
      {
        Margin = new Thickness(6, 0, 14, 16)
      };
      var formScroll = new ScrollViewer
      {
        Content = _formPanel,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
      };
      Grid.SetColumn(formScroll, 1);
      workspace.Children.Add(formScroll);
      Grid.SetRow(workspace, 2);
      root.Children.Add(workspace);

      _statusText = new TextBlock
      {
        Text = "状态：等待读取或填写",
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
      Grid.SetRow(statusScroll, 3);
      root.Children.Add(statusScroll);
      Content = root;

      RenderAll();
    }

    internal event Action<string> StatusChanged;

    internal void RequestReadCurrentFile()
    {
      if (_busy) return;
      SetBusy(true, "正在通过 Revit ExternalEvent 读取当前文件……");
      try
      {
        RevitExternalEventDispatcher.RequestStage01Read(
          ApplyReadResult,
          ApplyFailure);
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void ValidateCurrentModel()
    {
      NativeStage01ValidationResult validation = _viewModel.Validate();
      ExpandOptionalSectionsWithErrors();
      RenderAll();
      SetStatus(validation.IsValid
        ? "校验通过：当前表单可提交。"
        : "校验未通过：共 " + validation.Messages.Count + " 个问题。" );
    }

    private void RequestWrite()
    {
      if (_busy) return;
      NativeStage01ValidationResult validation = _viewModel.Validate();
      if (!validation.IsValid)
      {
        string firstField = validation.Messages.First().FieldKey;
        NativeStage01FieldDefinition field =
          NativeRuleCatalog.Current.Stage01FieldsByKey.TryGetValue(
            firstField,
            out NativeStage01FieldDefinition found)
            ? found
            : null;
        if (field != null) _viewModel.SetActiveGroup(field.UiGroup);
        ExpandOptionalSectionsWithErrors();
        RenderAll();
        SetStatus(
          "校验未通过：" + validation.Messages.First().Message);
        return;
      }

      SetBusy(true, "正在写入 Revit，并执行事务内与事务后回读……");
      var request = new NativeStage01WriteRequest
      {
        Model = _viewModel.Model.Clone(),
        ConfirmBlankProject = false,
        AllowReinitialize = _allowReinitialize.IsChecked == true
      };
      try
      {
        RevitExternalEventDispatcher.RequestStage01Write(
          request,
          ApplyWriteResult,
          ApplyFailure);
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void ApplyReadResult(NativeStage01ReadResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(
          new Action<NativeStage01ReadResult>(ApplyReadResult),
          result);
        return;
      }
      SetBusy(false, string.Empty);
      if (result == null)
      {
        SetStatus("读取失败：Stage01 未返回结果。" );
        return;
      }
      _viewModel.LoadModel(result.Model);
      _viewModel.Validate();
      ExpandOptionalSectionsWithErrors();
      _allowReinitialize.IsChecked = false;
      RenderAll();
      SetStatus(
        result.Status
        + (result.Messages.Count == 0
          ? string.Empty
          : "｜" + string.Join(" ", result.Messages)));
    }

    private void ApplyWriteResult(NativeStage01WriteResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(
          new Action<NativeStage01WriteResult>(ApplyWriteResult),
          result);
        return;
      }
      SetBusy(false, string.Empty);
      if (result == null)
      {
        SetStatus("写入失败：Stage01 未返回结果。" );
        return;
      }
      if (result.Success)
      {
        _viewModel.MarkSaved();
        _allowReinitialize.IsChecked = false;
      }
      ExpandOptionalSectionsWithErrors();
      RenderAll();
      string details = result.Messages.Count == 0
        ? string.Empty
        : "｜" + string.Join(" ", result.Messages);
      if (!string.IsNullOrWhiteSpace(result.FailureReportPath))
        details += "｜失败报告：" + result.FailureReportPath;
      SetStatus(result.Status + details);
    }

    private void ApplyFailure(Exception exception)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action<Exception>(ApplyFailure), exception);
        return;
      }
      SetBusy(false, string.Empty);
      SetStatus(
        "Revit 请求失败："
        + (exception == null ? "未知错误" : exception.Message));
    }

    private void RenderAll()
    {
      RenderDirectory();
      RenderForm();
      RefreshSummaryText();
    }

    private void RefreshSummaryText()
    {
      _summaryText.Text = "当前目录："
        + DisplayName(_viewModel.ActiveGroup)
        + "｜未填必填项："
        + _viewModel.GetMissingRequiredCount(_viewModel.ActiveGroup)
        + "｜"
        + (_viewModel.IsDirty ? "有未写入修改" : "已加载状态");
    }

    private void RenderDirectory()
    {
      _directoryPanel.Children.Clear();
      _directoryPanel.Children.Add(new TextBlock
      {
        Text = "文件初始化目录",
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(6, 4, 6, 8)
      });
      foreach (string group in _viewModel.Groups)
      {
        int missing = _viewModel.GetMissingRequiredCount(group);
        var button = new Button
        {
          Content = DisplayName(group)
            + (missing > 0 ? "  ·  " + missing : string.Empty),
          Tag = group,
          Margin = new Thickness(0, 3, 0, 0),
          Padding = new Thickness(10, 8, 10, 8),
          HorizontalContentAlignment = HorizontalAlignment.Left,
          FontWeight = string.Equals(
            group,
            _viewModel.ActiveGroup,
            StringComparison.Ordinal)
              ? FontWeights.SemiBold
              : FontWeights.Normal
        };
        button.Click += (_, __) =>
        {
          _viewModel.SetActiveGroup((string)button.Tag);
          RenderAll();
        };
        _directoryPanel.Children.Add(button);
      }
    }

    private void RenderForm()
    {
      _activeOptionalExpander = null;
      _formPanel.Children.Clear();
      _formPanel.Children.Add(new TextBlock
      {
        Text = DisplayName(_viewModel.ActiveGroup),
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 10)
      });
      if (string.Equals(
        _viewModel.ActiveGroup,
        NativeStage01ViewModel.ConditionsGroup,
        StringComparison.Ordinal))
      {
        RenderConditions();
        return;
      }

      IReadOnlyList<NativeStage01FieldDefinition> fields =
        _viewModel.ActiveFields;
      if (fields.Any(value => value.IsOrganization))
        RenderOrganizationToolbar();

      NativeStage01FieldDefinition[] requiredFields = fields
        .Where(NativeStage01Validator.IsRequired)
        .ToArray();
      NativeStage01FieldDefinition[] optionalFields = fields
        .Where(value => !NativeStage01Validator.IsRequired(value))
        .ToArray();

      foreach (NativeStage01FieldDefinition field in requiredFields)
        _formPanel.Children.Add(BuildFieldCard(field));

      if (optionalFields.Length > 0)
      {
        string group = _viewModel.ActiveGroup;
        if (_viewModel.HasOptionalValidationError(group))
          _optionalExpansionByGroup[group] = true;
        bool isExpanded = _optionalExpansionByGroup.TryGetValue(
          group,
          out bool remembered)
          && remembered;
        var optionalPanel = new StackPanel
        {
          Margin = new Thickness(0, 6, 0, 0)
        };
        foreach (NativeStage01FieldDefinition field in optionalFields)
          optionalPanel.Children.Add(BuildFieldCard(field));

        var expander = new Expander
        {
          Header = OptionalHeader(group),
          IsExpanded = isExpanded,
          Content = optionalPanel,
          Margin = new Thickness(0, 4, 0, 12),
          FontWeight = FontWeights.SemiBold
        };
        expander.Expanded += (_, __) =>
          _optionalExpansionByGroup[group] = true;
        expander.Collapsed += (_, __) =>
          _optionalExpansionByGroup[group] = false;
        _activeOptionalExpander = expander;
        _formPanel.Children.Add(expander);
      }

      if (fields.Count == 0)
      {
        _formPanel.Children.Add(new TextBlock
        {
          Text = "当前目录没有数据库字段。",
          Margin = new Thickness(0, 10, 0, 0)
        });
      }
    }

    private void RenderOrganizationToolbar()
    {
      var toolbar = new WrapPanel
      {
        Margin = new Thickness(0, 0, 0, 10)
      };
      toolbar.Children.Add(new TextBlock
      {
        Text = "组织 " + (_viewModel.OrganizationIndex + 1)
          + " / " + _viewModel.Model.Organizations.Count,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 8, 12, 0),
        FontWeight = FontWeights.SemiBold
      });
      toolbar.Children.Add(SmallButton("上一条", () =>
      {
        _viewModel.MoveOrganization(-1);
        RenderAll();
      }));
      toolbar.Children.Add(SmallButton("下一条", () =>
      {
        _viewModel.MoveOrganization(1);
        RenderAll();
      }));
      toolbar.Children.Add(SmallButton("新增组织", () =>
      {
        _viewModel.AddOrganization();
        RenderAll();
      }));
      toolbar.Children.Add(SmallButton("删除当前组织", () =>
      {
        _viewModel.RemoveCurrentOrganization();
        RenderAll();
      }));
      _formPanel.Children.Add(toolbar);
    }

    private void RenderConditions()
    {
      foreach (IGrouping<string, NativeConditionDefinition> group in
        _viewModel.Conditions
          .GroupBy(value => value.Group ?? string.Empty)
          .OrderBy(value => value.Key, StringComparer.Ordinal))
      {
        _formPanel.Children.Add(new TextBlock
        {
          Text = string.IsNullOrWhiteSpace(group.Key)
            ? "项目条件"
            : group.Key,
          FontWeight = FontWeights.SemiBold,
          Margin = new Thickness(0, 10, 0, 5)
        });
        foreach (NativeConditionDefinition condition in group)
        {
          var checkBox = new CheckBox
          {
            Content = condition.DisplayName,
            IsChecked = _viewModel.GetCondition(condition.ConditionId),
            Margin = new Thickness(0, 4, 0, 4),
            IsEnabled = !_busy
          };
          checkBox.Checked += (_, __) =>
          {
            _viewModel.SetCondition(condition.ConditionId, true);
            RefreshSummaryText();
          };
          checkBox.Unchecked += (_, __) =>
          {
            _viewModel.SetCondition(condition.ConditionId, false);
            RefreshSummaryText();
          };
          _formPanel.Children.Add(checkBox);
        }
      }
    }

    private FrameworkElement BuildFieldCard(
      NativeStage01FieldDefinition field)
    {
      bool required = NativeStage01Validator.IsRequired(field);
      var panel = new StackPanel();
      var label = new TextBlock
      {
        Text = field.Label
          + (required ? "  *" : "  （可选）")
          + (field.Deferred ? "  · 后续阶段计算" : string.Empty),
        FontWeight = required ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = required ? Brushes.DarkRed : Brushes.Black,
        TextWrapping = TextWrapping.Wrap
      };
      panel.Children.Add(label);

      FrameworkElement editor = CreateEditor(field);
      panel.Children.Add(editor);
      panel.Children.Add(new TextBlock
      {
        Text = "示例：" + Example(field)
          + "｜H-IFC：" + field.IfcEntity
          + " / " + field.IfcPropertySet
          + " / " + field.IfcProperty,
        FontSize = 11,
        Foreground = Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0)
      });
      foreach (NativeStage01ValidationMessage message in
        _viewModel.ValidationMessagesForField(field.FieldKey))
      {
        panel.Children.Add(new TextBlock
        {
          Text = message.Message,
          Foreground = Brushes.DarkRed,
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
        Margin = new Thickness(0, 0, 0, 9),
        Background = field.ReadOnly || field.Deferred
          ? new SolidColorBrush(Color.FromRgb(248, 249, 250))
          : Brushes.White
      };
    }

    private FrameworkElement CreateEditor(NativeStage01FieldDefinition field)
    {
      bool editable = !_busy && !field.ReadOnly && !field.Deferred;
      string value = _viewModel.GetFieldValue(field);
      if (field.Kind == NativeStage01FieldKind.Boolean)
      {
        var checkBox = new CheckBox
        {
          Content = "启用",
          IsChecked = bool.TryParse(value, out bool parsed) && parsed,
          IsEnabled = editable,
          Margin = new Thickness(0, 7, 0, 0)
        };
        checkBox.Checked += (_, __) =>
          SetFieldValueFromEditor(field, "true");
        checkBox.Unchecked += (_, __) =>
          SetFieldValueFromEditor(field, "false");
        return checkBox;
      }
      if (field.Kind == NativeStage01FieldKind.Enum
        && field.AllowedValues.Count > 0)
      {
        var combo = new ComboBox
        {
          ItemsSource = field.AllowedValues,
          SelectedItem = value,
          IsEnabled = editable,
          Margin = new Thickness(0, 6, 0, 0),
          MinHeight = 28
        };
        combo.SelectionChanged += (_, __) =>
          SetFieldValueFromEditor(
            field,
            Convert.ToString(combo.SelectedItem) ?? string.Empty);
        return combo;
      }
      var textBox = new TextBox
      {
        Text = value,
        IsReadOnly = !editable,
        IsEnabled = !field.Deferred,
        Margin = new Thickness(0, 6, 0, 0),
        MinHeight = 28,
        Padding = new Thickness(5),
        TextWrapping = TextWrapping.Wrap
      };
      textBox.TextChanged += (_, __) =>
        SetFieldValueFromEditor(field, textBox.Text);
      return textBox;
    }

    private void SetFieldValueFromEditor(
      NativeStage01FieldDefinition field,
      string value)
    {
      string normalized = value ?? string.Empty;
      if (string.Equals(
        _viewModel.GetFieldValue(field),
        normalized,
        StringComparison.Ordinal))
        return;
      _viewModel.SetFieldValue(field, normalized);
      RefreshSummaryText();
      UpdateOptionalHeader();
    }

    private void ExpandOptionalSectionsWithErrors()
    {
      foreach (string group in _viewModel.Groups)
      {
        if (_viewModel.HasOptionalValidationError(group))
          _optionalExpansionByGroup[group] = true;
      }
    }

    private string OptionalHeader(string group)
    {
      return "选填项（共 "
        + _viewModel.GetOptionalFieldCount(group)
        + " 项，已填写 "
        + _viewModel.GetFilledOptionalFieldCount(group)
        + " 项）";
    }

    private void UpdateOptionalHeader()
    {
      if (_activeOptionalExpander == null) return;
      _activeOptionalExpander.Header = OptionalHeader(_viewModel.ActiveGroup);
    }

    private void SetBusy(bool busy, string status)
    {
      _busy = busy;
      _readButton.IsEnabled = !busy;
      _validateButton.IsEnabled = !busy;
      _writeButton.IsEnabled = !busy;
      _allowReinitialize.IsEnabled = !busy;
      if (!string.IsNullOrWhiteSpace(status)) SetStatus(status);
      RenderAll();
    }

    private void SetStatus(string status)
    {
      _statusText.Text = "状态：" + (status ?? string.Empty);
      StatusChanged?.Invoke(status ?? string.Empty);
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

    private static Button SmallButton(string text, Action action)
    {
      var button = new Button
      {
        Content = text,
        Padding = new Thickness(8, 4, 8, 4),
        Margin = new Thickness(0, 3, 5, 0)
      };
      button.Click += (_, __) => action();
      return button;
    }

    private static string DisplayName(string group)
    {
      if (string.IsNullOrWhiteSpace(group)) return "文件初始化";
      int separator = group.IndexOf('_');
      return separator >= 0 && separator + 1 < group.Length
        ? group.Substring(separator + 1)
        : group;
    }

    private static string Example(NativeStage01FieldDefinition field)
    {
      if (field.AllowedValues.Count > 0)
        return string.Join(" / ", field.AllowedValues.Take(3));
      if (field.FieldKey == NativeStage01Keys.BaseX)
        return "3373266.866（X = 南北坐标）";
      if (field.FieldKey == NativeStage01Keys.BaseY)
        return "38589642.165（Y = 东西坐标）";
      if (field.FieldKey == NativeStage01Keys.BaseElevation)
        return "24.000 m";
      if (field.FieldKey == NativeStage01Keys.TrueNorthAngle)
        return "0°";
      string label = field.Label ?? string.Empty;
      if (label.Contains("邮箱")) return "name@example.com";
      if (label.Contains("手机") || label.Contains("电话"))
        return "13800138000";
      if (label.Contains("邮政编码")) return "430000";
      switch (field.Kind)
      {
        case NativeStage01FieldKind.Number: return "1.25";
        case NativeStage01FieldKind.Integer: return "1";
        case NativeStage01FieldKind.Boolean: return "是 / 否";
        case NativeStage01FieldKind.Guid:
          return "11111111-2222-4333-8444-555555555555";
        case NativeStage01FieldKind.DateTime: return "2026-08-11";
        default: return field.Label;
      }
    }
  }
}
