using System.Text.Json.Serialization;

namespace SmartPS.Models.Ocr;

public class OcrProcessingInfo
{
    [JsonPropertyName("total_ms")]
    public double TotalMs { get; set; }
}
