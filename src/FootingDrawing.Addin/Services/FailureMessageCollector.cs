using Autodesk.Revit.DB;

namespace FootingDrawing.Addin.Services;

/// <summary>Chỉ thu thập FailureMessage khi commit; không xoá warning và không tự resolve.</summary>
public sealed class FailureMessageCollector(List<string> warnings) : IFailuresPreprocessor
{
    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        foreach (var message in failuresAccessor.GetFailureMessages())
        {
            var severity = message.GetSeverity();
            var text = message.GetDescriptionText();
            warnings.Add($"Revit commit [{severity}]: {text}");
        }

        return FailureProcessingResult.Continue;
    }
}
