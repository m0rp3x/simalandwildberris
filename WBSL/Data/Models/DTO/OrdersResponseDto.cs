using System.Text.Json.Serialization;

namespace WBSL.Data.Models.DTO;

public class OrdersResponseDto
{
    [JsonPropertyName("orders")]
    public List<OrderDto> Orders { get; set; } = new();
}