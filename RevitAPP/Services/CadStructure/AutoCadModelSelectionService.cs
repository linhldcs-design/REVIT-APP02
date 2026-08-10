using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using Serilog;

namespace RevitAPP.Services.CadStructure;

internal sealed record AutoCadModelSelectionResult(
    CadStructureTransferPackage? Package,
    string? Error)
{
    public bool IsValid => Package is not null && string.IsNullOrWhiteSpace(Error);
    public static AutoCadModelSelectionResult Failed(string error) => new(null, error);

    /// <summary>
    /// Hatched areas from the same scan. They mark which bays a slab drops in, so they travel
    /// beside the geometry rather than inside the package the Grid and Column workflows share.
    /// </summary>
    public IReadOnlyList<CadHatchRegion> Hatches { get; init; } = Array.Empty<CadHatchRegion>();
}

/// <summary>
/// Reads LINE/POLYLINE/INSERT geometry from the active AutoCAD document through late-bound
/// COM. Block definitions are traversed read-only; no EXPLODE or database mutation occurs.
/// </summary>
internal static class AutoCadModelSelectionService
{
    private static readonly string[] ProgIds =
    {
        "AutoCAD.Application.26",
        "AutoCAD.Application.25.1",
        "AutoCAD.Application.25",
        "AutoCAD.Application.24.3",
        "AutoCAD.Application"
    };

    private const string SelectionSetName = "LDL_MODEL_FROM_CAD_PICK";
    private const short DxfEntityType = 0;
    private const int MaximumBlockDepth = 5;
    private const int MaximumSelectedEntityCount = 5000;
    private static readonly TimeSpan MaximumReadDuration = TimeSpan.FromSeconds(15);

    public static AutoCadModelSelectionResult Select() => SelectInternal(null);

    public static AutoCadModelSelectionResult SelectBeam(CadStructureTransferPackage gridPackage) =>
        SelectInternal(gridPackage);

    public static AutoCadModelSelectionResult SelectSlab(CadStructureTransferPackage gridPackage) =>
        SelectInternal(gridPackage, includeHatch: true);

    /// <summary>
    /// Picks a point inside each bay that stays open, the way the HATCH command picks an area.
    /// Nothing is selected and the drawing is not touched; the points only say which bays to leave
    /// out of the pour.
    /// </summary>
    public static AutoCadModelSelectionResult SelectOpeningOutlines(
        CadStructureTransferPackage slabPackage) =>
        SelectInternal(slabPackage, promptOverride:
            "\nQuét chọn đường bao của các ô KHÔNG đổ sàn rồi nhấn Enter...\n");

    /// <summary>
    /// Picks the shaded areas that are lowered. Reading them from the slab selection takes every
    /// hatch the window happened to cover; picking says which ones the plan means, the same way the
    /// openings are picked rather than guessed.
    /// </summary>
    public static AutoCadModelSelectionResult SelectHatchRegions(
        CadStructureTransferPackage slabPackage) =>
        SelectInternal(slabPackage, includeHatch: true, promptOverride:
            "\nQuét chọn các vùng HATCH sàn hạ rồi nhấn Enter...\n");

    private static AutoCadModelSelectionResult SelectInternal(
        CadStructureTransferPackage? gridPackage,
        bool includeHatch = false,
        string? promptOverride = null)
    {
        object? application = null;
        object? document = null;
        object? selection = null;
        object? utility = null;
        try
        {
            application = GetRunningInstance();
            if (application is null)
                return AutoCadModelSelectionResult.Failed(
                    "Không tìm thấy AutoCAD đang mở.\n\nHãy mở bản vẽ chứa lưới và cột rồi thử lại.");

            document = Get(application, "ActiveDocument");
            if (document is null)
                return AutoCadModelSelectionResult.Failed("AutoCAD đang mở nhưng không có bản vẽ nào.");

            Set(application, "Visible", true);
            TryActivate(application, document);
            var drawingName = Safe(() => Get(document, "FullName")?.ToString());
            if (string.IsNullOrWhiteSpace(drawingName))
                drawingName = Safe(() => Get(document, "Name")?.ToString()) ?? "AutoCAD";
            var insUnits = ReadInsUnits(document);
            if (gridPackage is not null
                && (!string.Equals(drawingName, gridPackage.SourceDrawing, StringComparison.OrdinalIgnoreCase)
                    || insUnits != gridPackage.InsUnits))
                return AutoCadModelSelectionResult.Failed(
                    "Beam Lines phải được chọn trong cùng bản vẽ và cùng INSUNITS với Grid Axes.");
            selection = CreateSelectionSet(document);
            if (selection is null)
                return AutoCadModelSelectionResult.Failed("Không tạo được vùng chọn trong AutoCAD.");

            utility = Get(document, "Utility");
            SafeCall(utility, "Prompt", promptOverride
                ?? "\nQuét chọn lưới và rectangle/block cột rồi nhấn Enter...\n");
            Call(selection, "SelectOnScreen",
                new short[] { DxfEntityType },
                new object[] { promptOverride is not null
                    ? "LINE,LWPOLYLINE,POLYLINE"
                    : gridPackage is null
                        ? "LINE,LWPOLYLINE,POLYLINE,INSERT"
                        : includeHatch
                            ? "LINE,LWPOLYLINE,POLYLINE,INSERT,TEXT,MTEXT,HATCH"
                            : "LINE,LWPOLYLINE,POLYLINE,INSERT,TEXT,MTEXT" });

            var selectedCount = Convert.ToInt32(Get(selection, "Count"));
            if (selectedCount > MaximumSelectedEntityCount)
                return AutoCadModelSelectionResult.Failed(
                    $"Vùng chọn có {selectedCount:N0} đối tượng, vượt giới hạn {MaximumSelectedEntityCount:N0}.\n\n"
                    + "Hãy chỉ quét layer lưới/cột hoặc chia bản vẽ thành nhiều vùng nhỏ.");

            CadStructurePoint2? anchor;
            if (gridPackage is null)
            {
                SafeCall(utility, "Prompt", "\nChọn điểm móc nguồn của Grid/Column...\n");
                var anchorValue = Call(utility!, "GetPoint", Type.Missing,
                    "\nChọn điểm móc nguồn của Grid/Column: ");
                anchor = ToPoint(anchorValue);
            }
            else
            {
                anchor = gridPackage.SourceAnchor;
            }
            if (anchor is null)
                return AutoCadModelSelectionResult.Failed("Không đọc được điểm móc AutoCAD.");

            SafeCall(utility, "Prompt", "\nĐang đọc LINE/BLOCK đã chọn, vui lòng chờ...\n");
            var reader = new BlockAwareEntityReader(document);
            var segments = reader.ReadSelection(selection);
            if (segments.Count == 0)
                return AutoCadModelSelectionResult.Failed(
                    "Vùng chọn không có LINE, POLYLINE hoặc BLOCK chứa rectangle dùng được.");

            Log.Information(
                "Picked {SegmentCount} normalized segments and anchor ({AnchorX}, {AnchorY}) from AutoCAD",
                segments.Count, anchor.Value.X, anchor.Value.Y);

            return new AutoCadModelSelectionResult(
                new CadStructureTransferPackage(
                    CadStructureTransferPackage.CurrentSchemaVersion,
                    Guid.NewGuid().ToString("N"),
                    DateTime.UtcNow,
                    drawingName,
                    Safe(() => Get(application, "Version")?.ToString()) ?? string.Empty,
                    insUnits,
                    anchor.Value,
                    segments)
                {
                    Annotations = reader.Annotations
                },
                null)
            {
                Hatches = reader.Hatches
            };
        }
        catch (Exception exception) when (IsUserCancel(exception))
        {
            return AutoCadModelSelectionResult.Failed(string.Empty);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "AutoCAD model selection failed");
            return AutoCadModelSelectionResult.Failed(
                "Không lấy được lưới/cột từ AutoCAD.\n\n" + Innermost(exception).Message);
        }
        finally
        {
            if (selection is not null) TryDelete(selection);
            Release(selection);
            Release(utility);
            Release(document);
            Release(application);
        }
    }

    private sealed class BlockAwareEntityReader
    {
        private readonly object _document;
        private readonly List<CadStructureSegment> _segments = new();
        private readonly List<CadStructureAnnotation> _annotations = new();
        private readonly List<CadHatchRegion> _hatches = new();
        private readonly Stopwatch _readStopwatch = new();
        private object? _blocks;
        private int _nextId = 1;

        public BlockAwareEntityReader(object document) => _document = document;

        public IReadOnlyList<CadStructureAnnotation> Annotations => _annotations;

        public IReadOnlyList<CadHatchRegion> Hatches => _hatches;

        public IReadOnlyList<CadStructureSegment> ReadSelection(object selection)
        {
            _readStopwatch.Restart();
            _blocks = Get(_document, "Blocks");
            var read = new List<object>();
            try
            {
                var count = Convert.ToInt32(Get(selection, "Count"));
                for (var index = 0; index < count; index++)
                {
                    ThrowIfReadBudgetExceeded();
                    object? entity = null;
                    try
                    {
                        entity = Call(selection, "Item", index);
                        if (entity is not null)
                            ReadEntity(entity, Transform2.Identity, string.Empty, null, null, 0,
                                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    }
                    catch (InvalidDataException)
                    {
                        throw;
                    }
                    catch (TimeoutException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(exception, "Skipped unreadable selected AutoCAD entity {EntityIndex}", index);
                    }
                    finally
                    {
                        // AutoCAD can hand back one shared wrapper for successive Item calls, so
                        // releasing per iteration tears down the object the next read receives and
                        // silently drops entities the drawing really has. Collect them and release
                        // once the whole selection has been read.
                        if (entity is not null) read.Add(entity);
                    }
                }

                return _segments;
            }
            finally
            {
                foreach (var entity in read) Release(entity);
                Release(_blocks);
                _blocks = null;
            }
        }

        private void ReadEntity(
            object entity,
            Transform2 transform,
            string sourcePath,
            string? inheritedText,
            string? inheritedLayer,
            int depth,
            ISet<string> blockStack)
        {
            ThrowIfReadBudgetExceeded();
            var objectName = Safe(() => Get(entity, "ObjectName")?.ToString()) ?? string.Empty;
            var entityLayer = Safe(() => Get(entity, "Layer")?.ToString()) ?? string.Empty;
            var layer = string.Equals(entityLayer, "0", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(inheritedLayer)
                ? inheritedLayer!
                : entityLayer;

            if (string.Equals(objectName, "AcDbLine", StringComparison.Ordinal))
            {
                var start = ToPoint(Get(entity, "StartPoint"));
                var end = ToPoint(Get(entity, "EndPoint"));
                if (start is not null && end is not null)
                    AddSegment(transform.Apply(start.Value), transform.Apply(end.Value), layer,
                        sourcePath, inheritedText);
                return;
            }

            if (objectName is "AcDbText" or "AcDbMText")
            {
                var position = ToPoint(Safe(() => Get(entity, "InsertionPoint")));
                var text = Safe(() => Get(entity, "TextString")?.ToString()) ?? inheritedText;
                var rotation = Safe(() => Convert.ToDouble(Get(entity, "Rotation"))) * 180.0 / Math.PI;
                if (position is not null && !string.IsNullOrWhiteSpace(text))
                    AddAnnotation(transform.Apply(position.Value), text!, rotation, layer,
                        sourcePath, objectName == "AcDbMText");
                return;
            }

            if (objectName is "AcDbPolyline" or "AcDb2dPolyline")
            {
                var normal = ToDoubles(Safe(() => Get(entity, "Normal")));
                if (normal.Length >= 3
                    && (Math.Abs(normal[0]) > 1e-9
                        || Math.Abs(normal[1]) > 1e-9
                        || Math.Abs(normal[2] - 1.0) > 1e-9))
                {
                    Log.Warning("Skipped non-WCS polyline on layer {Layer}", layer);
                    return;
                }
                ReadPolyline(entity, transform, layer, sourcePath, inheritedText, objectName);
                return;
            }

            if (objectName == "AcDbHatch")
            {
                ReadHatch(entity, transform, layer);
                return;
            }

            if (objectName == "AcDbBlockReference")
                ReadBlock(entity, transform, sourcePath, inheritedText, layer, depth, blockStack);
        }

        private void ReadPolyline(
            object entity,
            Transform2 transform,
            string layer,
            string sourcePath,
            string? sourceText,
            string objectName)
        {
            var values = ToDoubles(Safe(() => Get(entity, "Coordinates")));
            var stride = objectName == "AcDbPolyline" ? 2 : 3;
            if (values.Length < stride * 2) return;
            var closed = Safe(() => Convert.ToBoolean(Get(entity, "Closed"))) == true;

            var handle = Safe(() => Get(entity, "Handle")?.ToString()) ?? string.Empty;
            var polylinePath = string.IsNullOrWhiteSpace(handle)
                ? sourcePath
                : sourcePath + "/PL@" + handle;

            var points = new List<CadStructurePoint2>();
            for (var index = 0; index + 1 < values.Length; index += stride)
                points.Add(transform.Apply(new CadStructurePoint2(values[index], values[index + 1])));

            // V1 detects straight outlines only. Treating an arc chord as a straight side could
            // turn a rounded block into a false column, so a bulged side is dropped -- but the
            // straight sides of the same polyline are still real beam boundaries. Discarding the
            // whole outline would lose a beam whenever one corner happens to be filleted.
            var curved = new bool[points.Count];
            var curvedSides = 0;
            if (objectName is "AcDbPolyline" or "AcDb2dPolyline")
            {
                for (var index = 0; index < points.Count; index++)
                {
                    var bulge = Safe(() => Convert.ToDouble(Call(entity, "GetBulge", index)));
                    if (Math.Abs(bulge) <= 1e-9) continue;
                    curved[index] = true;
                    curvedSides++;
                }
            }

            for (var index = 0; index < points.Count - 1; index++)
            {
                if (curved[index]) continue;
                AddSegment(points[index], points[index + 1], layer, polylinePath, sourceText);
            }

            if (closed && points.Count > 2 && !curved[points.Count - 1])
                AddSegment(points[^1], points[0], layer, polylinePath, sourceText);

            if (curvedSides > 0)
                Log.Warning("Skipped {CurvedSides} curved side(s) of a polyline on layer {Layer}",
                    curvedSides, layer);
        }

        /// <summary>
        /// Reads a hatched area as the rectangle it covers plus the style that fills it. A plan
        /// marks each slab drop with its own pattern, so the pattern, its scale and its angle
        /// carry the meaning; the outline only says which bays are covered, never where a slab
        /// boundary runs.
        /// </summary>
        private void ReadHatch(object entity, Transform2 transform, string layer)
        {
            ThrowIfReadBudgetExceeded();
            var bounds = CallWithOutputs(entity, "GetBoundingBox", 2);
            var lower = bounds is null ? null : ToPoint(bounds[0]);
            var upper = bounds is null ? null : ToPoint(bounds[1]);

            // GetBoundingBox travels through by-reference arguments, which not every AutoCAD build
            // hands back through late binding. Falling back to the loop vertices keeps the hatch
            // usable instead of dropping the drop it marks.
            if (lower is null || upper is null)
            {
                var loop = ReadHatchLoopExtent(entity);
                if (loop is null)
                {
                    Log.Debug("Could not read AutoCAD hatch bounds on layer {Layer}", layer);
                    return;
                }
                lower = loop.Value.Lower;
                upper = loop.Value.Upper;
            }

            if (upper.Value.X - lower.Value.X < 1e-6 || upper.Value.Y - lower.Value.Y < 1e-6) return;

            var corners = new[]
            {
                transform.Apply(lower.Value),
                transform.Apply(new CadStructurePoint2(upper.Value.X, lower.Value.Y)),
                transform.Apply(upper.Value),
                transform.Apply(new CadStructurePoint2(lower.Value.X, upper.Value.Y))
            };

            _hatches.Add(new CadHatchRegion(_nextId++, corners)
            {
                PatternName = Safe(() => Get(entity, "PatternName")?.ToString()) ?? string.Empty,
                PatternScale = Safe(() => Convert.ToDouble(Get(entity, "PatternScale"))),
                PatternAngleDegrees =
                    Safe(() => Convert.ToDouble(Get(entity, "PatternAngle"))) * 180.0 / Math.PI
            });
        }

        /// <summary>
        /// Extent of a hatch taken from the objects its boundary loops are built on, for when the
        /// bounding box cannot be read through late binding.
        /// </summary>
        private static (CadStructurePoint2 Lower, CadStructurePoint2 Upper)? ReadHatchLoopExtent(object entity)
        {
            var points = new List<CadStructurePoint2>();
            try
            {
                var loops = Convert.ToInt32(Get(entity, "NumberOfLoops"));
                for (var index = 0; index < loops; index++)
                {
                    var objects = Safe(() => Call(entity, "GetLoopAt", index)) as object[];
                    if (objects is null) continue;
                    foreach (var item in objects)
                    {
                        if (item is null) continue;
                        try
                        {
                            var name = Safe(() => Get(item, "ObjectName")?.ToString()) ?? string.Empty;
                            if (name is "AcDbPolyline" or "AcDb2dPolyline")
                            {
                                var values = ToDoubles(Safe(() => Get(item, "Coordinates")));
                                var stride = name == "AcDbPolyline" ? 2 : 3;
                                for (var vertex = 0; vertex + 1 < values.Length; vertex += stride)
                                    points.Add(new CadStructurePoint2(values[vertex], values[vertex + 1]));
                            }
                            else if (name == "AcDbLine")
                            {
                                var start = ToPoint(Safe(() => Get(item, "StartPoint")));
                                var end = ToPoint(Safe(() => Get(item, "EndPoint")));
                                if (start is not null) points.Add(start.Value);
                                if (end is not null) points.Add(end.Value);
                            }
                        }
                        finally
                        {
                            Release(item);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Could not read AutoCAD hatch loops");
                return null;
            }

            if (points.Count < 3) return null;
            return (
                new CadStructurePoint2(points.Min(point => point.X), points.Min(point => point.Y)),
                new CadStructurePoint2(points.Max(point => point.X), points.Max(point => point.Y)));
        }

        private void AddAnnotation(
            CadStructurePoint2 position,
            string text,
            double rotationDegrees,
            string layer,
            string sourcePath,
            bool isMText)
        {
            ThrowIfReadBudgetExceeded();
            _annotations.Add(new CadStructureAnnotation(
                _nextId++, position, text, rotationDegrees, layer, sourcePath, isMText));
        }

        private void ReadBlock(
            object reference,
            Transform2 parent,
            string parentPath,
            string? inheritedText,
            string? inheritedLayer,
            int depth,
            ISet<string> blockStack)
        {
            if (depth >= MaximumBlockDepth)
            {
                Log.Warning("Skipped AutoCAD block deeper than {MaximumDepth}: {BlockPath}",
                    MaximumBlockDepth, parentPath);
                return;
            }

            var name = Safe(() => Get(reference, "Name")?.ToString()) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || !blockStack.Add(name)) return;

            object? definition = null;
            var children = new List<object>();
            try
            {
                definition = _blocks is null ? null : Call(_blocks, "Item", name);
                if (definition is null) return;
                if (Safe(() => Convert.ToBoolean(Get(definition, "IsXRef"))) == true) return;

                var insertion = ToPoint(Get(reference, "InsertionPoint")) ?? default;
                var rotation = Safe(() => Convert.ToDouble(Get(reference, "Rotation")));
                var scaleX = Safe(() => Convert.ToDouble(Get(reference, "XScaleFactor")));
                var scaleY = Safe(() => Convert.ToDouble(Get(reference, "YScaleFactor")));
                if (Math.Abs(scaleX) < 1e-12) scaleX = 1.0;
                if (Math.Abs(scaleY) < 1e-12) scaleY = 1.0;
                var origin = ToPoint(Safe(() => Get(definition, "Origin"))) ?? default;
                var local = Transform2.ForBlock(insertion, rotation, scaleX, scaleY, origin);
                var combined = parent.Compose(local);
                var effectiveName = Safe(() => Get(reference, "EffectiveName")?.ToString()) ?? name;
                var handle = Safe(() => Get(reference, "Handle")?.ToString()) ?? string.Empty;
                var instanceName = string.IsNullOrWhiteSpace(handle)
                    ? effectiveName
                    : effectiveName + "@" + handle;
                var path = string.IsNullOrWhiteSpace(parentPath)
                    ? instanceName
                    : parentPath + "/" + instanceName;
                var text = ReadFirstAttribute(reference) ?? inheritedText;
                var blockLayer = string.Equals(inheritedLayer, "0", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : inheritedLayer;

                var count = Convert.ToInt32(Get(definition, "Count"));
                for (var index = 0; index < count; index++)
                {
                    ThrowIfReadBudgetExceeded();
                    object? child = null;
                    try
                    {
                        child = Call(definition, "Item", index);
                        if (child is not null)
                            ReadEntity(child, combined, path, text, blockLayer, depth + 1, blockStack);
                    }
                    finally
                    {
                        // Released after the whole definition has been read: a shared wrapper
                        // would otherwise be torn down while later entities still need it.
                        if (child is not null) children.Add(child);
                    }
                }
            }
            finally
            {
                foreach (var child in children) Release(child);
                blockStack.Remove(name);
                Release(definition);
            }
        }

        private static string? ReadFirstAttribute(object reference)
        {
            try
            {
                if (!Convert.ToBoolean(Get(reference, "HasAttributes"))) return null;
                var attributes = Call(reference, "GetAttributes") as Array;
                if (attributes is null) return null;
                string? first = null;
                foreach (var value in attributes)
                {
                    try
                    {
                        var text = value is null ? null : Get(value, "TextString")?.ToString();
                        if (first is null && !string.IsNullOrWhiteSpace(text)) first = text;
                    }
                    finally
                    {
                        Release(value);
                    }
                }
                return first;
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Could not read AutoCAD block attributes");
            }
            return null;
        }

        private void AddSegment(
            CadStructurePoint2 start,
            CadStructurePoint2 end,
            string layer,
            string sourcePath,
            string? sourceText)
        {
            ThrowIfReadBudgetExceeded();
            if (start.DistanceTo(end) < 1e-9) return;
            if (_segments.Count >= CadStructureAnalyzer.MaximumSegmentCount)
                throw new InvalidDataException(
                    $"CAD selection exceeds {CadStructureAnalyzer.MaximumSegmentCount:N0} segments. Split it into smaller batches.");
            _segments.Add(new CadStructureSegment(
                _nextId++, start, end, layer, sourcePath, sourceText));
        }

        private void ThrowIfReadBudgetExceeded()
        {
            if (_readStopwatch.Elapsed <= MaximumReadDuration) return;
            throw new TimeoutException(
                "Đọc hình học AutoCAD vượt quá 15 giây. Hãy chỉ chọn layer lưới/cột hoặc chia nhỏ vùng quét.");
        }
    }

    private readonly record struct Transform2(
        double M11, double M12, double M21, double M22, double Tx, double Ty)
    {
        public static Transform2 Identity => new(1, 0, 0, 1, 0, 0);

        public static Transform2 ForBlock(
            CadStructurePoint2 insertion,
            double rotation,
            double scaleX,
            double scaleY,
            CadStructurePoint2 origin)
        {
            var cosine = Math.Cos(rotation);
            var sine = Math.Sin(rotation);
            var m11 = cosine * scaleX;
            var m12 = -sine * scaleY;
            var m21 = sine * scaleX;
            var m22 = cosine * scaleY;
            return new Transform2(
                m11, m12, m21, m22,
                insertion.X - m11 * origin.X - m12 * origin.Y,
                insertion.Y - m21 * origin.X - m22 * origin.Y);
        }

        public CadStructurePoint2 Apply(CadStructurePoint2 point) => new(
            M11 * point.X + M12 * point.Y + Tx,
            M21 * point.X + M22 * point.Y + Ty);

        /// <summary>Returns this ∘ child: child-local coordinates become world coordinates.</summary>
        public Transform2 Compose(Transform2 child) => new(
            M11 * child.M11 + M12 * child.M21,
            M11 * child.M12 + M12 * child.M22,
            M21 * child.M11 + M22 * child.M21,
            M21 * child.M12 + M22 * child.M22,
            M11 * child.Tx + M12 * child.Ty + Tx,
            M21 * child.Tx + M22 * child.Ty + Ty);
    }

    private static object? CreateSelectionSet(object document)
    {
        object? sets = Get(document, "SelectionSets");
        if (sets is null) return null;
        try
        {
            var count = Convert.ToInt32(Get(sets, "Count"));
            for (var index = count - 1; index >= 0; index--)
            {
                object? existing = null;
                try
                {
                    existing = Call(sets, "Item", index);
                    if (string.Equals(Get(existing!, "Name")?.ToString(), SelectionSetName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Call(existing!, "Delete");
                        break;
                    }
                }
                finally
                {
                    Release(existing);
                }
            }
            return Call(sets, "Add", SelectionSetName);
        }
        finally
        {
            Release(sets);
        }
    }

    private static int ReadInsUnits(object document)
    {
        try
        {
            var value = Call(document, "GetVariable", "INSUNITS");
            return value is null ? 4 : Convert.ToInt32(value);
        }
        catch
        {
            return 4;
        }
    }

    private static CadStructurePoint2? ToPoint(object? value)
    {
        var values = ToDoubles(value);
        return values.Length < 2 ? null : new CadStructurePoint2(values[0], values[1]);
    }

    private static double[] ToDoubles(object? value)
    {
        if (value is double[] doubles) return doubles;
        if (value is not Array array) return Array.Empty<double>();
        var result = new double[array.Length];
        for (var index = 0; index < array.Length; index++)
            result[index] = Convert.ToDouble(array.GetValue(index));
        return result;
    }

    private static void TryActivate(object application, object document)
    {
        try
        {
            Call(document, "Activate");
            Set(application, "WindowState", 3);
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Could not focus AutoCAD");
        }
    }

    private static void TryDelete(object selection)
    {
        try { Call(selection, "Delete"); }
        catch (Exception exception) { Log.Debug(exception, "Could not delete AutoCAD selection set"); }
    }

    private static object? Get(object target, string name) => target.GetType().InvokeMember(
        name, BindingFlags.GetProperty, null, target, null);

    private static void Set(object target, string name, object value) => target.GetType().InvokeMember(
        name, BindingFlags.SetProperty, null, target, new[] { value });

    private static object? Call(object target, string name, params object[] arguments) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments);

    /// <summary>
    /// Invokes a COM method whose results come back through by-reference arguments, such as
    /// GetBoundingBox. InvokeMember only writes them back when told which arguments are by-ref,
    /// so without the modifier the array stays null and the bounds look unreadable.
    /// </summary>
    private static object?[]? CallWithOutputs(object target, string name, int outputCount)
    {
        try
        {
            var arguments = new object?[outputCount];
            var modifier = new ParameterModifier(outputCount);
            for (var index = 0; index < outputCount; index++) modifier[index] = true;
            target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target,
                arguments, new[] { modifier }, null, null);
            return arguments;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "AutoCAD COM call {Method} with outputs failed", name);
            return null;
        }
    }

    private static void SafeCall(object? target, string name, params object[] arguments)
    {
        if (target is null) return;
        try { Call(target, name, arguments); }
        catch (Exception exception) { Log.Debug(exception, "AutoCAD COM call {Method} failed", name); }
    }

    private static T? Safe<T>(Func<T?> read)
    {
        try { return read(); }
        catch { return default; }
    }

    private static void Release(object? value)
    {
        if (value is null || !Marshal.IsComObject(value)) return;
        try { Marshal.ReleaseComObject(value); }
        catch (Exception exception) { Log.Debug(exception, "Could not release AutoCAD COM object"); }
    }

    private static Exception Innermost(Exception exception) =>
        exception.InnerException is null ? exception : Innermost(exception.InnerException);

    private static bool IsUserCancel(Exception exception)
    {
        var hresult = Innermost(exception).HResult;
        return hresult is unchecked((int)0x80004004) or unchecked((int)0x8004005E);
    }

    private static object? GetRunningInstance()
    {
        foreach (var progId in ProgIds)
        {
            if (CLSIDFromProgID(progId, out var classId) < 0) continue;
            if (GetActiveObject(ref classId, IntPtr.Zero, out var instance) >= 0
                && instance is not null) return instance;
        }
        return null;
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CLSIDFromProgID(string progId, out Guid classId);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int GetActiveObject(
        ref Guid classId,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}
