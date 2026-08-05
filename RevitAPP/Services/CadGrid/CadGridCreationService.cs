using Autodesk.Revit.DB;
using Serilog;

namespace RevitAPP.Services.CadGrid;

internal sealed record CadGridCreationResult(
    IReadOnlyList<ElementId> CreatedIds,
    int ExistingCount,
    IReadOnlyList<string> Errors,
    TransactionStatus? FinalTransactionStatus = null);

internal sealed class CadGridCreationService
{
    /// <summary>Two grids within this distance (feet) are treated as the same grid.</summary>
    private const double DuplicateToleranceFeet = 1.0 / 304.8;

    /// <summary>Angular slack when deciding whether two grids are parallel.</summary>
    private const double ParallelToleranceRadians = 1e-4;

    /// <summary>Below this the line has no meaningful direction in the plan plane.</summary>
    private const double PlanProjectionToleranceFeet = 1e-9;

    /// <summary>
    /// Creates the planned grids in one transaction so a single Undo removes the whole
    /// batch. Grids that coincide with an existing one are skipped rather than duplicated.
    /// </summary>
    public CadGridCreationResult CreateFromLines(
        Document document,
        IReadOnlyList<CadGridPlannedGrid> planned)
    {
        var createdIds = new List<ElementId>();
        var errors = new List<string>();
        // Grids belonging to elevation/section views run along Z and have no projection
        // in the plan plane, so they can never duplicate a plan grid and would make the
        // perpendicular test degenerate.
        var existing = new FilteredElementCollector(document)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .Select(grid => grid.Curve)
            .OfType<Line>()
            .Where(line => HasPlanProjection(line))
            .ToList();

        var existingCount = 0;
        var toCreate = new List<CadGridPlannedGrid>();
        foreach (var candidate in planned)
        {
            if (existing.Any(line => IsSameGrid(line, candidate.Curve)))
            {
                existingCount++;
                continue;
            }

            toCreate.Add(candidate);
            existing.Add(candidate.Curve);
        }

        if (toCreate.Count == 0)
            return new CadGridCreationResult(createdIds, existingCount, errors);

        using var transaction = new Transaction(document, "Tạo Lưới từ Cad");
        transaction.Start();

        foreach (var candidate in toCreate)
        {
            using var subTransaction = new SubTransaction(document);
            try
            {
                subTransaction.Start();
                var grid = Grid.Create(document, candidate.Curve);
                TryRename(grid, candidate.Name);
                subTransaction.Commit();
                createdIds.Add(grid.Id);
            }
            catch (Exception exception)
            {
                if (subTransaction.GetStatus() == TransactionStatus.Started)
                    subTransaction.RollBack();

                errors.Add(
                    $"Trục {candidate.SourceAnchorName} offset {Math.Round(candidate.OffsetMm):0} mm: "
                    + exception.Message);
                Log.Warning(
                    exception,
                    "Could not create Grid offset {OffsetMm} mm from anchor {Anchor}",
                    candidate.OffsetMm,
                    candidate.SourceAnchorName);
            }
        }

        TransactionStatus finalStatus;
        if (createdIds.Count == 0)
        {
            transaction.RollBack();
            finalStatus = TransactionStatus.RolledBack;
        }
        else
        {
            finalStatus = transaction.Commit();
            if (finalStatus != TransactionStatus.Committed)
            {
                createdIds.Clear();
                errors.Add(
                    $"Transaction không được commit (trạng thái: {finalStatus}). Không có Grid nào được ghi nhận.");
            }
        }

        return new CadGridCreationResult(createdIds, existingCount, errors, finalStatus);
    }

    /// <summary>
    /// Compares infinite lines, not segments: a grid extended to a different length is
    /// still the same grid. Parallel direction plus a coincident point implies identity.
    /// </summary>
    private static bool IsSameGrid(Line existing, Line candidate)
    {
        var existingNormal = PlanNormal(existing);
        var candidateNormal = PlanNormal(candidate);
        if (existingNormal is null || candidateNormal is null) return false;

        // An exact direction comparison is too strict here: a planned line is rebuilt
        // through offset arithmetic, so its direction differs from the stored grid's in
        // the last bits and every duplicate would be missed.
        var cosine = Math.Abs(existingNormal.DotProduct(candidateNormal));
        if (cosine < Math.Cos(ParallelToleranceRadians)) return false;

        var toCandidate = candidate.GetEndPoint(0) - existing.GetEndPoint(0);
        return Math.Abs(existingNormal.DotProduct(new XYZ(toCandidate.X, toCandidate.Y, 0)))
            < DuplicateToleranceFeet;
    }

    /// <summary>
    /// Applies the sequenced name, keeping Revit's automatic name when that name is
    /// already taken. A naming clash must not cost the grid itself.
    /// </summary>
    private static void TryRename(Grid grid, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            grid.Name = name;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            Log.Warning("Grid name {Name} is already in use; kept the automatic name", name);
        }
    }

    private static bool HasPlanProjection(Line line) => PlanNormal(line) is not null;

    /// <summary>
    /// Unit normal of the line's projection onto the plan plane, or null when the line
    /// is vertical in model space and therefore has no such projection.
    /// </summary>
    private static XYZ? PlanNormal(Line line)
    {
        var direction = line.Direction;
        var normal = new XYZ(-direction.Y, direction.X, 0);
        return normal.GetLength() < PlanProjectionToleranceFeet ? null : normal.Normalize();
    }

}
