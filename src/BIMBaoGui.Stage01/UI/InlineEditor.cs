using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI.Canvas;

namespace BIMBaoGui.Stage01.UI
{
  internal static class InlineEditor
  {
    private static Form _active;

    public static void ShowText(
      GH_Canvas canvas,
      RectangleF canvasBounds,
      string value,
      string guide,
      Func<string, string> validate,
      Action<string> accepted)
    {
      CloseActive();
      Form form = CreateHost(canvas, canvasBounds, 88);
      var guideLabel = CreateLabel(guide, Color.FromArgb(86, 103, 122), 7, 4, form.ClientSize.Width - 14, 20);
      var editor = new TextBox
      {
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
        Text = value ?? string.Empty,
        BackColor = Color.White,
        ForeColor = Color.FromArgb(32, 42, 56),
        Location = new Point(7, 26),
        Size = new Size(Math.Max(120, form.ClientSize.Width - 84), 25)
      };
      var ok = CreateButton("确定", form.ClientSize.Width - 71, 25, 64, 27);
      var errorLabel = CreateLabel(string.Empty, Color.FromArgb(190, 53, 53), 7, 56, form.ClientSize.Width - 14, 24);

      bool finished = false;
      Action<bool> finish = commit =>
      {
        if (finished) return;
        if (commit)
        {
          string error = validate?.Invoke(editor.Text);
          if (!string.IsNullOrWhiteSpace(error))
          {
            errorLabel.Text = error;
            editor.BackColor = Color.FromArgb(255, 244, 244);
            editor.Focus();
            editor.SelectAll();
            return;
          }
          accepted?.Invoke(editor.Text);
        }
        finished = true;
        form.Close();
      };

      ok.Click += (sender, args) => finish(true);
      editor.KeyDown += (sender, args) =>
      {
        if (args.KeyCode == Keys.Enter)
        {
          args.SuppressKeyPress = true;
          finish(true);
        }
        else if (args.KeyCode == Keys.Escape)
        {
          args.SuppressKeyPress = true;
          finish(false);
        }
      };
      form.Deactivate += (sender, args) => finish(false);
      form.FormClosed += (sender, args) => { if (ReferenceEquals(_active, form)) _active = null; };
      form.Controls.Add(guideLabel);
      form.Controls.Add(editor);
      form.Controls.Add(ok);
      form.Controls.Add(errorLabel);
      Show(form, canvas, () =>
      {
        editor.Focus();
        editor.SelectAll();
      });
    }

    public static void ShowChoice(
      GH_Canvas canvas,
      RectangleF canvasBounds,
      string value,
      string guide,
      string[] choices,
      Func<string, string> validate,
      Action<string> accepted)
    {
      CloseActive();
      Form form = CreateHost(canvas, canvasBounds, 88);
      var guideLabel = CreateLabel(guide, Color.FromArgb(86, 103, 122), 7, 4, form.ClientSize.Width - 14, 20);
      var editor = new ComboBox
      {
        DropDownStyle = ComboBoxStyle.DropDownList,
        IntegralHeight = true,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
        Location = new Point(7, 26),
        Size = new Size(Math.Max(120, form.ClientSize.Width - 84), 25)
      };
      editor.Items.AddRange(choices ?? Array.Empty<string>());
      int selected = editor.FindStringExact(value ?? string.Empty);
      editor.SelectedIndex = selected >= 0 ? selected : -1;
      var ok = CreateButton("确定", form.ClientSize.Width - 71, 25, 64, 27);
      var errorLabel = CreateLabel(string.Empty, Color.FromArgb(190, 53, 53), 7, 56, form.ClientSize.Width - 14, 24);

      bool finished = false;
      Action<bool> finish = commit =>
      {
        if (finished) return;
        string result = Convert.ToString(editor.SelectedItem) ?? string.Empty;
        if (commit)
        {
          string error = validate?.Invoke(result);
          if (!string.IsNullOrWhiteSpace(error))
          {
            errorLabel.Text = error;
            editor.BackColor = Color.FromArgb(255, 244, 244);
            editor.Focus();
            editor.DroppedDown = true;
            return;
          }
          accepted?.Invoke(result);
        }
        finished = true;
        form.Close();
      };

      ok.Click += (sender, args) => finish(true);
      editor.SelectionChangeCommitted += (sender, args) =>
      {
        errorLabel.Text = string.Empty;
        editor.BackColor = Color.White;
      };
      editor.KeyDown += (sender, args) =>
      {
        if (args.KeyCode == Keys.Enter)
        {
          args.SuppressKeyPress = true;
          finish(true);
        }
        else if (args.KeyCode == Keys.Escape)
        {
          args.SuppressKeyPress = true;
          finish(false);
        }
      };
      form.Deactivate += (sender, args) => finish(false);
      form.FormClosed += (sender, args) => { if (ReferenceEquals(_active, form)) _active = null; };
      form.Controls.Add(guideLabel);
      form.Controls.Add(editor);
      form.Controls.Add(ok);
      form.Controls.Add(errorLabel);
      Show(form, canvas, () =>
      {
        editor.Focus();
        editor.DroppedDown = true;
      });
    }

    public static void CloseActive()
    {
      if (_active == null) return;
      try { _active.Close(); }
      catch { }
      _active = null;
    }

    private static Label CreateLabel(string text, Color color, int x, int y, int width, int height)
    {
      return new Label
      {
        AutoEllipsis = true,
        Font = new Font("Microsoft YaHei UI", 7.6f, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = color,
        Location = new Point(x, y),
        Size = new Size(Math.Max(40, width), height),
        Text = text ?? string.Empty,
        TextAlign = ContentAlignment.MiddleLeft
      };
    }

    private static Button CreateButton(string text, int x, int y, int width, int height)
    {
      return new Button
      {
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold, GraphicsUnit.Point),
        ForeColor = Color.FromArgb(31, 92, 166),
        BackColor = Color.FromArgb(235, 242, 251),
        Location = new Point(x, y),
        Size = new Size(width, height),
        Text = text
      };
    }

    private static Form CreateHost(GH_Canvas canvas, RectangleF canvasBounds, int height)
    {
      PointF topLeft = canvas.Viewport.ProjectPoint(new PointF(canvasBounds.Left, canvasBounds.Top));
      PointF bottomRight = canvas.Viewport.ProjectPoint(new PointF(canvasBounds.Right, canvasBounds.Bottom));
      Point screenPoint = canvas.PointToScreen(Point.Round(topLeft));
      int width = Math.Max(310, (int) Math.Round(bottomRight.X - topLeft.X));
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
