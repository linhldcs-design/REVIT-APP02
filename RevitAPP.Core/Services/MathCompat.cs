namespace RevitAPP.Core.Services;

/// <summary>
///     Polyfill cac API khong co tren .NET Framework 4.8 (Revit 2023/2024) — chay ca net48 lan net8.
///     - double.IsFinite: chi co tu .NET Core 3.
///     - Math.Clamp: chi co tu .NET Core 2.
/// </summary>
public static class MathCompat
{
    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    public static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;
}
