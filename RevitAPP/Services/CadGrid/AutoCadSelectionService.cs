using System.Reflection;
using System.Runtime.InteropServices;
using RevitAPP.Core.Models.CadGrid;
using Serilog;

namespace RevitAPP.Services.CadGrid;

internal sealed record AutoCadSelectionResult(
    CadGridTransferPackage? Package,
    string? Error)
{
    public bool IsValid => Package is not null && string.IsNullOrWhiteSpace(Error);

    public static AutoCadSelectionResult Failed(string error) => new(null, error);
}

/// <summary>
/// Drives a running AutoCAD instance over COM so the user can pick grid lines without
/// leaving Revit or running a command on the AutoCAD side.
/// <para>
/// Every COM call goes through <see cref="InvokeMember"/> rather than the C# `dynamic`
/// keyword. `dynamic` drags in the runtime binder, which loads System.Linq.Expressions
/// 6.0.0.0 and collides with the 8.0.0.0 copy Revit has already preloaded — Revit then
/// refuses the whole command with an assembly version conflict. Plain reflection has no
/// such dependency, and it keeps the add-in free of AutoCAD interop assemblies so one
/// build works across AutoCAD releases.
/// </para>
/// </summary>
internal static class AutoCadSelectionService
{
    // AutoCAD's version-neutral ProgID can point at an older registration even while a
    // newer release is running. Try every supported release explicitly, newest first.
    private static readonly string[] ProgIds =
    {
        "AutoCAD.Application.26",   // AutoCAD 2027
        "AutoCAD.Application.25.1", // AutoCAD 2026
        "AutoCAD.Application.25",   // AutoCAD 2025
        "AutoCAD.Application.24.3", // AutoCAD 2024
        "AutoCAD.Application"
    };

    /// <summary>Name of the transient selection set reused on every pick.</summary>
    private const string SelectionSetName = "LDL_GRID_PICK";

    /// <summary>DXF group code 0 selects on entity type.</summary>
    private const short DxfEntityType = 0;

    /// <summary>
    /// Brings AutoCAD forward, asks the user to select lines there, and returns them as a
    /// transfer package. The AutoCAD document is only read, never modified.
    /// </summary>
    public static AutoCadSelectionResult SelectLines()
    {
        object? application = null;
        object? document = null;
        object? utility = null;
        object? selection = null;
        try
        {
            application = GetRunningInstance();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not attach to AutoCAD over COM");
            return AutoCadSelectionResult.Failed(
                "Không tìm thấy AutoCAD đang mở.\n\n"
                + "Hãy mở AutoCAD cùng bản vẽ chứa lưới trục rồi thử lại.");
        }

        if (application is null)
            return AutoCadSelectionResult.Failed("Không tìm thấy AutoCAD đang mở.");

        try
        {
            document = Get(application, "ActiveDocument");
            if (document is null)
                return AutoCadSelectionResult.Failed(
                    "AutoCAD đang mở nhưng không có bản vẽ nào.");

            Set(application, "Visible", true);
            TryActivate(application, document);

            selection = CreateSelectionSet(document);
            if (selection is null)
                return AutoCadSelectionResult.Failed(
                    "Không tạo được vùng chọn trong AutoCAD.");

            utility = Get(document, "Utility");
            if (utility is not null)
                Call(utility, "Prompt",
                    "\nQuét chọn các đường LINE lưới trục rồi nhấn Enter...\n");

            // Filter to LINE so a stray dimension or text cannot enter the selection.
            Call(
                selection,
                "SelectOnScreen",
                new short[] { DxfEntityType },
                new object[] { "LINE" });

            var lines = ReadLines(selection);
            if (lines.Count == 0)
                return AutoCadSelectionResult.Failed(
                    "Chưa chọn được đường LINE nào trong AutoCAD.");
            if (lines.Count < 2)
                return AutoCadSelectionResult.Failed("Cần chọn ít nhất 2 đường LINE.");

            Log.Information("Picked {LineCount} lines from AutoCAD", lines.Count);

            return new AutoCadSelectionResult(
                new CadGridTransferPackage(
                    CadGridTransferPackage.CurrentSchemaVersion,
                    Guid.NewGuid().ToString("N"),
                    DateTime.UtcNow,
                    Safe(() => Get(document, "Name")?.ToString()) ?? "AutoCAD",
                    Safe(() => Get(application, "Version")?.ToString()) ?? string.Empty,
                    ReadInsUnits(document),
                    lines),
                null);
        }
        catch (Exception exception) when (IsUserCancel(exception))
        {
            // Escape during SelectOnScreen is a normal cancel, not a failure.
            return AutoCadSelectionResult.Failed(string.Empty);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "AutoCAD COM selection failed");
            return AutoCadSelectionResult.Failed(
                "Không lấy được vùng chọn từ AutoCAD.\n\n" + Innermost(exception).Message);
        }
        finally
        {
            if (selection is not null) TryDelete(selection);
            Release(selection);
            Release(utility);
            Release(document);
            Release(application);
        }
    }

    /// <summary>
    /// Replaces any selection set left over from an earlier run: AutoCAD refuses to add a
    /// set whose name is already taken.
    /// </summary>
    private static object? CreateSelectionSet(object document)
    {
        object? sets = Get(document, "SelectionSets");
        if (sets is null) return null;

        try
        {
            var count = Convert.ToInt32(Get(sets, "Count"));
            for (var index = count - 1; index >= 0; index--)
            {
                object? existing = null;
                try
                {
                    existing = Call(sets, "Item", index);
                    if (!string.Equals(
                            Get(existing!, "Name")?.ToString(),
                            SelectionSetName,
                            StringComparison.OrdinalIgnoreCase)) continue;

                    Call(existing!, "Delete");
                    break;
                }
                finally
                {
                    Release(existing);
                }
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not clear previous AutoCAD selection set");
        }

        try
        {
            return Call(sets, "Add", SelectionSetName);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not create AutoCAD selection set");
            return null;
        }
        finally
        {
            Release(sets);
        }
    }

    private static IReadOnlyList<CadGridTransferLine> ReadLines(object selection)
    {
        var lines = new List<CadGridTransferLine>();
        var id = 1;

        var count = Convert.ToInt32(Get(selection, "Count"));
        for (var index = 0; index < count; index++)
        {
            object? entity = null;
            try
            {
                entity = Call(selection, "Item", index);
                if (entity is null) continue;
                if (!string.Equals(
                        Get(entity, "ObjectName")?.ToString(),
                        "AcDbLine",
                        StringComparison.Ordinal)) continue;

                if (Get(entity, "StartPoint") is not double[] start
                    || Get(entity, "EndPoint") is not double[] end
                    || start.Length < 2
                    || end.Length < 2) continue;

                lines.Add(new CadGridTransferLine(id++, start[0], start[1], end[0], end[1]));
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Skipped an unreadable AutoCAD entity");
            }
            finally
            {
                Release(entity);
            }
        }

        return lines;
    }

    private static int ReadInsUnits(object document)
    {
        try
        {
            var value = Call(document, "GetVariable", "INSUNITS");
            return value is null ? 4 : Convert.ToInt32(value);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not read INSUNITS; assuming millimetres");
            return 4;
        }
    }

    private static void TryActivate(object application, object document)
    {
        try
        {
            Call(document, "Activate");
            Set(application, "WindowState", 3); // acMax
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not bring AutoCAD to the foreground");
        }
    }

    private static void TryDelete(object selection)
    {
        try
        {
            Call(selection, "Delete");
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not delete temporary AutoCAD selection set");
        }
    }

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            null,
            target,
            null);

    private static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.SetProperty,
            null,
            target,
            new[] { value });

    private static object? Call(object target, string name, params object[] arguments) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            null,
            target,
            arguments);

    private static T? Safe<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
    }

    private static void Release(object? value)
    {
        if (value is null || !Marshal.IsComObject(value)) return;

        try
        {
            Marshal.ReleaseComObject(value);
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Could not release AutoCAD COM object");
        }
    }

    private static Exception Innermost(Exception exception) =>
        exception.InnerException is null ? exception : Innermost(exception.InnerException);

    /// <summary>Escape or a right-click cancel surfaces as one of these HRESULTs.</summary>
    private static bool IsUserCancel(Exception exception)
    {
        var hresult = Innermost(exception).HResult;
        return hresult is unchecked((int)0x80004004) // E_ABORT
            or unchecked((int)0x8004005E);
    }

    /// <summary>
    /// .NET (Core) dropped Marshal.GetActiveObject, so the running-object table is queried
    /// through the OLE automation entry points it used to wrap.
    /// </summary>
    private static object? GetRunningInstance()
    {
        foreach (var progId in ProgIds)
        {
            var hresult = CLSIDFromProgID(progId, out var classId);
            if (hresult < 0) continue;

            hresult = GetActiveObject(ref classId, IntPtr.Zero, out var instance);
            if (hresult >= 0 && instance is not null) return instance;
        }

        return null;
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CLSIDFromProgID(string progId, out Guid classId);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int GetActiveObject(
        ref Guid classId,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}
