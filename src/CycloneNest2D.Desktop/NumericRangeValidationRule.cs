using System.Globalization;
using System.Windows.Controls;

namespace CycloneNest2D.Desktop;

public sealed class NumericRangeValidationRule : ValidationRule
{
    public double Minimum { get; set; } = double.NegativeInfinity;
    public double Maximum { get; set; } = double.PositiveInfinity;
    public string ErrorMessage { get; set; } = "Giá trị không hợp lệ.";

    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        if (!double.TryParse(value?.ToString(), NumberStyles.Float | NumberStyles.AllowThousands, cultureInfo, out double number) ||
            !double.IsFinite(number))
            return new ValidationResult(false, "Nhập một số hợp lệ.");

        if (number < Minimum || number > Maximum)
            return new ValidationResult(false, ErrorMessage);

        return ValidationResult.ValidResult;
    }
}
