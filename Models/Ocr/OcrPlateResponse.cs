using System.Text.Json.Serialization;

namespace SmartPS.Models.Ocr;

public class OcrPlateResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("input_type")]
    public string? InputType { get; set; }

    [JsonPropertyName("output_image")]
    public string? OutputImage { get; set; }

    [JsonPropertyName("processing")]
    public OcrProcessingInfo? Processing { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("plates")]
    public List<OcrPlateItem> Plates { get; set; } = new();

    [JsonPropertyName("error")]
    public OcrErrorInfo? Error { get; set; }

    public bool IsSuccess => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);

    public string? PrimaryPlateText => Plates.FirstOrDefault()?.PlateText;
    public double? PrimaryOcrConfidence => Plates.FirstOrDefault()?.OcrConfidence;
    public double? PrimaryDetectionConfidence => Plates.FirstOrDefault()?.DetectionConfidence;
    public string? PrimaryCropPath => Plates.FirstOrDefault()?.CropPath;
    public double TotalMilliseconds => Processing?.TotalMs ?? 0;
}
