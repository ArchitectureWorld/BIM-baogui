using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02B
{
  internal sealed class NativeStage02BView : UserControl
  {
    private readonly NativeIssueHub _hub;
    private readonly NativeStage02BViewModel _viewModel;
    private readonly StackPanel _rows;
    private readonly TextBlock _status;
    private readonly Button _saveAll;
    private readonly Button _retry;
    private readonly Dictionary<string, FrameworkElement> _rowByPropertyId =
      new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _inputByPropertyId =
      new Dictionary<string, TextBox>(StringComparer.Ordinal);
    private bool _busy;

    internal NativeStage02BView(NativeIssueHub hub)
    {
      _hub = hub ?? throw new ArgumentNullException(nameof(hub));
      _viewModel = new NativeStage02BViewModel();
      Background = Brushes.White;
      var root = new Grid();
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
        Text = "02B 项目实际指标",
        FontSize = 20,
        FontWeight = FontWeights.SemiBold
      });
      heading.Children.Add(new TextBlock
      {
        Text = "六项实际指标均由人工录入；每项独立写入、回读和审计。",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 5, 0, 0)
      });
      Grid.SetRow(heading, 0);
      root.Children.Add(heading);

      var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
      _saveAll = ActionButton("保存全部");
      _retry = ActionButton("仅重试失败项");
      _saveAll.Click += (_, __) => Save(_viewModel.BuildSaveAllRequest());
      _retry.Click += (_, __) => Retry();
      actions.Children.Add(_saveAll);
      actions.Children.Add(_retry);
      Grid.SetRow(actions, 1);
      root.Children.Add(actions);

      _rows = new StackPanel();
      root.Children.Add(new ScrollViewer
      {
        Content = _rows,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
      });
      Grid.SetRow(root.Children[root.Children.Count - 1], 2);

      _status = new TextBlock
      {
        Text = "状态：等待读取 02B 指标",
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(8),
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250))
      };
      Grid.SetRow(_status, 3);
      root.Children.Add(_status);
      Content = root;
      RenderRows();
      Loaded += (_, __) => RequestRead();
    }

    internal event Action<string> StatusChanged;

    internal void NavigateToMetric(string propertyId)
    {
      if (!_rowByPropertyId.TryGetValue(propertyId ?? string.Empty,
        out FrameworkElement row)) return;
      row.BringIntoView();
      if (_inputByPropertyId.TryGetValue(propertyId,
        out TextBox input)) input.Focus();
    }

    private void RequestRead()
    {
      if (_busy) return;
      SetBusy(true, "正在读取 02B 当前值……");
      try
      {
        RevitExternalEventDispatcher.RequestStage02BRead(
          ApplyRead,
          ApplyFailure);
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void Save(NativeStage02BWriteRequest request)
    {
      if (_busy) return;
      SetBusy(true, "正在逐指标写入并回读……");
      try
      {
        RevitExternalEventDispatcher.RequestStage02BWrite(
          request,
          ApplyWrite,
          ApplyFailure);
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void Retry()
    {
      try
      {
        Save(_viewModel.BuildRetryRequest(_viewModel.LastWriteResult
          ?? throw new InvalidOperationException("当前没有可重试的失败项。")));
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void ApplyRead(NativeStage02BReadResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action<NativeStage02BReadResult>(ApplyRead),
          result);
        return;
      }
      _viewModel.ApplyRead(result);
      _hub.ResetForDocument(result?.Identity?.DocumentFingerprint);
      _hub.Replace("STAGE02B", result?.Issues
        ?? Array.Empty<NativeIssueRecord>());
      RenderRows();
      SetBusy(false, "02B 当前值已读取。");
    }

    private void ApplyWrite(NativeStage02BWriteResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action<NativeStage02BWriteResult>(ApplyWrite),
          result);
        return;
      }
      _viewModel.ApplyWrite(result);
      RenderRows();
      int failed = result?.FailedPropertyIds?.Count ?? 0;
      string suffix = string.IsNullOrWhiteSpace(result?.TechnicalErrorCode)
        ? string.Empty : "｜" + result.TechnicalErrorCode;
      SetBusy(false, failed == 0
        ? "六项指标已写入并回读。" + suffix
        : "已完成部分写入，失败 " + failed + " 项。" + suffix);
    }

    private void ApplyFailure(Exception exception)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action<Exception>(ApplyFailure), exception);
        return;
      }
      SetBusy(false, "02B 操作失败：" + (exception?.Message ?? "未知错误"));
    }

    private void RenderRows()
    {
      _rows.Children.Clear();
      _rowByPropertyId.Clear();
      _inputByPropertyId.Clear();
      foreach (NativeStage02BMetricDefinition metric in _viewModel.Metrics)
      {
        NativeStage02BMetricInput input = _viewModel.Inputs.Single(value =>
          value.PropertyId == metric.PropertyId);
        NativeStage02BMetricRecord record = _viewModel.RecordFor(metric.PropertyId);
        NativeStage02BMetricOutcome outcome = _viewModel.OutcomeFor(metric.PropertyId);
        var panel = new StackPanel();
        panel.Children.Add(Line("指标名称", metric.Property.IfcProperty));
        panel.Children.Add(Line("完整 identity", metric.Identity));
        panel.Children.Add(Line("单位", string.IsNullOrWhiteSpace(
          metric.Property.CanonicalUnit) ? "—" : metric.Property.CanonicalUnit));
        panel.Children.Add(new TextBlock
        {
          Text = "人工输入",
          FontWeight = FontWeights.SemiBold,
          Margin = new Thickness(0, 6, 0, 2)
        });
        var inputBox = new TextBox
        {
          Text = input.RawValue,
          IsEnabled = !_busy,
          Padding = new Thickness(6),
          MinHeight = 28
        };
        inputBox.TextChanged += (_, __) => input.RawValue = inputBox.Text;
        panel.Children.Add(inputBox);
        panel.Children.Add(Line("上次成功值",
          record?.LastSuccessfulCanonicalValue ?? "—"));
        panel.Children.Add(Line("本次状态", outcome == null ? "未尝试"
          : outcome.Succeeded ? "成功" : "失败｜" + outcome.ErrorCode));
        panel.Children.Add(Line("官方载体状态",
          (record?.OfficialCarrierStatus ?? metric.OfficialCarrierStatus)
            .ToString()));
        var border = new Border
        {
          Child = panel,
          BorderBrush = new SolidColorBrush(Color.FromRgb(224, 227, 232)),
          BorderThickness = new Thickness(1),
          CornerRadius = new CornerRadius(4),
          Padding = new Thickness(10),
          Margin = new Thickness(0, 0, 0, 9)
        };
        _rows.Children.Add(border);
        _rowByPropertyId[metric.PropertyId] = border;
        _inputByPropertyId[metric.PropertyId] = inputBox;
      }
      _retry.IsEnabled = !_busy
        && (_viewModel.LastWriteResult?.FailedPropertyIds?.Count ?? 0) > 0;
    }

    private void SetBusy(bool value, string status)
    {
      _busy = value;
      _saveAll.IsEnabled = !value;
      _retry.IsEnabled = !value
        && (_viewModel.LastWriteResult?.FailedPropertyIds?.Count ?? 0) > 0;
      foreach (TextBox input in _inputByPropertyId.Values)
        input.IsEnabled = !value;
      _status.Text = "状态：" + (status ?? string.Empty);
      StatusChanged?.Invoke(status ?? string.Empty);
    }

    private static TextBlock Line(string label, string value)
    {
      return new TextBlock
      {
        Text = label + "：" + (value ?? string.Empty),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0)
      };
    }

    private static Button ActionButton(string text)
    {
      return new Button
      {
        Content = text,
        Padding = new Thickness(14, 7, 14, 7),
        Margin = new Thickness(0, 0, 8, 0),
        MinWidth = 120
      };
    }
  }
}
