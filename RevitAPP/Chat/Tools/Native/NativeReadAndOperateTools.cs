using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools.Native;

internal static class NativeToolSupport
{
    internal static JObject OpenSchema() => new()
    {
        ["type"] = "object", ["properties"] = new JObject(), ["additionalProperties"] = true
    };

    internal static long Id(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    internal static ElementId ElementId(long value)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(value);
#else
        return new ElementId(checked((int)value));
#endif
    }

    internal static IReadOnlyList<ElementId> ReadIds(JObject input)
    {
        var data = input["data"] as JObject ?? input;
        var values = data["elementIds"] as JArray;
        if (values is not null) return values.Values<long>().Distinct().Select(ElementId).ToList();
        var single = data.Value<long?>("elementId");
        return single.HasValue ? new[] { ElementId(single.Value) } : Array.Empty<ElementId>();
    }

    internal static BuiltInCategory? ParseCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse(value, true, out BuiltInCategory category) ? category : null;
    }

    internal static object Describe(Element element) => new
    {
        id = Id(element.Id),
        name = element.Name,
        category = element.Category?.Name ?? string.Empty,
        categoryId = element.Category is null ? 0 : Id(element.Category.Id),
        typeId = Id(element.GetTypeId())
    };
}

public sealed class NativeSayHelloTool : IChatTool
{
    public string Name => "say_hello";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public ToolSchema Schema => new(Name, "Hiện hộp thoại kiểm tra native Revit command. Tham số: message.", NativeToolSupport.OpenSchema());
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var message = input.Value<string>("message") ?? "Hello from RevitAPP Chat AI";
        TaskDialog.Show("RevitAPP Chat AI", message);
        return new { success = true, message };
    }
}

public sealed class NativeGetAvailableFamilyTypesTool : IChatTool
{
    public string Name => "get_available_family_types";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public ToolSchema Schema => new(Name, "Lấy family type; tham số: categoryList, familyNameFilter, limit.", NativeToolSupport.OpenSchema());
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var categories = (input["categoryList"] as JArray)?.Values<string>()
            .Select(NativeToolSupport.ParseCategory).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        var familyFilter = input.Value<string>("familyNameFilter");
        var limit = Math.Clamp(input.Value<int?>("limit") ?? 500, 1, 5000);
        var types = new FilteredElementCollector(ctx.Doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(x => categories is null || categories.Count == 0 || (x.Category is not null && categories.Any(c => NativeToolSupport.Id(x.Category.Id) == (long)c)))
            .Where(x => string.IsNullOrWhiteSpace(familyFilter) || x.FamilyName.IndexOf(familyFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            .Take(limit).Select(x => new { id = NativeToolSupport.Id(x.Id), familyName = x.FamilyName, typeName = x.Name, category = x.Category?.Name ?? string.Empty }).ToList();
        return new { success = true, count = types.Count, familyTypes = types };
    }
}

public sealed class NativeGetCurrentViewElementsTool : IChatTool
{
    public string Name => "get_current_view_elements";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public ToolSchema Schema => new(Name, "Lấy phần tử view hiện tại; tham số: modelCategoryList, annotationCategoryList, includeHidden, limit.", NativeToolSupport.OpenSchema());
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var requested = new HashSet<BuiltInCategory>();
        foreach (var key in new[] { "modelCategoryList", "annotationCategoryList" })
            foreach (var name in (input[key] as JArray)?.Values<string>() ?? Array.Empty<string>())
                if (NativeToolSupport.ParseCategory(name) is { } category) requested.Add(category);
        var includeHidden = input.Value<bool?>("includeHidden") ?? false;
        var limit = Math.Clamp(input.Value<int?>("limit") ?? 1000, 1, 10000);
        IEnumerable<Element> query = new FilteredElementCollector(ctx.Doc, ctx.Doc.ActiveView.Id).WhereElementIsNotElementType();
        if (requested.Count > 0) query = query.Where(e => e.Category is not null && requested.Any(c => NativeToolSupport.Id(e.Category.Id) == (long)c));
        if (!includeHidden) query = query.Where(e => !e.IsHidden(ctx.Doc.ActiveView));
        var elements = query.Take(limit).Select(NativeToolSupport.Describe).ToList();
        return new { success = true, viewId = NativeToolSupport.Id(ctx.Doc.ActiveView.Id), count = elements.Count, elements };
    }
}

public sealed class NativeAiElementFilterTool : IChatTool
{
    public string Name => "ai_element_filter";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public ToolSchema Schema => new(Name, "Tìm phần tử Revit theo category/type và trả về ElementId.", new JObject
    {
        ["type"] = "object", ["properties"] = new JObject { ["data"] = new JObject
        {
            ["type"] = "object", ["properties"] = new JObject
            {
                ["filterCategory"] = new JObject { ["type"] = "string" },
                ["filterElementType"] = new JObject { ["type"] = "string" },
                ["filterFamilySymbolId"] = new JObject { ["type"] = "integer" },
                ["includeTypes"] = new JObject { ["type"] = "boolean", ["default"] = false },
                ["includeInstances"] = new JObject { ["type"] = "boolean", ["default"] = true },
                ["filterVisibleInCurrentView"] = new JObject { ["type"] = "boolean" },
                ["maxElements"] = new JObject { ["type"] = "integer" }
            }
        } }, ["required"] = new JArray("data")
    });
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var data = input["data"] as JObject ?? input;
        var category = NativeToolSupport.ParseCategory(data.Value<string>("filterCategory"));
        var visible = data.Value<bool?>("filterVisibleInCurrentView") ?? false;
        var includeTypes = data.Value<bool?>("includeTypes") ?? false;
        var includeInstances = data.Value<bool?>("includeInstances") ?? true;
        var max = Math.Clamp(data.Value<int?>("maxElements") ?? 1000, 1, 10000);
        FilteredElementCollector collector = visible
            ? new FilteredElementCollector(ctx.Doc, ctx.Doc.ActiveView.Id)
            : new FilteredElementCollector(ctx.Doc);
        IEnumerable<Element> query = collector;
        if (category.HasValue) query = query.Where(e => e.Category is not null && NativeToolSupport.Id(e.Category.Id) == (long)category.Value);
        if (!includeTypes) query = query.Where(e => e is not ElementType);
        if (!includeInstances) query = query.Where(e => e is ElementType);
        var typeName = data.Value<string>("filterElementType");
        if (!string.IsNullOrWhiteSpace(typeName)) query = query.Where(e => e.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        var symbolId = data.Value<long?>("filterFamilySymbolId");
        if (symbolId.HasValue) query = query.Where(e => NativeToolSupport.Id(e.GetTypeId()) == symbolId.Value);
        var elements = query.Take(max).Select(NativeToolSupport.Describe).ToList();
        return new { success = true, count = elements.Count, elements };
    }
}

public sealed class NativeOperateElementTool : IChatTool, IConfirmableChatTool
{
    public string Name => "operate_element";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => true;
    public ToolSchema Schema => new(Name, "Chọn, tô màu, ẩn, cô lập hoặc xóa các ElementId.", new JObject
    {
        ["type"] = "object", ["properties"] = new JObject { ["data"] = new JObject
        {
            ["type"] = "object", ["properties"] = new JObject
            {
                ["elementIds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } },
                ["action"] = new JObject { ["type"] = "string", ["enum"] = new JArray("Select", "SelectionBox", "SetColor", "SetTransparency", "Delete", "Hide", "TempHide", "Isolate", "Unhide", "ResetIsolate", "Highlight") },
                ["transparencyValue"] = new JObject { ["type"] = "number" },
                ["colorValue"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } }
            }, ["required"] = new JArray("elementIds", "action")
        } }, ["required"] = new JArray("data")
    });
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var data = input["data"] as JObject ?? input;
        var action = data.Value<string>("action") ?? string.Empty;
        var ids = NativeToolSupport.ReadIds(input).Where(id => ctx.Doc.GetElement(id) is not null).ToList();
        if (ids.Count == 0) return new { success = false, count = 0, message = "Không tìm thấy ElementId hợp lệ." };
        if (action.Equals("Select", StringComparison.OrdinalIgnoreCase) || action.Equals("SelectionBox", StringComparison.OrdinalIgnoreCase))
        {
            ctx.UiDoc.Selection.SetElementIds(ids);
            if (action.Equals("SelectionBox", StringComparison.OrdinalIgnoreCase)) ctx.UiDoc.ShowElements(ids);
        }
        else
        {
            using var tx = new Transaction(ctx.Doc, "Chat AI - " + action);
            tx.Start();
            if (action.Equals("Delete", StringComparison.OrdinalIgnoreCase)) ctx.Doc.Delete(ids);
            else if (action.Equals("Hide", StringComparison.OrdinalIgnoreCase)) ctx.Doc.ActiveView.HideElements(ids);
            else if (action.Equals("Unhide", StringComparison.OrdinalIgnoreCase)) ctx.Doc.ActiveView.UnhideElements(ids);
            else if (action.Equals("TempHide", StringComparison.OrdinalIgnoreCase)) ctx.Doc.ActiveView.HideElementsTemporary(ids);
            else if (action.Equals("Isolate", StringComparison.OrdinalIgnoreCase)) ctx.Doc.ActiveView.IsolateElementsTemporary(ids);
            else if (action.Equals("ResetIsolate", StringComparison.OrdinalIgnoreCase)) ctx.Doc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            else if (action.Equals("SetColor", StringComparison.OrdinalIgnoreCase) || action.Equals("Highlight", StringComparison.OrdinalIgnoreCase))
            {
                var rgb = (data["colorValue"] as JArray)?.Values<byte>().ToArray() ?? new byte[] { 255, 0, 0 };
                if (rgb.Length < 3) rgb = new byte[] { 255, 0, 0 };
                var settings = new OverrideGraphicSettings().SetProjectionLineColor(new Color(rgb[0], rgb[1], rgb[2]));
                foreach (var id in ids) ctx.Doc.ActiveView.SetElementOverrides(id, settings);
            }
            else if (action.Equals("SetTransparency", StringComparison.OrdinalIgnoreCase))
            {
                var value = Math.Clamp((int)Math.Round(data.Value<double?>("transparencyValue") ?? 50), 0, 100);
                var settings = new OverrideGraphicSettings().SetSurfaceTransparency(value);
                foreach (var id in ids) ctx.Doc.ActiveView.SetElementOverrides(id, settings);
            }
            else { tx.RollBack(); return new { success = false, count = 0, message = $"Action không được hỗ trợ: {action}" }; }
            tx.Commit();
        }
        ctx.UiDoc.RefreshActiveView();
        return new { success = true, count = ids.Count, action, elementIds = ids.Select(NativeToolSupport.Id).ToList() };
    }
}

public sealed class NativeDeleteElementTool : IChatTool, IConfirmableChatTool
{
    public string Name => "delete_element";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => true;
    public ToolSchema Schema => new(Name, "Xóa vĩnh viễn phần tử theo elementId hoặc elementIds.",
        new JsonSchemaBuilder().Integer("elementId", "ElementId cần xóa.")
            .IntegerArray("elementIds", "Nhiều ElementId cần xóa.").Build());
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var ids = NativeToolSupport.ReadIds(input).Where(id => ctx.Doc.GetElement(id) is not null).ToList();
        if (ids.Count == 0) return new { success = false, count = 0, message = "Không tìm thấy ElementId hợp lệ." };
        using var tx = new Transaction(ctx.Doc, "Chat AI - Delete elements");
        tx.Start();
        var deleted = ctx.Doc.Delete(ids).Select(NativeToolSupport.Id).ToList();
        tx.Commit();
        return new { success = true, requestedCount = ids.Count, count = deleted.Count, deletedElementIds = deleted };
    }
}

public sealed class NativeExportRoomDataTool : IChatTool
{
    public string Name => "export_room_data";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public ToolSchema Schema => new(Name, "Xuất dữ liệu phòng gồm diện tích, thể tích, chu vi và level.", NativeToolSupport.OpenSchema());
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var includeUnplaced = input.Value<bool?>("includeUnplacedRooms") ?? false;
        var includeNotEnclosed = input.Value<bool?>("includeNotEnclosedRooms") ?? false;
        var rooms = new FilteredElementCollector(ctx.Doc).OfClass(typeof(SpatialElement)).OfType<Room>()
            .Where(r => (includeUnplaced || r.Location is not null) && (includeNotEnclosed || r.Area > 0))
            .Select(r => new { id = NativeToolSupport.Id(r.Id), number = r.Number, name = r.Name, level = ctx.Doc.GetElement(r.LevelId)?.Name ?? string.Empty, areaSquareFeet = r.Area, volumeCubicFeet = r.Volume, perimeterFeet = r.Perimeter }).ToList();
        return new { success = true, count = rooms.Count, rooms };
    }
}

public sealed class NativeGetMaterialQuantitiesTool : IChatTool
{
    public string Name => "get_material_quantities";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public ToolSchema Schema => new(Name, "Bóc khối lượng vật liệu theo category hoặc phần tử đang chọn.", NativeToolSupport.OpenSchema());
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var selectedOnly = input.Value<bool?>("selectedElementsOnly") ?? false;
        var filters = (input["categoryFilters"] as JArray)?.Values<string>().Select(NativeToolSupport.ParseCategory).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        IEnumerable<Element> elements = selectedOnly
            ? ctx.UiDoc.Selection.GetElementIds().Select(ctx.Doc.GetElement).Where(e => e is not null)!
            : new FilteredElementCollector(ctx.Doc).WhereElementIsNotElementType();
        if (filters is { Count: > 0 }) elements = elements.Where(e => e.Category is not null && filters.Any(c => NativeToolSupport.Id(e.Category.Id) == (long)c));
        var totals = new Dictionary<long, (string Name, double Area, double Volume, int Elements)>();
        foreach (var element in elements)
        foreach (var materialId in element.GetMaterialIds(false))
        {
            var key = NativeToolSupport.Id(materialId);
            var current = totals.TryGetValue(key, out var found)
                ? found
                : (Name: ctx.Doc.GetElement(materialId)?.Name ?? string.Empty, Area: 0d, Volume: 0d, Elements: 0);
            totals[key] = (current.Name, current.Area + element.GetMaterialArea(materialId, false), current.Volume + element.GetMaterialVolume(materialId), current.Elements + 1);
        }
        var materials = totals.Select(x => new { materialId = x.Key, name = x.Value.Name, areaSquareFeet = x.Value.Area, volumeCubicFeet = x.Value.Volume, elementCount = x.Value.Elements }).ToList();
        return new { success = true, count = materials.Count, materials };
    }
}

public sealed class NativeAnalyzeModelStatisticsTool : IChatTool
{
    public string Name => "analyze_model_statistics";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public ToolSchema Schema => new(Name, "Thống kê model theo category, type, family và level.", NativeToolSupport.OpenSchema());
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var detailed = input.Value<bool?>("includeDetailedTypes") ?? false;
        var elements = new FilteredElementCollector(ctx.Doc).WhereElementIsNotElementType().ToList();
        var categories = elements.GroupBy(e => e.Category?.Name ?? "<No Category>").OrderByDescending(g => g.Count())
            .Select(g => new { name = g.Key, count = g.Count() }).ToList();
        object? types = null;
        if (detailed)
            types = elements.GroupBy(e => e.GetTypeId()).Select(g => new { typeId = NativeToolSupport.Id(g.Key), name = ctx.Doc.GetElement(g.Key)?.Name ?? string.Empty, count = g.Count() }).OrderByDescending(x => x.count).ToList();
        return new { success = true, totalElements = elements.Count, categoryCount = categories.Count, categories, types };
    }
}
