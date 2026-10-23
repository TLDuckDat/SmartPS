using SmartPS.Services.OcrLisencePlate;

namespace SmartPS.Tests.Unit;

/// <summary>The CLI engine prints the result paths on stdout and writes a compact JSON; both map onto OcrPlateResponse.</summary>
public class LicensePlateOcrResultParsingTests
{
    [Fact]
    public void Output_paths_are_read_from_the_JSON_and_Annotated_lines()
    {
        var stdout = "Processed image: C:\\in\\car.jpg\r\n" +
                     "Vehicles=5, with_plate=1, with_ocr=1, status_ok=1\r\n" +
                     "JSON: C:\\engine\\car_20261022_230838\\car.json\r\n" +
                     "Annotated: C:\\engine\\car_20261022_230838\\car_annotated.jpg\r\n";

        var (json, annotated) = LicensePlateOcrService.ParseOutputPaths(stdout);

        Assert.Equal("C:\\engine\\car_20261022_230838\\car.json", json);
        Assert.Equal("C:\\engine\\car_20261022_230838\\car_annotated.jpg", annotated);
    }

    [Fact]
    public void Missing_output_lines_give_null_paths()
    {
        var (json, annotated) = LicensePlateOcrService.ParseOutputPaths("ALPR error: something failed\n");

        Assert.Null(json);
        Assert.Null(annotated);
    }

    [Fact]
    public void Ok_vehicles_become_plates_ordered_by_confidence()
    {
        const string json = """
            {
              "status": "ok",
              "vehicles": [
                { "vehicle_index": 0, "vehicle_type": "car", "license": "30K-555.55", "confidence": 0.62, "status": "ok" },
                { "vehicle_index": 1, "vehicle_type": "motorbike", "license": "29B1-123.45", "confidence": 0.91, "status": "ok" }
              ]
            }
            """;

        var response = LicensePlateOcrService.ParseEngineResult(json, "C:\\out\\a.jpg", 6120);

        Assert.True(response.IsSuccess);
        Assert.Equal(2, response.Count);
        Assert.Equal("29B1-123.45", response.PrimaryPlateText);
        Assert.Equal(0.91, response.PrimaryOcrConfidence);
        Assert.Equal("C:\\out\\a.jpg", response.OutputImage);
        Assert.Equal(6120, response.TotalMilliseconds);
    }

    [Fact]
    public void Vehicles_without_a_readable_plate_are_skipped()
    {
        const string json = """
            {
              "status": "ok",
              "vehicles": [
                { "vehicle_index": 0, "license": "", "confidence": 0.4, "status": "ok" },
                { "vehicle_index": 1, "license": "51F-999.99", "confidence": 0.3, "status": "no_plate" },
                { "vehicle_index": 2, "license": "30A-678.90", "confidence": 0.77, "status": "ok" }
              ]
            }
            """;

        var response = LicensePlateOcrService.ParseEngineResult(json, null, 10);

        var plate = Assert.Single(response.Plates);
        Assert.Equal("30A-678.90", plate.PlateText);
    }

    [Theory]
    [InlineData("""{ "status": "ok", "vehicles": [] }""")]
    [InlineData("""{ "status": "no_vehicle" }""")]
    public void No_plate_is_an_error_response(string json)
    {
        var response = LicensePlateOcrService.ParseEngineResult(json, null, 10);

        Assert.False(response.IsSuccess);
        Assert.Empty(response.Plates);
        Assert.Equal("NO_PLATE", response.Error?.Code);
    }
}
