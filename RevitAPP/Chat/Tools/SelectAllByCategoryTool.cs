using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools;

/// <summary>
/// Selects a complete category in one Revit API call. This intentionally avoids the
/// bounded AI filter -> element id -> operate pipeline used by the optional MCP tools.
/// </summary>
public sealed class SelectAllByCategoryTool : IChatTool
{
    private static readonly IReadOnlyDictionary<string, BuiltInCategory> Categories =
        new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["structural_columns"] = BuiltInCategory.OST_StructuralColumns,
            ["structural_column_tags"] = BuiltInCategory.OST_StructuralColumnTags,
            ["structural_framing"] = BuiltInCategory.OST_StructuralFraming,
            ["walls"] = BuiltInCategory.OST_Walls,
            ["structural_foundations"] = BuiltInCategory.OST_StructuralFoundation,
            ["grids"] = BuiltInCategory.OST_Grids,
            ["rebar"] = BuiltInCategory.OST_Rebar
        };

    public string Name => "select_all_by_category";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;

    public ToolSchema Schema => new(
        Name,
        "Chọn đầy đủ mọi phần tử thuộc một category trong toàn dự án hoặc view hiện tại. " +
        "Dùng trực tiếp cho yêu cầu 'chọn hết/tất cả'; không gọi ai_element_filter trước.",
        new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["category"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray(Categories.Keys),
                    ["description"] = "Nhóm phần tử cần chọn. Cột kết cấu = structural_columns; tag/ký hiệu cột = structural_column_tags."
                },
                ["scope"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("project", "current_view"),
                    ["default"] = "project",
                    ["description"] = "project chọn toàn dự án; current_view chỉ chọn trong view hiện tại."
                }
            },
            ["required"] = new JArray("category")
        });

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var categoryName = input.Value<string>("category") ?? string.Empty;
        if (!Categories.TryGetValue(categoryName, out var category))
            return new { success = false, count = 0, message = $"Category không được hỗ trợ: {categoryName}" };

        var scope = input.Value<string>("scope") ?? "project";
        FilteredElementCollector collector;
        if (scope.Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            collector = new FilteredElementCollector(ctx.Doc);
        }
        else if (scope.Equals("current_view", StringComparison.OrdinalIgnoreCase))
        {
            collector = new FilteredElementCollector(ctx.Doc, ctx.Doc.ActiveView.Id);
        }
        else
        {
            return new { success = false, count = 0, message = $"Scope không hợp lệ: {scope}" };
        }

        var ids = collector
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .Select(element => element.Id)
            .ToList();

        ctx.UiDoc.Selection.SetElementIds(ids);
        ctx.UiDoc.RefreshActiveView();

        return new
        {
            success = true,
            count = ids.Count,
            category = categoryName,
            scope,
            message = $"Đã chọn {ids.Count} phần tử."
        };
    }
}
