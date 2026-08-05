namespace RevitAPP.Core.Services;

public static class CadGridUnitConverter
{
    public static double MillimetresPerDrawingUnit(int insUnits) => insUnits switch
    {
        1 => 25.4,       // inches
        2 => 304.8,      // feet
        4 => 1.0,        // millimetres
        5 => 10.0,       // centimetres
        6 => 1000.0,     // metres
        14 => 100.0,     // decimetres
        _ => throw new InvalidDataException(
            $"INSUNITS={insUnits} chưa được hỗ trợ. Hãy đặt bản vẽ theo mm, cm, m, inch hoặc feet.")
    };
}
