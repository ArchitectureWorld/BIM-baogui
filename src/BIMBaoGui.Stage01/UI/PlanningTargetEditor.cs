using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using BIMBaoGui.Stage01.Core;
using Grasshopper.GUI.Canvas;

namespace BIMBaoGui.Stage01.UI
{
  internal static class PlanningTargetEditor
  {
    private static Form _active;

    public static void Show(
      GH_Canvas canvas,
      RectangleF canvasBounds,
      PlanningTargetDefinition definition,
      PlanningTargetValue current,
      Action<PlanningTargetValue> accepted,
      Action cleared)
    {
      if (canvas == null || definition == null) return;
      CloseActive();

      Form form = CreateHost(canvas, canvasBounds, 142);
      var title = new Label
      {
        AutoEllipsis = true,
        Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold, GraphicsUnit.Point),
        ForeColor = Color.FromArgb(31, 42, 55),
        Location = new Point(9, 6),
        Size = new Size(form.ClientSize.Width - 18, 22),
        Text = definition.Label + "｜示例：" + definition.Example,
        TextAlign = ContentAlignment.MiddleLeft
      };

      var op = new ComboBox
      {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
        Location = new Point(9, 34),
        Size = new Size(84, 27)
      };
      op.Items.AddRange(new object[] { "≤", "≥", "=", "区间" });

      var value1 = new TextBox
      {
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
        Location = new Point(101, 34),
        Size = new Size(92, 27),
        Text = current?.Value1.ToString(CultureInfo.InvariantCulture) ?? string.Empty
      };
      var separator = new Label
      {
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = Color.FromArgb(102, 116, 133),
        Location = new Point(198, 34),
        Size = new Size(22, 27),
        Text = "至",
        TextAlign = ContentAlignment.MiddleCenter
      };
      var value2 = new TextBox
      {
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
        Location = new Point(224, 34),
        Size = new Size(92, 27),
        Text = current?.Value2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
      };
      var unit = new Label
      {
        Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point),
        ForeColor = Color.FromArgb(31, 92, 166),
        Location = new Point(322, 34),
        Size = new Size(55, 27),
        Text = UnitText(definition.Unit),
        TextAlign = ContentAlignment.MiddleCenter
      };
      var ok = CreateButton("确定", form.ClientSize.Width - 151, 72, 66, 29, Color.FromArgb(31, 92, 166), Color.White);
      var clear = CreateButton("清空", form.ClientSize.Width - 78, 72, 66, 29, Color.FromArgb(242, 246, 251), Color.FromArgb(190, 53, 53));
      var errorLabel = new Label
      {
        AutoEllipsis = true,
        Font = new Font("Microsoft YaHei UI", 7.7f, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = Color.FromArgb(190, 53, 53),
        Location = new Point(9, 105),
        Size = new Size(form.ClientSize.Width - 18, 29),
        TextAlign = ContentAlignment.MiddleLeft
      };

      PlanningTargetOperator selectedOperator = current?.Operator ?? definition.DefaultOperator;
      op.SelectedItem = OperatorText(selectedOperator);
      UpdateRangeState();

      bool finished = false;
      Action<bool> finish = commit =>
      {
        if (finished) return;
        if (commit)
        {
          PlanningTargetOperator parsedOperator = ParseOperator(Convert.ToString(op.SelectedItem));
          if (!PlanningTargetValue.TryCreate(
            definition.MetricCode,
            parsedOperator,
            value1.Text,
            value2.Text,
            definition.Unit,
            "项目初始化",
            out PlanningTargetValue target,
            out string error))
          {
            errorLabel.Text = error;
            value1.BackColor = Color.FromArgb(255, 244, 244);
            if (parsedOperator == PlanningTargetOperator.Range)
              value2.BackColor = Color.FromArgb(255, 244, 244);
            value1.Focus();
            return;
          }
          accepted?.Invoke(target);
        }
        finished = true;
        form.Close();
      };

      void UpdateRangeState()
      {
        bool range = ParseOperator(Convert.ToString(op.SelectedItem)) == PlanningTargetOperator.Range;
        separator.Visible = range;
        value2.Visible = range;
        if (!range) value2.Text = string.Empty;
      }

      op.SelectedIndexChanged += (sender, args) =>
      {
        errorLabel.Text = string.Empty;
        UpdateRangeState();
      };
      value1.TextChanged += (sender, args) =>
      {
        errorLabel.Text = string.Empty;
        value1.BackColor = Color.White;
      };
      value2.TextChanged += (sender, args) =>
      {
        errorLabel.Text = string.Empty;
        value2.BackColor = Color.White;
      };
      ok.Click += (sender, args) => finish(true);
      clear.Click += (sender, args) =>
      {
        cleared?.Invoke();
        finished = true;
        form.Close();
      };
      form.Deactivate += (sender, args) => finish(false);
      form.FormClosed += (sender, args) => { if (ReferenceEquals(_active, form)) _active = null; };
      form.Controls.Add(title);
      form.Controls.Add(op);
      form.Controls.Add(value1);
      form.Controls.Add(separator);
      form.Controls.Add(value2);
      form.Controls.Add(unit);
      form.Controls.Add(ok);
      form.Controls.Add(clear);
      form.Controls.Add(errorLabel);
      Show(form, canvas, () =>
      {
        value1.Focus();
        value1.SelectAll();
      });
    }

    public static void CloseActive()
    {
      if (_active == null) return;
      try { _active.Close(); }
      catch { }
      _active = null;
    }

    private static Button CreateButton(string text, int x, int y, int width, int height, Color back, Color fore)
    {
      return new Button
      {
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold, GraphicsUnit.Point),
        ForeColor = fore,
        BackColor = back,
        Location = new Point(x, y),
        Size = new Size(width, height),
        Text = text
      };
    }

    private static string UnitText(PlanningTargetUnit unit)
    {
      switch (unit)
      {
        case PlanningTargetUnit.Percent: return "%";
        case PlanningTargetUnit.Count: return "个";
        default: return "比值";
      }
    }

    private static string OperatorText(PlanningTargetOperator value)
    {
      switch (value)
      {
        case PlanningTargetOperator.LessOrEqual: return "≤";
        case PlanningTargetOperator.GreaterOrEqual: return "≥";
        case PlanningTargetOperator.Range: return "区间";
        default: return "=";
      }
    }

    private static PlanningTargetOperator ParseOperator(string value)
    {
      switch (value)
      {
        case "≤": return PlanningTargetOperator.LessOrEqual;
        case "≥": return PlanningTargetOperator.GreaterOrEqual;
        case "区间": return PlanningTargetOperator.Range;
        default: return PlanningTargetOperator.Equal;
      }
    }

    private static Form CreateHost(GH_Canvas canvas, RectangleF canvasBounds, int height)
    {
      PointF topLeft = canvas.Viewport.ProjectPoint(new PointF(canvasBounds.Left, canvasBounds.Top));
      Point screenPoint = canvas.PointToScreen(Point.Round(topLeft));
      int width = Math.Max(420, (int) Math.Round(canvasBounds.Width * canvas.Viewport.Zoom));
      return new Form
      {
        FormBorderStyle = FormBorderStyle.FixedSingle,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = screenPoint,
        ClientSize = new Size(width, height),
        TopMost = true,
        MinimizeBox = false,
        MaximizeBox = false,
        ControlBox = false,
        BackColor = Color.White
      };
    }

    private static void Show(Form form, GH_Canvas canvas, Action shown)
    {
      _active = form;
      form.Shown += (sender, args) => shown?.Invoke();
      Form owner = canvas.FindForm();
      if (owner == null) form.Show();
      else form.Show(owner);
    }
  }
}
