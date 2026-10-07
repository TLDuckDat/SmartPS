namespace SmartPS.Models.GateControl;

/// <summary>Lý do từ chối cho xe vào bãi.</summary>
public enum CheckInRejectReason
{
    None = 0,
    EmptyPlate,
    AlreadyInside,
    PermissionDenied,
    Blacklisted,
    NoSlotAvailable,
    SlotNotFound,
    SlotVehicleTypeMismatch,
    SlotAudienceNotAllowed,
    SlotNotAvailable,
    PlateInvalid,
    ClassificationFailed
}
