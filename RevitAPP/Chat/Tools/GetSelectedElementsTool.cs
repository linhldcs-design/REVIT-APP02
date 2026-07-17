using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Đọc các phần tử đang chọn trong Revit (id, category, tên). Read-only — không cần license/transaction.
///     Giúp LLM lấy id để truyền vào các tool vẽ thép.
/// </summary>
public sealed class GetSelectedElementsTool : IChatTool
{
    public string Name => "get_selected_elements";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;

    public ToolSchema Schema => new(
        Name,
        "Lấy danh sách phần tử đang được chọn trong Revit (id, category, tên). Dùng để biết id cột/dầm/tường/móng " +
        "người dùng đang chọn trước khi vẽ thép.",
        new JsonSchemaBuilder().Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var elements = new List<object>();
        foreach (var id in ctx.UiDoc.Selection.GetElementIds())
        {
            var element = ctx.Doc.GetElement(id);
            if (element == null) continue;
            elements.Add(new
            {
                id = ElementIdValue(id),
                categoryId = element.Category == null ? 0 : ElementIdValue(element.Category.Id),
                category = element.Category?.Name ?? string.Empty,
                name = element.Name
            });
        }

        return new { success = true, count = elements.Count, elements };
    }

    private static long ElementIdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
