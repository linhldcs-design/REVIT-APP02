using System.Reflection;
using System.IO;
using Autodesk.Revit.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools.Native;

/// <summary>Biên dịch và chạy C# ngay trong RevitAPP; không cần Revit MCP hoặc command registry.</summary>
public sealed class NativeDynamicCodeTool : IChatTool, IConfirmableChatTool
{
    public const int MaxCodeLength = 1200;
    public string Name => "send_code_to_revit";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => true;

    public ToolSchema Schema => new(Name,
        "Chạy C# trực tiếp trong Revit. Code có biến Document và parameters; tự mở Transaction nếu thay đổi model.",
        new JsonSchemaBuilder().Text("code", "Thân hàm C#; phải return một giá trị.", true).Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var code = input.Value<string?>("code")?.Trim();
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Thiếu code C#.");
        if (code.Length > MaxCodeLength)
            throw new ArgumentException($"Mã C# tối đa {MaxCodeLength} ký tự để người dùng có thể xem đầy đủ trước khi xác nhận.");

        var source = $$"""
            using System;
            using System.Linq;
            using System.Collections.Generic;
            using Autodesk.Revit.DB;
            using Newtonsoft.Json.Linq;
            public static class RevitAppUserScript
            {
                public static object Execute(Document Document, JObject parameters)
                {
                    {{code}}
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
        var compilation = CSharpCompilation.Create("RevitAppUserScript_" + Guid.NewGuid().ToString("N"),
            new[] { syntaxTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            var diagnostics = emit.Diagnostics.Where(value => value.Severity == DiagnosticSeverity.Error)
                .Take(12).Select(value => value.ToString());
            throw new InvalidOperationException("C# biên dịch lỗi: " + string.Join(" | ", diagnostics));
        }

        var assembly = Assembly.Load(stream.ToArray());
        var method = assembly.GetType("RevitAppUserScript")?.GetMethod("Execute",
                         BindingFlags.Public | BindingFlags.Static)
                     ?? throw new InvalidOperationException("Không tìm thấy hàm C# đã biên dịch.");
        try
        {
            var value = method.Invoke(null, new object[] { ctx.Doc, input });
            return new { success = true, result = value };
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(exception.InnerException?.Message ?? exception.Message,
                exception.InnerException ?? exception);
        }
    }
}
