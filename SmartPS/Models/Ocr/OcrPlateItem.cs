using System.Text.Json.Serialization;

namespace SmartPS.Models.Ocr;

public class OcrPlateItem
{
    [JsonPropertyName("plate_text")]
    public string PlateText { get; set; } = string.Empty;

    [JsonPropertyName("detection_confidence")]
    public double DetectionConfidence { get; set; }

    [JsonPropertyName("ocr_confidence")]
    public double OcrConfidence { get; set; }

    [JsonPropertyName("crop_path")]
    public string? CropPath { get; set; }
}
