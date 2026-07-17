using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Commands;
using BeamRebarStartupCommand = BeamRebarPro.Commands.StartupCommand;
using FootingDrawingCommand = FootingDrawing.Addin.Commands.FootingDrawingCommand;
using FootingRebarStartupCommand = IsolatedFootingRebar.Commands.StartupCommand;
using WallRebarStartupCommand = WallRebar.Commands.StartupCommand;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Danh mục tool cho panel chat: gom schema (gửi cho LLM) và dispatch execute theo tên.
///     Việc mở Transaction + license gate do bridge (phase 4) xử lý dựa trên cờ của từng tool.
/// </summary>
public sealed class ChatToolRegistry
{
    private readonly Dictionary<string, IChatTool> _tools;

    public ChatToolRegistry()
    {
        var tools = new List<IChatTool>
        {
            new DrawColumnRebarTool(),
            new DrawBeamRebarTool(),
            new DrawWallRebarTool(),
            new DrawFootingRebarTool(),
            new DrawBeamDrawingTool(),
            new GetSelectedElementsTool(),
            new GetCurrentViewInfoTool(),
            new GetOpenExcelWorkbooksTool(),
            new FindExcelFilesTool(),
            new InspectExcelFileTool(),
            new ReadExcelTableTool()
        };
        tools.AddRange(CreateMcpProxyTools());
        tools.AddRange(CreateRibbonCommandTools());
        _tools = tools.ToDictionary(tool => tool.Name);
    }

    public IReadOnlyList<ToolSchema> Schemas => _tools.Values.Select(tool => tool.Schema).ToList();

    public bool TryGet(string name, out IChatTool tool) => _tools.TryGetValue(name, out tool!);

    public IChatTool Get(string name) =>
        _tools.TryGetValue(name, out var tool)
            ? tool
            : throw new ArgumentException($"Tool không tồn tại: {name}");

    private static IEnumerable<IChatTool> CreateMcpProxyTools()
    {
        yield return Proxy("say_hello", "say_hello", "Hiện hộp thoại kiểm tra native Revit command. Tham số: message.");
        yield return Proxy("get_available_family_types", "get_available_family_types", "Lấy family type; tham số: categoryList, familyNameFilter, limit.");
        yield return Proxy("get_current_view_elements", "get_current_view_elements", "Lấy phần tử view hiện tại; tham số: modelCategoryList, annotationCategoryList, includeHidden, limit.");
        yield return Proxy("create_point_based_element", "create_point_based_element", "Tạo family đặt theo điểm. Truyền đúng tham số MCP: familyTypeId và các điểm/toạ độ mm.", write: true);
        yield return Proxy("create_line_based_element", "create_line_based_element", "Tạo phần tử theo đường như tường. Truyền family/type, level và đường đầu-cuối theo mm.", write: true);
        yield return Proxy("create_surface_based_element", "create_surface_based_element", "Tạo phần tử theo mặt như sàn. Truyền type, level và boundary theo mm.", write: true);
        yield return Proxy("color_elements", "color_splash", "Tô màu phần tử theo category và parameter. Tham số: categoryName, parameterName, useGradient, customColors.", write: true);
        yield return Proxy("tag_all_walls", "tag_walls", "Gắn tag cho tường trong view. Tham số: useLeader, tagTypeId.", write: true);
        yield return new RevitMcpProxyTool("delete_element", "delete_element",
            "Xóa vĩnh viễn phần tử theo ElementId. Tham số: elementId hoặc elementIds.",
            new JsonSchemaBuilder().Integer("elementId", "ElementId cần xóa.").IntegerArray("elementIds", "Nhiều ElementId cần xóa.").Build(), true, true);
        yield return new RevitMcpProxyTool("ai_element_filter", "ai_element_filter",
            "Tìm phần tử Revit theo category/type. Ví dụ lưới: data.filterCategory=OST_Grids. Kết quả trả về ElementId để truyền sang operate_element.",
            AiElementFilterSchema());
        yield return new RevitMcpProxyTool("operate_element", "operate_element",
            "Chọn, tô màu, ẩn, cô lập hoặc xóa các ElementId. Muốn chọn tất cả phần tử đã tìm được: data.action=Select.",
            OperateElementSchema(), true, true);
        yield return Proxy("export_room_data", "export_room_data", "Xuất dữ liệu phòng. Tham số: includeUnplacedRooms, includeNotEnclosedRooms.");
        yield return Proxy("get_material_quantities", "get_material_quantities", "Bóc khối lượng vật liệu. Tham số: categoryFilters, selectedElementsOnly.");
        yield return Proxy("analyze_model_statistics", "analyze_model_statistics", "Thống kê model. Tham số: includeDetailedTypes.");
        yield return Proxy("create_grid", "create_grid", "Tạo hệ trục. Tham số bắt buộc: xCount, xSpacing, yCount, ySpacing; đơn vị mm.", write: true);
        yield return Proxy("create_structural_framing_system", "create_structural_framing_system", "Tạo hệ dầm kết cấu theo biên chữ nhật và khoảng cách, đơn vị mm.", write: true);
        yield return Proxy("create_room", "create_room", "Tạo phòng tại level/toạ độ và đặt tên, số phòng.", write: true);
        yield return Proxy("tag_all_rooms", "tag_rooms", "Gắn tag phòng trong view. Tham số: useLeader, tagTypeId, roomIds.", write: true);
        yield return Proxy("create_level", "create_level", "Tạo level theo cao độ mm, có thể tạo floor plan.", write: true);
        yield return new RevitMcpProxyTool("send_code_to_revit", "send_code_to_revit",
            "Chạy C# trực tiếp trong Revit. Code có sẵn biến Document và parameters; không tự mở Transaction.",
            new JsonSchemaBuilder()
                .Text("code", "Mã C# đặt trong thân Execute; dùng biến Document để truy cập model.", true)
                .Build(), true, true);
        yield return Proxy("create_dimensions", "create_dimensions", "Tạo kích thước. Tham số dimensions gồm startPoint, endPoint, linePoint, elementIds, viewId; đơn vị mm.", write: true);
    }

    private static IEnumerable<IChatTool> CreateRibbonCommandTools()
    {
        yield return Ribbon("open_license", "Mở đúng cửa sổ License của nút RevitAPP License.", () => new LicenseCommand().Execute());
        yield return Ribbon("run_hello_world", "Chạy đúng nút RevitAPP Hello World.", () => new HelloWorldCommand().Execute());
        yield return Ribbon("open_translate_text", "Mở quy trình Dịch Text của RevitAPP; dùng TextNote đang chọn hoặc cho phép pick.", () => new TranslateTextCommand().Execute());
        yield return Ribbon("open_renumber_schedule", "Mở cửa sổ Đánh Số Schedule của RevitAPP.", () => new RenumberScheduleCommand().Execute());
        yield return Ribbon("open_column_rebar", "Mở đúng cửa sổ Vẽ Thép Cột của RevitAPP.", () => new DrawColumnRebarCommand().Execute());
        yield return Ribbon("open_beam_drawing", "Mở đúng cửa sổ Bản Vẽ Dầm của RevitAPP.", () => new BeamDrawingCommand().Execute());
        yield return Ribbon("open_beam_rebar", "Mở đúng cửa sổ Vẽ Thép Dầm của RevitAPP.", () => new BeamRebarStartupCommand().Execute());
        yield return Ribbon("open_footing_rebar", "Mở đúng cửa sổ Vẽ Móng Đơn của RevitAPP.", () => new FootingRebarStartupCommand().Execute());
        yield return Ribbon("open_footing_drawing", "Mở đúng công cụ Bản Vẽ Móng của RevitAPP.", () => new FootingDrawingCommand().Execute());
        yield return Ribbon("open_footing_section", "Mở đúng công cụ Mặt Cắt Móng của RevitAPP.", () => new FootingSectionDrawingCommand().Execute());
        yield return Ribbon("open_wall_rebar", "Mở đúng cửa sổ Vẽ Thép Tường của RevitAPP.", () => new WallRebarStartupCommand().Execute());
        yield return Ribbon("open_align_views", "Mở đúng công cụ Căn Chỉnh View của RevitAPP.", () => new AlignSheetViewportsCommand().Execute());
        yield return Ribbon("toggle_point_cloud", "Chạy đúng nút Point Cloud để hiện hoặc ẩn panel.", () => new TogglePointCloudPanelCommand().Execute());
        yield return Ribbon("run_point_cloud_poc", "Chạy đúng nút PC POC của RevitAPP.", () => new PointCloudPocCommand().Execute());
        yield return new RevitMcpProxyTool("focus_chat_ai", "__focus_chat_ai",
            "Đại diện nút Chat AI: cửa sổ Chat hiện tại đã ở phía trước; không mở lồng thêm cửa sổ.");
    }

    private static RibbonCommandTool Ribbon(string name, string description, Action action) =>
        new(name, description, action);

    private static RevitMcpProxyTool Proxy(string name, string method, string description,
        bool write = false, bool dangerous = false) =>
        new(name, method, description, requiresConfirmation: write, isDangerous: dangerous);

    private static JObject AiElementFilterSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JObject
        {
            ["data"] = new JObject
            {
                ["type"] = "object",
                ["description"] = "Điều kiện lọc phần tử Revit.",
                ["properties"] = new JObject
                {
                    ["filterCategory"] = new JObject { ["type"] = "string", ["description"] = "BuiltInCategory, ví dụ OST_Grids, OST_Walls, OST_StructuralFraming." },
                    ["filterElementType"] = new JObject { ["type"] = "string" },
                    ["filterFamilySymbolId"] = new JObject { ["type"] = "integer" },
                    ["includeTypes"] = new JObject { ["type"] = "boolean", ["default"] = false },
                    ["includeInstances"] = new JObject { ["type"] = "boolean", ["default"] = true },
                    ["filterVisibleInCurrentView"] = new JObject { ["type"] = "boolean" },
                    ["maxElements"] = new JObject { ["type"] = "integer", ["description"] = "Số phần tử tối đa." }
                }
            }
        },
        ["required"] = new JArray("data")
    };

    private static JObject OperateElementSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JObject
        {
            ["data"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["elementIds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } },
                    ["action"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("Select", "SelectionBox", "SetColor", "SetTransparency", "Delete", "Hide", "TempHide", "Isolate", "Unhide", "ResetIsolate", "Highlight")
                    },
                    ["transparencyValue"] = new JObject { ["type"] = "number" },
                    ["colorValue"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } }
                },
                ["required"] = new JArray("elementIds", "action")
            }
        },
        ["required"] = new JArray("data")
    };
}
