using DentalConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DentalConnect.Tests;

[CollectionDefinition("Postgres API")]
public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>;

public sealed class PostgresApiFixture : IAsyncLifetime
{
    public string? ConnectionString { get; } = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
    public bool Available => !string.IsNullOrWhiteSpace(ConnectionString);
    public WebApplicationFactory<Program>? Factory { get; private set; }

    public async Task InitializeAsync()
    {
        if (!Available)
            return;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("COGNITO_USER_POOL_ID", "us-east-1_test");
            builder.UseSetting("COGNITO_CLIENT_ID", "test-client");
            builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString!);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.AuthenticationScheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.AuthenticationScheme, _ => { });
                services.RemoveAll<DbContextOptions<DentalConnectDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DentalConnectDbContext>>();
                services.AddDbContext<DentalConnectDbContext>(options =>
                    options.UseNpgsql(ConnectionString!)
                        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
            });
        });

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>().Database.MigrateAsync();
    }

    public IServiceScope CreateScope() => Factory!.Services.CreateScope();

    public Task DisposeAsync()
    {
        Factory?.Dispose();
        return Task.CompletedTask;
    }
}
