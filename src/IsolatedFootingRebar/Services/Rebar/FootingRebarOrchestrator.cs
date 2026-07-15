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

        if (model.BottomEnabled)
            meshCount += meshCreator.Create(foundation, frame, model.BottomX, model.BottomY,
                atTop: false, model.Cover, warnings);

        if (model.TopEnabled)
            meshCount += meshCreator.Create(foundation, frame, model.TopX, model.TopY,
                atTop: true, model.Cover, warnings);

        if (model.MidEnabled)
            meshCount += meshCreator.CreateMid(foundation, frame, model.MidX, model.MidY,
                model.MidLayers, model.Cover, warnings);

        if (model.VerticalEnabled)
        {
            // Thanh kê phải nằm GIỮA 2 lưới: chân trên thép đáy, đỉnh dưới thép trên. Truyền bề dày
            // cụm lưới đáy (X+Y) và trên (X+Y) để lùi cao độ kê khỏi vùng thép chịu lực.
            var bottomStackFeet = model.BottomEnabled
                ? model.BottomX.Diameter.Feet + model.BottomY.Diameter.Feet : 0;
            var topStackFeet = model.TopEnabled
                ? model.TopX.Diameter.Feet + model.TopY.Diameter.Feet : 0;
            verticalCount += dowelCreator.Create(foundation, frame, geometry.Pedestal,
                model.Vertical, model.Cover, bottomStackFeet, topStackFeet, warnings);
        }

        if (model.HorizontalEnabled)
            stirrupCount += stirrupCreator.Create(foundation, frame, geometry.Pedestal,
                model.Horizontal, model.Cover, warnings);

        return new RebarCreationResult(meshCount, verticalCount, stirrupCount, warnings);
    }
}
