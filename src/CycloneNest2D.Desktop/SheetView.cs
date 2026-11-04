using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CycloneNest2D.Core;
using CycloneNest2D.UI;
using Rect = System.Windows.Rect;

namespace CycloneNest2D.Desktop;

/// <summary>
/// Vẽ một tấm và các chi tiết đã đặt. Lăn chuột = phóng to/thu nhỏ; kéo chuột giữa/phải hoặc kéo nền = di chuyển khung nhìn;
/// kéo chi tiết = dời (đỏ nếu vị trí không hợp lệ; vẫn giữ vị trí khi thả); nhấp đúp hoặc phím R = xoay 90°;
/// phím mũi tên = đẩy tới vật cản/mép tấm (Shift = nhích 1 mm); R xoay, M lật; Esc = huỷ thao tác kéo.
/// </summary>
public sealed class SheetView : FrameworkElement
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(PreviewScene), typeof(SheetView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSceneChanged));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(SheetView),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(MainViewModel), typeof(SheetView),
        new PropertyMetadata(null, OnModelChanged));

    public PreviewScene? Scene { get => (PreviewScene?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public MainViewModel? Model { get => (MainViewModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public double ZoomPercent => _zoom * 100;
    public event EventHandler? ZoomChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? DeleteSelectedPlacementsRequested;
    public IReadOnlyCollection<int> SelectedIndices => _selectedIndices;
    public void ClearSelection() => SetSelection(Array.Empty<int>());
    public bool PanMode
    {
        get => _panMode;
        set
        {
            _panMode = value;
            if (value) _zoomRectMode = false;
            Cursor = _zoomRectMode ? Cursors.Cross : value ? Cursors.Hand : Cursors.Arrow;
        }
    }
    public bool ZoomRectMode
    {
        get => _zoomRectMode;
        set
        {
            _zoomRectMode = value;
            if (value) _panMode = false;
            Cursor = value ? Cursors.Cross : _panMode ? Cursors.Hand : Cursors.Arrow;
        }
    }

    private double _zoom = 1, _ox, _oy;
    private bool _autoFit = true;
    private bool _panMode;
    private bool _zoomRectMode, _zoomRectActive;
    private Point _zoomRectStart, _zoomRectEnd;
    private bool _selectionBoxActive, _selectionBoxAdditive;
    private Point _selectionBoxStart, _selectionBoxEnd;
    private double _lastW = -1, _lastH = -1;
    private readonly Dictionary<int, Geometry> _geo = new();
    private readonly HashSet<int> _selectedIndices = new();
    private static readonly Brush[] Fills = BuildFills();

    private int _hover = -1;
    private Point _mouse;

    private int _dragIdx = -1;
    private Vector _grab;
    private Point _dragPos;
    private Point _dragOrigin;
    private Point _downScreen;
    private bool _dragMoved;
    private string? _dragError;
    private long _lastValidationTimestamp;
    private Point _lastValidatedPosition;
    private PartItem? _dropPreviewSource;
    private PreviewPart? _dropPreviewPart;
    private Geometry? _dropPreviewGeometry;
    private Point _dropPreviewCenter;
    private string? _dropPreviewError;

    private bool _panning;
    private Point _panStart, _panOrigin;

    public SheetView()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        SizeChanged += (_, _) => { if (_autoFit) Fit(); };
    }

    private static Brush[] BuildFills()
    {
        var list = new List<Brush>();
        foreach (var hex in PreviewScene.Palette)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(Color.FromArgb(0xB4, c.R, c.G, c.B));
            b.Freeze();
            list.Add(b);
        }
        return list.ToArray();
    }

    // ---------- thay đổi dữ liệu ----------

    private static void OnSceneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SheetView)d).SceneChanged();

    private void SceneChanged()
    {
        _geo.Clear();
        var s = Scene;
        if (s != null)
            foreach (var p in s.Parts) _geo[p.Index] = BuildGeometry(p);
        _hover = -1;
        CancelDrag();
        SyncSelectionFromModel();
        if (s != null && (s.SheetWidth != _lastW || s.SheetHeight != _lastH))
        {
            _lastW = s.SheetWidth;
            _lastH = s.SheetHeight;
            _autoFit = true;
            Fit();
        }
        InvalidateVisual();
    }

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SheetView)d;
        if (e.OldValue is MainViewModel o) o.ResetViewRequested -= v.OnResetView;
        if (e.NewValue is MainViewModel n) n.ResetViewRequested += v.OnResetView;
    }

    private void OnResetView()
    {
        _autoFit = true;
        Fit();
    }

    private void SyncSelectionFromModel()
    {
        _selectedIndices.Clear();
        if (Model != null)
            foreach (int index in Model.SelectedPlacementIndices) _selectedIndices.Add(index);
        if (_selectedIndices.Count == 0 && SelectedIndex >= 0) _selectedIndices.Add(SelectedIndex);
    }

    private void SetSelection(IEnumerable<int> indices, int primary = -1)
    {
        _selectedIndices.Clear();
        foreach (int index in indices) _selectedIndices.Add(index);
        if (primary < 0 || !_selectedIndices.Contains(primary)) primary = _selectedIndices.Order().FirstOrDefault(-1);
        SelectedIndex = primary;
        Model?.SetSelectedPlacements(_selectedIndices, primary);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private static Geometry BuildGeometry(PreviewPart p)
    {
        var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = g.Open())
        {
            foreach (var loop in p.Loops)
            {
                if (loop.Length < 3) continue;
                ctx.BeginFigure(new Point(loop[0].X, loop[0].Y), true, true);
                var pts = new List<Point>(loop.Length - 1);
                for (int i = 1; i < loop.Length; i++) pts.Add(new Point(loop[i].X, loop[i].Y));
                ctx.PolyLineTo(pts, true, false);
            }
        }
        g.Freeze();
        return g;
    }

    // ---------- khung nhìn ----------

    private Point ToWorld(Point screen) =>
        new((screen.X - _ox) / _zoom, (Scene?.SheetHeight ?? 0) - (screen.Y - _oy) / _zoom);

    public Point WorldFromControl(Point point) => ToWorld(point);

    public void SetDropPreview(PartItem? part, Point center, Point controlPosition, string? error)
    {
        if (part == null)
        {
            _dropPreviewSource = null;
            _dropPreviewPart = null;
            _dropPreviewGeometry = null;
            _dropPreviewError = null;
            InvalidateVisual();
            return;
        }

        if (!ReferenceEquals(part, _dropPreviewSource))
        {
            _dropPreviewSource = part;
            _dropPreviewPart = null;
            _dropPreviewGeometry = null;
            if (part.IsValid)
            {
                var definition = part.ToDefinition();
                var placement = new Placement(part.Name, 1, 0, 0, definition.Width, definition.Height, false, definition.Shape);
                var sheet = new SheetResult { Index = 1, Width = definition.Width, Height = definition.Height };
                sheet.Placements.Add(placement);
                var preview = PreviewBuilder.Build(sheet,
                    new SheetDefinition(definition.Width, definition.Height, 0, 0)).Parts.Single();
                _dropPreviewPart = preview;
                _dropPreviewGeometry = BuildGeometry(preview);
            }
        }

        _dropPreviewCenter = center;
        _dropPreviewError = error;
        _mouse = controlPosition;
        InvalidateVisual();
    }

    public void Fit()
    {
        var s = Scene;
        if (s == null || ActualWidth < 50 || ActualHeight < 50 || s.SheetWidth <= 0 || s.SheetHeight <= 0) return;
        const double pad = 46;
        _zoom = Math.Min((ActualWidth - 2 * pad) / s.SheetWidth, (ActualHeight - 2 * pad) / s.SheetHeight);
        _ox = (ActualWidth - s.SheetWidth * _zoom) / 2;
        _oy = (ActualHeight - s.SheetHeight * _zoom) / 2;
        _autoFit = true;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomToFit()
    {
        _autoFit = true;
        Fit();
    }

    public void ZoomIn() => ZoomAbout(new Point(ActualWidth / 2, ActualHeight / 2), 1.2);

    public void ZoomOut() => ZoomAbout(new Point(ActualWidth / 2, ActualHeight / 2), 1 / 1.2);

    public void SetZoomPercent(double percent) => ZoomAbout(new Point(ActualWidth / 2, ActualHeight / 2), (percent / 100) / _zoom);

    private void ZoomAbout(Point point, double factor)
    {
        double next = Math.Clamp(_zoom * factor, 0.01, 60);
        double ratio = next / _zoom;
        _ox = point.X - (point.X - _ox) * ratio;
        _oy = point.Y - (point.Y - _oy) * ratio;
        _zoom = next;
        _autoFit = false;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------- vẽ ----------

    private void DrawCanvasGrid(DrawingContext dc)
    {
        dc.DrawRectangle(ThemePalette.Brush("CanvasBgBrush", Color.FromRgb(0xE6, 0xED, 0xF5)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var minor = ThemePalette.Pen("CanvasGridMinorBrush", Color.FromArgb(0x10, 0, 0, 0), 1);
        var major = ThemePalette.Pen("CanvasGridMajorBrush", Color.FromArgb(0x18, 0, 0, 0), 1);
        minor.Freeze(); major.Freeze();
        const double step = 24;
        int nx = (int)Math.Ceiling(ActualWidth / step), ny = (int)Math.Ceiling(ActualHeight / step);
        for (int i = 0; i <= nx; i++)
        {
            double x = i * step;
            dc.DrawLine(i % 5 == 0 ? major : minor, new Point(x, 0), new Point(x, ActualHeight));
        }
        for (int i = 0; i <= ny; i++)
        {
            double y = i * step;
            dc.DrawLine(i % 5 == 0 ? major : minor, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    private void DrawRulers(DrawingContext dc, PreviewScene scene)
    {
        var pen = ThemePalette.Pen("CanvasTextBrush", Color.FromRgb(0x94, 0xA0, 0xAE), 1);
        pen.Freeze();
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Text(string value) => new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            ThemePalette.AppTypeface, 11, ThemePalette.Brush("CanvasTextBrush", Color.FromRgb(0x5F, 0x6D, 0x7E)), dpi);

        bool endpointHasLabel = Math.Abs(scene.SheetWidth % 500) < 1e-6;
        var unit = Text("mm");
        var unitBounds = new Rect(
            Math.Max(2, _ox + scene.SheetWidth * _zoom - unit.Width),
            Math.Max(2, _oy - unit.Height - 11),
            unit.Width,
            unit.Height);

        for (double mm = 0; mm <= scene.SheetWidth + 1e-6; mm += 100)
        {
            double x = _ox + mm * _zoom;
            if (x < 0 || x > ActualWidth) continue;
            bool label = Math.Abs(mm % 500) < 1e-6;
            dc.DrawLine(pen, new Point(x, _oy - (label ? 9 : 4)), new Point(x, _oy));
            if (label)
            {
                bool isEndpoint = Math.Abs(mm - scene.SheetWidth) < 1e-6;
                var ft = Text(mm.ToString("0", CultureInfo.CurrentUICulture) +
                              (isEndpoint ? " mm" : ""));
                double tx = Math.Clamp(x - ft.Width / 2, 2, Math.Max(2, ActualWidth - ft.Width - 2));
                var labelBounds = new Rect(tx, Math.Max(2, _oy - ft.Height - 11), ft.Width, ft.Height);
                if (!isEndpoint && labelBounds.IntersectsWith(unitBounds)) continue;
                dc.DrawText(ft, new Point(tx, Math.Max(2, _oy - ft.Height - 11)));
            }
        }

        for (double mm = 0; mm <= scene.SheetHeight + 1e-6; mm += 100)
        {
            double y = _oy + (scene.SheetHeight - mm) * _zoom;
            if (y < 0 || y > ActualHeight) continue;
            bool label = Math.Abs(mm % 500) < 1e-6;
            dc.DrawLine(pen, new Point(_ox - (label ? 9 : 4), y), new Point(_ox, y));
            if (label)
            {
                var ft = Text(mm.ToString("0", CultureInfo.CurrentUICulture));
                double tx = Math.Max(2, _ox - ft.Width - 12);
                double ty = Math.Clamp(y - ft.Height / 2, 2, Math.Max(2, ActualHeight - ft.Height - 2));
                dc.DrawText(ft, new Point(tx, ty));
            }
        }
        if (!endpointHasLabel)
            dc.DrawText(unit, new Point(unitBounds.X, unitBounds.Y));
    }

    protected override void OnRender(DrawingContext dc)
    {
        DrawCanvasGrid(dc);
        var s = Scene;
        if (s == null || s.SheetWidth <= 0) return;

        double H = s.SheetHeight;
        var sheetRect = new Rect(_ox, _oy, s.SheetWidth * _zoom, H * _zoom);
        dc.DrawRectangle(ThemePalette.Brush("SheetFillBrush", Colors.White), ThemePalette.Pen("SheetBorderBrush", Color.FromRgb(0x7D, 0x8A, 0x99), 1.5), sheetRect);
        DrawRulers(dc, s);

        if (s.Margin > 0)
        {
            var dash = new Pen(new SolidColorBrush(Color.FromRgb(0xCC, 0x44, 0x44)), 1) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(null, dash, new Rect(_ox + s.Margin * _zoom, _oy + s.Margin * _zoom,
                Math.Max(0, (s.SheetWidth - 2 * s.Margin) * _zoom), Math.Max(0, (H - 2 * s.Margin) * _zoom)));
        }

        var outline = ThemePalette.Brush("PartOutlineBrush", Color.FromRgb(0x20, 0x20, 0x20));
        double thin = 1.2 / _zoom, thick = 2.6 / _zoom;
        var invalid = new SolidColorBrush(Color.FromArgb(0xC0, 0xE0, 0x30, 0x30));
        var selPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x00)), thick);
        var hoverPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x66, 0xCC)), thick);
        var normalPen = new Pen(outline, thin);

        dc.PushTransform(new MatrixTransform(_zoom, 0, 0, -_zoom, _ox, _oy + H * _zoom));
        foreach (var p in s.Parts)
        {
            if (_dragMoved && _selectedIndices.Contains(p.Index)) continue;
            if (!_geo.TryGetValue(p.Index, out var g)) continue;
            var pen = p.InvalidReason != null
                ? new Pen(Brushes.Red, thick)
                : _selectedIndices.Contains(p.Index) ? selPen : p.Index == _hover ? hoverPen : normalPen;
            dc.DrawGeometry(p.InvalidReason == null ? Fills[p.ColorIndex % Fills.Length] : invalid, pen, g);
        }
        if (_dragMoved)
        {
            dc.PushTransform(new TranslateTransform(_dragPos.X - _dragOrigin.X, _dragPos.Y - _dragOrigin.Y));
            foreach (var dragged in s.Parts.Where(p => _selectedIndices.Contains(p.Index)))
            {
                if (!_geo.TryGetValue(dragged.Index, out var geometry)) continue;
                dc.DrawGeometry(_dragError == null ? Fills[dragged.ColorIndex % Fills.Length] : invalid,
                                _dragError == null ? selPen : new Pen(Brushes.Red, thick), geometry);
            }
            dc.Pop();
        }
        if (_dropPreviewPart != null && _dropPreviewGeometry != null)
        {
            dc.PushTransform(new TranslateTransform(
                _dropPreviewCenter.X - _dropPreviewPart.Width / 2,
                _dropPreviewCenter.Y - _dropPreviewPart.Height / 2));
            dc.DrawGeometry(
                _dropPreviewError == null ? Fills[_dropPreviewPart.ColorIndex % Fills.Length] : invalid,
                _dropPreviewError == null ? selPen : new Pen(Brushes.Red, thick),
                _dropPreviewGeometry);
            dc.Pop();
        }
        dc.Pop();

        // Nhãn tên chi tiết (chỉ khi đủ chỗ)
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var p in s.Parts)
        {
            double px = _dragMoved && _selectedIndices.Contains(p.Index) ? p.X + _dragPos.X - _dragOrigin.X : p.X;
            double py = _dragMoved && _selectedIndices.Contains(p.Index) ? p.Y + _dragPos.Y - _dragOrigin.Y : p.Y;
            double sw = p.Width * _zoom, sh = p.Height * _zoom;
            if (sw < 70 || sh < 18) continue;
            var ft = new FormattedText(p.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                ThemePalette.AppTypeface, 12, ThemePalette.Brush("PartOutlineBrush", Colors.Black), dpi)
            { MaxTextWidth = Math.Max(10, sw - 6), MaxTextHeight = 16, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(ft, new Point(_ox + px * _zoom + 4, _oy + (H - (py + p.Height)) * _zoom + 3));
        }

        // Thông tin khi rê chuột / đang kéo
        string? tip = null;
        if (_dragIdx >= 0 && _dragMoved)
            tip = _dragError == null ? $"X {_dragPos.X:0.#}, Y {_dragPos.Y:0.#}" : $"X {_dragPos.X:0.#}, Y {_dragPos.Y:0.#}\nKhông hợp lệ: {_dragError}";
        else if (_dropPreviewSource != null)
            tip = _dropPreviewError == null
                ? $"{_dropPreviewSource.Name} · thả để đặt"
                : $"{_dropPreviewSource.Name}\nKhông hợp lệ: {_dropPreviewError}";
        else if (_hover >= 0 && _dragIdx < 0)
        {
            var hp = s.Parts.FirstOrDefault(q => q.Index == _hover);
            if (hp != null) tip = hp.InvalidReason == null
                ? $"{hp.Label}\n{hp.Width:0.#} x {hp.Height:0.#} mm"
                : $"{hp.Label}\nKhông hợp lệ: {hp.InvalidReason}";
        }
        if (tip != null)
        {
            var ft = new FormattedText(tip, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                ThemePalette.AppTypeface, 12, Brushes.Black, dpi);
            var at = new Point(Math.Min(_mouse.X + 14, Math.Max(0, ActualWidth - ft.Width - 12)),
                               Math.Min(_mouse.Y + 14, Math.Max(0, ActualHeight - ft.Height - 8)));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xE0)), new Pen(Brushes.Gray, 1),
                new Rect(at.X - 4, at.Y - 2, ft.Width + 8, ft.Height + 4));
            dc.DrawText(ft, at);
        }
        if (_zoomRectActive)
        {
            var rect = new Rect(_zoomRectStart, _zoomRectEnd);
            var fill = new SolidColorBrush(Color.FromArgb(0x28, 0x21, 0x85, 0xD5));
            var border = new Pen(new SolidColorBrush(Color.FromRgb(0x21, 0x85, 0xD5)), 1) { DashStyle = DashStyles.Dash };
            fill.Freeze(); border.Freeze();
            dc.DrawRectangle(fill, border, rect);
        }
        if (_selectionBoxActive)
        {
            var rect = new Rect(_selectionBoxStart, _selectionBoxEnd);
            var fill = new SolidColorBrush(Color.FromArgb(0x28, 0x00, 0x96, 0xD6));
            var border = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x96, 0xD6)), 1);
            fill.Freeze(); border.Freeze();
            dc.DrawRectangle(fill, border, rect);
        }
    }

    // ---------- chuột ----------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var pos = e.GetPosition(this);
        double f = e.Delta > 0 ? 1.15 : 1 / 1.15;
        double nz = Math.Clamp(_zoom * f, 0.01, 60);
        double r = nz / _zoom;
        _ox = pos.X - (pos.X - _ox) * r;
        _oy = pos.Y - (pos.Y - _oy) * r;
        _zoom = nz;
        _autoFit = false;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var pos = e.GetPosition(this);
        _mouse = pos;
        var s = Scene;
        if (ZoomRectMode)
        {
            _zoomRectActive = true;
            _zoomRectStart = _zoomRectEnd = pos;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (PanMode) { StartPan(pos); e.Handled = true; return; }
        if (s == null) return;

        var w = ToWorld(pos);
        int idx = s.HitTest(w.X, w.Y);
        bool additive = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (idx >= 0)
        {
            if (additive)
            {
                var toggled = _selectedIndices.ToHashSet();
                if (!toggled.Add(idx)) toggled.Remove(idx);
                SetSelection(toggled, toggled.Contains(idx) ? idx : toggled.Order().FirstOrDefault(-1));
                e.Handled = true;
                return;
            }
            if (!_selectedIndices.Contains(idx)) SetSelection(new[] { idx }, idx);
            else if (SelectedIndex != idx) SetSelection(_selectedIndices, idx);
            if (e.ClickCount == 2) { Model?.RotateSelectedAt(w.X, w.Y); return; }
            var part = s.Parts.FirstOrDefault(q => q.Index == idx);
            if (part == null || Model == null || !Model.IsIdle) return;
            _dragIdx = idx;
            _grab = new Vector(w.X - part.X, w.Y - part.Y);
            _dragPos = new Point(part.X, part.Y);
            _dragOrigin = _dragPos;
            _downScreen = pos;
            _dragMoved = false;
            _dragError = null;
            CaptureMouse();
        }
        else
        {
            _selectionBoxAdditive = additive;
            if (!additive) SetSelection(Array.Empty<int>());
            _selectionBoxActive = true;
            _selectionBoxStart = _selectionBoxEnd = pos;
            CaptureMouse();
            InvalidateVisual();
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
            StartPan(e.GetPosition(this));
    }

    private void StartPan(Point pos)
    {
        _panning = true;
        _panStart = pos;
        _panOrigin = new Point(_ox, _oy);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        _mouse = pos;
        var s = Scene;

        if (_zoomRectActive)
        {
            _zoomRectEnd = pos;
            InvalidateVisual();
        }
        else if (_selectionBoxActive)
        {
            _selectionBoxEnd = pos;
            InvalidateVisual();
        }
        else if (_panning)
        {
            _ox = _panOrigin.X + (pos.X - _panStart.X);
            _oy = _panOrigin.Y + (pos.Y - _panStart.Y);
            _autoFit = false;
            InvalidateVisual();
        }
        else if (_dragIdx >= 0 && s != null)
        {
            if (!_dragMoved && (pos - _downScreen).Length < 4) return;
            _dragMoved = true;
            var w = ToWorld(pos);
            // làm tròn 0,5 mm để dễ đặt đúng chỗ
            double x = Math.Round((w.X - _grab.X) * 2) / 2;
            double y = Math.Round((w.Y - _grab.Y) * 2) / 2;
            _dragPos = new Point(x, y);
            long now = Stopwatch.GetTimestamp();
            if (_lastValidationTimestamp == 0 || Stopwatch.GetElapsedTime(_lastValidationTimestamp, now).TotalMilliseconds >= 35 ||
                (_dragPos - _lastValidatedPosition).Length >= 3)
            {
                _dragError = Model?.CheckMoveSelected(x - _dragOrigin.X, y - _dragOrigin.Y);
                _lastValidationTimestamp = now;
                _lastValidatedPosition = _dragPos;
            }
            InvalidateVisual();
        }
        else if (s != null)
        {
            var w = ToWorld(pos);
            int h = s.HitTest(w.X, w.Y);
            _hover = h;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_zoomRectActive)
        {
            var rect = new Rect(_zoomRectStart, _zoomRectEnd);
            _zoomRectActive = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (rect.Width >= 12 && rect.Height >= 12)
            {
                double factor = Math.Min((ActualWidth - 2 * 46) / rect.Width, (ActualHeight - 2 * 46) / rect.Height);
                double next = Math.Clamp(_zoom * factor, 0.01, 60);
                double ratio = next / _zoom;
                var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                _ox = ActualWidth / 2 - (center.X - _ox) * ratio;
                _oy = ActualHeight / 2 - (center.Y - _oy) * ratio;
                _zoom = next;
                _autoFit = false;
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_selectionBoxActive)
        {
            var rect = new Rect(_selectionBoxStart, _selectionBoxEnd);
            _selectionBoxActive = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            var selected = _selectionBoxAdditive ? _selectedIndices.ToHashSet() : new HashSet<int>();
            if (Scene is { } scene && rect.Width >= 3 && rect.Height >= 3)
                foreach (var part in scene.Parts)
                {
                    var bounds = new Rect(_ox + part.X * _zoom, _oy + (scene.SheetHeight - part.Y - part.Height) * _zoom,
                        part.Width * _zoom, part.Height * _zoom);
                    if (rect.IntersectsWith(bounds)) selected.Add(part.Index);
                }
            SetSelection(selected, selected.Contains(SelectedIndex) ? SelectedIndex : selected.Order().FirstOrDefault(-1));
            e.Handled = true;
            return;
        }
        if (_dragIdx >= 0)
        {
            bool moved = _dragMoved;
            double dx = _dragPos.X - _dragOrigin.X, dy = _dragPos.Y - _dragOrigin.Y;
            CancelDrag();
            if (moved) Model?.MoveSelectedAllowInvalid(dx, dy);
        }
        EndPan();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right) EndPan();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1 && _dragIdx < 0) { _hover = -1; InvalidateVisual(); }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _panning = false;
        _zoomRectActive = false;
        _selectionBoxActive = false;
        if (_dragIdx >= 0) CancelDrag();
    }

    private void EndPan()
    {
        if (!_panning) return;
        _panning = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void CancelDrag()
    {
        bool had = _dragIdx >= 0;
        _dragIdx = -1;
        _dragMoved = false;
        _dragError = null;
        if (had && IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    // ---------- bàn phím ----------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var s = Scene;
        if (e.Key == Key.Escape)
        {
            _zoomRectActive = false;
            _selectionBoxActive = false;
            ZoomRectMode = false;
            if (_panning) EndPan();
            CancelDrag();
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete && _selectedIndices.Count > 0)
        {
            DeleteSelectedPlacementsRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (s == null || Model == null || SelectedIndex < 0) return;
        var part = s.Parts.FirstOrDefault(q => q.Index == SelectedIndex);
        if (part == null) return;

        bool fineStep = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.R:
            {
                var anchor = ToWorld(_mouse);
                if (s.HitTest(anchor.X, anchor.Y) == SelectedIndex)
                    Model.RotateSelectedAt(anchor.X, anchor.Y);
                else
                    Model.RotateSelected();
                e.Handled = true;
                break;
            }
            case Key.M:
            {
                var anchor = ToWorld(_mouse);
                if (s.HitTest(anchor.X, anchor.Y) == SelectedIndex)
                    Model.MirrorSelectedAt(anchor.X, anchor.Y);
                else
                    Model.MirrorSelected();
                e.Handled = true;
                break;
            }
            case Key.Left: Move(-1, 0); e.Handled = true; break;
            case Key.Right: Move(1, 0); e.Handled = true; break;
            case Key.Up: Move(0, 1); e.Handled = true; break;
            case Key.Down: Move(0, -1); e.Handled = true; break;
        }

        void Move(double dx, double dy)
        {
            if (fineStep) Model.TryMoveSelected(dx, dy);
            else Model.MoveSelectedUntilBlocked(dx, dy);
        }
    }
}
