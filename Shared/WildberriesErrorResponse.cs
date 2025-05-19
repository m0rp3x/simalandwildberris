using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared;

public class WildberriesErrorResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("message")]
    public string Message { get; set; } = null!;

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}
