using System.Globalization;

internal sealed record BenchmarkConfig
{
    public bool Full { get; init; }
    public bool ShowHelp { get; init; }
    public bool Diagnostics { get; init; }
    public int Repetitions { get; init; } = 1;
    public double MaxSeconds { get; init; } = 1.5;
    public string? DxfPath { get; init; }
    public string? OutputPrefix { get; init; }
    public double SheetWidth { get; init; } = 2500;
    public double SheetHeight { get; init; } = 1250;
    public double Margin { get; init; } = 5;
    public double Spacing { get; init; } = 3;

    public static BenchmarkConfig Parse(string[] args)
    {
        bool full = false, help = false, diagnostics = false;
        int? repetitions = null;
        double? maxSeconds = null;
        string? dxf = null, output = null;
        double sheetWidth = 2500, sheetHeight = 1250, margin = 5, spacing = 3;

        for (int index = 0; index < args.Length; index++)
        {
            string value = args[index];
            string NextValue()
            {
                if (++index >= args.Length) throw new ArgumentException($"Thiếu giá trị cho {value}.");
                return args[index];
            }

            switch (value)
            {
                case "--quick": full = false; break;
                case "--full": full = true; break;
                case "--diagnostics": diagnostics = true; break;
                case "--help":
                case "-h": help = true; break;
                case "--repeat": repetitions = ParseInt(NextValue(), value, 1, 20); break;
                case "--max-seconds": maxSeconds = ParseDouble(NextValue(), value, 0.1, 3600); break;
                case "--dxf": dxf = NextValue(); break;
                case "--output": output = NextValue(); break;
                case "--sheet-width": sheetWidth = ParseDouble(NextValue(), value, 1, 100_000); break;
                case "--sheet-height": sheetHeight = ParseDouble(NextValue(), value, 1, 100_000); break;
                case "--margin": margin = ParseDouble(NextValue(), value, 0, 10_000); break;
                case "--spacing": spacing = ParseDouble(NextValue(), value, 0, 10_000); break;
                default: throw new ArgumentException($"Không nhận biết tham số '{value}'.");
            }
        }

        if (dxf is not null && !File.Exists(dxf) && !Directory.Exists(dxf))
            throw new ArgumentException($"Không tìm thấy DXF hoặc thư mục: {dxf}");
        if (output is not null && string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Đường dẫn output không được để trống.");
        var config = new BenchmarkConfig
        {
            Full = full,
            ShowHelp = help,
            Diagnostics = diagnostics,
            Repetitions = repetitions ?? (full ? 3 : 1),
            MaxSeconds = maxSeconds ?? (full ? 5 : 1.5),
            DxfPath = dxf,
            OutputPrefix = output,
            SheetWidth = sheetWidth,
            SheetHeight = sheetHeight,
            Margin = margin,
            Spacing = spacing
        };
        if (2 * margin >= Math.Min(sheetWidth, sheetHeight))
            throw new ArgumentException("Hai lần margin phải nhỏ hơn cạnh ngắn của tấm.");
        return config;
    }

    private static int ParseInt(string value, string option, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
            parsed < min || parsed > max)
            throw new ArgumentException($"{option} phải nằm trong {min}..{max}.");
        return parsed;
    }

    private static double ParseDouble(string value, string option, double min, double max)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            !double.IsFinite(parsed) || parsed < min || parsed > max)
            throw new ArgumentException($"{option} phải là số hữu hạn trong {min}..{max}.");
        return parsed;
    }
}
