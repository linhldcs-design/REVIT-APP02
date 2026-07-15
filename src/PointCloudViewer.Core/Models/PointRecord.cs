namespace PointCloudViewer.Core.Models;

public readonly struct PointRecord
{
    public PointRecord(double x, double y, double z, RgbaColor color, double nx = 0, double ny = 0, double nz = 1, double scalar = 0)
    {
        X = x;
        Y = y;
        Z = z;
        Color = color;
        NormalX = nx;
        NormalY = ny;
        NormalZ = nz;
        Scalar = scalar;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public RgbaColor Color { get; }
    public double NormalX { get; }
    public double NormalY { get; }
    public double NormalZ { get; }
    public double Scalar { get; }
}
