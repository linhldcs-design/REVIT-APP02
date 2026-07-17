using Autodesk.Revit.UI;
using RevitAPP.Licensing;

namespace RevitAPP.Commands;

internal static class LicenseCommandGate
{
    public static bool Ensure(string commandTitle)
    {
        var (ok, message) = LicenseService.EnsureValid();
        if (!ok) TaskDialog.Show(commandTitle, message);
        return ok;
    }
}
