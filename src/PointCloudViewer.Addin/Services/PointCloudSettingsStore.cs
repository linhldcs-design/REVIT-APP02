using PointCloudViewer.Core.Caching;
using PointCloudViewer.Core.Models;

namespace PointCloudViewer.Addin.Services;

public sealed class PointCloudSettingsStore(RenderSettingsChangeClassifier changeClassifier)
{
    private PointCloudRenderSettings _settings = PointCloudRenderSettings.CreateDefault();

    public event EventHandler<RenderSettingsChangedEventArgs>? SettingsChanged;

    public PointCloudRenderSettings Current => _settings.Clone();

    public void Update(Action<PointCloudRenderSettings> update)
    {
        var before = _settings.Clone();
        var next = _settings.Clone();
        update(next);
        next.Clamp();

        var impact = changeClassifier.Classify(before, next);
        if (impact == RenderSettingsChangeImpact.None)
        {
            return;
        }

        _settings = next;
        SettingsChanged?.Invoke(this, new RenderSettingsChangedEventArgs(before, next.Clone(), impact));
    }

    public void Reset()
    {
        var before = _settings.Clone();
        var next = PointCloudRenderSettings.CreateDefault();
        var impact = changeClassifier.Classify(before, next);
        _settings = next;
        SettingsChanged?.Invoke(this, new RenderSettingsChangedEventArgs(before, next.Clone(), impact));
    }
}

public sealed class RenderSettingsChangedEventArgs(
    PointCloudRenderSettings previous,
    PointCloudRenderSettings current,
    RenderSettingsChangeImpact impact) : EventArgs
{
    public PointCloudRenderSettings Previous { get; } = previous;
    public PointCloudRenderSettings Current { get; } = current;
    public RenderSettingsChangeImpact Impact { get; } = impact;
}
