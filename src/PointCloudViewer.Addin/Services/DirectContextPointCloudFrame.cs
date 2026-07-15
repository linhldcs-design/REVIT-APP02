using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;

namespace PointCloudViewer.Addin.Services;

public sealed class DirectContextPointCloudFrame
{
    public static readonly DirectContextPointCloudFrame Empty = new([], null, 0);

    public DirectContextPointCloudFrame(
        IReadOnlyList<VertexPositionColored> vertices,
        Outline? outline,
        int nativeInstanceCount)
    {
        Vertices = vertices;
        Outline = outline;
        NativeInstanceCount = nativeInstanceCount;
    }

    public IReadOnlyList<VertexPositionColored> Vertices { get; }
    public Outline? Outline { get; }
    public int NativeInstanceCount { get; }
    public int PointCount => Vertices.Count;
}
