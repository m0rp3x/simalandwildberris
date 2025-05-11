using System.Text.Json;
using System.Text.Json.Serialization;

namespace WBSL.Data.Client;

public interface IChadGptClient
{
    Task<string> ShortenAsync(string prompt, string text);
}

public class ChadGptClient : IChadGptClient
{
    private readonly string        _apiKey;
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        
    {
        PropertyNameCaseInsensitive = true
    };

    public ChadGptClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["ChadGpt:ApiKey"] 
                  ?? throw new InvalidOperationException("ChadGpt:ApiKey missing");
    }


    public async Task<string?> ShortenAsync(string prompt, string text)
    {
        var payload = new
        {
            message = $"{prompt}\n{text}",
            api_key = _apiKey
        };

        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsJsonAsync("", payload);
        }
        catch (Exception ex)
        {
            throw new Exception($"[HTTP] Ошибка при запросе к ChadGPT: {ex.Message}", ex);
        }

        var content = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"[HTTP {(int)resp.StatusCode}] {content}");

        ChadGptResponse? json;
        try
        {
            json = JsonSerializer.Deserialize<ChadGptResponse>(content, _jsonOptions);
        }
        catch (Exception ex)
        {
            throw new Exception($"Не смогли десериализовать ответ ChadGPT: {content}", ex);
        }

        if (json == null)
            throw new Exception($"Пустой ответ от ChadGPT: {content}");

        if (!json.IsSuccess)
            throw new Exception($"ChadGPT вернул ошибку: {json.ErrorMessage}. Response: {content}");

        return json.Response.Trim();
    }

    private class ChadGptResponse
    {
        [JsonPropertyName("is_success")]
        public bool IsSuccess { get; set; }

        [JsonPropertyName("response")]
        public string Response { get; set; } = "";

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }
}
