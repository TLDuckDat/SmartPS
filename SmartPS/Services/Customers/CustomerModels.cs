using SmartPS.Models.Parking;

namespace SmartPS.Services.Customers;

public enum CustomerListFilter
{
    All = 0,
    Residents = 1,
    NonResidents = 2
}

public enum TicketDisplayStatus
{
    None = 0,
    Active = 1,
    ExpiringSoon = 2,
    Expired = 3,
    Suspended = 4,
    NotStarted = 5
}

public enum CustomerValidationError
{
    FullNameRequired,
    FullNameTooLong,
    PhoneInvalid,
    EmailInvalid,
    IdentityCardTooLong,
    ApartmentRequired,
    ApartmentInvalid,
    BuildingTooLong,
    NotesTooLong,
    PlateInvalid,
    DuplicatePlateInRequest,
    VehicleTypeRequired
}

public sealed record CustomerQuery
{
    public const int DefaultPageSize = 50;

    public string? SearchText { get; init; }

    public CustomerListFilter Filter { get; init; } = CustomerListFilter.All;

    public int PageIndex { get; init; }

    /// <summary>Kích thước trang, được giới hạn trong [1, 200].</summary>
    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed record CustomerListItem(
    int CustomerId,
    string FullName,
    string PhoneNumber,
    bool IsResident,
    string? ApartmentCode,
    string? Building,
    CustomerType Type,
    bool IsActive,
    IReadOnlyList<string> LicensePlates,
    string? PrimaryTicketCode,
    DateTime? PrimaryTicketEndUtc,
    TicketDisplayStatus TicketStatus);

public sealed record CustomerPage(IReadOnlyList<CustomerListItem> Items, int TotalCount, int PageIndex, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record CustomerSummary(int TotalCustomers, int ResidentCount, int ActiveTicketCount, int ExpiringSoonCount, decimal ActiveTicketRevenue);

public sealed record CustomerVehicleDto(
    int CustomerVehicleId,
    int CustomerId,
    string LicensePlate,
    int VehicleTypeId,
    string VehicleTypeName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? RemovedAt);

public sealed record MonthlyTicketDto(
    int TicketId,
    string TicketCode,
    int CustomerId,
    string LicensePlate,
    int? PlanId,
    string? PlanName,
    int VehicleTypeId,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    decimal Price,
    MonthlyTicketStatus Status,
    TicketDisplayStatus DisplayStatus,
    string? Notes);

public sealed record CustomerDetails(
    int CustomerId,
    string FullName,
    string PhoneNumber,
    string? Email,
    string? IdentityCard,
    bool IsResident,
    string? ApartmentCode,
    string? Building,
    CustomerType Type,
    bool IsActive,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<CustomerVehicleDto> Vehicles,
    IReadOnlyList<MonthlyTicketDto> Tickets);

public sealed record NewVehicle(string LicensePlate, int VehicleTypeId);

public sealed record CustomerUpsertRequest
{
    public string FullName { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;

    public string? Email { get; init; }

    public string? IdentityCard { get; init; }

    public bool IsResident { get; init; }

    public string? ApartmentCode { get; init; }

    public string? Building { get; init; }

    public CustomerType Type { get; init; } = CustomerType.Regular;

    public string? Notes { get; init; }

    /// <summary>Chỉ dùng khi tạo mới khách hàng.</summary>
    public IReadOnlyList<NewVehicle> Vehicles { get; init; } = Array.Empty<NewVehicle>();
}

public sealed record CreateTicketRequest(int CustomerId, string LicensePlate, int PlanId, DateOnly? StartDateVn = null, string? Notes = null);

internal static class TicketMapping
{
    public static MonthlyTicketDto ToDto(MonthlyTicket ticket, string? planName, DateTime nowUtc)
        => new(
            ticket.TicketId,
            ticket.TicketCode,
            ticket.CustomerId,
            ticket.RegisteredLicensePlate,
            ticket.PlanId,
            planName,
            ticket.VehicleTypeId,
            ticket.StartDate,
            ticket.EndDate,
            ticket.MonthlyPrice,
            ticket.Status,
            TicketStatusEvaluator.Evaluate(ticket.Status, ticket.StartDate, ticket.EndDate, nowUtc),
            ticket.Notes);
}
