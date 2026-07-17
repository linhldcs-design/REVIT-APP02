using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace RevitAPP.Commands
{
    /// <summary>
    ///     Shows a first hello world message in Revit.
    /// </summary>
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class HelloWorldCommand : ExternalCommand
    {
        public override void Execute()
        {
            if (!LicenseCommandGate.Ensure("Hello World")) return;
            TaskDialog.Show("RevitAI", "Hello World from your first Revit add-in!");
        }
    }
}
