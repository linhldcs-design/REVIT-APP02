using Autodesk.Revit.DB;

namespace RevitAPP.Models
{
    public class ScheduleRenumberFieldOption
    {
        public ScheduleRenumberFieldOption(ScheduleFieldId fieldId, string columnName, string parameterName, ElementId parameterId)
        {
            FieldId = fieldId;
            ColumnName = columnName;
            ParameterName = parameterName;
            ParameterId = parameterId;
        }

        public ScheduleFieldId FieldId { get; }
        public string ColumnName { get; }
        public string ParameterName { get; }
        public ElementId ParameterId { get; }

        public override string ToString()
        {
            if (string.Equals(ColumnName, ParameterName, StringComparison.CurrentCultureIgnoreCase))
            {
                return ColumnName;
            }

            return $"{ColumnName} ({ParameterName})";
        }
    }
}
