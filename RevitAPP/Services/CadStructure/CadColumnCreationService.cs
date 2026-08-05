using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.CadStructure;
using Serilog;

namespace RevitAPP.Services.CadStructure;

internal sealed record CadColumnCreationResult(
    IReadOnlyList<ElementId> CreatedIds,
    int ExistingCount,
    IReadOnlyList<string> Errors,
    TransactionStatus? FinalStatus);

internal static class CadColumnCreationService
{
    private const double MillimetresPerFoot = 304.8;
    private const double DuplicateToleranceFeet = 1.0 / MillimetresPerFoot;

    public static CadColumnCreationResult Create(
        Document document,
        IReadOnlyList<CadColumnCandidate> candidates,
        CadStructurePoint2 sourceAnchorMm,
        XYZ targetAnchor,
        double placementRotationDegrees,
        CadColumnFamilyOption family,
        string widthParameter,
        string heightParameter,
        CadColumnLevelOption baseLevel,
        CadColumnLevelOption topLevel,
        double baseOffsetMm,
        double topOffsetMm)
    {
        var created = new List<ElementId>();
        var errors = new List<string>();
        var existing = 0;
        if (topLevel.Elevation * MillimetresPerFoot + topOffsetMm
            <= baseLevel.Elevation * MillimetresPerFoot + baseOffsetMm)
            return Invalid("Top Level + offset phải cao hơn Base Level + offset.");
        if (!ValidLengthParameter(family.SeedSymbol, widthParameter)
            || !ValidLengthParameter(family.SeedSymbol, heightParameter))
            return Invalid("Tham số Width/Height phải là type parameter kiểu Length và ghi được.");

        var rotation = placementRotationDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(rotation);
        var sine = Math.Sin(rotation);

        using var group = new TransactionGroup(document, "Model From CAD - Create Columns");
        group.Start();
        try
        {
            Dictionary<(int Width, int Height), FamilySymbol> types;
            using (var typeTransaction = new Transaction(document, "Prepare CAD column types"))
            {
                typeTransaction.Start();
                types = candidates
                    .Select(candidate => SizeKey(candidate.WidthMm, candidate.HeightMm))
                    .Distinct()
                    .ToDictionary(
                        key => key,
                        key => FindOrCreateSymbol(document, family, widthParameter,
                            heightParameter, key.Width, key.Height));
                typeTransaction.Commit();
            }

            var existingColumns = ExistingColumns(document);
            using (var createTransaction = new Transaction(document, "Create Columns from CAD"))
            {
                createTransaction.Start();
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var relativeX = candidate.CenterMm.X - sourceAnchorMm.X;
                        var relativeY = candidate.CenterMm.Y - sourceAnchorMm.Y;
                        var point = new XYZ(
                            targetAnchor.X + (relativeX * cosine - relativeY * sine) / MillimetresPerFoot,
                            targetAnchor.Y + (relativeX * sine + relativeY * cosine) / MillimetresPerFoot,
                            baseLevel.Level.Elevation);

                        var symbol = types[SizeKey(candidate.WidthMm, candidate.HeightMm)];
                        var totalAngle = (candidate.AngleDegrees + placementRotationDegrees)
                                         * Math.PI / 180.0;
                        if (existingColumns.Any(instance => IsExactDuplicate(
                                instance, point, symbol, widthParameter, heightParameter,
                                candidate.WidthMm, candidate.HeightMm, baseLevel.Level.Id,
                                topLevel.Level.Id, baseOffsetMm, topOffsetMm, totalAngle)))
                        {
                            existing++;
                            continue;
                        }

                        if (!symbol.IsActive)
                        {
                            symbol.Activate();
                            document.Regenerate();
                        }

                        var instance = document.Create.NewFamilyInstance(
                            point, symbol, baseLevel.Level, StructuralType.Column);
                        SetRequiredElementId(instance, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, baseLevel.Level.Id);
                        SetRequiredElementId(instance, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, topLevel.Level.Id);
                        SetRequiredDouble(instance, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM,
                            baseOffsetMm / MillimetresPerFoot);
                        SetRequiredDouble(instance, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM,
                            topOffsetMm / MillimetresPerFoot);

                        if (Math.Abs(totalAngle) > 1e-9)
                        {
                            var axis = Line.CreateBound(point, point + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(document, instance.Id, axis, totalAngle);
                        }

                        existingColumns.Add(instance);
                        created.Add(instance.Id);
                    }
                    catch (Exception exception)
                    {
                        Log.Error(exception, "Could not create CAD column candidate {CandidateId}", candidate.Id);
                        errors.Add($"Cột C{candidate.Id}: {exception.Message}");
                        throw;
                    }
                }
                createTransaction.Commit();
            }

            var status = group.Assimilate();
            return new CadColumnCreationResult(created, existing, errors, status);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Create Columns from CAD rolled back");
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            if (errors.Count == 0) errors.Add(exception.Message);
            return new CadColumnCreationResult(
                Array.Empty<ElementId>(), existing, errors, TransactionStatus.RolledBack);
        }
    }

    private static FamilySymbol FindOrCreateSymbol(
        Document document,
        CadColumnFamilyOption family,
        string widthParameter,
        string heightParameter,
        int widthMm,
        int heightMm)
    {
        foreach (var symbol in family.Symbols)
        {
            var width = symbol.LookupParameter(widthParameter);
            var height = symbol.LookupParameter(heightParameter);
            if (width?.StorageType != StorageType.Double || height?.StorageType != StorageType.Double) continue;
            if (Math.Abs(width.AsDouble() * MillimetresPerFoot - widthMm) <= 1.0
                && Math.Abs(height.AsDouble() * MillimetresPerFoot - heightMm) <= 1.0)
                return symbol;
        }

        var baseName = $"{widthMm}x{heightMm}";
        var name = baseName;
        var suffix = 2;
        while (family.Symbols.Any(symbol => string.Equals(symbol.Name, name,
                   StringComparison.OrdinalIgnoreCase)))
            name = baseName + "_" + suffix++;

        var duplicate = family.SeedSymbol.Duplicate(name) as FamilySymbol
                        ?? throw new InvalidOperationException($"Không duplicate được type '{name}'.");
        var widthParam = duplicate.LookupParameter(widthParameter)
                         ?? throw new InvalidOperationException($"Không tìm thấy tham số '{widthParameter}'.");
        var heightParam = duplicate.LookupParameter(heightParameter)
                          ?? throw new InvalidOperationException($"Không tìm thấy tham số '{heightParameter}'.");
        if (widthParam.IsReadOnly || heightParam.IsReadOnly)
            throw new InvalidOperationException("Tham số b/h của family đang read-only.");
        widthParam.Set(widthMm / MillimetresPerFoot);
        heightParam.Set(heightMm / MillimetresPerFoot);
        return duplicate;
    }

    private static bool ValidLengthParameter(FamilySymbol symbol, string name)
    {
        var parameter = symbol.LookupParameter(name);
        return parameter is not null
               && parameter.StorageType == StorageType.Double
               && !parameter.IsReadOnly
               && parameter.Definition.GetDataType() == SpecTypeId.Length;
    }

    private static CadColumnCreationResult Invalid(string error) =>
        new(Array.Empty<ElementId>(), 0, new[] { error }, TransactionStatus.Uninitialized);

    private static List<FamilyInstance> ExistingColumns(Document document) =>
        new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .ToList();

    private static bool IsExactDuplicate(
        FamilyInstance instance,
        XYZ point,
        FamilySymbol symbol,
        string widthParameter,
        string heightParameter,
        double widthMm,
        double heightMm,
        ElementId baseLevelId,
        ElementId topLevelId,
        double baseOffsetMm,
        double topOffsetMm,
        double rotationRadians)
    {
        if (instance.Location is not LocationPoint location
            || HorizontalDistance(location.Point, point) > DuplicateToleranceFeet)
            return false;
        if (instance.Symbol.Family.Id != symbol.Family.Id) return false;
        if (instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId() != baseLevelId
            || instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId() != topLevelId)
            return false;

        var existingWidth = instance.Symbol.LookupParameter(widthParameter)?.AsDouble() * MillimetresPerFoot;
        var existingHeight = instance.Symbol.LookupParameter(heightParameter)?.AsDouble() * MillimetresPerFoot;
        if (existingWidth is null || existingHeight is null
            || Math.Abs(existingWidth.Value - widthMm) > 1.0
            || Math.Abs(existingHeight.Value - heightMm) > 1.0)
            return false;

        var existingBaseOffset = instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)
                                     ?.AsDouble() * MillimetresPerFoot;
        var existingTopOffset = instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)
                                    ?.AsDouble() * MillimetresPerFoot;
        if (existingBaseOffset is null || existingTopOffset is null
            || Math.Abs(existingBaseOffset.Value - baseOffsetMm) > 1.0
            || Math.Abs(existingTopOffset.Value - topOffsetMm) > 1.0)
            return false;

        return AngleDifferenceModuloPi(location.Rotation, rotationRadians) <= Math.PI / 1800.0;
    }

    private static double AngleDifferenceModuloPi(double first, double second)
    {
        var difference = Math.Abs(first - second) % Math.PI;
        return Math.Min(difference, Math.PI - difference);
    }

    private static double HorizontalDistance(XYZ first, XYZ second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static (int Width, int Height) SizeKey(double widthMm, double heightMm) =>
        ((int)Math.Round(widthMm), (int)Math.Round(heightMm));

    private static void SetRequiredElementId(
        FamilyInstance instance,
        BuiltInParameter parameter,
        ElementId value)
    {
        var target = instance.get_Parameter(parameter)
                     ?? throw new InvalidOperationException($"Family thieu tham so bat buoc {parameter}.");
        if (!target.IsReadOnly && !target.Set(value))
            throw new InvalidOperationException($"Khong gan duoc tham so {parameter}.");
        if (target.AsElementId() != value)
            throw new InvalidOperationException($"Tham so {parameter} khong nhan gia tri yeu cau.");
    }

    private static void SetRequiredDouble(
        FamilyInstance instance,
        BuiltInParameter parameter,
        double value)
    {
        var target = instance.get_Parameter(parameter)
                     ?? throw new InvalidOperationException($"Family thieu tham so bat buoc {parameter}.");
        if (!target.IsReadOnly && !target.Set(value))
            throw new InvalidOperationException($"Khong gan duoc tham so {parameter}.");
        if (Math.Abs(target.AsDouble() - value) > 1e-9)
            throw new InvalidOperationException($"Tham so {parameter} khong nhan gia tri yeu cau.");
    }
}
