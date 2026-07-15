using Autodesk.Revit.DB;
using Autodesk.Revit.DB.PointClouds;
using RevitAPP.Core.Models;
using Serilog;
using Color = Autodesk.Revit.DB.Color;
using RevitColorMode = Autodesk.Revit.DB.PointCloudColorMode;
using RevitAPP.Helpers;

namespace RevitAPP.Services.PointCloud;

/// <inheritdoc />
public sealed class PointCloudDisplayService : IPointCloudDisplayService
{
    public IReadOnlyList<PointCloudInfo> GetPointClouds(Document document)
    {
        // Liệt kê MỌI instance (kể cả không hỗ trợ override) để user luôn thấy point cloud trong project;
        // instance không hỗ trợ sẽ bị disable thao tác ở ViewModel chứ không biến mất.
        return new FilteredElementCollector(document)
            .OfClass(typeof(PointCloudInstance))
            .WhereElementIsNotElementType()
            .Cast<PointCloudInstance>()
            .Select(ToInfo)
            .OrderBy(info => info.Name)
            .ToList();
    }

    public PointCloudColorModeOption GetColorMode(View view, long instanceId)
    {
        try
        {
            var overrides = view.GetPointCloudOverrides();
            using var settings = overrides.GetPointCloudScanOverrideSettings(ElementIdHelper.Create(instanceId));
            return FromRevit(settings.ColorMode);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Đọc color mode point cloud {Id} thất bại — mặc định NoOverride", instanceId);
            return PointCloudColorModeOption.NoOverride;
        }
    }

    public bool SetColorMode(Document document, View view, long instanceId, PointCloudColorModeOption mode, Color? fixedColor)
    {
        if (mode == PointCloudColorModeOption.FixedColor && fixedColor == null)
        {
            Log.Warning("FixedColor mode yêu cầu màu nhưng không được cung cấp (instance {Id})", instanceId);
            return false;
        }

        return Apply(document, view, instanceId, settings =>
        {
            var revitMode = ToRevit(mode);
            settings.ColorMode = revitMode;
            if (mode == PointCloudColorModeOption.FixedColor)
            {
                // Color1/Color2 = cặp endpoint; với màu cố định dùng cùng một màu.
                using var colorSettings = new PointCloudColorSettings(fixedColor, fixedColor);
                settings.SetModeOverride(revitMode, colorSettings);
            }
        });
    }

    public bool SetScanVisibility(Document document, View view, long instanceId, string scanName, bool visible)
    {
        return Apply(document, view, instanceId, scanName, settings => settings.Visible = visible);
    }

    // ===== helpers =====

    /// <summary>Áp override cho toàn instance trong 1 Transaction.</summary>
    private static bool Apply(Document document, View view, long instanceId, Action<PointCloudOverrideSettings> mutate)
    {
        return Apply(document, view, instanceId, scanName: null, mutate);
    }

    /// <summary>Áp override cho instance (hoặc 1 scan nếu <paramref name="scanName" /> != null) trong 1 Transaction.</summary>
    private static bool Apply(Document document, View view, long instanceId, string? scanName, Action<PointCloudOverrideSettings> mutate)
    {
        try
        {
            var elementId = ElementIdHelper.Create(instanceId);
            if (document.GetElement(elementId) is not PointCloudInstance instance || !instance.SupportsOverrides)
            {
                Log.Warning("Point cloud {Id} không hỗ trợ override — bỏ qua", instanceId);
                return false;
            }

            using var transaction = new Transaction(document, "Đổi hiển thị Point Cloud");
            transaction.Start();

            var overrides = view.GetPointCloudOverrides();
            using var settings = scanName == null
                ? overrides.GetPointCloudScanOverrideSettings(elementId)
                : overrides.GetPointCloudScanOverrideSettings(elementId, scanName, document);

            mutate(settings);

            if (scanName == null)
                overrides.SetPointCloudScanOverrideSettings(elementId, settings);
            else
                overrides.SetPointCloudScanOverrideSettings(elementId, settings, scanName, document);

            transaction.Commit();
            return true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Áp override hiển thị point cloud {Id} thất bại", instanceId);
            return false;
        }
    }

    private static PointCloudInfo ToInfo(PointCloudInstance instance)
    {
        var supports = instance.SupportsOverrides;
        return new PointCloudInfo(
            instance.Id.ToValue(),
            instance.Name,
            supports,
            supports ? instance.GetScans().ToList() : Array.Empty<string>(),
            supports ? instance.GetRegions().ToList() : Array.Empty<string>());
    }

    private static RevitColorMode ToRevit(PointCloudColorModeOption mode) => mode switch
    {
        PointCloudColorModeOption.NoOverride => RevitColorMode.NoOverride,
        PointCloudColorModeOption.FixedColor => RevitColorMode.FixedColor,
        PointCloudColorModeOption.Elevation => RevitColorMode.Elevation,
        PointCloudColorModeOption.Intensity => RevitColorMode.Intensity,
        PointCloudColorModeOption.Normals => RevitColorMode.Normals,
        _ => RevitColorMode.NoOverride
    };

    private static PointCloudColorModeOption FromRevit(RevitColorMode mode) => mode switch
    {
        RevitColorMode.NoOverride => PointCloudColorModeOption.NoOverride,
        RevitColorMode.FixedColor => PointCloudColorModeOption.FixedColor,
        RevitColorMode.Elevation => PointCloudColorModeOption.Elevation,
        RevitColorMode.Intensity => PointCloudColorModeOption.Intensity,
        RevitColorMode.Normals => PointCloudColorModeOption.Normals,
        _ => PointCloudColorModeOption.NoOverride
    };
}
