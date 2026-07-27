using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

/// <summary>Đọc chữ ký thép tại 3 vùng của dầm; chỉ đọc Revit API, không sửa model.</summary>
public sealed class RebarStationProfileReader
{
    public BeamSpanSectionProfile Read(FamilyInstance beam, BeamSpanInput span, IReadOnlyList<Rebar> rebars)
    {
        var axis = Unit(Subtract(ToXyz(span.End), ToXyz(span.Start)));
        var length = span.Start.DistanceTo(span.End);
        var supportProbe = Math.Min(50.0 / 304.8, length * 0.05);
        return new BeamSpanSectionProfile(span.SourceId,
            Fingerprint(span, rebars, axis, length, supportProbe),
            Fingerprint(span, rebars, axis, length, length * 0.5),
            Fingerprint(span, rebars, axis, length, length - supportProbe));
    }

    private static RebarStationFingerprint Fingerprint(BeamSpanInput span, IReadOnlyList<Rebar> rebars,
        XYZ axis, double beamLength, double station)
    {
        var longitudinal = new List<(double Elevation, double Diameter, int Quantity)>();
        var stirrups = new List<StirrupZoneFingerprint>();
        var uncertain = false;
        var hasAdditional = false;

        foreach (var rebar in rebars)
        {
            try
            {
                var box = rebar.get_BoundingBox(null);
                var barType = rebar.Document.GetElement(rebar.GetTypeId()) as RebarBarType;
                if (box == null || barType == null) { uncertain = true; continue; }
                var range = ProjectionRange(box, ToXyz(span.Start), axis);
                if (station < range.Min - 0.01 || station > range.Max + 0.01) continue;

                var curves = rebar.GetCenterlineCurves(false, false, false,
                    MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                var curveProjections = curves.SelectMany(curve => new[]
                    {
                        Subtract(curve.GetEndPoint(0), ToXyz(span.Start)).DotProduct(axis),
                        Subtract(curve.GetEndPoint(1), ToXyz(span.Start)).DotProduct(axis)
                    }).ToList();
                var axialExtent = curveProjections.Count == 0 ? 0 : curveProjections.Max() - curveProjections.Min();
                var diameterMm = (barType.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER)?.AsDouble() ?? 0) * 304.8;
                if (axialExtent >= beamLength * 0.15)
                {
                    longitudinal.Add(((box.Min.Z + box.Max.Z) * 0.5, diameterMm,
                        Math.Max(1, rebar.NumberOfBarPositions)));
                    hasAdditional |= axialExtent < beamLength * 0.8;
                }
                else
                {
                    stirrups.Add(new StirrupZoneFingerprint(diameterMm, Math.Max(0, rebar.MaxSpacing)));
                }
            }
            catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.InvalidOperationException
                                                   or ArgumentException)
            {
                uncertain = true;
            }
        }

        var layers = longitudinal
            .GroupBy(item => (Elevation: Math.Round(item.Elevation / 0.01) * 0.01,
                Diameter: Math.Round(item.Diameter, 1)))
            .Select(group => new RebarLayerFingerprint(group.Key.Elevation, group.Key.Diameter,
                group.Sum(item => item.Quantity)))
            .OrderBy(item => item.ElevationFeet).ThenBy(item => item.DiameterMm).ToList();
        var zones = stirrups.Distinct().OrderBy(item => item.DiameterMm).ThenBy(item => item.SpacingFeet).ToList();
        return new RebarStationFingerprint(span.WidthFeet, span.HeightFeet, layers, zones,
            uncertain || layers.Count == 0 || zones.Count == 0)
        {
            HasAdditionalReinforcement = hasAdditional || layers.Count > 2
        };
    }

    private static (double Min, double Max) ProjectionRange(BoundingBoxXYZ box, XYZ origin, XYZ axis)
    {
        var values = new List<double>(8);
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            values.Add(Subtract(new XYZ(x, y, z), origin).DotProduct(axis));
        return (values.Min(), values.Max());
    }

    private static XYZ ToXyz(RevitAPP.Core.Models.BeamDrawing.Point3 point) => new(point.X, point.Y, point.Z);
    private static XYZ Subtract(XYZ left, XYZ right) => left - right;
    private static XYZ Unit(XYZ value) => value.Normalize();
}
