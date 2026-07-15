using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using RevitAPP.Models;

namespace RevitAPP.Services
{
    public class GeminiTranslationService
    {
        public async Task<string> TranslateAsync(string sourceText, TranslationOptions options)
        {
            var prompt = BuildPrompt(sourceText, options.SourceLanguage, options.TargetLanguage);
            var request = new GeminiRequest
            {
                Contents =
                [
                    new GeminiContent
                    {
                        Role = "user",
                        Parts =
                        [
                            new GeminiPart
                            {
                                Text = prompt
                            }
                        ]
                    }
                ],
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1,
                    CandidateCount = 1
                }
            };

            var responseText = await PostJsonAsync(BuildEndpoint(options.Model), options.ApiKey, Serialize(request))
                .ConfigureAwait(false);

            var geminiResponse = Deserialize<GeminiResponse>(responseText);
            var translatedText = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?
                .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part.Text))?.Text;

            if (string.IsNullOrWhiteSpace(translatedText))
            {
                throw new InvalidOperationException("Gemini did not return translated text.");
            }

            return CleanTranslation(translatedText!);
        }

        public async Task<IReadOnlyList<string>> TranslateBatchAsync(IReadOnlyList<string> sourceTexts, TranslationOptions options)
        {
            if (sourceTexts.Count == 0)
            {
                return Array.Empty<string>();
            }

            if (sourceTexts.Count == 1)
            {
                return new[] { await TranslateAsync(sourceTexts[0], options).ConfigureAwait(false) };
            }

            var prompt = BuildBatchPrompt(sourceTexts, options.SourceLanguage, options.TargetLanguage);
            var request = new GeminiRequest
            {
                Contents =
                [
                    new GeminiContent
                    {
                        Role = "user",
                        Parts =
                        [
                            new GeminiPart
                            {
                                Text = prompt
                            }
                        ]
                    }
                ],
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1,
                    CandidateCount = 1
                }
            };

            var responseText = await PostJsonAsync(BuildEndpoint(options.Model), options.ApiKey, Serialize(request))
                .ConfigureAwait(false);

            var translatedText = GetResponseText(responseText);
            return ParseBatchResponse(translatedText, sourceTexts.Count);
        }

        private static async Task<string> PostJsonAsync(string endpoint, string apiKey, string json)
        {
            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Accept = "application/json";
            request.Timeout = 60000;
            request.Headers["x-goog-api-key"] = apiKey;

            var requestBytes = Encoding.UTF8.GetBytes(json);
            using (var requestStream = await request.GetRequestStreamAsync().ConfigureAwait(false))
            {
                await requestStream.WriteAsync(requestBytes, 0, requestBytes.Length).ConfigureAwait(false);
            }

            try
            {
                using var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
                using var responseStream = response.GetResponseStream();
                using var reader = new StreamReader(responseStream);
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
            catch (WebException exception) when (exception.Response != null)
            {
                using var response = (HttpWebResponse)exception.Response;
                using var responseStream = response.GetResponseStream();
                using var reader = new StreamReader(responseStream);
                var errorText = await reader.ReadToEndAsync().ConfigureAwait(false);
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    throw new InvalidOperationException(BuildQuotaMessage(errorText), exception);
                }

                throw new InvalidOperationException($"Gemini API error {(int)response.StatusCode}: {errorText}", exception);
            }
        }

        private static string BuildPrompt(string sourceText, string sourceLanguage, string targetLanguage)
        {
            var sourceInstruction = string.IsNullOrWhiteSpace(sourceLanguage) ||
                                    sourceLanguage.Equals("Auto detect", StringComparison.OrdinalIgnoreCase)
                ? "Tu dong nhan dien ngon ngu nguon. "
                : $"Van ban nguon la {sourceLanguage}. ";

            return
                "Ban la chuyen gia trong nganh dich thuat ban ve nganh xay dung. " +
                "Hay dich chinh xac dung thuat ngu ky thuat xay dung, kien truc, ket cau va MEP. " +
                sourceInstruction +
                $"Dich van ban sau sang {targetLanguage}. " +
                "Chi tra ve ban dich, khong giai thich, khong them dau ngoac kep, khong them tien to.\n\n" +
                $"Van ban: {sourceText}";
        }

        private static string BuildBatchPrompt(IReadOnlyList<string> sourceTexts, string sourceLanguage, string targetLanguage)
        {
            var sourceInstruction = string.IsNullOrWhiteSpace(sourceLanguage) ||
                                    sourceLanguage.Equals("Auto detect", StringComparison.OrdinalIgnoreCase)
                ? "Tu dong nhan dien ngon ngu nguon. "
                : $"Van ban nguon la {sourceLanguage}. ";

            var builder = new StringBuilder();
            builder.Append("Ban la chuyen gia trong nganh dich thuat ban ve nganh xay dung. ");
            builder.Append("Hay dich chinh xac dung thuat ngu ky thuat xay dung, kien truc, ket cau va MEP. ");
            builder.Append(sourceInstruction);
            builder.Append($"Dich cac van ban sau sang {targetLanguage}. ");
            builder.Append("Bat buoc giu dung so thu tu. Chi tra ve moi ket qua tren mot dong theo dinh dang [[so]] ban dich. ");
            builder.Append("Khong giai thich, khong them dau ngoac kep, khong them tien to khac.\n\n");

            for (var index = 0; index < sourceTexts.Count; index++)
            {
                builder.Append("[[");
                builder.Append(index + 1);
                builder.Append("]] ");
                builder.AppendLine(sourceTexts[index]);
            }

            return builder.ToString();
        }

        private static IReadOnlyList<string> ParseBatchResponse(string responseText, int expectedCount)
        {
            var translations = new string[expectedCount];
            var matches = Regex.Matches(responseText, @"\[\[(\d+)\]\]\s*(.*?)(?=\r?\n\[\[\d+\]\]|\z)", RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups[1].Value, out var oneBasedIndex))
                {
                    continue;
                }

                var index = oneBasedIndex - 1;
                if (index < 0 || index >= translations.Length)
                {
                    continue;
                }

                translations[index] = CleanTranslation(match.Groups[2].Value);
            }

            if (translations.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException("Gemini did not return enough translated lines for the selected text notes.");
            }

            return translations;
        }

        private static string GetResponseText(string responseText)
        {
            var geminiResponse = Deserialize<GeminiResponse>(responseText);
            var translatedText = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?
                .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part.Text))?.Text;

            if (string.IsNullOrWhiteSpace(translatedText))
            {
                throw new InvalidOperationException("Gemini did not return translated text.");
            }

            return translatedText!;
        }

        private static string BuildQuotaMessage(string errorText)
        {
            var retryMatch = Regex.Match(errorText, @"""retryDelay""\s*:\s*""([^""]+)""");
            var retryText = retryMatch.Success ? $" Thu lai sau {retryMatch.Groups[1].Value}." : string.Empty;
            return "Gemini API da vuot gioi han request mien phi trong phut nay." + retryText +
                   " Add-in da gom nhieu text vao 1 request, nhung ban van can doi hoac nang quota/API billing neu tiep tuc gap loi.";
        }

        private static string BuildEndpoint(string model)
        {
            var modelName = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash" : model.Trim();
            if (modelName.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                modelName = modelName.Substring("models/".Length);
            }

            return $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelName)}:generateContent";
        }

        private static string CleanTranslation(string text)
        {
            return text.Trim().Trim('"', '\'', '`').Replace("\r", " ").Replace("\n", " ").Trim();
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

        private static string Serialize<T>(T value)
        {
            using var stream = new MemoryStream();
            var serializer = new DataContractJsonSerializer(typeof(T));
            serializer.WriteObject(stream, value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static T? Deserialize<T>(string json)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var serializer = new DataContractJsonSerializer(typeof(T));
            return (T?)serializer.ReadObject(stream);
        }

        [DataContract]
        private class GeminiRequest
        {
            [DataMember(Name = "contents")]
            public List<GeminiContent> Contents { get; set; } = [];

            [DataMember(Name = "generationConfig")]
            public GeminiGenerationConfig GenerationConfig { get; set; } = new();
        }

        [DataContract]
        private class GeminiGenerationConfig
        {
            [DataMember(Name = "temperature")]
            public double Temperature { get; set; }

            [DataMember(Name = "candidateCount")]
            public int CandidateCount { get; set; }
        }

        [DataContract]
        private class GeminiContent
        {
            [DataMember(Name = "role", EmitDefaultValue = false)]
            public string? Role { get; set; }

            [DataMember(Name = "parts")]
            public List<GeminiPart> Parts { get; set; } = [];
        }

        [DataContract]
        private class GeminiPart
        {
            [DataMember(Name = "text")]
            public string? Text { get; set; }
        }

        [DataContract]
        private class GeminiResponse
        {
            [DataMember(Name = "candidates")]
            public List<GeminiCandidate>? Candidates { get; set; }
        }

        [DataContract]
        private class GeminiCandidate
        {
            [DataMember(Name = "content")]
            public GeminiContent? Content { get; set; }
        }
    }
}
