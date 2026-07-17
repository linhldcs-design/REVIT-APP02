using Autodesk.Revit.DB;
using WallRebar.Models;

namespace WallRebar.Services.Rebar;

/// <summary>
///     Điều phối tạo thép cho một tường: xác thực họ thép, đọc hình học, dựng frame, mở Transaction và gọi
///     các creator. Caller (command/handler) KHÔNG mở Transaction — orchestrator tự mở/commit.
///     Tạo 2 lưới (mặt A &amp; mặt B), mỗi lưới có thanh dọc + ngang; thép giằng nối 2 mặt được tạo
///     theo cấu hình Tie và không phụ thuộc tùy chọn Draw Additional Rebar.
/// </summary>
public sealed class WallRebarOrchestrator
{
    public RebarCreationResult Create(Document document, Wall wall, WallRebarModel model)
    {
        using var total = WallRebarDiagnostics.Measure("wall.total", $"wallId={wall.Id.ToValue()}");
        using var transaction = new Transaction(document, "Tạo thép tường");
        using (WallRebarDiagnostics.Measure("transaction.start")) transaction.Start();
        RebarCreationResult result;
        using (WallRebarDiagnostics.Measure("engine.create"))
            result = CreateInTransaction(document, wall, model);
        using (WallRebarDiagnostics.Measure("transaction.commit")) transaction.Commit();
        return result;
    }

    /// <summary>Như <see cref="Create" /> nhưng KHÔNG tự mở Transaction — caller đã có transaction (vd revit-mcp).</summary>
    public RebarCreationResult CreateInTransaction(Document document, Wall wall, WallRebarModel model)
    {
        RebarFamilyValidator families;
        IReadOnlyList<string> familyErrors;
        using (WallRebarDiagnostics.Measure("families.validate"))
        {
            families = new RebarFamilyValidator(document);
            familyErrors = families.Validate(model);
        }
        if (familyErrors.Count > 0)
            return new RebarCreationResult(0, 0, 0, familyErrors);

        WallGeometry geometry;
        string error;
        using (WallRebarDiagnostics.Measure("geometry.read"))
            if (!new WallGeometryReader().TryRead(wall, out geometry, out error))
                return new RebarCreationResult(0, 0, 0, [error]);

        var frame = new WallFrame(geometry);
        var meshCreator = new WallMeshCreator(document, families);
        var tieCreator = new WallTieCreator(document, families);
        var additionalCreator = new WallAdditionalRebarCreator(document, families);
        var warnings = new List<string>();

        var leftRightFeet = model.Cover.LeftRightMm / 304.8;
        var barRadius = model.Vertical.Diameter.Feet / 2;
        var offA = leftRightFeet + barRadius;
        var offB = frame.ThicknessFeet - leftRightFeet - barRadius;

        if (offB - offA <= 1e-6)
            return new RebarCreationResult(0, 0, 0,
                ["Tường quá mỏng so với lớp bảo vệ 2 mặt — không bố trí được 2 lưới."]);

        var verticalCount = 0;
        var horizontalCount = 0;
        var tieCount = 0;

        // Mặt A: móc bẻ vào trong bê tông (+DirThickness). Mặt B: bẻ ngược (-DirThickness).
        WallMeshCreationResult faceA;
        using (WallRebarDiagnostics.Measure("mesh.faceA"))
            faceA = meshCreator.Create(wall, frame, model, offA, hookBendSign: 1, warnings);
        verticalCount += faceA.VerticalSetCount;
        horizontalCount += faceA.HorizontalSetCount;

        WallMeshCreationResult faceB;
        using (WallRebarDiagnostics.Measure("mesh.faceB"))
            faceB = meshCreator.Create(wall, frame, model, offB, hookBendSign: -1, warnings);
        verticalCount += faceB.VerticalSetCount;
        horizontalCount += faceB.HorizontalSetCount;

        if (model.DrawAdditionalRebar)
        {
            // Wall.Orientation points out of the exterior face. Face A has outward normal -DirThickness.
            var faceAIsExterior = wall.Orientation.DotProduct(frame.DirThickness) < 0;
            using (WallRebarDiagnostics.Measure("additional.create"))
                verticalCount += additionalCreator.Create(wall, frame, model, offA, offB,
                    faceAIsExterior, warnings);
        }

        if (model.Tie.Enabled)
            using (WallRebarDiagnostics.Measure("ties.create"))
                tieCount += tieCreator.Create(wall, frame, model, warnings);

        return new RebarCreationResult(verticalCount, horizontalCount, tieCount, warnings);
    }

    // meshCreator.Create trả tổng set (dọc + ngang) của 1 mặt; tách lại để báo cáo theo loại.
}
