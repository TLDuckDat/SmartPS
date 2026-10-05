namespace SmartPS.Models.GateControl;

public class GateCheckInRequest
{
    public string LicensePlate { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public string? AnnotatedImagePath { get; set; }
    public string? CropImagePath { get; set; }
    public int VehicleTypeId { get; set; }
    public int? SlotId { get; set; }
    public int? CreatedByUserId { get; set; }
}
