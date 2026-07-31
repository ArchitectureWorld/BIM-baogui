using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using BIMBaoGui.Stage01.Core;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace BIMBaoGui.Stage01.UI
{
  internal sealed class Stage01ComponentAttributes : GH_ComponentAttributes
  {
    private const float CardWidth = 790f;
    private const float CardHeight = 690f;
    private const float DirectoryWidth = 190f;
    private const float WorkspaceTop = 90f;
    private const float WorkspaceHeight = 478f;
    private const float WorkspaceGap = 12f;
    private const float FieldRowHeight = 42f;

    private static readonly Color Primary = Color.FromArgb(31, 92, 166);
    private static readonly Color PrimaryDark = Color.FromArgb(22, 68, 124);
    private static readonly Color Background = Color.FromArgb(242, 246, 251);
    private static readonly Color Surface = Color.White;
    private static readonly Color Border = Color.FromArgb(202, 213, 226);
    private static readonly Color Text = Color.FromArgb(31, 42, 55);
    private static readonly Color Muted = Color.FromArgb(102, 116, 133);
    private static readonly Color Success = Color.FromArgb(34, 139, 94);
    private static readonly Color Warning = Color.FromArgb(202, 124, 28);
    private static readonly Color Error = Color.FromArgb(190, 53, 53);
    private static readonly Color Required = Color.FromArgb(206, 62, 62);

    private readonly Stage01Component _owner;
    private readonly List<DirectoryHit> _directoryHits = new List<DirectoryHit>();
    private readonly List<FieldHit> _fieldHits = new List<FieldHit>();
    private readonly List<ConditionHit> _conditionHits = new List<ConditionHit>();

    private RectangleF _cardBounds;
    private RectangleF _directoryBounds;
    private RectangleF _contentBounds;
    private RectangleF _contentViewport;
    private RectangleF _scrollTrack;
    private RectangleF _scrollThumb;
    private RectangleF _previousOrganization;
    private RectangleF _nextOrganization;
    private RectangleF _addOrganization;
    private RectangleF _removeOrganization;
    private RectangleF _confirmBlank;
    private RectangleF _allowReinitialize;
    private RectangleF _showAllFields;
    private RectangleF _readButton;
    private RectangleF _validateButton;
    private RectangleF _commitButton;
    private RectangleF _resetButton;

    private GH_Canvas _hookedCanvas;
    private bool _scrollDragging;
    private float _scrollGrabOffset;
    private int _scrollItemCount;
    private int _visibleFieldCount;

    public Stage01ComponentAttributes(Stage01Component owner) : base(owner)
    {
      _owner = owner;
    }

    protected override void Layout()
    {
      RectangleF componentBox = LayoutComponentBox(Owner);
      componentBox.Width = CardWidth;
      componentBox.Height = CardHeight;
      _cardBounds = componentBox;
      Bounds = LayoutBounds(Owner, componentBox);
      LayoutOutputParams(Owner, componentBox);
    }

    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
      if (channel == GH_CanvasChannel.Wires)
      {
        base.Render(canvas, graphics, channel);
        return;
      }
      if (channel != GH_CanvasChannel.Objects) return;

      EnsureCanvasHook(canvas);
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      _directoryHits.Clear();
      _fieldHits.Clear();
      _conditionHits.Clear();
      ResetTransientBounds();

      DrawCard(graphics);
      DrawHeader(graphics);
      DrawWorkspace(graphics);
      DrawFooter(graphics);
      RenderComponentParameters(canvas, graphics, Owner, GH_Skin.palette_normal_standard);
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      if (e.Button != MouseButtons.Left)
        return base.RespondToMouseDown(sender, e);

      PointF point = e.CanvasLocation;
      foreach (DirectoryHit hit in _directoryHits)
      {
        if (!hit.Bounds.Contains(point)) continue;
        _owner.SetActiveGroup(hit.Group);
        return GH_ObjectResponse.Handled;
      }

      if (!_scrollThumb.IsEmpty && _scrollThumb.Contains(point))
      {
        _scrollDragging = true;
        _scrollGrabOffset = point.Y - _scrollThumb.Y;
        return GH_ObjectResponse.Capture;
      }
      if (!_scrollTrack.IsEmpty && _scrollTrack.Contains(point))
      {
        ScrollToThumbTop(point.Y - _scrollThumb.Height * 0.5f);
        return GH_ObjectResponse.Handled;
      }

      if (_previousOrganization.Contains(point)) { _owner.MoveOrganization(-1); return GH_ObjectResponse.Handled; }
      if (_nextOrganization.Contains(point)) { _owner.MoveOrganization(1); return GH_ObjectResponse.Handled; }
      if (_addOrganization.Contains(point)) { _owner.AddOrganization(); return GH_ObjectResponse.Handled; }
      if (_removeOrganization.Contains(point)) { _owner.RemoveCurrentOrganization(); return GH_ObjectResponse.Handled; }
      if (_confirmBlank.Contains(point)) { _owner.ToggleConfirmBlank(); return GH_ObjectResponse.Handled; }
      if (_allowReinitialize.Contains(point)) { _owner.ToggleAllowReinitialize(); return GH_ObjectResponse.Handled; }
      if (_showAllFields.Contains(point)) { _owner.ToggleShowAllFields(); return GH_ObjectResponse.Handled; }
      if (_readButton.Contains(point)) { _owner.ReadCurrentRevitFile(); return GH_ObjectResponse.Handled; }
      if (_validateButton.Contains(point)) { _owner.ValidateNow(); return GH_ObjectResponse.Handled; }
      if (_commitButton.Contains(point)) { _owner.CommitInitialization(); return GH_ObjectResponse.Handled; }
      if (_resetButton.Contains(point)) { _owner.ResetForm(); return GH_ObjectResponse.Handled; }

      foreach (ConditionHit hit in _conditionHits)
      {
        if (!hit.Bounds.Contains(point)) continue;
        _owner.ToggleCondition(hit.Key);
        return GH_ObjectResponse.Handled;
      }

      foreach (FieldHit hit in _fieldHits)
      {
        if (!hit.HitBounds.Contains(point)) continue;
        EditField(sender, hit.Definition, hit.EditorBounds);
        return GH_ObjectResponse.Handled;
      }

      return base.RespondToMouseDown(sender, e);
    }

    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      if (!_scrollDragging) return base.RespondToMouseMove(sender, e);
      ScrollToThumbTop(e.CanvasY - _scrollGrabOffset);
      return GH_ObjectResponse.Handled;
    }

    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      if (!_scrollDragging) return base.RespondToMouseUp(sender, e);
      _scrollDragging = false;
      return GH_ObjectResponse.Release;
    }

    private void EnsureCanvasHook(GH_Canvas canvas)
    {
      if (ReferenceEquals(_hookedCanvas, canvas)) return;
      if (_hookedCanvas != null) _hookedCanvas.MouseWheel -= CanvasMouseWheel;
      _hookedCanvas = canvas;
      if (_hookedCanvas != null) _hookedCanvas.MouseWheel += CanvasMouseWheel;
    }

    private void CanvasMouseWheel(object sender, MouseEventArgs e)
    {
      var canvas = sender as GH_Canvas;
      if (canvas == null) return;
      if (!ReferenceEquals(_owner.Attributes, this))
      {
        canvas.MouseWheel -= CanvasMouseWheel;
        if (ReferenceEquals(_hookedCanvas, canvas)) _hookedCanvas = null;
        return;
      }

      PointF canvasPoint = canvas.Viewport.UnprojectPoint(new PointF(e.X, e.Y));
      if (!_contentViewport.Contains(canvasPoint)) return;
      if (_visibleFieldCount <= 0 || _scrollItemCount <= _visibleFieldCount) return;

      _owner.ScrollFieldsByWheel(e.Delta, _visibleFieldCount);
      var handled = e as HandledMouseEventArgs;
      if (handled != null) handled.Handled = true;
      canvas.Invalidate();
    }

    private void ScrollToThumbTop(float requestedTop)
    {
      if (_scrollTrack.IsEmpty || _scrollThumb.IsEmpty || _visibleFieldCount <= 0) return;
      int maximumOffset = Math.Max(0, _scrollItemCount - _visibleFieldCount);
      if (maximumOffset == 0) return;
      float travel = Math.Max(1f, _scrollTrack.Height - _scrollThumb.Height);
      float clamped = Math.Max(_scrollTrack.Y, Math.Min(requestedTop, _scrollTrack.Bottom - _scrollThumb.Height));
      float ratio = (clamped - _scrollTrack.Y) / travel;
      int offset = (int) Math.Round(ratio * maximumOffset);
      _owner.SetScrollOffset(offset, _visibleFieldCount);
    }

    private void EditField(GH_Canvas canvas, FieldDefinition definition, RectangleF bounds)
    {
      if (!_owner.IsFieldEditable(definition)) return;
      if (definition.Kind == FieldKind.Boolean)
      {
        _owner.ToggleBooleanField(definition);
        return;
      }

      string current = _owner.GetFieldValue(definition);
      if (definition.Kind == FieldKind.Enum && definition.AllowedValues.Count > 0)
        InlineEditor.ShowChoice(canvas, bounds, current, definition.AllowedValues.ToArray(), value => _owner.SetFieldValue(definition, value));
      else
        InlineEditor.ShowText(canvas, bounds, current, value => _owner.SetFieldValue(definition, value));
    }

    private void ResetTransientBounds()
    {
      _scrollTrack = RectangleF.Empty;
      _scrollThumb = RectangleF.Empty;
      _previousOrganization = RectangleF.Empty;
      _nextOrganization = RectangleF.Empty;
      _addOrganization = RectangleF.Empty;
      _removeOrganization = RectangleF.Empty;
      _confirmBlank = RectangleF.Empty;
      _allowReinitialize = RectangleF.Empty;
      _showAllFields = RectangleF.Empty;
      _readButton = RectangleF.Empty;
      _validateButton = RectangleF.Empty;
      _commitButton = RectangleF.Empty;
      _resetButton = RectangleF.Empty;
      _scrollItemCount = 0;
      _visibleFieldCount = 0;
    }

    private void DrawCard(Graphics graphics)
    {
      using (GraphicsPath shadowPath = IconFactory.RoundedRectangle(new RectangleF(_cardBounds.X + 4, _cardBounds.Y + 5, _cardBounds.Width, _cardBounds.Height), 11))
      using (var shadow = new SolidBrush(Color.FromArgb(38, 20, 35, 55)))
        graphics.FillPath(shadow, shadowPath);
      using (GraphicsPath path = IconFactory.RoundedRectangle(_cardBounds, 11))
      using (var fill = new SolidBrush(Background))
      using (var pen = new Pen(Border, 1f))
      {
        graphics.FillPath(fill, path);
        graphics.DrawPath(pen, path);
      }
    }

    private void DrawHeader(Graphics graphics)
    {
      RectangleF header = new RectangleF(_cardBounds.X, _cardBounds.Y, _cardBounds.Width, 76f);
      using (GraphicsPath path = HeaderPath(header, 11f))
      using (var brush = new LinearGradientBrush(header, PrimaryDark, Primary, LinearGradientMode.Horizontal))
        graphics.FillPath(brush, path);

      using (var titleFont = new Font("Microsoft YaHei UI", 13.2f, FontStyle.Bold, GraphicsUnit.Point))
      using (var subFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
      using (var white = new SolidBrush(Color.White))
      using (var whiteMuted = new SolidBrush(Color.FromArgb(215, 235, 245, 255)))
      {
        graphics.DrawString("湖北BIM报规｜01 文件初始化", titleFont, white, _cardBounds.X + 20, _cardBounds.Y + 14);
        graphics.DrawString(BuildEnvironmentText(), subFont, whiteMuted, _cardBounds.X + 20, _cardBounds.Y + 45);
      }

      string status = _owner.CurrentStatus;
      Color statusColor = StatusColor(status);
      RectangleF pill = new RectangleF(_cardBounds.Right - 150, _cardBounds.Y + 18, 128, 31);
      FillRounded(graphics, pill, Color.FromArgb(238, Color.White), 15);
      using (var dot = new SolidBrush(statusColor)) graphics.FillEllipse(dot, pill.X + 11, pill.Y + 10, 10, 10);
      using (var statusFont = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Bold, GraphicsUnit.Point))
      using (var statusBrush = new SolidBrush(statusColor))
        DrawCentered(graphics, Compact(status, 10), statusFont, statusBrush, new RectangleF(pill.X + 26, pill.Y, pill.Width - 30, pill.Height));
    }

    private void DrawWorkspace(Graphics graphics)
    {
      _directoryBounds = new RectangleF(_cardBounds.X + 16, _cardBounds.Y + WorkspaceTop, DirectoryWidth, WorkspaceHeight);
      _contentBounds = new RectangleF(_directoryBounds.Right + WorkspaceGap, _directoryBounds.Y,
        _cardBounds.Right - 16 - (_directoryBounds.Right + WorkspaceGap), WorkspaceHeight);

      DrawDirectory(graphics, _directoryBounds);
      DrawContentPanel(graphics, _contentBounds);
    }

    private void DrawDirectory(Graphics graphics, RectangleF bounds)
    {
      FillRounded(graphics, bounds, Surface, 8);
      DrawBorder(graphics, bounds, Border, 8);

      using (var headingFont = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Bold, GraphicsUnit.Point))
      using (var headingBrush = new SolidBrush(Text))
        graphics.DrawString("初始化目录", headingFont, headingBrush, bounds.X + 14, bounds.Y + 12);

      IReadOnlyList<string> groups = _owner.GetVisibleGroups();
      float itemY = bounds.Y + 39;
      float itemHeight = 36f;
      float gap = 5f;
      using (var font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular, GraphicsUnit.Point))
      using (var selectedFont = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold, GraphicsUnit.Point))
      {
        foreach (string group in groups)
        {
          RectangleF item = new RectangleF(bounds.X + 8, itemY, bounds.Width - 16, itemHeight);
          bool selected = string.Equals(group, _owner.Model.ActiveGroup, StringComparison.Ordinal);
          bool required = _owner.GroupHasRequiredFields(group);
          int missing = _owner.GetMissingRequiredCount(group);
          Color fill = selected ? Primary : Color.FromArgb(248, 250, 253);
          Color border = selected ? Primary : Border;
          Color foreground = selected ? Color.White : Text;
          FillRounded(graphics, item, fill, 6);
          DrawBorder(graphics, item, border, 6);

          string prefix = group.Length >= 2 ? group.Substring(0, 2) : string.Empty;
          string label = prefix + "  " + Stage01UiPolicy.DecorateRequiredLabel(_owner.GetGroupDisplayName(group), required);
          using (var brush = new SolidBrush(foreground))
            DrawLeftCentered(graphics, label, selected ? selectedFont : font, brush,
              new RectangleF(item.X + 10, item.Y, item.Width - 42, item.Height));

          if (missing > 0)
          {
            RectangleF badge = new RectangleF(item.Right - 29, item.Y + 8, 20, 20);
            FillRounded(graphics, badge, selected ? Color.FromArgb(238, Color.White) : Color.FromArgb(255, 236, 236), 10);
            using (var badgeFont = new Font("Microsoft YaHei UI", 7.2f, FontStyle.Bold, GraphicsUnit.Point))
            using (var badgeBrush = new SolidBrush(selected ? Required : Error))
              DrawCentered(graphics, missing.ToString(), badgeFont, badgeBrush, badge);
          }
          else if (required)
          {
            using (var okBrush = new SolidBrush(selected ? Color.White : Success))
              graphics.FillEllipse(okBrush, item.Right - 20, item.Y + 14, 8, 8);
          }

          _directoryHits.Add(new DirectoryHit(group, item));
          itemY += itemHeight + gap;
        }
      }

      RectangleF legend = new RectangleF(bounds.X + 10, bounds.Bottom - 33, bounds.Width - 20, 23);
      using (var font = new Font("Microsoft YaHei UI", 7.2f, FontStyle.Regular, GraphicsUnit.Point))
      using (var requiredBrush = new SolidBrush(Required))
      using (var mutedBrush = new SolidBrush(Muted))
      {
        graphics.DrawString("* 必填目录", font, requiredBrush, legend.X, legend.Y + 4);
        graphics.DrawString("数字＝缺失必填项", font, mutedBrush, legend.X + 64, legend.Y + 4);
      }
    }

    private void DrawContentPanel(Graphics graphics, RectangleF bounds)
    {
      FillRounded(graphics, bounds, Surface, 8);
      DrawBorder(graphics, bounds, Border, 8);

      string group = _owner.Model.ActiveGroup;
      string title = _owner.GetGroupDisplayName(group);
      using (var titleFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold, GraphicsUnit.Point))
      using (var titleBrush = new SolidBrush(Text))
        graphics.DrawString(title, titleFont, titleBrush, bounds.X + 16, bounds.Y + 13);

      DrawTag(graphics, new RectangleF(bounds.Right - 174, bounds.Y + 12, 48, 22), "* 必填", Color.FromArgb(255, 238, 238), Required);
      DrawTag(graphics, new RectangleF(bounds.Right - 120, bounds.Y + 12, 44, 22), "选填", Color.FromArgb(240, 244, 249), Muted);
      DrawTag(graphics, new RectangleF(bounds.Right - 70, bounds.Y + 12, 54, 22), "系统", Color.FromArgb(232, 238, 245), Color.FromArgb(79, 98, 118));

      using (var pen = new Pen(Color.FromArgb(224, 230, 237), 1f))
        graphics.DrawLine(pen, bounds.X + 14, bounds.Y + 46, bounds.Right - 14, bounds.Y + 46);

      _contentViewport = new RectangleF(bounds.X + 14, bounds.Y + 56, bounds.Width - 28, bounds.Height - 70);
      switch (group)
      {
        case "10_项目条件": DrawConditions(graphics, _contentViewport); break;
        case "11_提交与校验": DrawExecution(graphics, _contentViewport); break;
        default: DrawFields(graphics, _contentViewport); break;
      }
    }

    private void DrawFields(Graphics graphics, RectangleF viewport)
    {
      IReadOnlyList<FieldDefinition> allFields = _owner.GetFieldsForActiveGroup();
      float top = viewport.Y;
      float availableHeight = viewport.Height;

      if (_owner.Model.ActiveGroup == "06_参建组织")
      {
        RectangleF orgBar = new RectangleF(viewport.X, top, viewport.Width - 12, 32);
        _previousOrganization = new RectangleF(orgBar.X, orgBar.Y + 2, 29, 27);
        _nextOrganization = new RectangleF(_previousOrganization.Right + 4, orgBar.Y + 2, 29, 27);
        _addOrganization = new RectangleF(orgBar.Right - 67, orgBar.Y + 2, 29, 27);
        _removeOrganization = new RectangleF(orgBar.Right - 33, orgBar.Y + 2, 29, 27);
        DrawSmallButton(graphics, _previousOrganization, "‹", false);
        DrawSmallButton(graphics, _nextOrganization, "›", false);
        DrawSmallButton(graphics, _addOrganization, "+", false);
        DrawSmallButton(graphics, _removeOrganization, "−", false);
        using (var font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold, GraphicsUnit.Point))
        using (var brush = new SolidBrush(Muted))
          DrawCentered(graphics, "参建单位 " + (_owner.Model.OrganizationIndex + 1) + " / " + _owner.Model.Organizations.Count,
            font, brush, new RectangleF(_nextOrganization.Right + 6, orgBar.Y, _addOrganization.Left - _nextOrganization.Right - 12, orgBar.Height));
        top += 39;
        availableHeight -= 39;
      }

      int visibleCount = Math.Max(1, (int) Math.Floor(availableHeight / FieldRowHeight));
      int offset = Stage01UiPolicy.ClampScrollOffset(_owner.Model.ScrollOffset, allFields.Count, visibleCount);
      IReadOnlyList<FieldDefinition> visible = allFields.Skip(offset).Take(visibleCount).ToList();
      _scrollItemCount = allFields.Count;
      _visibleFieldCount = visibleCount;

      if (visible.Count == 0)
      {
        using (var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
        using (var brush = new SolidBrush(Muted))
          DrawCentered(graphics, "当前目录没有需要填写的字段。", font, brush, viewport);
        return;
      }

      GraphicsState state = graphics.Save();
      graphics.SetClip(new RectangleF(viewport.X, top, viewport.Width - 12, availableHeight));
      float labelWidth = 188f;
      using (var labelFont = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular, GraphicsUnit.Point))
      using (var labelRequiredFont = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold, GraphicsUnit.Point))
      using (var valueFont = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Regular, GraphicsUnit.Point))
      {
        for (int index = 0; index < visible.Count; ++index)
        {
          FieldDefinition definition = visible[index];
          bool editable = _owner.IsFieldEditable(definition);
          bool required = definition.Essential && !definition.Deferred;
          string value = _owner.GetFieldValue(definition);
          RectangleF row = new RectangleF(viewport.X, top + index * FieldRowHeight, viewport.Width - 14, FieldRowHeight - 5);
          Color rowFill = index % 2 == 0 ? Color.FromArgb(250, 252, 254) : Color.FromArgb(246, 249, 252);
          FillRounded(graphics, row, rowFill, 5);

          RectangleF labelRect = new RectangleF(row.X + 8, row.Y, labelWidth - 14, row.Height);
          string decorated = Stage01UiPolicy.DecorateRequiredLabel(definition.Label, required);
          using (var labelBrush = new SolidBrush(definition.Deferred ? Color.FromArgb(155, Muted) : Text))
            DrawLeftCentered(graphics, decorated, required ? labelRequiredFont : labelFont, labelBrush, labelRect);

          RectangleF typeTag = new RectangleF(row.X + labelWidth - 43, row.Y + 8, 38, 21);
          if (!editable)
            DrawTag(graphics, typeTag, "系统", Color.FromArgb(232, 238, 245), Color.FromArgb(79, 98, 118));
          else if (required)
            DrawTag(graphics, typeTag, "必填", Color.FromArgb(255, 238, 238), Required);
          else
            DrawTag(graphics, typeTag, "选填", Color.FromArgb(240, 244, 249), Muted);

          RectangleF valueRect = new RectangleF(row.X + labelWidth, row.Y + 5, row.Width - labelWidth - 7, row.Height - 10);
          bool missingRequired = editable && required && string.IsNullOrWhiteSpace(value);
          Color valueBackground = editable ? Color.White : Color.FromArgb(235, 240, 246);
          Color valueBorder = !editable ? Color.FromArgb(214, 222, 231) : (missingRequired ? Color.FromArgb(226, 139, 139) : Color.FromArgb(184, 199, 215));
          Color valueColor = string.IsNullOrWhiteSpace(value) ? Color.FromArgb(145, 158, 170) : Text;
          FillRounded(graphics, valueRect, valueBackground, 5);
          DrawBorder(graphics, valueRect, valueBorder, 5);

          string display;
          if (!string.IsNullOrWhiteSpace(value))
            display = definition.Kind == FieldKind.Boolean ? (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ? "是" : "否") : Compact(value, 38);
          else
            display = editable ? "点击填写" : "由系统读取／生成";
          using (var valueBrush = new SolidBrush(valueColor))
            DrawLeftCentered(graphics, display, valueFont, valueBrush,
              new RectangleF(valueRect.X + 8, valueRect.Y, valueRect.Width - 28, valueRect.Height));

          if (editable)
          {
            using (var iconFont = new Font("Segoe UI Symbol", 7.4f, FontStyle.Regular, GraphicsUnit.Point))
            using (var iconBrush = new SolidBrush(PrimaryDark))
              DrawCentered(graphics, definition.Kind == FieldKind.Enum ? "▾" : "✎", iconFont, iconBrush,
                new RectangleF(valueRect.Right - 22, valueRect.Y, 18, valueRect.Height));
            _fieldHits.Add(new FieldHit(definition, row, valueRect));
          }
          else
          {
            using (var lockFont = new Font("Segoe UI Symbol", 7.2f, FontStyle.Regular, GraphicsUnit.Point))
            using (var lockBrush = new SolidBrush(Color.FromArgb(105, 121, 137)))
              DrawCentered(graphics, "●", lockFont, lockBrush,
                new RectangleF(valueRect.Right - 21, valueRect.Y, 17, valueRect.Height));
          }
        }
      }
      graphics.Restore(state);
      DrawScrollBar(graphics, new RectangleF(viewport.Right - 8, top, 7, availableHeight), allFields.Count, visibleCount, offset);
    }

    private void DrawScrollBar(Graphics graphics, RectangleF track, int itemCount, int visibleCount, int offset)
    {
      _scrollTrack = RectangleF.Empty;
      _scrollThumb = RectangleF.Empty;
      if (itemCount <= visibleCount || visibleCount <= 0) return;

      _scrollTrack = track;
      FillRounded(graphics, track, Color.FromArgb(232, 237, 243), 3.5f);
      int maximumOffset = Math.Max(1, itemCount - visibleCount);
      float thumbHeight = Math.Max(36f, track.Height * visibleCount / (float) itemCount);
      float travel = Math.Max(1f, track.Height - thumbHeight);
      float thumbY = track.Y + travel * Stage01UiPolicy.ClampScrollOffset(offset, itemCount, visibleCount) / maximumOffset;
      _scrollThumb = new RectangleF(track.X, thumbY, track.Width, thumbHeight);
      FillRounded(graphics, _scrollThumb, Color.FromArgb(125, 151, 180), 3.5f);
    }

    private void DrawConditions(Graphics graphics, RectangleF viewport)
    {
      float columnGap = 10f;
      float columnWidth = (viewport.Width - columnGap) / 2f;
      using (var font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
      using (var textBrush = new SolidBrush(Text))
      {
        for (int index = 0; index < _owner.Registry.Conditions.Count; ++index)
        {
          ConditionDefinition condition = _owner.Registry.Conditions[index];
          int column = index % 2;
          int row = index / 2;
          RectangleF bounds = new RectangleF(viewport.X + column * (columnWidth + columnGap), viewport.Y + row * 48, columnWidth, 37);
          FillRounded(graphics, bounds, Color.FromArgb(249, 251, 253), 5);
          DrawBorder(graphics, bounds, Border, 5);
          DrawCheckbox(graphics, new RectangleF(bounds.X + 10, bounds.Y + 10, 17, 17), _owner.Model.GetCondition(condition.Key));
          DrawLeftCentered(graphics, condition.Label, font, textBrush, new RectangleF(bounds.X + 36, bounds.Y, bounds.Width - 43, bounds.Height));
          _conditionHits.Add(new ConditionHit(condition.Key, bounds));
        }
      }

      using (var noteFont = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Regular, GraphicsUnit.Point))
      using (var noteBrush = new SolidBrush(Muted))
        graphics.DrawString("项目条件均为按实际情况选择；未勾选即表示当前文件不涉及该对象。", noteFont, noteBrush,
          new RectangleF(viewport.X + 2, viewport.Bottom - 48, viewport.Width - 4, 34));
    }

    private void DrawExecution(Graphics graphics, RectangleF viewport)
    {
      float x = viewport.X;
      float y = viewport.Y;
      _confirmBlank = new RectangleF(x, y, viewport.Width, 34);
      _allowReinitialize = new RectangleF(x, y + 42, viewport.Width, 34);
      _showAllFields = new RectangleF(x, y + 84, viewport.Width, 34);
      DrawToggleRow(graphics, _confirmBlank, "确认当前文件尚未开始正式建模（允许 Revit 模板默认内容）", _owner.Model.ConfirmBlankProject, true);
      DrawToggleRow(graphics, _allowReinitialize, "允许覆盖当前文件已有的初始化记录", _owner.Model.AllowReinitialize, false);
      DrawToggleRow(graphics, _showAllFields, "显示后续阶段只读／延期字段", _owner.Model.ShowAllFields, false);

      float buttonY = y + 137;
      float gap = 8f;
      float buttonWidth = (viewport.Width - gap * 3) / 4f;
      _readButton = new RectangleF(x, buttonY, buttonWidth, 39);
      _validateButton = new RectangleF(_readButton.Right + gap, buttonY, buttonWidth, 39);
      _commitButton = new RectangleF(_validateButton.Right + gap, buttonY, buttonWidth, 39);
      _resetButton = new RectangleF(_commitButton.Right + gap, buttonY, buttonWidth, 39);
      DrawActionButton(graphics, _readButton, "读取文件", Color.FromArgb(232, 240, 251), PrimaryDark, false);
      DrawActionButton(graphics, _validateButton, "执行校验", Color.FromArgb(235, 242, 247), Color.FromArgb(53, 74, 94), false);
      DrawActionButton(graphics, _commitButton, _owner.IsCommitting ? "提交中…" : "写入并回读", Primary, Color.White, _owner.IsCommitting);
      DrawActionButton(graphics, _resetButton, "重置表单", Color.FromArgb(247, 238, 238), Error, false);

      RectangleF summary = new RectangleF(x, buttonY + 53, viewport.Width, 105);
      FillRounded(graphics, summary, Color.FromArgb(246, 249, 252), 6);
      DrawBorder(graphics, summary, Color.FromArgb(222, 229, 237), 6);
      using (var font = new Font("Microsoft YaHei UI", 8.1f, FontStyle.Regular, GraphicsUnit.Point))
      using (var brush = new SolidBrush(Text))
        graphics.DrawString(BuildValidationSummary(), font, brush,
          new RectangleF(summary.X + 12, summary.Y + 10, summary.Width - 24, summary.Height - 20));
    }

    private void DrawFooter(Graphics graphics)
    {
      RectangleF messageBox = new RectangleF(_cardBounds.X + 16, _cardBounds.Y + 580, _cardBounds.Width - 32, 88);
      FillRounded(graphics, messageBox, Surface, 8);
      DrawBorder(graphics, messageBox, Border, 8);

      using (var headingFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point))
      using (var bodyFont = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Regular, GraphicsUnit.Point))
      using (var headingBrush = new SolidBrush(StatusColor(_owner.CurrentStatus)))
      using (var bodyBrush = new SolidBrush(Muted))
      {
        graphics.DrawString("状态｜" + _owner.CurrentStatus, headingFont, headingBrush, messageBox.X + 12, messageBox.Y + 9);
        string message = string.Join("\n", _owner.CurrentMessages.Take(3).Select(text => "• " + Compact(text, 104)));
        graphics.DrawString(message, bodyFont, bodyBrush,
          new RectangleF(messageBox.X + 12, messageBox.Y + 31, messageBox.Width - 24, messageBox.Height - 35));
      }

      using (var versionFont = new Font("Microsoft YaHei UI", 7f, FontStyle.Regular, GraphicsUnit.Point))
      using (var versionBrush = new SolidBrush(Color.FromArgb(145, 155, 165)))
        graphics.DrawString("Revit 2020 · Rhino 8 · BIMBaoGui Stage 01 v0.3.0", versionFont, versionBrush,
          _cardBounds.X + 18, _cardBounds.Bottom - 15);
    }

    private string BuildEnvironmentText()
    {
      if (!_owner.Snapshot.HostAvailable) return "等待 Rhino.Inside.Revit 活动文档";
      string title = string.IsNullOrWhiteSpace(_owner.Snapshot.DocumentTitle) ? "未命名文件" : _owner.Snapshot.DocumentTitle;
      return "Revit " + _owner.Snapshot.RevitVersion + " · " + Compact(title, 48);
    }

    private string BuildValidationSummary()
    {
      int errors = _owner.Validation?.ErrorCount ?? 0;
      int warnings = _owner.Validation?.WarningCount ?? 0;
      string environment = _owner.Snapshot.HostAvailable
        ? (_owner.Snapshot.Messages.Count == 0 ? "环境检查：通过" : "环境检查：" + _owner.Snapshot.Messages.Count + " 项阻断")
        : "环境检查：未连接 Revit";
      string modelGate = _owner.Snapshot.HostAvailable
        ? (_owner.Snapshot.IsBlank || _owner.Snapshot.IsInitialized ? "实质模型门禁：通过（模板默认内容不计）" : "实质模型门禁：未通过")
        : "实质模型门禁：等待读取";
      return environment + "\n" + modelGate + "\n字段校验：" + errors + " 个错误，" + warnings + " 个警告";
    }

    private static void DrawToggleRow(Graphics graphics, RectangleF bounds, string label, bool value, bool important)
    {
      FillRounded(graphics, bounds, important && !value ? Color.FromArgb(255, 247, 235) : Color.FromArgb(249, 251, 253), 5);
      DrawBorder(graphics, bounds, important && !value ? Color.FromArgb(231, 177, 102) : Border, 5);
      DrawCheckbox(graphics, new RectangleF(bounds.X + 9, bounds.Y + 8, 18, 18), value);
      using (var font = new Font("Microsoft YaHei UI", 8.3f, important ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point))
      using (var brush = new SolidBrush(Text))
        DrawLeftCentered(graphics, label, font, brush, new RectangleF(bounds.X + 36, bounds.Y, bounds.Width - 42, bounds.Height));
    }

    private static void DrawCheckbox(Graphics graphics, RectangleF bounds, bool value)
    {
      FillRounded(graphics, bounds, value ? Primary : Color.White, 3);
      DrawBorder(graphics, bounds, value ? Primary : Color.FromArgb(160, 176, 191), 3);
      if (!value) return;
      using (var pen = new Pen(Color.White, 1.8f))
      {
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        graphics.DrawLines(pen, new[]
        {
          new PointF(bounds.X + 4, bounds.Y + 9),
          new PointF(bounds.X + 7.5f, bounds.Y + 12.5f),
          new PointF(bounds.X + 14, bounds.Y + 5.5f)
        });
      }
    }

    private static void DrawTag(Graphics graphics, RectangleF bounds, string label, Color background, Color foreground)
    {
      FillRounded(graphics, bounds, background, bounds.Height * 0.5f);
      using (var font = new Font("Microsoft YaHei UI", 7.1f, FontStyle.Bold, GraphicsUnit.Point))
      using (var brush = new SolidBrush(foreground))
        DrawCentered(graphics, label, font, brush, bounds);
    }

    private static void DrawActionButton(Graphics graphics, RectangleF bounds, string label, Color background, Color foreground, bool disabled)
    {
      Color actualBackground = disabled ? Color.FromArgb(205, 214, 224) : background;
      Color actualForeground = disabled ? Color.FromArgb(125, 137, 149) : foreground;
      FillRounded(graphics, bounds, actualBackground, 6);
      DrawBorder(graphics, bounds, Color.FromArgb(180, actualBackground), 6);
      using (var font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold, GraphicsUnit.Point))
      using (var brush = new SolidBrush(actualForeground))
        DrawCentered(graphics, label, font, brush, bounds);
    }

    private static void DrawSmallButton(Graphics graphics, RectangleF bounds, string label, bool disabled)
    {
      Color fill = disabled ? Color.FromArgb(241, 244, 247) : Color.FromArgb(235, 241, 248);
      Color text = disabled ? Color.FromArgb(185, 193, 201) : PrimaryDark;
      FillRounded(graphics, bounds, fill, 5);
      DrawBorder(graphics, bounds, Color.FromArgb(207, 216, 225), 5);
      using (var font = new Font("Segoe UI Symbol", 11f, FontStyle.Bold, GraphicsUnit.Point))
      using (var brush = new SolidBrush(text))
        DrawCentered(graphics, label, font, brush, bounds);
    }

    private static GraphicsPath HeaderPath(RectangleF bounds, float radius)
    {
      float diameter = radius * 2;
      var path = new GraphicsPath();
      path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
      path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
      path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
      path.CloseFigure();
      return path;
    }

    private static void FillRounded(Graphics graphics, RectangleF bounds, Color color, float radius)
    {
      if (bounds.IsEmpty) return;
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var brush = new SolidBrush(color))
        graphics.FillPath(brush, path);
    }

    private static void DrawBorder(Graphics graphics, RectangleF bounds, Color color, float radius)
    {
      if (bounds.IsEmpty) return;
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var pen = new Pen(color, 1f))
        graphics.DrawPath(pen, path);
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
    {
      using (var format = new StringFormat
      {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter
      })
        graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
    }

    private static void DrawLeftCentered(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
    {
      using (var format = new StringFormat
      {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
      })
        graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
    }

    private static Color StatusColor(string status)
    {
      if (status == "初始化通过") return Success;
      if (status == "输入未完成" || status == "待提交" || status == "已修改待重新提交") return Warning;
      if (status == "提交中") return Primary;
      return Error;
    }

    private static string Compact(string value, int maximum)
    {
      string normalized = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
      return normalized.Length <= maximum ? normalized : normalized.Substring(0, Math.Max(0, maximum - 1)) + "…";
    }

    private sealed class DirectoryHit
    {
      public DirectoryHit(string group, RectangleF bounds) { Group = group; Bounds = bounds; }
      public string Group { get; }
      public RectangleF Bounds { get; }
    }

    private sealed class FieldHit
    {
      public FieldHit(FieldDefinition definition, RectangleF hitBounds, RectangleF editorBounds)
      {
        Definition = definition;
        HitBounds = hitBounds;
        EditorBounds = editorBounds;
      }
      public FieldDefinition Definition { get; }
      public RectangleF HitBounds { get; }
      public RectangleF EditorBounds { get; }
    }

    private sealed class ConditionHit
    {
      public ConditionHit(string key, RectangleF bounds) { Key = key; Bounds = bounds; }
      public string Key { get; }
      public RectangleF Bounds { get; }
    }
  }
}
