using Autodesk.Revit.DB;

namespace RevitAPP.Models
{
    public class ScheduleRenumberOptions
    {
        public ElementId ScheduleId { get; set; } = ElementId.InvalidElementId;
        public ScheduleFieldId TargetFieldId { get; set; } = ScheduleFieldId.InvalidScheduleFieldId;
        public ScheduleRenumberFormat Format { get; set; } = ScheduleRenumberFormat.Plain;
        public int StartNumber { get; set; } = 1;
        public int Step { get; set; } = 1;
        public string Prefix { get; set; } = string.Empty;
        public bool SkipReadOnlyElements { get; set; } = true;
    }
}
