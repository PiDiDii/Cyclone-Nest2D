using System.Windows;
using System.Windows.Media;

namespace CycloneNest2D.Desktop;

/// <summary>
/// Lấy brush theo theme hiện hành (Light/Dark) cho các control tự vẽ bằng DrawingContext.
/// Nếu thiếu resource (vd. chạy ngoài ứng dụng) thì dùng màu dự phòng.
/// </summary>
internal static class ThemePalette
{
    public static FontFamily AppFontFamily =>
        Application.Current?.TryFindResource("AppFont") as FontFamily ?? SystemFonts.MessageFontFamily;

    public static Typeface AppTypeface =>
        new(AppFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static Brush Brush(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush b) return b;
        var f = new SolidColorBrush(fallback);
        f.Freeze();
        return f;
    }

    /// <summary>True khi theme hiện tại tối (suy ra từ độ sáng nền canvas).</summary>
    public static bool IsDark
    {
        get
        {
            if (Brush("CanvasBgBrush", Colors.White) is SolidColorBrush b)
                return (0.299 * b.Color.R + 0.587 * b.Color.G + 0.114 * b.Color.B) < 110;
            return false;
        }
    }

    public static Pen Pen(string key, Color fallback, double thickness)
    {
        var p = new Pen(Brush(key, fallback), thickness);
        return p;
    }
}
