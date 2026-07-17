using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Services;

namespace RevitAPP.Chat.Tools;

public sealed class GetOpenExcelWorkbooksTool : IBackgroundChatTool
{
    public string Name => "get_open_excel_workbooks";
    public ToolSchema Schema => new(Name,
        "Lấy đường dẫn đầy đủ các workbook đang mở trong Microsoft Excel trên máy này. Không cần người dùng nhập đường dẫn.",
        new JsonSchemaBuilder().Build());
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var files = OpenExcelWorkbookFinder.Find();
        return new { success = true, count = files.Count, files,
            message = files.Count == 0 ? "Không có workbook Excel đã lưu nào đang mở." : $"Tìm thấy {files.Count} workbook đang mở." };
    }
}

public sealed class FindExcelFilesTool : IBackgroundChatTool
{
    public string Name => "find_excel_files";
    public ToolSchema Schema => new(Name, "Tìm file Excel/CSV trong một thư mục trước khi đọc.",
        new JsonSchemaBuilder().Text("directory", "Đường dẫn thư mục.", true)
            .Bool("recursive", "Tìm cả thư mục con.").Integer("limit", "Tối đa 200 file.").Build());
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public object Execute(JObject input, ChatToolContext ctx) => new
    {
        success = true,
        files = ExcelWorkbookReader.Find(input.Value<string>("directory") ?? string.Empty,
            input.Value<bool?>("recursive") ?? false, input.Value<int?>("limit") ?? 50)
    };
}

public sealed class InspectExcelFileTool : IBackgroundChatTool
{
    public string Name => "inspect_excel_file";
    public ToolSchema Schema => new(Name, "Đọc metadata workbook: tên sheet, số hàng và số cột. Không sửa file.",
        new JsonSchemaBuilder().Text("filePath", "Đường dẫn đầy đủ tới file Excel/CSV.", true).Build());
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public object Execute(JObject input, ChatToolContext ctx) => new
    {
        success = true,
        sheets = ExcelWorkbookReader.Inspect(input.Value<string>("filePath") ?? string.Empty)
    };
}

public sealed class ReadExcelTableTool : IBackgroundChatTool
{
    public string Name => "read_excel_table";
    public ToolSchema Schema => new(Name,
        "Đọc bảng từ Excel/CSV thành headers và rows để phân tích hoặc truyền sang tool Revit. Không sửa file.",
        new JsonSchemaBuilder().Text("filePath", "Đường dẫn đầy đủ tới file.", true)
            .Text("sheetName", "Tên sheet; bỏ trống để đọc sheet đầu.")
            .Integer("headerRow", "Dòng tiêu đề, đếm từ 1; mặc định 1.")
            .Integer("startRow", "Dòng dữ liệu đầu tiên; mặc định headerRow+1.")
            .Integer("maxRows", "Số hàng tối đa, 1-1000; mặc định 100.")
            .Integer("maxColumns", "Số cột tối đa, 1-200; mặc định 100.").Build());
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var header = input.Value<int?>("headerRow") ?? 1;
        return new
        {
            success = true,
            table = ExcelWorkbookReader.Read(input.Value<string>("filePath") ?? string.Empty,
                input.Value<string>("sheetName"), header, input.Value<int?>("startRow") ?? header + 1,
                input.Value<int?>("maxRows") ?? 100, input.Value<int?>("maxColumns") ?? 100)
        };
    }
}
