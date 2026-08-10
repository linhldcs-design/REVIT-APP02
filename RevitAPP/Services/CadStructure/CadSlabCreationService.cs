using Autodesk.Revit.DB;
using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
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
        var skippedHoles = 0;
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
                            if (SelfIntersects(loop))
                                throw new InvalidOperationException(
                                    "Đường bao tự cắt — kiểm tra biên sàn trong preview.");
                        }
                        // Revit refuses the whole floor when any two loops of the profile meet, so a
                        // hole crossing the outline or another hole is dropped rather than allowed
                        // to cost the slab. Everything it would have cut is poured instead, which
                        // the user can see and correct; nothing at all is the worse outcome.
                        var outerPoints = loops[0].Select(curve => curve.GetEndPoint(0)).ToArray();
                        for (var index = loops.Count - 1; index >= 1; index--)
                        {
                            var hole = loops[index].Select(curve => curve.GetEndPoint(0)).ToArray();
                            var clashes = LoopsMeet(loops[index], loops[0])
                                          || !hole.All(point => Inside(outerPoints, point));
                            for (var other = 1; other < index && !clashes; other++)
                                clashes = LoopsMeet(loops[index], loops[other]);
                            if (!clashes) continue;
                            loops.RemoveAt(index);
                            skippedHoles++;
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

            // A dropped hole is not a failure -- the slab was still created -- but the user has to
            // know a void the plan shows was poured over.
            if (skippedHoles > 0)
                errors.Add($"Đã bỏ {skippedHoles} lỗ nằm ngoài biên sàn hoặc cắt biên — "
                    + "vùng đó được đổ liền, kiểm tra lại trong Revit.");

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

        /// <summary>
        /// Whether a loop crosses or doubles back on itself. Revit only reports that the profile
        /// is invalid, without saying which loop, so it is worth naming here.
        /// </summary>
        /// <summary>
        /// Whether two loops of a profile meet: either their edges cross, or one lies inside the
        /// other's ring while reaching outside it. Revit accepts only loops that nest cleanly.
        /// </summary>
        static bool LoopsMeet(CurveLoop first, CurveLoop second)
        {
            var a = first.Select(curve => curve.GetEndPoint(0)).ToArray();
            var b = second.Select(curve => curve.GetEndPoint(0)).ToArray();
            for (var index = 0; index < a.Length; index++)
            for (var other = 0; other < b.Length; other++)
            {
                var a1 = a[index];
                var a2 = a[(index + 1) % a.Length];
                var b1 = b[other];
                var b2 = b[(other + 1) % b.Length];
                if (Crosses(a1, a2, b1, b2)) return true;
                // Loops need not cross to be refused: a hole whose side runs along the outline, or
                // merely touches it, leaves the profile just as invalid. Edges lying on one another
                // never cross, so they have to be caught by distance instead.
                if (Touches(a1, a2, b1) || Touches(a1, a2, b2)
                    || Touches(b1, b2, a1) || Touches(b1, b2, a2))
                    return true;
            }
            return false;
        }

        static bool Inside(XYZ[] loop, XYZ point)
        {
            var inside = false;
            for (int index = 0, previous = loop.Length - 1; index < loop.Length; previous = index++)
            {
                if (loop[index].Y > point.Y != loop[previous].Y > point.Y
                    && point.X < (loop[previous].X - loop[index].X) * (point.Y - loop[index].Y)
                        / (loop[previous].Y - loop[index].Y) + loop[index].X)
                    inside = !inside;
            }
            return inside;
        }

        static bool SelfIntersects(CurveLoop loop)
        {
            var points = loop.Select(curve => curve.GetEndPoint(0)).ToArray();
            for (var index = 0; index < points.Length; index++)
            for (var other = index + 2; other < points.Length; other++)
            {
                if (index == 0 && other == points.Length - 1) continue;
                if (Crosses(points[index], points[(index + 1) % points.Length],
                        points[other], points[(other + 1) % points.Length]))
                    return true;
            }
            return false;
        }

        static bool Crosses(XYZ a1, XYZ a2, XYZ b1, XYZ b2)
        {
            var d1 = Direction(b1, b2, a1);
            var d2 = Direction(b1, b2, a2);
            var d3 = Direction(a1, a2, b1);
            var d4 = Direction(a1, a2, b2);
            return d1 * d2 < 0 && d3 * d4 < 0;
        }

        /// <summary>
        /// Whether a point sits on a segment, within the tolerance a drawing is worked to.
        /// </summary>
        static bool Touches(XYZ from, XYZ to, XYZ point)
        {
            const double reachFeet = 0.5 / MillimetresPerFoot;
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-12) return false;
            var along = ((point.X - from.X) * dx + (point.Y - from.Y) * dy) / lengthSquared;
            if (along < 0.0 || along > 1.0) return false;
            var closestX = from.X + dx * along;
            var closestY = from.Y + dy * along;
            var offsetX = point.X - closestX;
            var offsetY = point.Y - closestY;
            return offsetX * offsetX + offsetY * offsetY <= reachFeet * reachFeet;
        }

        static double Direction(XYZ from, XYZ to, XYZ point) =>
            (to.X - from.X) * (point.Y - from.Y) - (to.Y - from.Y) * (point.X - from.X);

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
        // The type the plan asks for is the chosen one at the thickness written on the drawing.
        // Every floor type in Revit answers to the family name "Floor", so matching on that alone
        // took whichever type happened to be that thick -- a deck where the plan wanted a slab.
        // How a type is built is what makes it the same as another.
        if (Math.Abs(seed.ThicknessMm - thicknessMm) <= ThicknessToleranceMm)
            return seed.FloorType;

        var existing = new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault(type => BuiltLike(type, seed.FloorType)
                                    && Math.Abs(new CadSlabTypeOption(type).ThicknessMm - thicknessMm)
                                        <= ThicknessToleranceMm);
        if (existing is not null) return existing;

        // The copy is named after the type it came from, with the thickness the plan states.
        // Naming it after the family gave every generated type the name "Floor 120", which says
        // nothing about how the slab is built.
        var name = CadSlabTypeNaming.ForThickness(seed.Name, thicknessMm);
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

    /// <summary>
    /// Whether two floor types are made the same way: the same layers, of the same materials, in
    /// the same order. Their thicknesses may differ -- that is what is being varied.
    /// </summary>
    private static bool BuiltLike(FloorType candidate, FloorType seed)
    {
        var left = candidate.GetCompoundStructure();
        var right = seed.GetCompoundStructure();
        if (left is null || right is null) return false;
        if (left.LayerCount != right.LayerCount) return false;

        for (var layer = 0; layer < left.LayerCount; layer++)
        {
            if (left.GetLayerFunction(layer) != right.GetLayerFunction(layer)) return false;
            if (left.GetMaterialId(layer) != right.GetMaterialId(layer)) return false;
        }
        return true;
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
