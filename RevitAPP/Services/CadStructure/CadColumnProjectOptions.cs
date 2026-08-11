using Autodesk.Revit.DB;

namespace RevitAPP.Services.CadStructure;

internal sealed record CadColumnFamilyOption(
    string DisplayName,
    Family Family,
    FamilySymbol SeedSymbol,
    IReadOnlyList<string> LengthParameters,
    IReadOnlyList<FamilySymbol> Symbols)
{
    public override string ToString() => DisplayName;
}

internal sealed record CadColumnLevelOption(Level Level)
{
    public string Name => Level.Name;
    public double Elevation => Level.Elevation;
    public override string ToString() => Name;
}

internal sealed record CadBeamFamilyOption(
    string DisplayName,
    Family Family,
    FamilySymbol SeedSymbol,
    IReadOnlyList<string> LengthParameters,
    IReadOnlyList<FamilySymbol> Symbols)
{
    public override string ToString() => DisplayName;
}

internal sealed record CadColumnProjectOptions(
    IReadOnlyList<CadColumnFamilyOption> Families,
    IReadOnlyList<CadColumnLevelOption> Levels)
{
    public IReadOnlyList<CadBeamFamilyOption> BeamFamilies { get; init; } =
        Array.Empty<CadBeamFamilyOption>();

    public IReadOnlyList<CadSlabTypeOption> SlabTypes { get; init; } =
        Array.Empty<CadSlabTypeOption>();

    public IReadOnlyList<CadWallTypeOption> WallTypes { get; init; } =
        Array.Empty<CadWallTypeOption>();
}

internal static class CadColumnProjectOptionsReader
{
    public static CadColumnProjectOptions Read(Document document)
    {
        var symbols = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .OrderBy(symbol => symbol.FamilyName)
            .ThenBy(symbol => symbol.Name)
            .ToArray();

        var families = symbols
            .GroupBy(symbol => symbol.Family.Id)
            .Select(group =>
            {
                var familySymbols = group.ToArray();
                var seed = familySymbols[0];
                var parameters = seed.Parameters
                    .Cast<Parameter>()
                    .Where(parameter => parameter.StorageType == StorageType.Double
                                        && !parameter.IsReadOnly
                                        && parameter.Definition.GetDataType() == SpecTypeId.Length
                                        && !string.IsNullOrWhiteSpace(parameter.Definition?.Name))
                    .Select(parameter => parameter.Definition.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name)
                    .ToArray();
                return new CadColumnFamilyOption(
                    seed.FamilyName,
                    seed.Family,
                    seed,
                    parameters,
                    familySymbols);
            })
            .Where(option => option.LengthParameters.Count >= 2)
            .OrderBy(option => option.DisplayName)
            .ToArray();

        var levels = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .Select(level => new CadColumnLevelOption(level))
            .ToArray();

        var beamSymbols = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .OrderBy(symbol => symbol.FamilyName)
            .ThenBy(symbol => symbol.Name)
            .ToArray();
        var beamFamilies = beamSymbols
            .GroupBy(symbol => symbol.Family.Id)
            .Select(group =>
            {
                var familySymbols = group.ToArray();
                var seed = familySymbols[0];
                var parameters = seed.Parameters
                    .Cast<Parameter>()
                    .Where(parameter => parameter.StorageType == StorageType.Double
                                        && !parameter.IsReadOnly
                                        && parameter.Definition.GetDataType() == SpecTypeId.Length
                                        && !string.IsNullOrWhiteSpace(parameter.Definition?.Name))
                    .Select(parameter => parameter.Definition.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name)
                    .ToArray();
                return new CadBeamFamilyOption(seed.FamilyName, seed.Family, seed,
                    parameters, familySymbols);
            })
            .Where(option => option.LengthParameters.Count >= 2)
            .Where(option => option.SeedSymbol.Family.FamilyPlacementType
                             == FamilyPlacementType.CurveDrivenStructural)
            .OrderBy(option => option.DisplayName)
            .ToArray();

        // A slab type is only usable as a seed when its layers say which one carries the
        // structure, since that is the layer a stated thickness applies to.
        var slabTypes = new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Where(type => type.GetCompoundStructure() is not null)
            .Select(type => new CadSlabTypeOption(type))
            .OrderBy(option => option.Name)
            .ToArray();

        // A wall type is only usable as a seed when its layers say which one carries the
        // structure, since that is the layer a measured thickness applies to. Curtain and
        // stacked walls have no such layer and cannot be built to a thickness.
        var wallTypes = new FilteredElementCollector(document)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(type => type.Kind == WallKind.Basic)
            .Where(type => type.GetCompoundStructure() is not null)
            .Select(type => new CadWallTypeOption(type))
            .OrderBy(option => option.Name)
            .ToArray();

        return new CadColumnProjectOptions(families, levels)
        {
            BeamFamilies = beamFamilies,
            SlabTypes = slabTypes,
            WallTypes = wallTypes
        };
    }
}
