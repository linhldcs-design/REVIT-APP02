using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RevitAPP.Core.Models;
using Serilog;

namespace RevitAPP.Services.ColumnRebar;

/// <summary>
///     Lưu/đọc các preset cấu hình Vẽ Thép Cột (theo tên) vào document qua ExtensibleStorage (JSON).
///     Học pattern từ <see cref="PointCloud.PointCloudSettingsStore" />: 1 schema, 1 field string JSON.
///     Toàn bộ preset lưu chung trong <see cref="ProjectInfo" /> dưới dạng dictionary tên → cấu hình.
/// </summary>
public sealed class ColumnRebarConfigStore
{
    private static readonly Guid SchemaGuid = new("C4E7A1B9-6D28-4F53-9A17-3B8E2D5C71F0");
    private const string FieldName = "PresetsJson";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Đọc toàn bộ preset đã lưu; danh sách rỗng nếu chưa có.</summary>
    public IReadOnlyList<ColumnRebarConfig> LoadAll(Document document)
    {
        try
        {
            var schema = GetOrCreateSchema();
            var entity = document.ProjectInformation.GetEntity(schema);
            if (!entity.IsValid()) return Array.Empty<ColumnRebarConfig>();

            var json = entity.Get<string>(FieldName);
            if (string.IsNullOrEmpty(json)) return Array.Empty<ColumnRebarConfig>();

            return JsonSerializer.Deserialize<List<ColumnRebarConfig>>(json, JsonOptions)
                   ?? (IReadOnlyList<ColumnRebarConfig>)Array.Empty<ColumnRebarConfig>();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Đọc preset thép cột thất bại");
            return Array.Empty<ColumnRebarConfig>();
        }
    }

    /// <summary>
    ///     Lưu (thêm mới hoặc ghi đè theo tên) một preset. Wrap Transaction riêng —
    ///     KHÔNG gọi khi đang có transaction khác mở.
    /// </summary>
    public bool Save(Document document, ColumnRebarConfig config)
    {
        try
        {
            var presets = LoadAll(document)
                .Where(p => !string.Equals(p.Name, config.Name, StringComparison.OrdinalIgnoreCase))
                .Append(config)
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return Write(document, presets, "Lưu cấu hình thép cột");
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Lưu preset thép cột {Name} thất bại", config.Name);
            return false;
        }
    }

    /// <summary>Xoá preset theo tên; true nếu có xoá.</summary>
    public bool Delete(Document document, string name)
    {
        try
        {
            var presets = LoadAll(document);
            var remaining = presets
                .Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (remaining.Count == presets.Count) return false;

            return Write(document, remaining, "Xoá cấu hình thép cột");
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Xoá preset thép cột {Name} thất bại", name);
            return false;
        }
    }

    private static bool Write(Document document, IReadOnlyList<ColumnRebarConfig> presets, string transactionName)
    {
        var schema = GetOrCreateSchema();
        var json = JsonSerializer.Serialize(presets, JsonOptions);

        using var t = new Transaction(document, transactionName);
        t.Start();
        using var entity = new Entity(schema);
        entity.Set(FieldName, json);
        document.ProjectInformation.SetEntity(entity);
        t.Commit();
        return true;
    }

    private static Schema GetOrCreateSchema()
    {
        var existing = Schema.Lookup(SchemaGuid);
        if (existing != null) return existing;

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("RevitAppColumnRebarConfig");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(FieldName, typeof(string));
        return builder.Finish();
    }
}
