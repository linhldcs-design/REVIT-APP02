using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Services.BeamDrawing;
using RevitAPP.Helpers;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

public sealed class LongitudinalBeamSelectionReader
{
    public bool TryRead(Document document, IReadOnlyList<FamilyInstance> beams,
        out IReadOnlyList<BeamSpanInput> spans, out IReadOnlyList<BeamSpanSectionProfile> profiles, out string error)
    {
        var result = new List<BeamSpanInput>();
        var rebarsByHost = new FilteredElementCollector(document)
            .OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>()
            .GroupBy(rebar => rebar.GetHostId()).ToDictionary(group => group.Key, group => group.ToList());

        if (beams.Count != 1)
        {
            spans = []; profiles = [];
            error = "Chỉ chọn một cây dầm chạy xuyên qua các cột.";
            return false;
        }

        foreach (var beam in beams)
        {
            if (!rebarsByHost.TryGetValue(beam.Id, out var hostedRebars))
            {
                spans = [];
                profiles = [];
                error = $"Dầm {beam.Id} chưa có thép Rebar. Công cụ chỉ triển khai bản vẽ từ dầm đã có thép sẵn.";
                return false;
            }
            if (!new BeamGeometryReader().TryRead(document, beam, out var geometry, out error))
            {
                spans = [];
                profiles = [];
                return false;
            }
            var split = SplitAtColumns(document, beam, geometry);
            if (split.Count == 0)
            {
                spans = []; profiles = [];
                error = "Không tìm thấy ít nhất hai cột giao với cây dầm để xác định các nhịp.";
                return false;
            }
            result.AddRange(split);
        }

        spans = result;
        var profileReader = new RebarStationProfileReader();
        var selectedBeam = beams[0];
        profiles = result.Select(span =>
            profileReader.Read(selectedBeam, span, rebarsByHost[selectedBeam.Id])).ToList();
        error = string.Empty;
        return true;
    }

    private static IReadOnlyList<BeamSpanInput> SplitAtColumns(Document document, FamilyInstance beam,
        RevitAPP.Core.Models.BeamDrawing.BeamGeometry geometry)
    {
        var start = new XYZ(geometry.Start.X, geometry.Start.Y, geometry.Start.Z);
        var end = new XYZ(geometry.End.X, geometry.End.Y, geometry.End.Z);
        var axis = (end - start).Normalize();
        var length = geometry.LengthFeet;
        var search = beam.get_BoundingBox(null);
        var collector = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType();
        if (search != null)
        {
            var margin = 500.0 / 304.8;
            collector = collector.WherePasses(new BoundingBoxIntersectsFilter(new Outline(
                search.Min - new XYZ(margin, margin, margin), search.Max + new XYZ(margin, margin, margin))));
        }
        var stations = new List<double>();
        foreach (var column in collector.OfType<FamilyInstance>())
        {
            var point = column.Location is LocationPoint lp ? lp.Point :
                column.get_BoundingBox(null) is { } box ? (box.Min + box.Max) * 0.5 : null;
            if (point == null) continue;
            var along = (point - start).DotProduct(axis);
            if (along < -0.05 || along > length + 0.05) continue;
            var onAxis = start + axis * along;
            var delta = point - onAxis;
            if (new XYZ(delta.X, delta.Y, 0).GetLength() > 500.0 / 304.8) continue;
            stations.Add(Math.Clamp(along, 0, length));
        }
        var ordered = stations.OrderBy(value => value).Aggregate(new List<double>(), (values, value) =>
        {
            if (values.Count == 0 || value - values[^1] > 100.0 / 304.8) values.Add(value);
            return values;
        });
        if (ordered.Count < 2) return [];
        var hostId = beam.Id.ToValue();
        var spans = new List<BeamSpanInput>();
        for (var index = 0; index < ordered.Count - 1; index++)
        {
            var p0 = start + axis * ordered[index];
            var p1 = start + axis * ordered[index + 1];
            spans.Add(new BeamSpanInput(hostId * 1000 + index + 1,
                new RevitAPP.Core.Models.BeamDrawing.Point3(p0.X, p0.Y, p0.Z),
                new RevitAPP.Core.Models.BeamDrawing.Point3(p1.X, p1.Y, p1.Z),
                geometry.WidthFeet, geometry.HeightFeet, hostId));
        }
        return spans;
    }
}
