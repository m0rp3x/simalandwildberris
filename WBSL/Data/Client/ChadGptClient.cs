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
        const int maxInputLength = 3000;
        var fullPrompt = $"{prompt}\n{text}";
        if (fullPrompt.Length > maxInputLength)
            fullPrompt = fullPrompt[..maxInputLength];

        var payload = new
        {
            message = fullPrompt,
            api_key = _apiKey
        };

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var resp = await _http.PostAsJsonAsync("", payload, cts.Token);
                var content = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"[HTTP {(int)resp.StatusCode}] {content}");

                var json = JsonSerializer.Deserialize<ChadGptResponse>(content, _jsonOptions)
                           ?? throw new Exception($"Пустой ответ от ChadGPT: {content}");

                if (!json.IsSuccess)
                    throw new Exception($"ChadGPT вернул ошибку: {json.ErrorMessage}. Response: {content}");

                return json.Response.Trim();
            }
            catch (Exception ex)
            {
                if (attempt == 2)
                    throw new Exception($"[GPT FAIL] Попытка {attempt + 1}: {ex.Message}", ex);

                await Task.Delay(1000);
            }
        }

        return null;
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
