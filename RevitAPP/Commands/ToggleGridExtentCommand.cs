using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using Serilog;

namespace RevitAPP.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class ToggleGridExtentCommand : ExternalCommand
    {
        private static readonly DatumEnds[] DatumEndsToUpdate =
        {
            DatumEnds.End0,
            DatumEnds.End1
        };

        public override void Execute()
        {
            if (!LicenseCommandGate.Ensure("Chuyển lưới 3D/2D")) return;

            var document = Application.ActiveUIDocument.Document;
            var view = document.ActiveView;
            if (!IsSupportedView(view))
            {
                TaskDialog.Show("RevitAI", "Lệnh này chỉ sử dụng được trong mặt bằng, mặt đứng, mặt cắt hoặc view chi tiết.");
                return;
            }

            var visibleGrids = new FilteredElementCollector(document, view.Id)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();

            if (visibleGrids.Count == 0)
            {
                TaskDialog.Show("RevitAI", "Không tìm thấy lưới trục nào trong view hiện tại.");
                return;
            }

            var editableGrids = visibleGrids
                .Where(grid => CanReadBothEnds(grid, view))
                .ToList();

            if (editableGrids.Count == 0)
            {
                TaskDialog.Show("RevitAI", "Không có lưới trục nào có thể chuyển đổi trong view hiện tại.");
                return;
            }

            var targetExtent = editableGrids.Any(grid => HasModelExtent(grid, view))
                ? DatumExtentType.ViewSpecific
                : DatumExtentType.Model;

            var updatedCount = 0;
            var skippedCount = visibleGrids.Count - editableGrids.Count;

            using var transaction = new Transaction(document, "Chuyển lưới 3D/2D");
            transaction.Start();

            foreach (var grid in editableGrids)
            {
                using var subTransaction = new SubTransaction(document);
                subTransaction.Start();

                try
                {
                    foreach (var datumEnd in DatumEndsToUpdate)
                    {
                        grid.SetDatumExtentType(datumEnd, view, targetExtent);
                    }

                    subTransaction.Commit();
                    updatedCount++;
                }
                catch (Exception exception)
                {
                    subTransaction.RollBack();
                    skippedCount++;
                    Log.Warning(exception, "Could not change grid {GridId} extent in view {ViewId}", grid.Id, view.Id);
                }
            }

            transaction.Commit();

            var mode = targetExtent == DatumExtentType.ViewSpecific ? "2D" : "3D";
            TaskDialog.Show(
                "RevitAI",
                $"Đã chuyển {updatedCount} lưới trục sang {mode} trong view \"{view.Name}\"."
                + (skippedCount > 0 ? $"\nBỏ qua {skippedCount} lưới không thể chỉnh sửa." : string.Empty));
        }

        private static bool IsSupportedView(View view)
        {
            return !view.IsTemplate && (view is ViewPlan || view is ViewSection);
        }

        private static bool CanReadBothEnds(Grid grid, View view)
        {
            try
            {
                foreach (var datumEnd in DatumEndsToUpdate)
                {
                    _ = grid.GetDatumExtentTypeInView(datumEnd, view);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasModelExtent(Grid grid, View view)
        {
            return DatumEndsToUpdate.Any(
                datumEnd => grid.GetDatumExtentTypeInView(datumEnd, view) == DatumExtentType.Model);
        }
    }
}
