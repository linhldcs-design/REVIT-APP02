using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.CadStructure;
using Serilog;

namespace RevitAPP.Services.CadStructure;

internal sealed record CadBeamCreationResult(
    IReadOnlyList<ElementId> CreatedIds,
    int ExistingCount,
    IReadOnlyList<string> Errors,
    TransactionStatus? FinalStatus);

internal static class CadBeamCreationService
{
    private const double MillimetresPerFoot = 304.8;
    private const double DuplicateToleranceFeet = 1.0 / MillimetresPerFoot;

    public static CadBeamCreationResult Create(
        Document document,
        IReadOnlyList<CadBeamCandidate> candidates,
        CadStructurePoint2 sourceAnchorMm,
        XYZ targetAnchor,
        double placementRotationDegrees,
        CadBeamFamilyOption family,
        string widthParameter,
        string heightParameter,
        CadColumnLevelOption level,
        double zOffsetMm)
    {
        if (candidates.Count == 0) return Invalid("Không có dầm hợp lệ được chọn.");
        if (family.SeedSymbol.Family.FamilyPlacementType != FamilyPlacementType.CurveDrivenStructural)
            return Invalid("Family Structural Framing phải là Curve Driven Structural.");
        if (!ValidLengthParameter(family.SeedSymbol, widthParameter)
            || !ValidLengthParameter(family.SeedSymbol, heightParameter))
            return Invalid("Tham số Width/Height của Structural Framing không hợp lệ.");

        var created = new List<ElementId>();
        var errors = new List<string>();
        var existingCount = 0;
        var rotation = placementRotationDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(rotation);
        var sine = Math.Sin(rotation);

        using var group = new TransactionGroup(document, "Model From CAD - Create Beams");
        group.Start();
        try
        {
            Dictionary<(int Width, int Height), FamilySymbol> types;
            using (var transaction = new Transaction(document, "Prepare CAD beam types"))
            {
                transaction.Start();
                types = candidates.Select(candidate => SizeKey(
                        candidate.EffectiveWidthMm, candidate.EffectiveHeightMm))
                    .Distinct()
                    .ToDictionary(key => key, key => FindOrCreateSymbol(
                        family, widthParameter, heightParameter, key.Width, key.Height));
                transaction.Commit();
            }

            var existing = ExistingBeams(document);
            using (var transaction = new Transaction(document, "Create Beams from CAD"))
            {
                transaction.Start();
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var start = Transform(candidate.StartMm);
                        var end = Transform(candidate.EndMm);
                        if (start.DistanceTo(end) <= DuplicateToleranceFeet)
                            throw new InvalidOperationException("Tim dầm có chiều dài bằng 0.");
                        var symbol = types[SizeKey(candidate.EffectiveWidthMm, candidate.EffectiveHeightMm)];
                        if (existing.Any(instance => IsDuplicate(instance, start, end, symbol,
                                level.Level.Id, zOffsetMm)))
                        {
                            existingCount++;
                            continue;
                        }

                        if (!symbol.IsActive)
                        {
                            symbol.Activate();
                            document.Regenerate();
                        }

                        var line = Line.CreateBound(start, end);
                        var instance = document.Create.NewFamilyInstance(
                            line, symbol, level.Level, StructuralType.Beam);
                        SetRequiredElementId(instance, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, level.Level.Id);
                        SetRequiredDouble(instance, BuiltInParameter.Z_OFFSET_VALUE, zOffsetMm / MillimetresPerFoot);
                        SetRequiredDouble(instance, BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION, 0.0);
                        SetRequiredDouble(instance, BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION, 0.0);
                        SetTopJustification(instance);
                        if (!string.IsNullOrWhiteSpace(candidate.Mark))
                            SetIfWritable(instance, BuiltInParameter.ALL_MODEL_MARK, candidate.Mark);
                        existing.Add(instance);
                        created.Add(instance.Id);
                    }
                    catch (Exception exception)
                    {
                        Log.Error(exception, "Could not create CAD beam candidate {CandidateId}", candidate.Id);
                        errors.Add($"Dầm D{candidate.Id}: {exception.Message}");
                        throw;
                    }
                }
                transaction.Commit();
            }

            var status = group.Assimilate();
            return new CadBeamCreationResult(created, existingCount, errors, status);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Create Beams from CAD rolled back");
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            if (errors.Count == 0) errors.Add(exception.Message);
            return new CadBeamCreationResult(
                Array.Empty<ElementId>(), existingCount, errors, TransactionStatus.RolledBack);
        }

        XYZ Transform(CadStructurePoint2 point)
        {
            var x = point.X - sourceAnchorMm.X;
            var y = point.Y - sourceAnchorMm.Y;
            return new XYZ(
                targetAnchor.X + (x * cosine - y * sine) / MillimetresPerFoot,
                targetAnchor.Y + (x * sine + y * cosine) / MillimetresPerFoot,
                level.Level.Elevation);
        }
    }

    private static FamilySymbol FindOrCreateSymbol(
        CadBeamFamilyOption family,
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
        var duplicateWidth = duplicate.LookupParameter(widthParameter)
                    ?? throw new InvalidOperationException($"Không tìm thấy tham số '{widthParameter}'.");
        var duplicateHeight = duplicate.LookupParameter(heightParameter)
                     ?? throw new InvalidOperationException($"Không tìm thấy tham số '{heightParameter}'.");
        if (duplicateWidth.IsReadOnly || duplicateHeight.IsReadOnly)
            throw new InvalidOperationException("Tham số b/h của family đang read-only.");
        duplicateWidth.Set(widthMm / MillimetresPerFoot);
        duplicateHeight.Set(heightMm / MillimetresPerFoot);
        return duplicate;
    }

    private static bool IsDuplicate(
        FamilyInstance instance,
        XYZ start,
        XYZ end,
        FamilySymbol symbol,
        ElementId levelId,
        double zOffsetMm)
    {
        if (instance.Location is not LocationCurve { Curve: Line line }) return false;
        if (instance.Symbol.Id != symbol.Id) return false;
        var sameDirection = line.GetEndPoint(0).DistanceTo(start) <= DuplicateToleranceFeet
                            && line.GetEndPoint(1).DistanceTo(end) <= DuplicateToleranceFeet;
        var reverseDirection = line.GetEndPoint(0).DistanceTo(end) <= DuplicateToleranceFeet
                               && line.GetEndPoint(1).DistanceTo(start) <= DuplicateToleranceFeet;
        if (!sameDirection && !reverseDirection) return false;
        var existingLevel = instance.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId();
        var existingOffset = instance.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE)?.AsDouble()
                             * MillimetresPerFoot;
        return existingLevel == levelId
               && existingOffset is not null
               && Math.Abs(existingOffset.Value - zOffsetMm) <= 1.0;
    }

    private static List<FamilyInstance> ExistingBeams(Document document) =>
        new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .ToList();

    private static void SetTopJustification(FamilyInstance instance)
    {
        var parameter = instance.get_Parameter(BuiltInParameter.Z_JUSTIFICATION)
                        ?? throw new InvalidOperationException("Family thiếu tham số Z Justification.");
        var value = (int)ZJustification.Top;
        if (!parameter.IsReadOnly && !parameter.Set(value))
            throw new InvalidOperationException("Không gán được Z Justification = Top.");
        if (parameter.AsInteger() != value)
            throw new InvalidOperationException("Z Justification không nhận giá trị Top.");
    }

    private static void SetRequiredDouble(FamilyInstance instance, BuiltInParameter parameter, double value)
    {
        var target = instance.get_Parameter(parameter)
                     ?? throw new InvalidOperationException($"Family thiếu tham số {parameter}.");
        if (!target.IsReadOnly && !target.Set(value))
            throw new InvalidOperationException($"Không gán được tham số {parameter}.");
        if (Math.Abs(target.AsDouble() - value) > 1e-9)
            throw new InvalidOperationException($"Tham số {parameter} không nhận giá trị yêu cầu.");
    }

    private static void SetRequiredElementId(FamilyInstance instance, BuiltInParameter parameter, ElementId value)
    {
        var target = instance.get_Parameter(parameter)
                     ?? throw new InvalidOperationException($"Family thiếu tham số {parameter}.");
        if (!target.IsReadOnly && !target.Set(value))
            throw new InvalidOperationException($"Không gán được tham số {parameter}.");
        if (target.AsElementId() != value)
            throw new InvalidOperationException($"Tham số {parameter} không nhận giá trị yêu cầu.");
    }

    private static void SetIfWritable(FamilyInstance instance, BuiltInParameter parameter, string value)
    {
        var target = instance.get_Parameter(parameter);
        if (target is not null && !target.IsReadOnly) target.Set(value);
    }

    private static bool ValidLengthParameter(FamilySymbol symbol, string name)
    {
        var parameter = symbol.LookupParameter(name);
        return parameter is not null
               && parameter.StorageType == StorageType.Double
               && !parameter.IsReadOnly
               && parameter.Definition.GetDataType() == SpecTypeId.Length;
    }

    private static (int Width, int Height) SizeKey(double widthMm, double heightMm) =>
        ((int)Math.Round(widthMm), (int)Math.Round(heightMm));

    private static CadBeamCreationResult Invalid(string error) => new(
        Array.Empty<ElementId>(), 0, new[] { error }, TransactionStatus.Uninitialized);
}
