using Autodesk.Revit.DB;
using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using Serilog;

namespace RevitAPP.Services.CadStructure;

internal sealed record CadWallCreationResult(
    IReadOnlyList<ElementId> CreatedIds,
    int ExistingCount,
    IReadOnlyList<string> Errors,
    TransactionStatus? FinalStatus);

internal sealed record CadWallTypeOption(WallType WallType)
{
    public string Name => WallType.Name;

    /// <summary>
    /// Thickness across the wall, which is what a plan draws and what a type has to match to be
    /// reused rather than duplicated.
    /// </summary>
    public double ThicknessMm => WallType.Width * 304.8;

    public override string ToString() => $"{Name} ({ThicknessMm:0} mm)";
}

internal static class CadWallCreationService
{
    private const double MillimetresPerFoot = 304.8;
    private const double DuplicateToleranceFeet = 1.0 / MillimetresPerFoot;
    private const double ThicknessToleranceMm = 1.0;

    public static CadWallCreationResult Create(
        Document document,
        IReadOnlyList<CadWallCandidate> walls,
        CadStructurePoint2 sourceAnchorMm,
        XYZ targetAnchor,
        double placementRotationDegrees,
        CadWallTypeOption seedType,
        CadColumnLevelOption baseLevel,
        CadColumnLevelOption topLevel,
        double baseOffsetMm)
    {
        if (walls.Count == 0) return Invalid("Không có tường hợp lệ được chọn.");
        if (topLevel.Level.Id == baseLevel.Level.Id)
            return Invalid("Top Level phải khác Base Level.");
        if (topLevel.Elevation <= baseLevel.Elevation)
            return Invalid("Top Level phải cao hơn Base Level.");

        var created = new List<ElementId>();
        var errors = new List<string>();
        var existingCount = 0;
        var rotation = placementRotationDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(rotation);
        var sine = Math.Sin(rotation);

        using var group = new TransactionGroup(document, "Model From CAD - Create Walls");
        group.Start();
        try
        {
            Dictionary<int, WallType> types;
            using (var transaction = new Transaction(document, "Prepare CAD wall types"))
            {
                transaction.Start();
                types = walls
                    .Select(wall => (int)Math.Round(wall.EffectiveThicknessMm))
                    .Distinct()
                    .ToDictionary(key => key, key => FindOrCreateType(document, seedType, key));
                transaction.Commit();
            }

            var existing = ExistingWalls(document);
            using (var transaction = new Transaction(document, "Create Walls from CAD"))
            {
                transaction.Start();
                foreach (var wall in walls)
                {
                    try
                    {
                        var start = Transform(wall.StartMm);
                        var end = Transform(wall.EndMm);
                        if (start.DistanceTo(end) < DuplicateToleranceFeet)
                            throw new InvalidOperationException("Tường có chiều dài bằng 0.");

                        var line = Line.CreateBound(start, end);
                        var wallType = types[(int)Math.Round(wall.EffectiveThicknessMm)];

                        if (existing.Any(built => IsDuplicate(built, line, wallType,
                                baseLevel.Level.Id)))
                        {
                            existingCount++;
                            continue;
                        }

                        var created_ = Wall.Create(document, line, wallType.Id,
                            baseLevel.Level.Id, topLevel.Elevation - baseLevel.Elevation,
                            baseOffsetMm / MillimetresPerFoot, flip: false, structural: true);

                        // The wall follows its levels rather than a height typed into it, so
                        // moving a level moves the wall with it.
                        SetIfWritable(created_, BuiltInParameter.WALL_HEIGHT_TYPE,
                            topLevel.Level.Id);

                        existing.Add(created_);
                        created.Add(created_.Id);
                    }
                    catch (Exception exception)
                    {
                        Log.Error(exception, "Could not create CAD wall {WallId}", wall.Id);
                        errors.Add($"Tường W{wall.Id}: {exception.Message}");
                        throw;
                    }
                }
                transaction.Commit();
            }

            var status = group.Assimilate();
            return new CadWallCreationResult(created, existingCount, errors, status);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Create Walls from CAD rolled back");
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            if (errors.Count == 0) errors.Add(exception.Message);
            return new CadWallCreationResult(
                Array.Empty<ElementId>(), existingCount, errors, TransactionStatus.RolledBack);
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
    /// Reuses a wall type of the stated thickness and only duplicates the seed when none exists,
    /// so repeated runs land on the same types instead of filling the project with copies.
    /// </summary>
    private static WallType FindOrCreateType(
        Document document,
        CadWallTypeOption seed,
        int thicknessMm)
    {
        if (Math.Abs(seed.ThicknessMm - thicknessMm) <= ThicknessToleranceMm)
            return seed.WallType;

        // A wall type is the same as another when it is built the same way. Matching on the
        // family name alone would take whichever type happened to be that thick, which for
        // floors once meant a metal deck where the plan asked for a slab.
        var existing = new FilteredElementCollector(document)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .FirstOrDefault(type => BuiltLike(type, seed.WallType)
                                    && Math.Abs(type.Width * MillimetresPerFoot - thicknessMm)
                                        <= ThicknessToleranceMm);
        if (existing is not null) return existing;

        var name = CadSlabTypeNaming.ForThickness(seed.Name, thicknessMm);
        var duplicate = (WallType)seed.WallType.Duplicate(UniqueTypeName(document, name));
        var structure = duplicate.GetCompoundStructure();
        if (structure is null)
            throw new InvalidOperationException(
                $"Wall Type '{seed.Name}' không có cấu tạo lớp để đặt bề dày.");

        var coreIndex = structure.GetFirstCoreLayerIndex();
        if (coreIndex < 0 || coreIndex >= structure.LayerCount)
            throw new InvalidOperationException(
                $"Wall Type '{seed.Name}' không xác định được lớp kết cấu.");

        // Only the core layer takes the change; the finishes either side keep their thickness,
        // so a type with render on both faces stays that type at a new core thickness.
        var others = 0.0;
        for (var layer = 0; layer < structure.LayerCount; layer++)
            if (layer != coreIndex) others += structure.GetLayerWidth(layer);

        var core = thicknessMm / MillimetresPerFoot - others;
        if (core <= 0)
            throw new InvalidOperationException(
                $"Bề dày {thicknessMm} mm nhỏ hơn tổng các lớp hoàn thiện của '{seed.Name}'.");

        structure.SetLayerWidth(coreIndex, core);
        duplicate.SetCompoundStructure(structure);
        return duplicate;
    }

    /// <summary>
    /// Whether two wall types are made the same way: the same layers, of the same materials, in
    /// the same order. Their thicknesses may differ -- that is what is being varied.
    /// </summary>
    private static bool BuiltLike(WallType candidate, WallType seed)
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
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{name} ({suffix})";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static List<Wall> ExistingWalls(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(Wall))
            .Cast<Wall>()
            .ToList();

    /// <summary>
    /// Two walls are the same when they run between the same points on the same level with the
    /// same type, so running the import twice does not double every wall.
    /// </summary>
    private static bool IsDuplicate(Wall wall, Line line, WallType type, ElementId levelId)
    {
        if (wall.WallType.Id != type.Id) return false;
        if (wall.LevelId != levelId) return false;
        if (wall.Location is not LocationCurve location) return false;
        if (location.Curve is not Line existing) return false;

        var start = existing.GetEndPoint(0);
        var end = existing.GetEndPoint(1);
        var wantedStart = line.GetEndPoint(0);
        var wantedEnd = line.GetEndPoint(1);

        // A wall drawn the other way round is the same wall.
        return (Near(start, wantedStart) && Near(end, wantedEnd))
               || (Near(start, wantedEnd) && Near(end, wantedStart));
    }

    private static bool Near(XYZ first, XYZ second) =>
        first.DistanceTo(second) <= DuplicateToleranceFeet;

    private static void SetIfWritable(Element element, BuiltInParameter parameter, ElementId value)
    {
        var found = element.get_Parameter(parameter);
        if (found is null || found.IsReadOnly) return;
        found.Set(value);
    }

    private static CadWallCreationResult Invalid(string error) =>
        new(Array.Empty<ElementId>(), 0, new[] { error }, null);
}
