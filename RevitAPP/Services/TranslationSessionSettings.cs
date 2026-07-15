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
                    ApiKey = _lastOptions.ApiKey,
                    SourceLanguage = _lastOptions.SourceLanguage,
                    TargetLanguage = _lastOptions.TargetLanguage,
                    Model = _lastOptions.Model,
                    CaseMode = _lastOptions.CaseMode,
                    AppendToOriginal = _lastOptions.AppendToOriginal
                };

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;
            }

            return options;
        }

        public static void Save(TranslationOptions options)
        {
            _lastOptions = new TranslationOptions
            {
                ApiKey = options.ApiKey,
                SourceLanguage = options.SourceLanguage,
                TargetLanguage = options.TargetLanguage,
                Model = options.Model,
                CaseMode = options.CaseMode,
                AppendToOriginal = options.AppendToOriginal
            };
        }
    }
}
