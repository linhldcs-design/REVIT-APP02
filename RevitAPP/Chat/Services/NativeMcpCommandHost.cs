using System.Reflection;
using System.IO;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitAPP.Chat.Services;

/// <summary>
/// Loads the installed Revit MCP command assembly directly and invokes its command objects.
/// Commands keep their own ExternalEvent, so no TCP server or localhost connection is required.
/// Initialize must run inside a valid Revit API context.
/// </summary>
public static class NativeMcpCommandHost
{
    private static readonly string[] RequiredCommands =
    {
        "say_hello", "get_available_family_types", "get_current_view_elements",
        "create_point_based_element", "create_line_based_element", "create_surface_based_element",
        "color_splash", "tag_walls", "delete_element", "ai_element_filter", "operate_element",
        "export_room_data", "get_material_quantities", "analyze_model_statistics", "create_grid",
        "create_structural_framing_system", "create_room", "tag_rooms", "create_level",
        "send_code_to_revit", "create_dimensions"
    };
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CommandEntry> Commands = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static void Initialize(UIApplication uiApplication)
    {
        lock (Gate)
        {
            if (_initialized) return;

            var registryPath = FindRegistryPath(uiApplication.Application.VersionNumber);
            if (registryPath is null)
                throw new FileNotFoundException("Không tìm thấy commandRegistry.json của Revit MCP.");

            var registry = JObject.Parse(File.ReadAllText(registryPath));
            var assemblyPaths = (registry["Commands"] as JArray ?? registry["commands"] as JArray ?? new JArray())
                .Where(c => c.Value<bool?>("enabled") != false)
                .Select(c => c.Value<string>("assemblyPath"))
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var path in assemblyPaths)
                LoadCommands(path!, uiApplication);

            var missing = RequiredCommands.Where(name => !Commands.ContainsKey(name)).ToArray();
            WriteDiagnostic(registryPath, missing);
            if (missing.Length > 0)
                throw new InvalidOperationException(
                    $"Chỉ nạp được {RequiredCommands.Length - missing.Length}/{RequiredCommands.Length} Revit command. Thiếu: {string.Join(", ", missing)}");

            _initialized = true;
        }
    }

    public static string Execute(string method, JObject parameters)
    {
        CommandEntry entry;
        lock (Gate)
        {
            if (!_initialized)
                return Error("Native MCP command host chưa được khởi tạo. Hãy đóng và mở lại cửa sổ Chat AI.");
            if (!Commands.TryGetValue(method, out entry!))
                return Error($"Không tìm thấy native MCP command '{method}'.");
        }

        try
        {
            // RevitAPP is IL-repacked, while the MCP command assembly references its own
            // Newtonsoft.Json.dll. JObject therefore has a different assembly identity even
            // though its full type name is identical. Cross the boundary as JSON text.
            var foreignParameters = entry.ParseParameters.Invoke(null, new object[] { parameters.ToString(Formatting.None) });
            var output = entry.Execute.Invoke(entry.Instance,
                new[] { foreignParameters, "chat-" + Guid.NewGuid().ToString("N") });
            return output is string text ? text : JsonConvert.SerializeObject(output);
        }
        catch (TargetInvocationException ex)
        {
            return Error(ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    public static int Count { get { lock (Gate) return Commands.Count; } }

    private static void LoadCommands(string assemblyPath, UIApplication uiApplication)
    {
        var directory = Path.GetDirectoryName(assemblyPath)!;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var dependency = Path.Combine(directory, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
        };

        var assembly = Assembly.LoadFrom(assemblyPath);
        foreach (var type in SafeTypes(assembly))
        {
            if (type.IsAbstract) continue;
            var constructor = type.GetConstructor(new[] { typeof(UIApplication) });
            var nameProperty = type.GetProperty("CommandName", BindingFlags.Instance | BindingFlags.Public);
            var execute = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method => method.Name == "Execute" &&
                    method.GetParameters() is var args && args.Length == 2 &&
                    args[0].ParameterType.FullName == "Newtonsoft.Json.Linq.JObject" &&
                    args[1].ParameterType == typeof(string));
            var parameterType = execute?.GetParameters()[0].ParameterType;
            var parseParameters = parameterType?.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(string) }, null);
            if (constructor is null || nameProperty is null || execute is null || parseParameters is null) continue;

            try
            {
                var instance = constructor.Invoke(new object[] { uiApplication });
                var name = nameProperty.GetValue(instance) as string;
                if (!string.IsNullOrWhiteSpace(name)) Commands[name!] = new CommandEntry(instance, execute, parseParameters);
            }
            catch { /* unrelated/unsupported command type */ }
        }
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static string? FindRegistryPath(string revitVersion)
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Autodesk", "Revit", "Addins", revitVersion, "revit_mcp_plugin", "Commands", "commandRegistry.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "Revit", "Addins", revitVersion, "revit_mcp_plugin", "Commands", "commandRegistry.json")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void WriteDiagnostic(string registryPath, IReadOnlyCollection<string> missing)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitAPP");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "native-command-health.txt"),
                $"Time: {DateTime.Now:O}{Environment.NewLine}" +
                $"Registry: {registryPath}{Environment.NewLine}" +
                $"Loaded: {Commands.Count}{Environment.NewLine}" +
                $"Commands: {string.Join(", ", Commands.Keys.OrderBy(name => name))}{Environment.NewLine}" +
                $"Missing required: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}{Environment.NewLine}");
        }
        catch { /* diagnostics must not prevent Chat startup */ }
    }

    private static string Error(string message) => JsonConvert.SerializeObject(new { success = false, message });
    private sealed record CommandEntry(object Instance, MethodInfo Execute, MethodInfo ParseParameters);
}
