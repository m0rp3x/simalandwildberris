using System.Text.Json.Serialization;

namespace WBSL.Data.Models.DTO;

public class OrdersResponseDto
{
    [JsonPropertyName("next")]
    public long? Next { get; set; }
    
    [JsonPropertyName("orders")]
    public List<OrderDto> Orders { get; set; } = new();
}