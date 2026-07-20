using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools.Native;

internal static class NativeToolInput
{
    internal static double Mm(double value) => UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters);
    internal static long Id(JObject o, params string[] names)
    {
        foreach (var n in names)
            if (o[n]?.Type is JTokenType.Integer or JTokenType.Float) return o.Value<long>(n);
        return 0;
    }
    internal static Element? Element(Document d, JObject o, params string[] names) =>
        Id(o, names) is var id && id > 0 ? d.GetElement(ChatElementIdCompat.Create(id)) : null;
    internal static XYZ Point(JToken? token)
    {
        if (token is JArray a && a.Count >= 2)
            return new XYZ(Mm(a[0]!.Value<double>()), Mm(a[1]!.Value<double>()), Mm(a.Count > 2 ? a[2]!.Value<double>() : 0));
        var o = token as JObject ?? new JObject();
        return new XYZ(Mm(o.Value<double?>("x") ?? 0), Mm(o.Value<double?>("y") ?? 0), Mm(o.Value<double?>("z") ?? 0));
    }
    internal static JArray Items(JObject o, params string[] names)
    {
        foreach (var n in names) if (o[n] is JArray a) return a;
        return new JArray();
    }
    internal static ToolSchema Schema(string name, string description, JObject properties, params string[] required) =>
        new(name, description, new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JArray(required) });
    internal static JObject IdProperty(string text) => new() { ["type"] = "integer", ["description"] = text };
    internal static JObject Number(string text) => new() { ["type"] = "number", ["description"] = text };
    internal static JObject Points(string text) => new() { ["type"] = "array", ["description"] = text, ["items"] = new JObject { ["type"] = "object" } };
}

public sealed class CreatePointBasedElementTool : IChatTool, IConfirmableChatTool
{
    public string Name => "create_point_based_element";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Tạo family instance theo một hoặc nhiều điểm (mm).",
        new JObject { ["familyTypeId"] = NativeToolInput.IdProperty("ElementId của FamilySymbol"), ["points"] = NativeToolInput.Points("Danh sách {x,y,z} mm"), ["point"] = new JObject { ["type"] = "object" }, ["levelId"] = NativeToolInput.IdProperty("Level tùy chọn") }, "familyTypeId");
    public object Execute(JObject input, ChatToolContext ctx)
    {
        if (NativeToolInput.Element(ctx.Doc, input, "familyTypeId", "symbolId", "typeId") is not FamilySymbol symbol)
            return new { success = false, message = "Không tìm thấy FamilySymbol." };
        if (!symbol.IsActive) symbol.Activate();
        var level = NativeToolInput.Element(ctx.Doc, input, "levelId") as Level;
        var points = NativeToolInput.Items(input, "points", "locations").Select(NativeToolInput.Point).ToList();
        if (points.Count == 0 && input["point"] != null) points.Add(NativeToolInput.Point(input["point"]));
        if (points.Count == 0) return new { success = false, message = "Thiếu points." };
        var ids = new List<long>();
        foreach (var p in points)
        {
            var i = level == null
                ? ctx.Doc.Create.NewFamilyInstance(p, symbol, StructuralType.NonStructural)
                : ctx.Doc.Create.NewFamilyInstance(p, symbol, level, StructuralType.NonStructural);
            ids.Add(ChatElementIdCompat.Value(i.Id));
        }
        return new { success = true, count = ids.Count, elementIds = ids };
    }
}

public sealed class CreateLineBasedElementTool : IChatTool, IConfirmableChatTool
{
    public string Name => "create_line_based_element";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Tạo tường hoặc family line-based theo điểm đầu/cuối (mm).",
        new JObject { ["familyTypeId"] = NativeToolInput.IdProperty("WallType hoặc FamilySymbol"), ["levelId"] = NativeToolInput.IdProperty("Level"), ["startPoint"] = new JObject { ["type"] = "object" }, ["endPoint"] = new JObject { ["type"] = "object" }, ["height"] = NativeToolInput.Number("Chiều cao mm") }, "familyTypeId", "levelId", "startPoint", "endPoint");
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var type = NativeToolInput.Element(ctx.Doc, input, "familyTypeId", "typeId");
        var level = NativeToolInput.Element(ctx.Doc, input, "levelId") as Level;
        if (type == null || level == null) return new { success = false, message = "Không tìm thấy type hoặc level." };
        var line = Line.CreateBound(NativeToolInput.Point(input["startPoint"] ?? input["start"]), NativeToolInput.Point(input["endPoint"] ?? input["end"]));
        Element created;
        if (type is WallType wt)
            created = Wall.Create(ctx.Doc, line, wt.Id, level.Id, NativeToolInput.Mm(input.Value<double?>("height") ?? 3000), 0, false, false);
        else if (type is FamilySymbol fs)
        {
            if (!fs.IsActive) fs.Activate();
            created = ctx.Doc.Create.NewFamilyInstance(line, fs, level, StructuralType.Beam);
        }
        else return new { success = false, message = "Type phải là WallType hoặc FamilySymbol line-based." };
        return new { success = true, elementId = ChatElementIdCompat.Value(created.Id) };
    }
}

public sealed class CreateSurfaceBasedElementTool : IChatTool, IConfirmableChatTool
{
    public string Name => "create_surface_based_element";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Tạo sàn từ boundary khép kín (mm).",
        new JObject { ["familyTypeId"] = NativeToolInput.IdProperty("FloorType"), ["levelId"] = NativeToolInput.IdProperty("Level"), ["boundary"] = NativeToolInput.Points("Các đỉnh boundary") }, "familyTypeId", "levelId", "boundary");
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var ft = NativeToolInput.Element(ctx.Doc, input, "familyTypeId", "floorTypeId", "typeId") as FloorType;
        var level = NativeToolInput.Element(ctx.Doc, input, "levelId") as Level;
        var pts = NativeToolInput.Items(input, "boundary", "points", "profile").Select(NativeToolInput.Point).ToList();
        if (ft == null || level == null || pts.Count < 3) return new { success = false, message = "Cần FloorType, Level và ít nhất 3 điểm boundary." };
        var loop = new CurveLoop();
        for (var i = 0; i < pts.Count; i++) loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
        var floor = Floor.Create(ctx.Doc, new List<CurveLoop> { loop }, ft.Id, level.Id);
        return new { success = true, elementId = ChatElementIdCompat.Value(floor.Id) };
    }
}

public sealed class CreateGridTool : IChatTool, IConfirmableChatTool
{
    public string Name => "create_grid";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Tạo hệ trục chữ nhật, khoảng cách mm.", new JObject
    { ["xCount"] = new JObject { ["type"] = "integer" }, ["xSpacing"] = NativeToolInput.Number("mm"), ["yCount"] = new JObject { ["type"] = "integer" }, ["ySpacing"] = NativeToolInput.Number("mm"), ["origin"] = new JObject { ["type"] = "object" }, ["length"] = NativeToolInput.Number("Chiều dài trục mm") }, "xCount", "xSpacing", "yCount", "ySpacing");
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var nx = input.Value<int?>("xCount") ?? 0; var ny = input.Value<int?>("yCount") ?? 0;
        var sx = NativeToolInput.Mm(input.Value<double?>("xSpacing") ?? 0); var sy = NativeToolInput.Mm(input.Value<double?>("ySpacing") ?? 0);
        var o = NativeToolInput.Point(input["origin"]); var len = NativeToolInput.Mm(input.Value<double?>("length") ?? 20000);
        if (nx < 1 || ny < 1 || sx <= 0 || sy <= 0) return new { success = false, message = "Count và spacing phải lớn hơn 0." };
        var ids = new List<long>();
        for (var i = 0; i < nx; i++) ids.Add(ChatElementIdCompat.Value(Grid.Create(ctx.Doc, Line.CreateBound(o + XYZ.BasisX * sx * i, o + XYZ.BasisX * sx * i + XYZ.BasisY * len)).Id));
        for (var i = 0; i < ny; i++) ids.Add(ChatElementIdCompat.Value(Grid.Create(ctx.Doc, Line.CreateBound(o + XYZ.BasisY * sy * i, o + XYZ.BasisY * sy * i + XYZ.BasisX * len)).Id));
        return new { success = true, count = ids.Count, elementIds = ids };
    }
}

public sealed class CreateStructuralFramingSystemTool : IChatTool, IConfirmableChatTool
{
    public string Name => "create_structural_framing_system";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Tạo các dầm song song trong biên chữ nhật (mm).", new JObject
    { ["familyTypeId"] = NativeToolInput.IdProperty("FamilySymbol dầm"), ["levelId"] = NativeToolInput.IdProperty("Level"), ["startPoint"] = new JObject { ["type"] = "object" }, ["endPoint"] = new JObject { ["type"] = "object" }, ["spacing"] = NativeToolInput.Number("Khoảng cách dầm mm") }, "familyTypeId", "levelId", "startPoint", "endPoint", "spacing");
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var fs = NativeToolInput.Element(ctx.Doc, input, "familyTypeId", "beamTypeId", "typeId") as FamilySymbol;
        var level = NativeToolInput.Element(ctx.Doc, input, "levelId") as Level;
        if (fs == null || level == null) return new { success = false, message = "Không tìm thấy beam type hoặc level." };
        var a = NativeToolInput.Point(input["startPoint"]); var b = NativeToolInput.Point(input["endPoint"]); var spacing = NativeToolInput.Mm(input.Value<double?>("spacing") ?? 0);
        if (spacing <= 0) return new { success = false, message = "Spacing phải lớn hơn 0." };
        if (!fs.IsActive) fs.Activate();
        var width = Math.Abs(b.Y - a.Y); var count = Math.Max(1, (int)Math.Floor(width / spacing) + 1); var ids = new List<long>();
        for (var i = 0; i < count; i++) { var y = a.Y + Math.Sign(b.Y - a.Y == 0 ? 1 : b.Y - a.Y) * Math.Min(i * spacing, width); var line = Line.CreateBound(new XYZ(a.X, y, a.Z), new XYZ(b.X, y, b.Z)); ids.Add(ChatElementIdCompat.Value(ctx.Doc.Create.NewFamilyInstance(line, fs, level, StructuralType.Beam).Id)); }
        return new { success = true, count = ids.Count, elementIds = ids };
    }
}

public sealed class CreateRoomTool : IChatTool, IConfirmableChatTool
{
    public string Name => "create_room";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Tạo phòng tại level và tọa độ XY (mm).", new JObject { ["levelId"] = NativeToolInput.IdProperty("Level"), ["point"] = new JObject { ["type"] = "object" }, ["name"] = new JObject { ["type"] = "string" }, ["number"] = new JObject { ["type"] = "string" } }, "levelId", "point");
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var level = NativeToolInput.Element(ctx.Doc, input, "levelId") as Level; if (level == null) return new { success = false, message = "Không tìm thấy level." };
        var p = NativeToolInput.Point(input["point"] ?? input["location"]); var room = ctx.Doc.Create.NewRoom(level, new UV(p.X, p.Y));
        if (!string.IsNullOrWhiteSpace(input.Value<string>("name"))) room.Name = input.Value<string>("name");
        if (!string.IsNullOrWhiteSpace(input.Value<string>("number"))) room.Number = input.Value<string>("number");
        return new { success = true, elementId = ChatElementIdCompat.Value(room.Id), room.Name, room.Number };
    }
}

public sealed class CreateLevelTool : IChatTool, IConfirmableChatTool
{
    public string Name => "create_level";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Tạo level theo cao độ mm và tùy chọn tạo floor plan.", new JObject { ["elevation"] = NativeToolInput.Number("mm"), ["name"] = new JObject { ["type"] = "string" }, ["createFloorPlan"] = new JObject { ["type"] = "boolean", ["default"] = false } }, "elevation");
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var level = Level.Create(ctx.Doc, NativeToolInput.Mm(input.Value<double?>("elevation") ?? input.Value<double?>("elevationMm") ?? 0));
        if (!string.IsNullOrWhiteSpace(input.Value<string>("name"))) level.Name = input.Value<string>("name");
        long? viewId = null;
        if (input.Value<bool?>("createFloorPlan") == true)
        {
            var vft = new FilteredElementCollector(ctx.Doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.FloorPlan);
            if (vft != null) viewId = ChatElementIdCompat.Value(ViewPlan.Create(ctx.Doc, vft.Id, level.Id).Id);
        }
        return new { success = true, elementId = ChatElementIdCompat.Value(level.Id), floorPlanViewId = viewId };
    }
}

public sealed class TagAllWallsTool : IChatTool, IConfirmableChatTool
{
    public string Name => "tag_all_walls";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Gắn tag cho tất cả tường trong view hiện tại.", new JObject { ["useLeader"] = new JObject { ["type"] = "boolean" }, ["tagTypeId"] = NativeToolInput.IdProperty("Tag type tùy chọn") });
    public object Execute(JObject input, ChatToolContext ctx) => TagElements(ctx, input, BuiltInCategory.OST_Walls);
    internal static object TagElements(ChatToolContext ctx, JObject input, BuiltInCategory category)
    {
        var useLeader = input.Value<bool?>("useLeader") ?? false; var ids = new List<long>();
        foreach (var e in new FilteredElementCollector(ctx.Doc, ctx.Doc.ActiveView.Id).OfCategory(category).WhereElementIsNotElementType())
        {
            var bb = e.get_BoundingBox(ctx.Doc.ActiveView) ?? e.get_BoundingBox(null); if (bb == null) continue;
            try { var tag = IndependentTag.Create(ctx.Doc, ctx.Doc.ActiveView.Id, new Reference(e), useLeader, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, (bb.Min + bb.Max) / 2); var typeId = NativeToolInput.Id(input, "tagTypeId"); if (typeId > 0) tag.ChangeTypeId(ChatElementIdCompat.Create(typeId)); ids.Add(ChatElementIdCompat.Value(tag.Id)); } catch { /* unsupported element/view */ }
        }
        return new { success = true, count = ids.Count, elementIds = ids };
    }
}

public sealed class TagAllRoomsTool : IChatTool, IConfirmableChatTool
{
    public string Name => "tag_all_rooms";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Gắn tag cho phòng trong view hiện tại.", new JObject { ["roomIds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } }, ["tagTypeId"] = NativeToolInput.IdProperty("Tag type tùy chọn") });
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var requested = NativeToolInput.Items(input, "roomIds").Select(x => x.Value<long>()).ToHashSet(); var ids = new List<long>();
        var useLeader = input.Value<bool?>("useLeader") ?? false;
        var rooms = new FilteredElementCollector(ctx.Doc, ctx.Doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>();
        foreach (var room in rooms.Where(r => requested.Count == 0 || requested.Contains(ChatElementIdCompat.Value(r.Id))))
        {
            var p = (room.Location as LocationPoint)?.Point; if (p == null) continue;
            var tag = ctx.Doc.Create.NewRoomTag(new LinkElementId(room.Id), new UV(p.X, p.Y), ctx.Doc.ActiveView.Id); tag.HasLeader = useLeader; var typeId = NativeToolInput.Id(input, "tagTypeId"); if (typeId > 0) tag.ChangeTypeId(ChatElementIdCompat.Create(typeId)); ids.Add(ChatElementIdCompat.Value(tag.Id));
        }
        return new { success = true, count = ids.Count, elementIds = ids };
    }
}

public sealed class CreateDimensionsTool : IChatTool, IConfirmableChatTool
{
    public string Name => "create_dimensions";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Tạo dimension từ danh sách định nghĩa; điểm dùng mm.", new JObject { ["dimensions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } } }, "dimensions");
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var ids = new List<long>(); var errors = new List<string>();
        foreach (var token in NativeToolInput.Items(input, "dimensions"))
        {
            var d = token as JObject ?? new JObject(); var view = NativeToolInput.Element(ctx.Doc, d, "viewId") as View ?? ctx.Doc.ActiveView;
            var line = Line.CreateBound(NativeToolInput.Point(d["startPoint"]), NativeToolInput.Point(d["endPoint"])); var refs = new ReferenceArray();
            foreach (var id in NativeToolInput.Items(d, "elementIds")) { var e = ctx.Doc.GetElement(ChatElementIdCompat.Create(id.Value<long>())); if (e != null) refs.Append(new Reference(e)); }
            if (refs.Size < 2) { errors.Add("Dimension cần ít nhất 2 elementIds có reference."); continue; }
            try { ids.Add(ChatElementIdCompat.Value(ctx.Doc.Create.NewDimension(view, line, refs).Id)); } catch (Exception ex) { errors.Add(ex.Message); }
        }
        if (errors.Count > 0) throw new InvalidOperationException("Không tạo được toàn bộ dimension; đã rollback: " + string.Join(" | ", errors.Take(5)));
        return new { success = true, count = ids.Count, elementIds = ids, errors };
    }
}

public sealed class ColorElementsTool : IChatTool, IConfirmableChatTool
{
    public string Name => "color_elements";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;
    public bool RequiresConfirmation => true;
    public bool IsDangerous => false;
    public ToolSchema Schema => NativeToolInput.Schema(Name, "Tô màu phần tử trong view theo category và giá trị parameter.", new JObject { ["categoryName"] = new JObject { ["type"] = "string" }, ["parameterName"] = new JObject { ["type"] = "string" }, ["useGradient"] = new JObject { ["type"] = "boolean" }, ["customColors"] = new JObject { ["type"] = "array" }, ["elementIds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } } });
    public object Execute(JObject input, ChatToolContext ctx)
    {
        var requested = NativeToolInput.Items(input, "elementIds").Select(x => x.Value<long>()).ToHashSet(); IEnumerable<Element> elements;
        if (requested.Count > 0) elements = requested.Select(id => ctx.Doc.GetElement(ChatElementIdCompat.Create(id))).Where(e => e != null)!;
        else elements = new FilteredElementCollector(ctx.Doc, ctx.Doc.ActiveView.Id).WhereElementIsNotElementType();
        var category = NativeToolSupport.ParseCategory(input.Value<string>("categoryName"));
        if (category.HasValue) elements = elements.Where(e => e.Category != null && ChatElementIdCompat.Value(e.Category.Id) == (long)category.Value);
        var parameter = input.Value<string>("parameterName") ?? "Type"; var groups = elements.GroupBy(e => e.LookupParameter(parameter)?.AsValueString() ?? e.LookupParameter(parameter)?.AsString() ?? "(none)").ToList(); var count = 0;
        var custom = (input["customColors"] as JArray)?.OfType<JArray>().Where(a => a.Count >= 3).Select(a => new Color(a[0]!.Value<byte>(), a[1]!.Value<byte>(), a[2]!.Value<byte>())).ToList() ?? new List<Color>();
        var gradient = input.Value<bool?>("useGradient") ?? false;
        for (var i = 0; i < groups.Count; i++) { var hue = gradient ? i * 360.0 / Math.Max(1, groups.Count) : i * 137.508; var rad = hue * Math.PI / 180; var color = custom.Count > 0 ? custom[i % custom.Count] : new Color((byte)(128 + 110 * Math.Sin(rad)), (byte)(128 + 110 * Math.Sin(rad + 2.094)), (byte)(128 + 110 * Math.Sin(rad + 4.188))); var ogs = new OverrideGraphicSettings().SetProjectionLineColor(color).SetSurfaceTransparency(20); foreach (var e in groups[i]) { ctx.Doc.ActiveView.SetElementOverrides(e.Id, ogs); count++; } }
        return new { success = true, count, groups = groups.Count };
    }
}
