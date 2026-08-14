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
    private readonly Button _previewButton;
    private readonly Button _writeButton;
    private readonly CheckBox _problemOnly;
    private readonly TextBlock _summaryText;
    private readonly TextBlock _statusText;
    private readonly ListBox _elementList;
    private readonly StackPanel _fieldPanel;
    private NativeStage02Preview _preview;
    private NativeStage02PreviewRequest _resolvedRequest;
    private bool _busy;

    internal NativeStage02View()
    {
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

      var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
      heading.Children.Add(new TextBlock
      {
        Text = "02 构件与属性准备",
        FontSize = 20,
        FontWeight = FontWeights.SemiBold
      });
      _summaryText = new TextBlock
      {
        Text = "先生成只读预览，再确认写入。默认扫描全模型，也可读取当前 Revit 选择。",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 5, 0, 0)
      };
      heading.Children.Add(_summaryText);
      Grid.SetRow(heading, 0);
      root.Children.Add(heading);

      var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
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
      actions.Children.Add(_fullModel);
      actions.Children.Add(_currentSelection);
      actions.Children.Add(_previewButton);
      actions.Children.Add(_writeButton);
      actions.Children.Add(_problemOnly);
      Grid.SetRow(actions, 1);
      root.Children.Add(actions);

      var workspace = new Grid();
      workspace.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(290)
      });
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
      Grid.SetRow(workspace, 2);
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
      Grid.SetRow(statusScroll, 3);
      root.Children.Add(statusScroll);
      Content = root;
      RenderElements();
    }

    internal event Action<string> StatusChanged;

    private void RequestPreview()
    {
      if (_busy) return;
      _preview = null;
      _resolvedRequest = null;
      _writeButton.IsEnabled = false;
      var request = new NativeStage02PreviewRequest
      {
        ScopeMode = _fullModel.IsChecked == true
          ? NativeStage02ScopeMode.FullModel
          : NativeStage02ScopeMode.CustomSelection,
        CustomUniqueIds = Array.Empty<string>()
      };
      SetBusy(true, "正在通过 Revit ExternalEvent 读取构件与参数证据……" );
      try
      {
        RevitExternalEventDispatcher.RequestStage02Preview(
          request,
          ApplyPreviewResult,
          ApplyFailure);
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void RequestWrite()
    {
      if (_busy || _preview == null || _resolvedRequest == null) return;
      SetBusy(true, "正在回读预览、准备参数并按构件原子写入……" );
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
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
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
        _writeButton.IsEnabled = false;
        RenderElements();
        SetStatus(result == null
          ? "预览失败：未返回结果。"
          : result.Status + "｜" + string.Join(" ", result.Messages));
        return;
      }
      _preview = result.Preview;
      _resolvedRequest = result.ResolvedRequest;
      _writeButton.IsEnabled = true;
      RenderElements();
      SetStatus(result.Status + "｜" + string.Join(" ", result.Messages));
    }

    private void ApplyWriteResult(NativeStage02WriteResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(
          new Action<NativeStage02WriteResult>(ApplyWriteResult),
          result);
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
      }
      if (result.RequiresNewPreview && result.RefreshedPreview == null)
      {
        _preview = null;
        _resolvedRequest = null;
      }
      _writeButton.IsEnabled = _preview != null && !_busy;
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
      SetStatus(
        "Revit 请求失败："
        + (exception == null ? "未知错误" : exception.Message));
    }

    private void RenderElements()
    {
      NativeStage02ElementPlan selected =
        (_elementList.SelectedItem as ElementListItem)?.Plan;
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
          || value.Fields.Any(field =>
            field.Status != NativeStage02FieldStatus.Correct
            && field.Status != NativeStage02FieldStatus.NotApplicable));
      }
      foreach (NativeStage02ElementPlan element in elements)
        _elementList.Items.Add(new ElementListItem(element));
      if (selected != null)
      {
        ElementListItem match = _elementList.Items
          .Cast<ElementListItem>()
          .FirstOrDefault(value => value.Plan.Element.UniqueId
            == selected.Element.UniqueId);
        if (match != null) _elementList.SelectedItem = match;
      }
      if (_elementList.SelectedItem == null && _elementList.Items.Count > 0)
        _elementList.SelectedIndex = 0;

      _summaryText.Text = "构件="
        + _preview.Elements.Count
        + "｜阻断="
        + _preview.BlockedElementCount
        + "｜可执行="
        + _preview.ActionableElementCount
        + "｜待绑定="
        + _preview.PendingBindingFieldCount
        + "｜待写入="
        + _preview.PendingWriteFieldCount
        + "｜待填写="
        + _preview.PendingInputFieldCount
        + "｜SHA-256="
        + _preview.PreviewHash;
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
          Text = _preview == null
            ? "生成预览后在左侧选择构件。"
            : "当前筛选没有可显示构件。",
          Margin = new Thickness(8)
        });
        return;
      }
      NativeStage02ElementPlan plan = selected.Plan;
      _fieldPanel.Children.Add(new TextBlock
      {
        Text = plan.Element.ElementName
          + "｜Id="
          + plan.Element.ElementId
          + "｜"
          + plan.Element.Category,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4)
      });
      _fieldPanel.Children.Add(new TextBlock
      {
        Text = "角色="
          + (string.IsNullOrWhiteSpace(plan.RoleId) ? "未识别" : plan.RoleId)
          + "｜来源="
          + plan.RoleMatchSource
          + (string.IsNullOrWhiteSpace(plan.Message)
            ? string.Empty
            : "｜" + plan.Message),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10)
      });
      foreach (NativeStage02FieldPlan field in plan.Fields)
        _fieldPanel.Children.Add(FieldCard(field));
      if (plan.Fields.Count == 0)
      {
        _fieldPanel.Children.Add(new TextBlock
        {
          Text = plan.IsBlocked
            ? "该构件未通过精确载体识别，不会写入。"
            : "该构件没有 Stage02 字段。",
          Foreground = plan.IsBlocked ? Brushes.DarkRed : Brushes.DimGray,
          TextWrapping = TextWrapping.Wrap
        });
      }
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
        Text = field.Property.IfcEntity
          + " / "
          + field.Property.IfcPropertySet
          + " / "
          + field.Property.IfcProperty
          + "｜"
          + field.Property.ParameterGuid.ToString("D"),
        FontSize = 11,
        Foreground = Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0)
      });
      panel.Children.Add(new TextBlock
      {
        Text = "参数动作="
          + field.BindingAction
          + "｜值动作="
          + field.ValueAction
          + "｜当前="
          + Empty(field.CurrentCanonicalValue)
          + "｜建议="
          + Empty(field.ProposedCanonicalValue),
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
              ? Brushes.DarkRed
              : Brushes.DarkGoldenrod,
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
      _writeButton.IsEnabled = !busy && _preview != null;
      _fullModel.IsEnabled = !busy;
      _currentSelection.IsEnabled = !busy;
      _problemOnly.IsEnabled = !busy;
      if (!string.IsNullOrWhiteSpace(status)) SetStatus(status);
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

    private sealed class ElementListItem
    {
      internal ElementListItem(NativeStage02ElementPlan plan)
      {
        Plan = plan;
      }

      internal NativeStage02ElementPlan Plan { get; }

      public override string ToString()
      {
        string name = string.IsNullOrWhiteSpace(Plan.Element.ElementName)
          ? Plan.Element.Category
          : Plan.Element.ElementName;
        return name
          + " · Id="
          + Plan.Element.ElementId
          + " · "
          + (Plan.IsBlocked ? "阻断" : Plan.RoleId);
      }
    }
  }
}
