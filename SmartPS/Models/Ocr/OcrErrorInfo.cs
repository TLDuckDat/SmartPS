using System.Text.Json.Serialization;

namespace SmartPS.Models.Ocr;

public class OcrErrorInfo
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
