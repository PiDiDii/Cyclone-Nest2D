using System.Windows;
using System.Windows.Media;
using CycloneNest2D.Core;
using CycloneNest2D.UI;
using WpfRect = System.Windows.Rect;

namespace CycloneNest2D.Desktop;

/// <summary>Vẽ trước hình học DXF của chi tiết đang chọn trong tab Part.</summary>
public sealed class PartPreviewView : FrameworkElement
{
    private double _zoom = 1;
    private Vector _pan;

    public static readonly DependencyProperty PartProperty = DependencyProperty.Register(
        nameof(Part), typeof(PartItem), typeof(PartPreviewView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPartChanged));

    public PartItem? Part { get => (PartItem?)GetValue(PartProperty); set => SetValue(PartProperty, value); }

    public PartPreviewView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public void ZoomToFit()
    {
        _zoom = 1;
        _pan = new Vector();
        InvalidateVisual();
    }

    public void ZoomBy(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        _zoom = Math.Clamp(_zoom * factor, 0.12, 24);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta == 0) return;
        double next = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.12, 24);
        Vector fromCenter = e.GetPosition(this) - new Point(ActualWidth / 2, ActualHeight / 2);
        _pan = fromCenter - (fromCenter - _pan) * (next / _zoom);
        _zoom = next;
        InvalidateVisual();
        e.Handled = true;
    }

    private static void OnPartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (PartPreviewView)d;
        if (e.OldValue is PartItem oldPart) oldPart.Changed -= view.OnGeometryChanged;
        if (e.NewValue is PartItem newPart) newPart.Changed += view.OnGeometryChanged;
        view._zoom = 1;
        view._pan = new Vector();
        view.InvalidateVisual();
    }

    private void OnGeometryChanged(PartItem _) => Dispatcher.Invoke(InvalidateVisual);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(ThemePalette.Brush("ThumbBgBrush", Color.FromRgb(247, 250, 253)), null, new WpfRect(0, 0, ActualWidth, ActualHeight));
        var part = Part;
        if (part == null || part.Width <= 0 || part.Height <= 0)
        {
            DrawHint(dc, "Chọn một chi tiết hợp lệ để xem trước");
            return;
        }

        double pad = Math.Clamp(Math.Min(ActualWidth, ActualHeight) * 0.08, 2, 22);
        double scale = Math.Min(Math.Max(1, ActualWidth - 2 * pad) / part.Width, Math.Max(1, ActualHeight - 2 * pad) / part.Height) * _zoom;
        double drawW = part.Width * scale, drawH = part.Height * scale;
        double ox = (ActualWidth - drawW) / 2 + _pan.X, oy = (ActualHeight - drawH) / 2 + _pan.Y;
        var baseColor = (Color)ColorConverter.ConvertFromString(PreviewScene.Palette[PreviewScene.ColorIndexForName(part.Name)]);
        var pen = new Pen(new SolidColorBrush(baseColor), ActualWidth < 90 || ActualHeight < 60 ? 0.85 : 1.7);
        pen.Freeze();

        Point Map(double x, double y) => new(ox + (x - (part.Geometry?.MinX ?? 0)) * scale,
            oy + drawH - (y - (part.Geometry?.MinY ?? 0)) * scale);

        var shape = part.Geometry;
        if (shape != null && part.Flip) shape = ShapeOps.MirrorHorizontal(shape);
        if (shape == null)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xB4, baseColor.R, baseColor.G, baseColor.B)), pen, new WpfRect(ox, oy, drawW, drawH));
            if (ActualWidth >= 140 && ActualHeight >= 80) DrawDimension(dc, part, ox, oy, drawW, drawH);
            return;
        }

        var contours = ShapeOps.Build(shape);
        if (contours.Loops.Count > 0)
        {
            var filled = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var context = filled.Open())
            {
                foreach (var (xs, ys) in contours.Loops)
                {
                    if (xs.Length < 3) continue;
                    context.BeginFigure(Map(xs[0], ys[0]), true, true);
                    var points = new List<Point>(xs.Length - 1);
                    for (int i = 1; i < xs.Length; i++) points.Add(Map(xs[i], ys[i]));
                    context.PolyLineTo(points, true, false);
                }
            }
            filled.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xB4, baseColor.R, baseColor.G, baseColor.B)), null, filled);
        }

        foreach (var seg in shape.Segments)
        {
            switch (seg)
            {
                case LineSeg line:
                    dc.DrawLine(pen, Map(line.X1, line.Y1), Map(line.X2, line.Y2));
                    break;
                case CircleSeg circle:
                    dc.DrawEllipse(null, pen, Map(circle.Cx, circle.Cy), circle.R * scale, circle.R * scale);
                    break;
                case ArcSeg arc:
                {
                    int steps = Math.Clamp((int)Math.Ceiling(Math.Abs(arc.SweepRad) * Math.Max(arc.R * scale, 1) / 3), 8, 240);
                    Point previous = Map(arc.Cx + arc.R * Math.Cos(arc.StartRad), arc.Cy + arc.R * Math.Sin(arc.StartRad));
                    for (int i = 1; i <= steps; i++)
                    {
                        double a = arc.StartRad + arc.SweepRad * i / steps;
                        Point current = Map(arc.Cx + arc.R * Math.Cos(a), arc.Cy + arc.R * Math.Sin(a));
                        dc.DrawLine(pen, previous, current);
                        previous = current;
                    }
                    break;
                }
            }
        }
        if (ActualWidth >= 140 && ActualHeight >= 80) DrawDimension(dc, part, ox, oy, drawW, drawH);
    }

    private void DrawDimension(DrawingContext dc, PartItem part, double x, double y, double w, double h)
    {
        var text = new FormattedText($"{part.Width:0.##} × {part.Height:0.##} mm",
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, ThemePalette.Brush("TextSecondaryBrush", Color.FromRgb(82, 101, 122)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(Math.Max(4, (ActualWidth - text.Width) / 2), Math.Max(3, ActualHeight - text.Height - 4)));
    }

    private void DrawHint(DrawingContext dc, string message)
    {
        var text = new FormattedText(message, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12.5, ThemePalette.Brush("TextMutedBrush", Color.FromRgb(120, 144, 168)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(Math.Max(8, (ActualWidth - text.Width) / 2), Math.Max(8, (ActualHeight - text.Height) / 2)));
    }
}
