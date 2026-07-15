namespace RevitAPP.Models
{
    public class TranslationOptions
    {
        public string ApiKey { get; set; } = string.Empty;

        public string SourceLanguage { get; set; } = "Auto detect";

        public string TargetLanguage { get; set; } = "English";

        public string Model { get; set; } = "gemini-2.5-flash";

        public TranslationCase CaseMode { get; set; } = TranslationCase.Upper;

        public bool AppendToOriginal { get; set; } = true;
    }
}
