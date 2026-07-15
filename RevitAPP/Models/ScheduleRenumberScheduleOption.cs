using Autodesk.Revit.DB;

namespace RevitAPP.Models
{
    public class ScheduleRenumberScheduleOption
    {
        public ScheduleRenumberScheduleOption(ElementId scheduleId, string name, IReadOnlyList<ScheduleRenumberFieldOption> fields)
        {
            ScheduleId = scheduleId;
            Name = name;
            Fields = fields;
        }

        public ElementId ScheduleId { get; }
        public string Name { get; }
        public IReadOnlyList<ScheduleRenumberFieldOption> Fields { get; }

        public override string ToString()
        {
            return Name;
        }
    }
}
