namespace SmartPS.Models.Shifts;

public enum ShiftStatus
{
    Active = 0,
    Locked = 1,
    Reviewed = 2
}

public enum FinancialTransactionType
{
    ParkingFee = 0,
    Refund = 1,
    Adjustment = 2,
    Incident = 3
}

public enum AdjustmentDirection
{
    Increase = 0,
    Decrease = 1
}
