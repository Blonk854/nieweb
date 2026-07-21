using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nieweb.Data.Migrations.Npgsql;

/// <summary>
/// Constructs a <see cref="NiewebDbContext"/> for <c>dotnet ef</c> tooling
/// (migrations, scripts, database update) targeting PostgreSQL via Npgsql.
/// Not used at runtime - the API host wires the context through DI with
/// the provider selected by <c>Nieweb:Db:Provider</c> in configuration.
/// </summary>
/// <remarks>
/// The <c>NIEWEB_DESIGNTIME_DB</c> environment variable overrides the
/// default connection string if a developer wants to point at their own
/// dev database. <c>MigrationsAssembly</c> is pinned to this project so
/// EF discovers only the Npgsql migration set.
/// </remarks>
public sealed class NpgsqlDesignTimeFactory : IDesignTimeDbContextFactory<NiewebDbContext>
{
    public NiewebDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NIEWEB_DESIGNTIME_DB")
            ?? "Host=localhost;Database=nieweb_designtime;Username=nieweb;Password=nieweb";

        var options = new DbContextOptionsBuilder<NiewebDbContext>()
            .UseNpgsql(
                connectionString,
                b => b.MigrationsAssembly(typeof(NpgsqlDesignTimeFactory).Assembly.GetName().Name))
            .Options;

        return new NiewebDbContext(options);
    }
}
