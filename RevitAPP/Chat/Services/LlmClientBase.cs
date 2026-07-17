using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RevitAPP.Chat.Services;

/// <summary>
///     Nền chung cho các client LLM: 1 HttpClient dùng lại, POST JSON, map lỗi HTTP sang thông báo tiếng
///     Việt, bật TLS1.2 (cần cho net48). KHÔNG log API key hay URL đầy đủ (Gemini để key trong query).
/// </summary>
public abstract class LlmClientBase
{
    /// <summary>Giới hạn số vòng gọi tool để chống lặp vô hạn.</summary>
    protected const int MaxToolRounds = 8;

    private static readonly HttpClient Http = CreateHttpClient();

    protected abstract string ProviderName { get; }

    private static HttpClient CreateHttpClient()
    {
        // net48 mặc định không bật TLS1.2 → phải set thủ công cho HTTPS tới API.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        return new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    /// <summary>
    ///     POST 1 body JSON tới endpoint. Header bổ sung (auth…) do caller thêm vào request.
    /// </summary>
    protected async Task<JObject> PostJsonAsync(
        string url,
        Action<HttpRequestMessage> configureRequest,
        JObject body,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
        };
        configureRequest(request);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LlmClientException($"{ProviderName}: hết thời gian chờ phản hồi (timeout).");
        }
        catch (HttpRequestException ex)
        {
            throw new LlmClientException($"{ProviderName}: lỗi kết nối mạng. {ex.Message}");
        }

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new LlmClientException(MapError(response.StatusCode, content));

        try
        {
            return JObject.Parse(content);
        }
        catch (Exception)
        {
            throw new LlmClientException($"{ProviderName}: phản hồi không đọc được (JSON không hợp lệ).");
        }
    }

    private string MapError(HttpStatusCode status, string rawBody)
    {
        var detail = ExtractErrorMessage(rawBody);
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"{ProviderName}: API key không hợp lệ hoặc không có quyền (HTTP {(int)status}).",
            (HttpStatusCode)429 =>
                $"{ProviderName}: bị giới hạn tần suất, thử lại sau (HTTP 429).",
            >= HttpStatusCode.InternalServerError =>
                $"{ProviderName}: máy chủ lỗi (HTTP {(int)status}). {detail}",
            _ => $"{ProviderName}: yêu cầu lỗi (HTTP {(int)status}). {detail}"
        };
    }

    private static string ExtractErrorMessage(string rawBody)
    {
        try
        {
            var json = JObject.Parse(rawBody);
            return json["error"]?["message"]?.ToString()
                   ?? json["error"]?.ToString()
                   ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

/// <summary>Lỗi từ client LLM, message đã ở dạng thân thiện tiếng Việt để hiển thị lên chat.</summary>
public sealed class LlmClientException : Exception
{
    public LlmClientException(string message) : base(message)
    {
    }
}
