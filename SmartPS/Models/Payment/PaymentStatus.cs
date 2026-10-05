namespace SmartPS.Models.Payment;

public enum PaymentStatus
{
    Created = 0,
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Cancelled = 4,
    Expired = 5,
    Refunded = 6
}

public enum PaymentTransactionStatus
{
    Created = 0,
    Pending = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public enum PaymentAttemptStatus
{
    Started = 0,
    Succeeded = 1,
    Failed = 2
}
