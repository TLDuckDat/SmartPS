using SmartPS.Models.Parking;
using SmartPS.Data;
using Microsoft.EntityFrameworkCore;

namespace SmartPS.Services.Customers;

public class CustomersService
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbFactory;
    public CustomersService(IDbContextFactory<SmartPsDbContext> dbFactory) => _dbFactory = dbFactory;
}
