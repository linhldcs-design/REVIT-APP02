using Autodesk.Revit.DB;
using RevitAPP.Core.Models.CadStructure;
using Serilog;

namespace RevitAPP.Services.CadStructure;

internal sealed record CadSlabCreationResult(
    IReadOnlyList<ElementId> CreatedIds,
    int ExistingCount,
    IReadOnlyList<string> Errors,
    TransactionStatus? FinalStatus);

internal sealed record CadSlabTypeOption(FloorType FloorType)
{
    public string Name => FloorType.Name;

    /// <summary>
    /// Thickness of the structural layer, which is what a plan states and what a type has to match
    /// to be reused rather than duplicated.
    /// </summary>
    public double ThicknessMm
    {
        get
        {
            var structure = FloorType.GetCompoundStructure();
            if (structure is null) return 0.0;
            var index = structure.GetFirstCoreLayerIndex();
            if (index < 0 || index >= structure.LayerCount) return structure.GetWidth() * 304.8;
            return structure.GetLayerWidth(index) * 304.8;
        }
    }

    public override string ToString() => $"{Name} ({ThicknessMm:0} mm)";
}

internal static class CadSlabCreationService
{
    private const double MillimetresPerFoot = 304.8;
    private const double DuplicateToleranceFeet = 1.0 / MillimetresPerFoot;
    private const double ThicknessToleranceMm = 1.0;

    public static CadSlabCreationResult Create(
        Document document,
        IReadOnlyList<CadSlabRegionCandidate> regions,
        CadStructurePoint2 sourceAnchorMm,
        XYZ targetAnchor,
        double placementRotationDegrees,
        CadSlabTypeOption seedType,
        CadColumnLevelOption level)
    {
        if (regions.Count == 0) return Invalid("Không có sàn hợp lệ được chọn.");

        var created = new List<ElementId>();
        var errors = new List<string>();
        var existingCount = 0;
        var rotation = placementRotationDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(rotation);
        var sine = Math.Sin(rotation);

        using var group = new TransactionGroup(document, "Model From CAD - Create Slabs");
        group.Start();
        try
        {
            Dictionary<int, FloorType> types;
            using (var transaction = new Transaction(document, "Prepare CAD slab types"))
            {
                transaction.Start();
                types = regions
                    .Select(region => (int)Math.Round(region.EffectiveThicknessMm))
                    .Distinct()
                    .ToDictionary(key => key, key => FindOrCreateType(document, seedType, key));
                transaction.Commit();
            }

            var existing = ExistingFloors(document);
            using (var transaction = new Transaction(document, "Create Slabs from CAD"))
            {
                transaction.Start();
                foreach (var region in regions)
                {
                    try
                    {
                        var loops = BuildLoops(region);
                        // Revit only says the profile is invalid, so check the conditions here
                        // where the region is still known and the message can name what is wrong.
                        foreach (var loop in loops)
                        {
                            if (loop.NumberOfCurves() < 3)
                                throw new InvalidOperationException(
                                    "Đường bao có ít hơn 3 cạnh sau khi bỏ cạnh dài bằng 0.");
                            if (loop.IsOpen())
                                throw new InvalidOperationException("Đường bao không khép kín.");
                        }
                        if (loops[0].IsCounterclockwise(XYZ.BasisZ) == false)
                            loops[0] = Reversed(loops[0]);

                        var floorType = types[(int)Math.Round(region.EffectiveThicknessMm)];
                        var offsetFeet = region.EffectiveOffsetMm / MillimetresPerFoot;

                        if (existing.Any(floor => IsDuplicate(floor, loops[0], floorType,
                                level.Level.Id, offsetFeet)))
                        {
                            existingCount++;
                            continue;
                        }

                        var floor = Floor.Create(document, loops, floorType.Id, level.Level.Id);
                        SetRequiredDouble(floor,
                            BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM, offsetFeet);
                        existing.Add(floor);
                        created.Add(floor.Id);
                    }
                    catch (Exception exception)
                    {
                        Log.Error(exception, "Could not create CAD slab region {RegionId}", region.Id);
                        errors.Add($"Sàn S{region.Id}: {exception.Message}");
                        throw;
                    }
                }
                transaction.Commit();
            }

            var status = group.Assimilate();
            return new CadSlabCreationResult(created, existingCount, errors, status);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Create Slabs from CAD rolled back");
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            if (errors.Count == 0) errors.Add(exception.Message);
            return new CadSlabCreationResult(
                Array.Empty<ElementId>(), existingCount, errors, TransactionStatus.RolledBack);
        }

        static CurveLoop Reversed(CurveLoop loop)
        {
            var reversed = new CurveLoop();
            foreach (var curve in loop.Reverse()) reversed.Append(curve.CreateReversed());
            return reversed;
        }

        IList<CurveLoop> BuildLoops(CadSlabRegionCandidate region)
        {
            var loops = new List<CurveLoop> { ToCurveLoop(region.OuterLoop) };
            foreach (var hole in region.Holes) loops.Add(ToCurveLoop(hole));
            return loops;
        }

        CurveLoop ToCurveLoop(CadSlabLoop loop)
        {
            var points = loop.VerticesMm.Select(Transform).ToList();
            var result = new CurveLoop();
            for (var index = 0; index < points.Count; index++)
            {
                var start = points[index];
                var end = points[(index + 1) % points.Count];
                // Revit rejects a loop containing a zero-length curve, and a plan can leave two
                // vertices on top of each other where lines were trimmed to the same point.
                if (start.DistanceTo(end) <= DuplicateToleranceFeet) continue;
                result.Append(Line.CreateBound(start, end));
            }
            return result;
        }

        XYZ Transform(CadStructurePoint2 point)
        {
            var local = point - sourceAnchorMm;
            var x = (local.X * cosine - local.Y * sine) / MillimetresPerFoot;
            var y = (local.X * sine + local.Y * cosine) / MillimetresPerFoot;
            return new XYZ(targetAnchor.X + x, targetAnchor.Y + y, targetAnchor.Z);
        }
    }

    /// <summary>
    /// Reuses a floor type of the stated thickness and only duplicates the seed when none exists,
    /// so repeated runs land on the same types instead of filling the project with copies.
    /// </summary>
    private static FloorType FindOrCreateType(
        Document document,
        CadSlabTypeOption seed,
        int thicknessMm)
    {
        var existing = new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault(type => type.FamilyName == seed.FloorType.FamilyName
                                    && Math.Abs(new CadSlabTypeOption(type).ThicknessMm - thicknessMm)
                                        <= ThicknessToleranceMm);
        if (existing is not null) return existing;

        var name = $"{seed.FloorType.FamilyName} {thicknessMm}";
        var duplicate = (FloorType)seed.FloorType.Duplicate(UniqueTypeName(document, name));
        var structure = duplicate.GetCompoundStructure();
        if (structure is null)
            throw new InvalidOperationException(
                $"Floor Type '{seed.Name}' không có cấu tạo lớp để đặt chiều dày.");

        var coreIndex = structure.GetFirstCoreLayerIndex();
        if (coreIndex < 0 || coreIndex >= structure.LayerCount)
            throw new InvalidOperationException(
                $"Floor Type '{seed.Name}' không xác định được lớp kết cấu.");

        structure.SetLayerWidth(coreIndex, thicknessMm / MillimetresPerFoot);
        duplicate.SetCompoundStructure(structure);
        return duplicate;
    }

    private static string UniqueTypeName(Document document, string name)
    {
        var taken = new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{name} ({suffix})";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static List<Floor> ExistingFloors(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(Floor))
            .Cast<Floor>()
            .ToList();

    /// <summary>
    /// Two slabs are the same when they sit at the same level and offset, use the same type, and
    /// cover the same footprint. Comparing the sorted vertices keeps a rerun from stacking a
    /// second slab on the first regardless of where the loop starts.
    /// </summary>
    private static bool IsDuplicate(
        Floor floor,
        CurveLoop loop,
        FloorType type,
        ElementId levelId,
        double offsetFeet)
    {
        if (floor.GetTypeId() != type.Id) return false;
        if (floor.LevelId != levelId) return false;
        var existingOffset = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)
            ?.AsDouble() ?? 0.0;
        if (Math.Abs(existingOffset - offsetFeet) > DuplicateToleranceFeet) return false;

        var wanted = Fingerprint(loop.Select(curve => curve.GetEndPoint(0)));
        var actual = FootprintOf(floor);
        if (actual is null || actual.Count != wanted.Count) return false;
        for (var index = 0; index < wanted.Count; index++)
            if (Math.Abs(wanted[index].Item1 - actual[index].Item1) > DuplicateToleranceFeet
                || Math.Abs(wanted[index].Item2 - actual[index].Item2) > DuplicateToleranceFeet)
                return false;
        return true;
    }

    private static List<(double, double)>? FootprintOf(Floor floor)
    {
        var options = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Coarse };
        var geometry = floor.get_Geometry(options);
        if (geometry is null) return null;
        var points = new List<XYZ>();
        foreach (var item in geometry)
        {
            if (item is not Solid solid || solid.Faces.Size == 0) continue;
            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace planar) continue;
                if (Math.Abs(planar.FaceNormal.Z - 1.0) > 1e-6) continue;
                foreach (var loop in planar.GetEdgesAsCurveLoops())
                foreach (var curve in loop)
                    points.Add(curve.GetEndPoint(0));
                break;
            }
        }
        return points.Count == 0 ? null : Fingerprint(points);
    }

    private static List<(double, double)> Fingerprint(IEnumerable<XYZ> points) =>
        points
            .Select(point => (Math.Round(point.X, 4), Math.Round(point.Y, 4)))
            .Distinct()
            .OrderBy(point => point.Item1)
            .ThenBy(point => point.Item2)
            .ToList();

    private static void SetRequiredDouble(Element element, BuiltInParameter parameter, double value)
    {
        var target = element.get_Parameter(parameter);
        if (target is null || target.IsReadOnly)
            throw new InvalidOperationException($"Không ghi được tham số {parameter}.");
        target.Set(value);
    }

    private static CadSlabCreationResult Invalid(string error) => new(
        Array.Empty<ElementId>(), 0, new[] { error }, null);
}
