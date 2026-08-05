using RevitAPP.Models;

namespace RevitAPP.Services
{
    public static class TranslationSessionSettings
    {
        private static TranslationOptions? _lastOptions;

        public static TranslationOptions CreateOptions()
        {
            var options = _lastOptions == null
                ? new TranslationOptions()
                : new TranslationOptions
                {
                    SourceLanguage = _lastOptions.SourceLanguage,
                    TargetLanguage = _lastOptions.TargetLanguage,
                    CaseMode = _lastOptions.CaseMode,
                    AppendToOriginal = _lastOptions.AppendToOriginal
                };

            return options;
        }

        public static void Save(TranslationOptions options)
        {
            _lastOptions = new TranslationOptions
            {
                SourceLanguage = options.SourceLanguage,
                TargetLanguage = options.TargetLanguage,
                CaseMode = options.CaseMode,
                AppendToOriginal = options.AppendToOriginal
            };
        }
    }
}
