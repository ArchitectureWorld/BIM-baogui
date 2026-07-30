using System.Drawing;
using System.Drawing.Drawing2D;

namespace BIMBaoGui.Stage01.UI
{
  internal static class IconFactory
  {
    public static Bitmap CreateComponentIcon()
    {
      var bitmap = new Bitmap(24, 24);
      using (Graphics graphics = Graphics.FromImage(bitmap))
      {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using (var background = new SolidBrush(Color.FromArgb(31, 92, 166)))
          graphics.FillRoundedRectangle(background, new RectangleF(1, 1, 22, 22), 5);
        using (var white = new Pen(Color.White, 1.6f))
        {
          graphics.DrawLine(white, 6, 7, 18, 7);
          graphics.DrawLine(white, 6, 12, 14, 12);
          graphics.DrawLine(white, 6, 17, 12, 17);
          graphics.DrawEllipse(white, 15, 14, 4, 4);
        }
      }
      return bitmap;
    }

    private static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
      using (GraphicsPath path = RoundedRectangle(bounds, radius))
        graphics.FillPath(brush, path);
    }

    internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
      float diameter = radius * 2f;
      var path = new GraphicsPath();
      path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
      path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
      path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
      path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
      path.CloseFigure();
      return path;
    }
  }
}
