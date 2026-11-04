using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CycloneNest2D.Core;
using CycloneNest2D.UI;
using WpfRect = System.Windows.Rect;

namespace CycloneNest2D.Desktop;

public enum PartLabelMode
{
    None,
    IdNumber,
    PartName
}

/// <summary>Thumbnail của một tấm đã nest, dùng cùng màu và contour với preview chính.</summary>
public sealed class SheetThumbnailView : FrameworkElement
{
    public static readonly DependencyProperty SheetProperty = DependencyProperty.Register(
        nameof(Sheet), typeof(SheetResult), typeof(SheetThumbnailView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSheetChanged));

    public static readonly DependencyProperty LabelModeProperty = DependencyProperty.Register(
        nameof(LabelMode), typeof(PartLabelMode), typeof(SheetThumbnailView),
        new FrameworkPropertyMetadata(PartLabelMode.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(int), typeof(SheetThumbnailView),
        new FrameworkPropertyMetadata(0, OnRevisionChanged));

    public static readonly DependencyProperty PartIdsProperty = DependencyProperty.Register(
        nameof(PartIds), typeof(IReadOnlyDictionary<string, int>), typeof(SheetThumbnailView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private PreviewScene? _scene;

    public PartLabelMode LabelMode
    {
        get => (PartLabelMode)GetValue(LabelModeProperty);
        set => SetValue(LabelModeProperty, value);
    }

    public IReadOnlyDictionary<string, int>? PartIds
    {
        get => (IReadOnlyDictionary<string, int>?)GetValue(PartIdsProperty);
        set => SetValue(PartIdsProperty, value);
    }

    public int Revision
    {
        get => (int)GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public SheetResult? Sheet
    {
        get => (SheetResult?)GetValue(SheetProperty);
        set => SetValue(SheetProperty, value);
    }

    public SheetThumbnailView() => ClipToBounds = true;

    private static void OnSheetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SheetThumbnailView)d).RefreshScene();

    private static void OnRevisionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SheetThumbnailView)d).RefreshScene();

    private void RefreshScene()
    {
        var sheet = Sheet;
        _scene = sheet == null || sheet.Width <= 0 || sheet.Height <= 0
            ? null
            : PreviewBuilder.Build(sheet, new SheetDefinition(sheet.Width, sheet.Height, 0, 0));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(ThemePalette.Brush("ThumbBgBrush", Color.FromRgb(0xF5, 0xF7, 0xFA)), null,
            new WpfRect(0, 0, ActualWidth, ActualHeight));

        var scene = _scene;
        if (scene == null || ActualWidth < 4 || ActualHeight < 4 || scene.SheetWidth <= 0 || scene.SheetHeight <= 0)
            return;

        const double pad = 4;
        double scale = Math.Min((ActualWidth - pad * 2) / scene.SheetWidth,
                                 (ActualHeight - pad * 2) / scene.SheetHeight);
        if (!(scale > 0) || double.IsInfinity(scale)) return;
        double width = scene.SheetWidth * scale;
        double height = scene.SheetHeight * scale;
        double ox = (ActualWidth - width) / 2;
        double oy = (ActualHeight - height) / 2;
        dc.DrawRectangle(ThemePalette.Brush("SheetFillBrush", Colors.White), ThemePalette.Pen("SheetBorderBrush", Color.FromRgb(0x9A, 0xA6, 0xB4), 1),
            new WpfRect(ox, oy, width, height));

        Point Map(double x, double y) => new(ox + x * scale, oy + (scene.SheetHeight - y) * scale);
        foreach (var part in scene.Parts)
        {
            Color baseColor = (Color)ColorConverter.ConvertFromString(PreviewScene.Palette[
                Math.Clamp(part.ColorIndex, 0, PreviewScene.Palette.Length - 1)]);
            var fill = new SolidColorBrush(Color.FromArgb(0xB5, baseColor.R, baseColor.G, baseColor.B));
            fill.Freeze();
            var outline = ThemePalette.IsDark
                ? new SolidColorBrush(Color.FromRgb(
                    (byte)Math.Min(255, baseColor.R * 0.5 + 130), (byte)Math.Min(255, baseColor.G * 0.5 + 130), (byte)Math.Min(255, baseColor.B * 0.5 + 130)))
                : new SolidColorBrush(Color.FromRgb(
                    (byte)(baseColor.R * 0.62), (byte)(baseColor.G * 0.62), (byte)(baseColor.B * 0.62)));
            outline.Freeze();
            var pen = new Pen(outline, Math.Clamp(0.8 / Math.Max(scale, 0.02), 0.55, 1.1));
            pen.Freeze();

            if (part.Loops.Count == 0)
            {
                dc.DrawRectangle(fill, pen, new WpfRect(ox + part.X * scale,
                    oy + (scene.SheetHeight - part.Y - part.Height) * scale,
                    Math.Max(0.5, part.Width * scale), Math.Max(0.5, part.Height * scale)));
                continue;
            }

            var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var context = geometry.Open())
            {
                foreach (var loop in part.Loops)
                {
                    if (loop.Length < 3) continue;
                    context.BeginFigure(Map(loop[0].X, loop[0].Y), true, true);
                    var points = new List<Point>(loop.Length - 1);
                    for (int i = 1; i < loop.Length; i++) points.Add(Map(loop[i].X, loop[i].Y));
                    context.PolyLineTo(points, true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(fill, pen, geometry);
        }

        if (LabelMode != PartLabelMode.None)
        {
            foreach (var part in scene.Parts)
            {
                string label;
                if (LabelMode == PartLabelMode.IdNumber)
                {
                    if (PartIds is not { Count: > 0 } ids || !ids.TryGetValue(part.Name, out int id)) continue;
                    label = id.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    label = part.Name;
                }

                double partWidth = part.Width * scale;
                double partHeight = part.Height * scale;
                double maxTextWidth = partWidth - 6;
                double maxTextHeight = partHeight - 4;
                if (maxTextWidth < 10 || maxTextHeight < 8) continue;
                Point center = Map(part.X + part.Width / 2, part.Y + part.Height / 2);
                double fontSize = LabelMode == PartLabelMode.IdNumber ? 10 : 9;
                var typeface = new Typeface(ThemePalette.AppFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                FormattedText text = CreateLabel(label, fontSize, typeface);
                while ((text.Width > maxTextWidth || text.Height > maxTextHeight) && fontSize > 5)
                {
                    fontSize -= 0.5;
                    text = CreateLabel(label, fontSize, typeface);
                }
                string displayLabel = label;
                while ((text.Width > maxTextWidth || text.Height > maxTextHeight) && displayLabel.Length > 2)
                {
                    displayLabel = displayLabel[..^2] + "…";
                    text = CreateLabel(displayLabel, fontSize, typeface);
                }
                if (text.Width > maxTextWidth || text.Height > maxTextHeight) continue;

                double tagWidth = Math.Min(partWidth - 2, Math.Max(19, text.Width + 8));
                double tagHeight = Math.Min(partHeight - 2, Math.Max(16, text.Height + 4));
                var tagRect = new WpfRect(center.X - tagWidth / 2, center.Y - tagHeight / 2, tagWidth, tagHeight);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18)),
                    new Pen(Brushes.White, 0.8), tagRect, 2, 2);
                dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
            }
        }

        FormattedText CreateLabel(string label, double fontSize, Typeface typeface) =>
            new(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize,
                Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
