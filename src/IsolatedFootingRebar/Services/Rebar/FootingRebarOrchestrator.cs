using Autodesk.Revit.DB;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services.Rebar;

/// <summary>
///     Điều phối tạo thép cho một móng đơn: xác thực họ thép, đọc hình học, dựng frame, mở Transaction
///     và gọi các creator. Caller (command/api) KHÔNG mở Transaction — orchestrator tự mở/commit.
///     Phase này tạo lưới đáy + trên; mid/dowel/stirrup nối thêm ở phase sau cùng Transaction này.
/// </summary>
public sealed class FootingRebarOrchestrator
{
    public RebarCreationResult Create(Document document, Element foundation, FootingRebarModel model)
    {
        using var transaction = new Transaction(document, "Tạo thép móng đơn");
        transaction.Start();
        var result = CreateInTransaction(document, foundation, model);
        transaction.Commit();
        return result;
    }

    /// <summary>Như <see cref="Create" /> nhưng KHÔNG tự mở Transaction — caller đã có transaction (vd revit-mcp).</summary>
    public RebarCreationResult CreateInTransaction(Document document, Element foundation, FootingRebarModel model)
    {
        var warnings = new List<string>();
        var families = new RebarFamilyValidator(document);

        var familyErrors = families.Validate(model);
        if (familyErrors.Count > 0)
            return new RebarCreationResult(0, 0, 0, familyErrors);

        var dirXOverride = model.DirXOverride is { } d ? new XYZ(d.X, d.Y, d.Z) : null;
        if (!new FootingGeometryReader().TryRead(foundation, dirXOverride, out var geometry, out var error))
            return new RebarCreationResult(0, 0, 0, [error]);

        var frame = new FootingFrame(geometry);
        var meshCreator = new MeshBarCreator(document, families);
        var dowelCreator = new DowelCreator(document, families);
        var stirrupCreator = new FootingStirrupCreator(document, families);

        var meshCount = 0;
        var verticalCount = 0;
        var stirrupCount = 0;

        FootingRebarPreviewPlan previewPlan;
        try
        {
            previewPlan = FootingRebarPreviewFactory.Build(geometry, model);
        }
        catch (Exception ex)
        {
            return new RebarCreationResult(0, 0, 0, [$"Không dựng được hình học thép: {ex.Message}"]);
        }

        var useIndividualMesh = FootingRebarPreviewFactory.RequiresIndividualMeshBars(previewPlan, geometry, model);
        if (useIndividualMesh)
        {
            var meshKinds = new HashSet<FootingPreviewBarKind>
            {
                FootingPreviewBarKind.BottomX, FootingPreviewBarKind.BottomY,
                FootingPreviewBarKind.TopX, FootingPreviewBarKind.TopY,
                FootingPreviewBarKind.MidX, FootingPreviewBarKind.MidY
            };
            var clippedPaths = previewPlan.Paths.Where(path => meshKinds.Contains(path.Kind)).ToArray();
            meshCount += meshCreator.CreateIndividual(foundation, frame, clippedPaths, warnings);
        }

        if (!useIndividualMesh && model.BottomEnabled)
            meshCount += meshCreator.Create(foundation, frame, model.BottomX, model.BottomY,
                atTop: false, model.Cover, warnings);

        if (!useIndividualMesh && model.TopEnabled)
            meshCount += meshCreator.Create(foundation, frame, model.TopX, model.TopY,
                atTop: true, model.Cover, warnings);

        if (!useIndividualMesh && model.MidEnabled)
            meshCount += meshCreator.CreateMid(foundation, frame, model.MidX, model.MidY,
                model.MidLayers, model.Cover, warnings);

        if (model.VerticalEnabled)
        {
            // Thanh kê phải nằm GIỮA 2 lưới: chân trên thép đáy, đỉnh dưới thép trên. Truyền bề dày
            // cụm lưới đáy (X+Y) và trên (X+Y) để lùi cao độ kê khỏi vùng thép chịu lực.
            // Preview đã lọc từng ghế theo tiết diện đa giác. Tạo trực tiếp cùng centerline để Preview/Create là một nguồn hình học.
            verticalCount += dowelCreator.CreateIndividual(foundation, frame, previewPlan.Paths, warnings);
        }

        if (model.HorizontalEnabled)
            stirrupCount += stirrupCreator.CreateIndividual(
                foundation, frame, previewPlan.Paths, model.Horizontal, warnings);

        return new RebarCreationResult(meshCount, verticalCount, stirrupCount, warnings);
    }
}
