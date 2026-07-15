using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RevitAPP.Core.Models;
using Serilog;
using RevitAPP.Helpers;

namespace RevitAPP.Services.PointCloud;

/// <summary>
///     Lưu/đọc <see cref="PointCloudRenderState" /> per-View qua ExtensibleStorage (JSON).
///     Học pattern từ Qbitec (schema QbitecStorage lưu display settings per-view) —
///     chuyển view / lưu file vẫn giữ nguyên cấu hình hiển thị.
/// </summary>
public sealed class PointCloudSettingsStore
{
    private static readonly Guid SchemaGuid = new("B7D4E1A2-3C9F-4A8B-9E12-7F3A2C5D8E91");
    private const string FieldName = "RenderStateJson";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Đọc state đã lưu cho view; null nếu chưa có.</summary>
    public PointCloudRenderState? Load(View view)
    {
        try
        {
            var schema = GetOrCreateSchema();
            var entity = view.GetEntity(schema);
            if (!entity.IsValid()) return null;

            var json = entity.Get<string>(FieldName);
            if (string.IsNullOrEmpty(json)) return null;

            return JsonSerializer.Deserialize<PointCloudRenderState>(json, JsonOptions);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Đọc render state cho view {Id} thất bại", view.Id.ToValue());
            return null;
        }
    }

    /// <summary>Lưu state cho view (wrap Transaction).</summary>
    public bool Save(View view, PointCloudRenderState state)
    {
        try
        {
            var schema = GetOrCreateSchema();
            var json = JsonSerializer.Serialize(state, JsonOptions);

            using var t = new Transaction(view.Document, "Lưu hiển thị Point Cloud");
            t.Start();
            using var entity = new Entity(schema);
            entity.Set(FieldName, json);
            view.SetEntity(entity);
            t.Commit();
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Lưu render state cho view {Id} thất bại", view.Id.ToValue());
            return false;
        }
    }

    private static Schema GetOrCreateSchema()
    {
        var existing = Schema.Lookup(SchemaGuid);
        if (existing != null) return existing;

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("RevitAppPointCloudRenderState");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(FieldName, typeof(string));
        return builder.Finish();
    }
}
