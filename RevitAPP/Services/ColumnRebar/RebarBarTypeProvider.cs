using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models;
using RevitAPP.Helpers;

namespace RevitAPP.Services.ColumnRebar;

/// <summary>Lấy danh sách RebarBarType trong dự án dưới dạng model thuần để binding UI.</summary>
public sealed class RebarBarTypeProvider
{
    public IReadOnlyList<RebarBarTypeOption> GetAll(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(RebarBarType))
            .Cast<RebarBarType>()
            .Select(barType => new RebarBarTypeOption(
                barType.Id.ToValue(),
                barType.Name,
                Math.Round(UnitUtils.ConvertFromInternalUnits(barType.BarModelDiameter, UnitTypeId.Millimeters), 1)))
            .OrderBy(option => option.DiameterMm)
            .ThenBy(option => option.Name)
            .ToList();
    }
}
