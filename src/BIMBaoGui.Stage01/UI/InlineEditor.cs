using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI.Canvas;

namespace BIMBaoGui.Stage01.UI
{
  internal static class InlineEditor
  {
    private static Form _active;

    public static void ShowText(GH_Canvas canvas, RectangleF canvasBounds, string value, Action<string> accepted)
    {
      CloseActive();
      var form = CreateHost(canvas, canvasBounds);
      var editor = new TextBox
      {
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Fill,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
        Text = value ?? string.Empty,
        BackColor = Color.White,
        ForeColor = Color.FromArgb(32, 42, 56)
      };
      bool finished = false;
      Action<bool> finish = commit =>
      {
        if (finished) return;
        finished = true;
        string result = editor.Text;
        form.Close();
        if (commit) accepted?.Invoke(result);
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
      form.Deactivate += (sender, args) => finish(true);
      form.FormClosed += (sender, args) => { if (ReferenceEquals(_active, form)) _active = null; };
      form.Controls.Add(editor);
      Show(form, canvas, () =>
      {
        editor.Focus();
        editor.SelectAll();
      });
    }

    public static void ShowChoice(GH_Canvas canvas, RectangleF canvasBounds, string value, string[] choices, Action<string> accepted)
    {
      CloseActive();
      var form = CreateHost(canvas, canvasBounds);
      var editor = new ComboBox
      {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        IntegralHeight = true,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point)
      };
      editor.Items.AddRange(choices ?? Array.Empty<string>());
      int selected = editor.FindStringExact(value ?? string.Empty);
      editor.SelectedIndex = selected >= 0 ? selected : (editor.Items.Count > 0 ? 0 : -1);
      bool finished = false;
      Action<bool> finish = commit =>
      {
        if (finished) return;
        finished = true;
        string result = Convert.ToString(editor.SelectedItem) ?? string.Empty;
        form.Close();
        if (commit) accepted?.Invoke(result);
      };
      editor.SelectionChangeCommitted += (sender, args) => finish(true);
      editor.KeyDown += (sender, args) =>
      {
        if (args.KeyCode == Keys.Escape)
        {
          args.SuppressKeyPress = true;
          finish(false);
        }
      };
      form.Deactivate += (sender, args) => finish(true);
      form.FormClosed += (sender, args) => { if (ReferenceEquals(_active, form)) _active = null; };
      form.Controls.Add(editor);
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

    private static Form CreateHost(GH_Canvas canvas, RectangleF canvasBounds)
    {
      PointF topLeft = canvas.Viewport.ProjectPoint(new PointF(canvasBounds.Left, canvasBounds.Top));
      PointF bottomRight = canvas.Viewport.ProjectPoint(new PointF(canvasBounds.Right, canvasBounds.Bottom));
      Point screenPoint = canvas.PointToScreen(Point.Round(topLeft));
      int width = Math.Max(150, (int) Math.Round(bottomRight.X - topLeft.X));
      int height = Math.Max(26, (int) Math.Round(bottomRight.Y - topLeft.Y));
      return new Form
      {
        FormBorderStyle = FormBorderStyle.None,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = screenPoint,
        ClientSize = new Size(width, height),
        TopMost = true,
        MinimizeBox = false,
        MaximizeBox = false
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
