using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartPS.Data;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Override for <see cref="IntegrationServices.Create"/>: the container's context factory creates contexts carrying the
/// given EF interceptors (any kind: command, save-changes, …). Test helpers keep using unhooked connections.
/// </summary>
public static class InterceptorInstall
{
    public static Action<IServiceCollection> With(PostgresDatabaseFixture fixture, params IInterceptor[] interceptors) => services =>
    {
        services.RemoveAll<IDbContextFactory<SmartPsDbContext>>();
        services.AddSingleton<IDbContextFactory<SmartPsDbContext>>(new Factory(fixture.ConnectionString, interceptors));
    };

    private sealed class Factory : IDbContextFactory<SmartPsDbContext>
    {
        private readonly DbContextOptions<SmartPsDbContext> _options;

        public Factory(string connectionString, IInterceptor[] interceptors)
        {
            _options = new DbContextOptionsBuilder<SmartPsDbContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(interceptors)
                .Options;
        }

        public SmartPsDbContext CreateDbContext() => new(_options);
    }
}
