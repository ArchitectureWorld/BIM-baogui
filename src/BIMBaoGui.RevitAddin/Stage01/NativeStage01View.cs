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
    private const string TotalPlanSections =
      "项目登记信息｜项目位置与坐标｜规划目标与限值｜其他项目输入";
    private readonly Action<string> _navigateToMetric;
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
    private readonly Dictionary<string, FrameworkElement> _fieldCardByKey =
      new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
    private Expander _activeOptionalExpander;
    private bool _busy;

    internal NativeStage01View()
      : this(_ => { })
    {
    }

    internal NativeStage01View(Action<string> navigateToMetric)
    {
      _navigateToMetric = navigateToMetric
        ?? throw new ArgumentNullException(nameof(navigateToMetric));
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
        Text = "先明确项目条件，再填写项目、子项、坐标、高程、真北、单位和组织信息。",
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

    internal void NavigateToField(string fieldKey)
    {
      if (string.IsNullOrWhiteSpace(fieldKey)
        || !NativeRuleCatalog.Current.Stage01FieldsByKey.TryGetValue(
          fieldKey,
          out NativeStage01FieldDefinition field))
      {
        return;
      }
      string group = NativeStage01ViewModel.GroupForField(field);
      _viewModel.SetActiveGroup(group);
      if (!NativeStage01Validator.IsRequired(field))
        _optionalExpansionByGroup[group] = true;
      RenderAll();
      if (_fieldCardByKey.TryGetValue(fieldKey, out FrameworkElement card))
        card.BringIntoView();
    }

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
        if (IsConditionValidationField(firstField))
        {
          _viewModel.SetActiveGroup(
            NativeStage01ViewModel.ConditionsGroup);
        }
        else
        {
          NativeStage01FieldDefinition field =
            NativeRuleCatalog.Current.Stage01FieldsByKey.TryGetValue(
              firstField,
              out NativeStage01FieldDefinition found)
              ? found
              : null;
          if (field != null)
            _viewModel.SetActiveGroup(NativeStage01ViewModel.GroupForField(field));
        }
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
      _viewModel.LoadReadResult(result);
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
        _viewModel.MarkSaved(result);
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
        + "｜总平分区：" + TotalPlanSections
        + "｜未填必填项："
        + _viewModel.GetMissingRequiredCount(_viewModel.ActiveGroup)
        + "｜现场差异："
        + _viewModel.Drifts.Count
        + "｜"
        + (_viewModel.RequiresMigrationConfirmation
          ? "等待迁移确认"
          : _viewModel.IsDirty ? "有未写入修改" : "已加载状态");
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
      _fieldCardByKey.Clear();
      _formPanel.Children.Clear();
      bool isConditionsGroup = string.Equals(
        _viewModel.ActiveGroup,
        NativeStage01ViewModel.ConditionsGroup,
        StringComparison.Ordinal);
      _formPanel.Children.Add(new TextBlock
      {
        Text = DisplayName(_viewModel.ActiveGroup)
          + (isConditionsGroup ? "（必填）" : string.Empty),
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 10)
      });
      RenderReadEvidence();
      if (isConditionsGroup)
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

    private void RenderReadEvidence()
    {
      if (_viewModel.StorageState != NativeStage01StorageState.Current
        && _viewModel.StorageState
          != NativeStage01StorageState.MigratableLegacy)
      {
        return;
      }
      if (!_viewModel.RequiresMigrationConfirmation
        && _viewModel.Drifts.Count == 0
        && string.IsNullOrWhiteSpace(_viewModel.SourcePayloadVersion))
      {
        return;
      }

      var panel = new StackPanel();
      if (_viewModel.RequiresMigrationConfirmation)
      {
        panel.Children.Add(new TextBlock
        {
          Text = "等待迁移确认：Payload "
            + _viewModel.SourcePayloadVersion
            + " 已生成 "
            + NativeStage01Canonicalizer.PayloadSchemaVersion
            + " 内存候选；点击“写入并回读”才会确认迁移。",
          Foreground = Brushes.DarkOrange,
          FontWeight = FontWeights.SemiBold,
          TextWrapping = TextWrapping.Wrap,
          Margin = new Thickness(0, 0, 0, 7)
        });
      }
      if (_viewModel.Drifts.Count == 0)
      {
        panel.Children.Add(new TextBlock
        {
          Text = "Revit 原生字段与上次确认值一致。",
          Foreground = Brushes.DarkGreen,
          TextWrapping = TextWrapping.Wrap
        });
      }
      if (_viewModel.Drifts.Count > 0)
      {
        panel.Children.Add(new TextBlock
        {
          Text = "现场值漂移",
          FontWeight = FontWeights.SemiBold,
          Foreground = Brushes.DarkOrange,
          Margin = new Thickness(0, 7, 0, 3)
        });
      }
      foreach (NativeStage01Drift drift in _viewModel.Drifts)
      {
        var driftPanel = new StackPanel
        {
          Margin = new Thickness(0, 5, 0, 5)
        };
        driftPanel.Children.Add(new TextBlock
        {
          Text = drift.Label,
          FontWeight = FontWeights.SemiBold
        });
        driftPanel.Children.Add(new TextBlock
        {
          Text = "上次确认值：" + DisplayEvidenceValue(drift.StoredValue),
          TextWrapping = TextWrapping.Wrap
        });
        driftPanel.Children.Add(new TextBlock
        {
          Text = "当前 RVT 值：" + DisplayEvidenceValue(drift.LiveValue),
          TextWrapping = TextWrapping.Wrap
        });
        driftPanel.Children.Add(new TextBlock
        {
          Text = "状态：已变化（未自动覆盖）",
          Foreground = Brushes.DarkOrange
        });
        panel.Children.Add(driftPanel);
      }
      _formPanel.Children.Add(new Border
      {
        Child = panel,
        Background = new SolidColorBrush(Color.FromRgb(252, 249, 240)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(222, 190, 120)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10),
        Margin = new Thickness(0, 0, 0, 12)
      });
    }

    private static string DisplayEvidenceValue(string value)
    {
      return string.IsNullOrEmpty(value) ? "（空）" : value;
    }

    private void RenderOrganizationToolbar()
    {
      var toolbar = new WrapPanel
      {
        Margin = new Thickness(0, 0, 0, 10)
      };
      toolbar.Children.Add(new TextBlock
      {
        Text = "组织 " + _viewModel.OrganizationDisplayIndex
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
      NativeProjectConditionDeclarationDecision decision =
        _viewModel.GetConditionDeclarationDecision();
      _formPanel.Children.Add(new Border
      {
        Child = new TextBlock
        {
          Text = "项目条件为必填声明：请选择一个或多个实际条件；如果均不适用，请明确勾选“无上述项目条件（已确认）”。",
          TextWrapping = TextWrapping.Wrap,
          Foreground = decision.IsValid ? Brushes.DarkGreen : Brushes.DarkRed
        },
        Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(224, 227, 232)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10),
        Margin = new Thickness(0, 0, 0, 10)
      });

      var noneCheckBox = new CheckBox
      {
        Content = "无上述项目条件（已确认）",
        IsChecked = _viewModel.GetNoConditions(),
        Margin = new Thickness(0, 5, 0, 12),
        Padding = new Thickness(8),
        FontWeight = FontWeights.SemiBold,
        IsEnabled = !_busy
      };
      noneCheckBox.Checked += (_, __) =>
      {
        _viewModel.SetNoConditions(true);
        RenderAll();
      };
      noneCheckBox.Unchecked += (_, __) =>
      {
        _viewModel.SetNoConditions(false);
        RenderAll();
      };
      _formPanel.Children.Add(noneCheckBox);

      foreach (IGrouping<string, NativeConditionDefinition> group in
        _viewModel.Conditions
          .GroupBy(value => value.Group ?? string.Empty)
          .OrderBy(value => value.Key, StringComparer.Ordinal))
      {
        _formPanel.Children.Add(new TextBlock
        {
          Text = string.IsNullOrWhiteSpace(group.Key)
            ? "实际项目条件"
            : DisplayName(group.Key),
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
            RenderAll();
          };
          checkBox.Unchecked += (_, __) =>
          {
            _viewModel.SetCondition(condition.ConditionId, false);
            RenderAll();
          };
          _formPanel.Children.Add(checkBox);
        }
      }

      foreach (NativeStage01ValidationMessage message in
        _viewModel.ValidationMessagesForField(
          NativeProjectConditionDeclarationPolicy.NoneConditionId))
      {
        _formPanel.Children.Add(new TextBlock
        {
          Text = message.Message,
          Foreground = Brushes.DarkRed,
          FontSize = 11,
          TextWrapping = TextWrapping.Wrap,
          Margin = new Thickness(0, 8, 0, 0)
        });
      }
    }

    private FrameworkElement BuildFieldCard(
      NativeStage01FieldDefinition field)
    {
      bool required = NativeStage01Validator.IsRequired(field);
      NativeStage01FieldPresentation presentation =
        _viewModel.GetFieldPresentation(field);
      bool planningTarget =
        NativeStage01FieldPresentationPolicy.IsPlanningTarget(field);
      var panel = new StackPanel();
      var label = new TextBlock
      {
        Text = field.Label
          + (planningTarget ? "  · 规划目标/限值" : string.Empty)
          + (required ? "  *" : "  （可选）")
          + (field.Deferred ? "  · 后续阶段计算" : string.Empty),
        FontWeight = required ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = required ? Brushes.DarkRed : Brushes.Black,
        TextWrapping = TextWrapping.Wrap
      };
      panel.Children.Add(label);

      FrameworkElement editor = string.Equals(
        presentation.NavigationTarget,
        "02B",
        StringComparison.Ordinal)
          ? CreateStage02BReferenceEditor(presentation)
          : planningTarget
            ? CreatePlanningTargetEditor(field)
            : CreateEditor(field);
      panel.Children.Add(editor);
      panel.Children.Add(new TextBlock
      {
        Text = "当前状态值：" + DisplayEvidenceValue(presentation.CurrentValue)
          + "｜来源：" + presentation.Source
          + "｜回读：" + presentation.ReadbackState
          + "｜当前清单：" + (presentation.InCurrentChecklist ? "是" : "否")
          + (string.IsNullOrWhiteSpace(presentation.Unit)
            ? string.Empty
            : "｜单位：" + presentation.Unit),
        FontSize = 11,
        Foreground = presentation.ReadbackState
          == NativeStage01FieldOperationState.Failed
            ? Brushes.DarkRed
            : Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0)
      });
      if (!string.IsNullOrWhiteSpace(presentation.IssueMessage))
      {
        panel.Children.Add(new TextBlock
        {
          Text = presentation.IssueCode + "：" + presentation.IssueMessage,
          Foreground = Brushes.DarkRed,
          FontSize = 11,
          TextWrapping = TextWrapping.Wrap,
          Margin = new Thickness(0, 3, 0, 0)
        });
      }
      panel.Children.Add(new TextBlock
      {
        Text = "示例：" + Example(field)
          + "｜H-IFC 映射目标：" + field.IfcEntity
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

      var card = new Border
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
      _fieldCardByKey[field.FieldKey] = card;
      return card;
    }

    private FrameworkElement CreateStage02BReferenceEditor(
      NativeStage01FieldPresentation presentation)
    {
      var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
      panel.Children.Add(new TextBlock
      {
        Text = string.IsNullOrWhiteSpace(presentation.CurrentValue)
          ? "尚未取得 02B 当前值"
          : presentation.CurrentValue
            + (string.IsNullOrWhiteSpace(presentation.Unit)
              ? string.Empty
              : " " + presentation.Unit),
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap
      });
      var button = SmallButton("转到 02B 填写", () =>
        NavigateToMetric("ca21e324-046b-5bfd-84c8-0d3470082303"));
      button.IsEnabled = !_busy;
      panel.Children.Add(button);
      return panel;
    }

    private void NavigateToMetric(string propertyId)
    {
      _navigateToMetric(propertyId);
    }

    private FrameworkElement CreatePlanningTargetEditor(
      NativeStage01FieldDefinition field)
    {
      NativePlanningTargetValue current = _viewModel.GetPlanningTarget(field);
      string[] operators = { "≤", "≥", "=", "区间" };
      var panel = new Grid { Margin = new Thickness(0, 6, 0, 0) };
      panel.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(90)
      });
      panel.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(1, GridUnitType.Star)
      });
      panel.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(1, GridUnitType.Star)
      });
      var operatorBox = new ComboBox
      {
        ItemsSource = operators,
        SelectedItem = DisplayPlanningOperator(current?.Operator),
        IsEnabled = !_busy,
        MinHeight = 28,
        Margin = new Thickness(0, 0, 6, 0)
      };
      var value1 = new TextBox
      {
        Text = current?.Value1 ?? string.Empty,
        IsEnabled = !_busy,
        MinHeight = 28,
        Padding = new Thickness(5),
        Margin = new Thickness(0, 0, 6, 0)
      };
      var value2 = new TextBox
      {
        Text = current?.Value2 ?? string.Empty,
        IsEnabled = !_busy,
        MinHeight = 28,
        Padding = new Thickness(5)
      };
      Action update = () => _viewModel.SetPlanningTarget(
        field,
        Convert.ToString(operatorBox.SelectedItem) ?? "≤",
        value1.Text,
        value2.Text,
        field.CanonicalUnit);
      operatorBox.SelectionChanged += (_, __) => update();
      value1.LostFocus += (_, __) => update();
      value2.LostFocus += (_, __) => update();
      Grid.SetColumn(operatorBox, 0);
      Grid.SetColumn(value1, 1);
      Grid.SetColumn(value2, 2);
      panel.Children.Add(operatorBox);
      panel.Children.Add(value1);
      panel.Children.Add(value2);
      return panel;
    }

    private static string DisplayPlanningOperator(string value)
    {
      switch (value)
      {
        case "GreaterOrEqual": return "≥";
        case "Equal": return "=";
        case "Range": return "区间";
        default: return "≤";
      }
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

    private static bool IsConditionValidationField(string fieldKey)
    {
      return string.Equals(
          fieldKey,
          NativeProjectConditionDeclarationPolicy.NoneConditionId,
          StringComparison.Ordinal)
        || NativeRuleCatalog.Current.Conditions.Any(condition =>
          string.Equals(
            condition.ConditionId,
            fieldKey,
            StringComparison.Ordinal));
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
