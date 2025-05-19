using System.Text.Json.Serialization;

namespace WBSL.Data.Models.DTO;


public class OrderOptionsDto
{
    [JsonPropertyName("isB2B")]
    public bool IsB2B { get; set; }
}