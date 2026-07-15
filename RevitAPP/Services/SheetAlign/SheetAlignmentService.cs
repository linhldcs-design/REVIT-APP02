using Autodesk.Revit.DB;
using RevitAPP.Core.Models;
using RevitAPP.Core.Services;
using Serilog;

namespace RevitAPP.Services.SheetAlign
{
    /// <summary>
    ///     Căn chỉnh viewport giữa các sheet theo 1 điểm neo lưới trục: trên mỗi sheet, điểm giao
    ///     của 2 trục được chọn sẽ rơi đúng cùng vị trí trên giấy như sheet mẫu. Đồng thời sao chép
    ///     vị trí nhãn (tên bản vẽ) từ viewport mẫu.
    /// </summary>
    public sealed class SheetAlignmentService
    {
        /// <summary>Lấy tất cả lưới trục trong tài liệu.</summary>
        public IReadOnlyList<Grid> GetGrids(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .Where(grid => grid.Curve is Line)
                .OrderBy(grid => grid.Name)
                .ToList();
        }

        /// <summary>Tất cả sheet thật (bỏ placeholder) để hiển thị cho user chọn.</summary>
        public IReadOnlyList<ViewSheet> GetSheets(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(sheet => !sheet.IsPlaceholder)
                .OrderBy(sheet => sheet.SheetNumber)
                .ToList();
        }

        /// <summary>
        ///     Điểm giao 2 lưới trục trong toạ độ model (XY). Trả null nếu hai trục song song.
        /// </summary>
        public XYZ? GetGridIntersectionModel(Grid gridA, Grid gridB)
        {
            if (gridA.Curve is not Line lineA || gridB.Curve is not Line lineB)
            {
                return null;
            }

            var pA = lineA.GetEndPoint(0);
            var dA = lineA.Direction;
            var pB = lineB.GetEndPoint(0);
            var dB = lineB.Direction;

            var hit = SheetAlignMath.IntersectLines(
                (pA.X, pA.Y), (dA.X, dA.Y),
                (pB.X, pB.Y), (dB.X, dB.Y));

            if (hit == null)
            {
                return null;
            }

            return new XYZ(hit.Value.X, hit.Value.Y, 0);
        }

        /// <summary>Viewport đầu tiên hợp lệ trên sheet (view dạng mặt bằng, không template).</summary>
        public Viewport? GetPrimaryViewport(Document document, ViewSheet sheet)
        {
            foreach (var viewportId in sheet.GetAllViewports())
            {
                if (document.GetElement(viewportId) is not Viewport viewport)
                {
                    continue;
                }

                if (document.GetElement(viewport.ViewId) is View view && !view.IsTemplate)
                {
                    return viewport;
                }
            }

            return null;
        }

        /// <summary>Vị trí điểm neo (model) trên không gian giấy của viewport.</summary>
        public XYZ GetPaperAnchor(Viewport viewport, XYZ modelAnchor)
        {
#if REVIT2023_OR_GREATER
            return viewport.GetProjectionToSheetTransform().OfPoint(modelAnchor);
#else
            // Revit 2022 chưa có GetProjectionToSheetTransform. Chiếu điểm model vào
            // hệ trục của view rồi quy đổi theo scale quanh tâm crop/viewport.
            if (viewport.Document.GetElement(viewport.ViewId) is not View view)
                return viewport.GetBoxCenter();

            var crop = view.CropBox;
            var cropMid = (crop.Min + crop.Max) * 0.5;
            var modelCenter = crop.Transform.OfPoint(cropMid);
            var delta = modelAnchor - modelCenter;
            var scale = Math.Max(1, view.Scale);
            var paperCenter = viewport.GetBoxCenter();
            return paperCenter + new XYZ(
                delta.DotProduct(view.RightDirection) / scale,
                delta.DotProduct(view.UpDirection) / scale,
                0);
#endif
        }

        /// <summary>
        ///     Căn chỉnh các sheet đích theo sheet mẫu. Gọi trong 1 Transaction duy nhất.
        /// </summary>
        public SheetAlignResult Apply(
            Document document,
            ViewSheet masterSheet,
            IReadOnlyList<ViewSheet> targetSheets,
            Grid gridA,
            Grid gridB)
        {
            var result = new SheetAlignResult();

            var modelAnchor = GetGridIntersectionModel(gridA, gridB);
            if (modelAnchor == null)
            {
                result.Skipped.Add(new SheetAlignSkip(masterSheet.SheetNumber,
                    "Hai trục được chọn song song, không có giao điểm."));
                return result;
            }

            var masterViewport = GetPrimaryViewport(document, masterSheet);
            if (masterViewport == null)
            {
                result.Skipped.Add(new SheetAlignSkip(masterSheet.SheetNumber,
                    "Sheet mẫu không có viewport hợp lệ."));
                return result;
            }

            var paperMaster = GetPaperAnchor(masterViewport, modelAnchor);
            var masterRotation = masterViewport.Rotation;

            // Vị trí tuyệt đối của đầu nhãn (tên bản vẽ) trên giấy ở sheet mẫu:
            // LabelOffset là vector từ góc trái-dưới box -> đầu nhãn, nên cộng vào MinimumPoint của box.
            var masterLabelPaper = masterViewport.GetBoxOutline().MinimumPoint + masterViewport.LabelOffset;

            using var transaction = new Transaction(document, "Căn chỉnh view theo lưới trục");
            transaction.Start();

            foreach (var sheet in targetSheets)
            {
                if (sheet.Id == masterSheet.Id)
                {
                    continue;
                }

                var viewport = GetPrimaryViewport(document, sheet);
                if (viewport == null)
                {
                    result.Skipped.Add(new SheetAlignSkip(sheet.SheetNumber, "Không có viewport hợp lệ."));
                    continue;
                }

                if (viewport.Rotation != masterRotation)
                {
                    result.Skipped.Add(new SheetAlignSkip(sheet.SheetNumber, "Viewport xoay khác sheet mẫu."));
                    continue;
                }

                var paperCurrent = GetPaperAnchor(viewport, modelAnchor);
                var delta = SheetAlignMath.ComputeDelta((paperMaster.X, paperMaster.Y), (paperCurrent.X, paperCurrent.Y));

                var currentCenter = viewport.GetBoxCenter();
                viewport.SetBoxCenter(new XYZ(currentCenter.X + delta.X, currentCenter.Y + delta.Y, currentCenter.Z));

                // Box đã dịch -> đọc lại góc trái-dưới mới, đặt nhãn về đúng vị trí giấy như sheet mẫu
                // (bù theo kích thước box riêng nên nhãn khớp tuyệt đối kể cả box khác mẫu).
                document.Regenerate();
                var newMin = viewport.GetBoxOutline().MinimumPoint;
                viewport.LabelOffset = new XYZ(
                    masterLabelPaper.X - newMin.X,
                    masterLabelPaper.Y - newMin.Y,
                    0);

                result.UpdatedCount++;
                Log.Debug("Aligned sheet {Sheet}: delta=({Dx:F4},{Dy:F4})", sheet.SheetNumber, delta.X, delta.Y);
            }

            transaction.Commit();

            Log.Information("Sheet align done: updated {Updated}, skipped {Skipped}",
                result.UpdatedCount, result.Skipped.Count);

            return result;
        }
    }
}
