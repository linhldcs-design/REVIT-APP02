using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RevitAPP.Models;

namespace RevitAPP.Services
{
    public class GoogleTranslateFreeService
    {
        private const string Endpoint = "https://translate.googleapis.com/translate_a/single";
        private const int MaxBatchCharacters = 3500;
        private static readonly HttpClient Client = CreateClient();

        private static readonly IReadOnlyDictionary<string, string> LanguageCodes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["auto"] = "auto",
                ["auto detect"] = "auto",
                ["tự động"] = "auto",
                ["english"] = "en",
                ["tiếng anh"] = "en",
                ["vietnamese"] = "vi",
                ["tiếng việt"] = "vi",
                ["chinese"] = "zh-CN",
                ["tiếng trung"] = "zh-CN",
                ["japanese"] = "ja",
                ["tiếng nhật"] = "ja",
                ["korean"] = "ko",
                ["tiếng hàn"] = "ko",
                ["french"] = "fr",
                ["tiếng pháp"] = "fr",
                ["german"] = "de",
                ["tiếng đức"] = "de",
                ["spanish"] = "es",
                ["tiếng tây ban nha"] = "es",
                ["thai"] = "th",
                ["tiếng thái"] = "th",
                ["russian"] = "ru",
                ["tiếng nga"] = "ru",
                ["portuguese"] = "pt",
                ["tiếng bồ đào nha"] = "pt"
            };

        public async Task<IReadOnlyList<string>> TranslateBatchAsync(
            IReadOnlyList<string> sourceTexts,
            TranslationOptions options)
        {
            if (sourceTexts.Count == 0)
            {
                return Array.Empty<string>();
            }

            var sourceLanguage = ResolveLanguageCode(options.SourceLanguage, allowAutoDetect: true);
            var targetLanguage = ResolveLanguageCode(options.TargetLanguage, allowAutoDetect: false);
            var translations = new string[sourceTexts.Count];

            foreach (var batch in CreateBatches(sourceTexts))
            {
                var requestText = BuildBatchText(sourceTexts, batch);
                var translatedText = await PostAsync(requestText, sourceLanguage, targetLanguage).ConfigureAwait(false);
                ParseBatchText(translatedText, batch, translations);
            }

            return translations;
        }

        public static string ApplyCase(string text, TranslationCase caseMode)
        {
            return caseMode switch
            {
                TranslationCase.Upper => text.ToUpper(CultureInfo.CurrentCulture),
                TranslationCase.Lower => text.ToLower(CultureInfo.CurrentCulture),
                _ => text
            };
        }

        private static IEnumerable<IReadOnlyList<int>> CreateBatches(IReadOnlyList<string> sourceTexts)
        {
            var batch = new List<int>();
            var characterCount = 0;

            for (var index = 0; index < sourceTexts.Count; index++)
            {
                var entryLength = sourceTexts[index].Length + 16;
                if (batch.Count > 0 && characterCount + entryLength > MaxBatchCharacters)
                {
                    yield return batch;
                    batch = new List<int>();
                    characterCount = 0;
                }

                batch.Add(index);
                characterCount += entryLength;
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }

        private static string BuildBatchText(IReadOnlyList<string> sourceTexts, IReadOnlyList<int> batch)
        {
            var builder = new StringBuilder();
            foreach (var index in batch)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append("[[");
                builder.Append(index + 1);
                builder.Append("]] ");
                builder.Append(sourceTexts[index]);
            }

            return builder.ToString();
        }

        private static void ParseBatchText(
            string translatedText,
            IReadOnlyList<int> batch,
            IList<string> translations)
        {
            var matches = Regex.Matches(
                translatedText,
                @"\[\[(\d+)\]\]\s*(.*?)(?=\r?\n\[\[\d+\]\]|\z)",
                RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups[1].Value, out var oneBasedIndex))
                {
                    continue;
                }

                var index = oneBasedIndex - 1;
                if (index >= 0 && index < translations.Count)
                {
                    translations[index] = CleanTranslation(match.Groups[2].Value);
                }
            }

            if (batch.Any(index => string.IsNullOrWhiteSpace(translations[index])))
            {
                throw new InvalidOperationException(
                    "Google Translate khong tra ve du ban dich. Vui long thu lai voi it TextNote hon.");
            }
        }

        private static async Task<string> PostAsync(string text, string sourceLanguage, string targetLanguage)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client"] = "gtx",
                ["sl"] = sourceLanguage,
                ["tl"] = targetLanguage,
                ["dt"] = "t",
                ["q"] = text
            });
            using var response = await Client.PostAsync(Endpoint, content).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Google Translate loi {(int)response.StatusCode}. Vui long thu lai sau.");
            }

            return ParseResponse(responseText);
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }

        private static string ParseResponse(string responseText)
        {
            var root = JArray.Parse(responseText);
            var segments = root[0] as JArray;
            var translation = string.Concat(
                segments?.OfType<JArray>().Select(segment => segment[0]?.Value<string>() ?? string.Empty)
                ?? Enumerable.Empty<string>());

            if (string.IsNullOrWhiteSpace(translation))
            {
                throw new InvalidOperationException("Google Translate khong tra ve ban dich.");
            }

            return translation;
        }

        private static string ResolveLanguageCode(string value, bool allowAutoDetect)
        {
            var normalized = value.Trim();
            if (LanguageCodes.TryGetValue(normalized, out var languageCode))
            {
                if (!allowAutoDetect && languageCode == "auto")
                {
                    throw new InvalidOperationException("Ngon ngu dich khong the la Auto detect.");
                }

                return languageCode;
            }

            if (Regex.IsMatch(normalized, @"^[a-z]{2,3}(?:-[A-Za-z]{2,4})?$"))
            {
                return normalized;
            }

            throw new InvalidOperationException(
                $"Ngon ngu '{value}' chua duoc ho tro. Hay nhap ma ngon ngu nhu en, vi, ja, ko, zh-CN.");
        }

        private static string CleanTranslation(string text)
        {
            return text.Trim().Trim('"', '\'', '`').Trim();
        }
    }
}
