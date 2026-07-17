using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Services;

/// <summary>
///     Thực thi 1 tool: nhận tên tool + args (JObject), trả kết quả dạng chuỗi JSON. Do phase 4 cung cấp
///     (marshal sang Revit UI thread qua ExternalEvent).
/// </summary>
public delegate Task<string> ToolExecutor(string toolName, JObject arguments, CancellationToken ct);

/// <summary>
///     Client LLM trung lập. Gửi lịch sử hội thoại + danh sách tool → chạy vòng lặp tool-calling nội bộ
///     (execute qua <see cref="ToolExecutor"/>) → trả text trả lời cuối cùng của trợ lý.
/// </summary>
public interface ILlmClient
{
    Task<string> SendAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolSchema> tools,
        ToolExecutor executor,
        CancellationToken ct);
}
