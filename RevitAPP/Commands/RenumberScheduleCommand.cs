using RevitAPP.Helpers;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Models;
using RevitAPP.Views;

namespace RevitAPP.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class RenumberScheduleCommand : ExternalCommand
    {
        public override void Execute()
        {
            if (!LicenseCommandGate.Ensure("Đánh Số Schedule")) return;
            var uiDocument = Application.ActiveUIDocument;
            var document = uiDocument.Document;
            var schedules = GetScheduleOptions(document);

            if (schedules.Count == 0)
            {
                TaskDialog.Show("RevitAI", "Khong tim thay Schedule nao co field hop le de danh so.");
                return;
            }

            var activeScheduleId = document.ActiveView is ViewSchedule activeSchedule
                ? activeSchedule.Id
                : ElementId.InvalidElementId;

            var window = new ScheduleRenumberOptionsWindow(Application.MainWindowHandle, schedules, activeScheduleId);
            if (window.ShowDialog() != true)
            {
                return;
            }

            var schedule = document.GetElement(window.Options.ScheduleId) as ViewSchedule;
            if (schedule == null)
            {
                TaskDialog.Show("RevitAI", "Khong tim thay Schedule da chon.");
                return;
            }

            var result = Renumber(document, schedule, window.Options);
            TaskDialog.Show("RevitAI",
                $"Da danh so {result.UpdatedCount} element.\nBo qua {result.SkippedCount} element.\nBang thong ke: {schedule.Name}");
        }

        private static List<ScheduleRenumberScheduleOption> GetScheduleOptions(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(schedule => !schedule.IsTemplate)
                .Select(schedule => new ScheduleRenumberScheduleOption(schedule.Id, schedule.Name, GetFieldOptions(schedule)))
                .Where(schedule => schedule.Fields.Count > 0)
                .OrderBy(schedule => schedule.Name)
                .ToList();
        }

        private static IReadOnlyList<ScheduleRenumberFieldOption> GetFieldOptions(ViewSchedule schedule)
        {
            var definition = schedule.Definition;

            return definition.GetFieldOrder()
                .Select(definition.GetField)
                .Where(field => field.ParameterId != ElementId.InvalidElementId)
                .Select(field => new ScheduleRenumberFieldOption(
                    field.FieldId,
                    GetColumnName(field),
                    field.GetName(),
                    field.ParameterId))
                .OrderBy(field => field.ColumnName)
                .ToList();
        }

        private static string GetColumnName(ScheduleField field)
        {
            return string.IsNullOrWhiteSpace(field.ColumnHeading) ? field.GetName() : field.ColumnHeading;
        }

        private static RenumberResult Renumber(Document document, ViewSchedule schedule, ScheduleRenumberOptions options)
        {
            var targetField = schedule.Definition.GetField(options.TargetFieldId);
            var targetParameterId = targetField.ParameterId;
            var sortSpecs = GetScheduleSortSpecs(schedule);

            var rows = new FilteredElementCollector(document, schedule.Id)
                .WhereElementIsNotElementType()
                .ToElements()
                .Select((element, index) => new ScheduleRowElement(
                    element,
                    sortSpecs
                        .Select(sortSpec => GetParameterDisplayValue(document, element, sortSpec.ParameterId))
                        .ToList(),
                    index))
                .ToList();

            if (sortSpecs.Count > 0)
            {
                rows.Sort(new ScheduleRowElementComparer(sortSpecs));
            }

            var updatedCount = 0;
            var skippedCount = 0;
            var currentNumber = options.StartNumber;

            using var transaction = new Transaction(document, "Renumber schedule");
            transaction.Start();

            foreach (var row in rows)
            {
                var parameter = GetElementParameter(document, row.Element, targetParameterId);
                if (parameter == null || parameter.IsReadOnly)
                {
                    skippedCount++;

                    if (!options.SkipReadOnlyElements)
                    {
                        currentNumber += options.Step;
                    }

                    continue;
                }

                var value = FormatNumber(currentNumber, options);
                if (TrySetParameter(parameter, value, currentNumber))
                {
                    updatedCount++;
                }
                else
                {
                    skippedCount++;
                }

                currentNumber += options.Step;
            }

            transaction.Commit();

            return new RenumberResult(updatedCount, skippedCount);
        }

        private static List<ScheduleSortSpec> GetScheduleSortSpecs(ViewSchedule schedule)
        {
            var definition = schedule.Definition;

            return definition.GetSortGroupFields()
                .Select(sortGroupField =>
                {
                    var field = definition.GetField(sortGroupField.FieldId);
                    return new ScheduleSortSpec(field.ParameterId, sortGroupField.SortOrder == ScheduleSortOrder.Descending);
                })
                .Where(sortSpec => sortSpec.ParameterId != ElementId.InvalidElementId)
                .ToList();
        }

        private static Parameter? GetElementParameter(Document document, Element element, ElementId parameterId)
        {
            if (parameterId == ElementId.InvalidElementId)
            {
                return null;
            }

            if (parameterId.ToValue() < 0)
            {
                return element.get_Parameter((BuiltInParameter)parameterId.ToValue());
            }

            if (document.GetElement(parameterId) is ParameterElement parameterElement)
            {
                return element.get_Parameter(parameterElement.GetDefinition());
            }

            return element.Parameters
                .Cast<Parameter>()
                .FirstOrDefault(parameter => parameter.Id == parameterId);
        }

        private static string GetParameterDisplayValue(Document document, Element element, ElementId? parameterId)
        {
            if (parameterId == null)
            {
                return string.Empty;
            }

            var parameter = GetElementParameter(document, element, parameterId);
            if (parameter == null)
            {
                return string.Empty;
            }

            return parameter.AsValueString()
                   ?? parameter.AsString()
                   ?? parameter.AsInteger().ToString();
        }

        private static string FormatNumber(int number, ScheduleRenumberOptions options)
        {
            return options.Format switch
            {
                ScheduleRenumberFormat.TwoDigits => number.ToString("00"),
                ScheduleRenumberFormat.ThreeDigits => number.ToString("000"),
                ScheduleRenumberFormat.Prefix => $"{options.Prefix}{number}",
                _ => number.ToString()
            };
        }

        private static bool TrySetParameter(Parameter parameter, string value, int number)
        {
            try
            {
                return parameter.StorageType switch
                {
                    StorageType.String => parameter.Set(value),
                    StorageType.Integer => parameter.Set(number),
                    StorageType.Double => parameter.Set(number),
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }

        private readonly record struct ScheduleRowElement(Element Element, IReadOnlyList<string> SortValues, int OriginalIndex);

        private readonly record struct ScheduleSortSpec(ElementId ParameterId, bool Descending);

        private readonly record struct RenumberResult(int UpdatedCount, int SkippedCount);

        private class ScheduleRowElementComparer : IComparer<ScheduleRowElement>
        {
            private readonly IReadOnlyList<ScheduleSortSpec> _sortSpecs;

            public ScheduleRowElementComparer(IReadOnlyList<ScheduleSortSpec> sortSpecs)
            {
                _sortSpecs = sortSpecs;
            }

            public int Compare(ScheduleRowElement x, ScheduleRowElement y)
            {
                for (var index = 0; index < _sortSpecs.Count; index++)
                {
                    var comparison = StringComparer.CurrentCultureIgnoreCase.Compare(x.SortValues[index], y.SortValues[index]);
                    if (comparison == 0)
                    {
                        continue;
                    }

                    return _sortSpecs[index].Descending ? -comparison : comparison;
                }

                return x.OriginalIndex.CompareTo(y.OriginalIndex);
            }
        }
    }
}
