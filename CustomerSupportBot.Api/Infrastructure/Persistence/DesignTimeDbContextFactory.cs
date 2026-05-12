// Infrastructure/Persistence/DesignTimeDbContextFactory.cs
// `dotnet ef` komutları için design-time fabrikası.
// Runtime'da Persistence:Provider InMemory iken bile migration üretebilmek
// için ConnectionStrings:PostgreSQL'i doğrudan okur.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CustomerSupportBot.Api.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<CustomerSupportDbContext>
{
    public CustomerSupportDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:PostgreSQL design-time için tanımlı olmalı.");

        var options = new DbContextOptionsBuilder<CustomerSupportDbContext>()
            .UseNpgsql(connectionString, npg =>
                npg.MigrationsHistoryTable("__ef_migrations_history", "public"))
            .Options;

        return new CustomerSupportDbContext(options);
    }
}
