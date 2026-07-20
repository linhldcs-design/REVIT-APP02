using System.Collections.Generic;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Tools.Native;
using RevitAPP.Commands;
using BeamRebarStartupCommand = BeamRebarPro.Commands.StartupCommand;
using FootingDrawingCommand = FootingDrawing.Addin.Commands.FootingDrawingCommand;
using FootingRebarStartupCommand = IsolatedFootingRebar.Commands.StartupCommand;
using WallRebarStartupCommand = WallRebar.Commands.StartupCommand;

namespace RevitAPP.Chat.Tools;

/// <summary>Danh mục tool gửi cho LLM và dispatch theo tên.</summary>
public sealed class ChatToolRegistry
{
    private readonly Dictionary<string, IChatTool> _tools;

    public ChatToolRegistry()
    {
        var tools = new List<IChatTool>
        {
            new DrawColumnRebarTool(),
            new DrawBeamRebarTool(),
            new DrawBeamRebarFromOpenExcelTool(),
            new DrawWallRebarTool(),
            new DrawFootingRebarTool(),
            new DrawBeamDrawingTool(),
            new DrawFootingDrawingTool(),
            new DrawFootingSectionTool(),
            new ArrangeFootingSheetTool(),
            new DrawAndArrangeFootingSheetTool(),
            new GetSelectedElementsTool(),
            new GetCurrentViewInfoTool(),
            new SelectAllByCategoryTool(),
            new GetOpenExcelWorkbooksTool(),
            new FindExcelFilesTool(),
            new InspectExcelFileTool(),
            new ReadExcelTableTool()
        };
        tools.AddRange(CreateNativeAutomationTools());
        tools.AddRange(CreateRibbonCommandTools());
        _tools = tools.ToDictionary(tool => tool.Name);
    }

    public IReadOnlyList<ToolSchema> Schemas => _tools.Values.Select(tool => tool.Schema).ToList();

    public bool TryGet(string name, out IChatTool tool) => _tools.TryGetValue(name, out tool!);

    public IChatTool Get(string name) => _tools.TryGetValue(name, out var tool)
        ? tool
        : throw new ArgumentException($"Tool không tồn tại: {name}");

    private static IEnumerable<IChatTool> CreateNativeAutomationTools()
    {
        yield return new NativeSayHelloTool();
        yield return new NativeGetAvailableFamilyTypesTool();
        yield return new NativeGetCurrentViewElementsTool();
        yield return new CreatePointBasedElementTool();
        yield return new CreateLineBasedElementTool();
        yield return new CreateSurfaceBasedElementTool();
        yield return new ColorElementsTool();
        yield return new TagAllWallsTool();
        yield return new NativeDeleteElementTool();
        yield return new NativeAiElementFilterTool();
        yield return new NativeOperateElementTool();
        yield return new NativeExportRoomDataTool();
        yield return new NativeGetMaterialQuantitiesTool();
        yield return new NativeAnalyzeModelStatisticsTool();
        yield return new CreateGridTool();
        yield return new CreateStructuralFramingSystemTool();
        yield return new CreateRoomTool();
        yield return new TagAllRoomsTool();
        yield return new CreateLevelTool();
        yield return new NativeDynamicCodeTool();
        yield return new CreateDimensionsTool();
    }

    private static IEnumerable<IChatTool> CreateRibbonCommandTools()
    {
        yield return Ribbon("open_license", "Mở cửa sổ License của RevitAPP.", () => new LicenseCommand().Execute(), false);
        yield return Ribbon("run_hello_world", "Chạy nút Hello World.", () => new HelloWorldCommand().Execute());
        yield return Ribbon("open_translate_text", "Mở công cụ Dịch Text.", () => new TranslateTextCommand().Execute());
        yield return Ribbon("open_renumber_schedule", "Mở công cụ Đánh Số Schedule.", () => new RenumberScheduleCommand().Execute());
        yield return Ribbon("open_column_rebar", "Mở công cụ Vẽ Thép Cột.", () => new DrawColumnRebarCommand().Execute());
        yield return Ribbon("open_beam_drawing", "Mở công cụ Bản Vẽ Dầm.", () => new BeamDrawingCommand().Execute());
        yield return Ribbon("open_beam_rebar", "Mở công cụ Vẽ Thép Dầm.", () => new BeamRebarStartupCommand().Execute());
        yield return Ribbon("open_footing_rebar", "Mở công cụ Vẽ Móng Đơn.", () => new FootingRebarStartupCommand().Execute());
        yield return Ribbon("open_footing_drawing", "Mở công cụ Bản Vẽ Móng.", () => new FootingDrawingCommand().Execute());
        yield return Ribbon("open_footing_section", "Mở công cụ Mặt Cắt Móng.", () => new FootingSectionDrawingCommand().Execute());
        yield return Ribbon("open_wall_rebar", "Mở công cụ Vẽ Thép Tường.", () => new WallRebarStartupCommand().Execute());
        yield return Ribbon("open_align_views", "Mở công cụ Căn Chỉnh View.", () => new AlignSheetViewportsCommand().Execute());
        yield return Ribbon("toggle_point_cloud", "Hiện hoặc ẩn panel Point Cloud.", () => new TogglePointCloudPanelCommand().Execute());
        yield return Ribbon("run_point_cloud_poc", "Chạy nút PC POC.", () => new PointCloudPocCommand().Execute());
        yield return new FocusChatTool();
    }

    private static RibbonCommandTool Ribbon(string name, string description, Action action, bool requiresLicense = true) =>
        new(name, description, action, requiresLicense);
}
