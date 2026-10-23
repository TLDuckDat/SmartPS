namespace SmartPS.Models.Incidents;

public enum IncidentType
{
    LostTicket = 0,
    UnreadablePlate = 1,
    Overstay = 2,
    VehicleDamage = 3,
    Security = 4,
    Equipment = 5,
    Other = 6
}

public enum IncidentStatus
{
    Open = 0,
    Resolved = 1
}
