using System.IO;
using RevitAPP.Core.Models.DwgExport;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.DwgExport;

public sealed record DwgSetupOption(string Name, DwgFileVersion DefaultVersion)
{
    public override string ToString() => Name;
}

public sealed record DwgVersionOption(DwgFileVersion Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record DwgSheetPreviewItem(
    int Ordinal,
    long SheetId,
    string SheetNumber,
    string SheetName,
    string ScalesLabel,
    bool IsMixedScale,
    bool IsValid,
    string StatusMessage);

public sealed record PrintSetOption(
    long ElementId,
    string Name,
    IReadOnlyList<DwgSheetPreviewItem> Sheets)
{
    public override string ToString() => Name;
}

public sealed record DwgExportRequest(
    string SetupName,
    DwgFileVersion FileVersion,
    PrintSetOption PrintSet,
    string OutputPath);

public interface IDwgOutputPathPicker
{
    string? Pick(string suggestedFileName, string currentPath);
}

public sealed class DwgOutputPathPicker : IDwgOutputPathPicker
{
    public string? Pick(string suggestedFileName, string currentPath)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "AutoCAD drawing (*.dwg)|*.dwg",
            DefaultExt = ".dwg",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(currentPath)
                ? suggestedFileName
                : Path.GetFileName(currentPath),
            InitialDirectory = DwgOutputPathPolicy.GetExistingInitialDirectory(currentPath)
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
