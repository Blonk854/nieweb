using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nieweb.Data.Migrations.Sqlite;

/// <summary>
/// Constructs a <see cref="NiewebDbContext"/> for <c>dotnet ef</c> tooling
/// (migrations, scripts, database update) targeting SQLite. Not used at
/// runtime - the API host wires the context through DI with the provider
/// selected by <c>Nieweb:Db:Provider</c> in configuration.
/// </summary>
/// <remarks>
/// The <c>NIEWEB_DESIGNTIME_DB</c> environment variable overrides the
/// default file path if a developer wants to point at their own dev
/// database. <c>MigrationsAssembly</c> is pinned to this project so EF
/// discovers only the Sqlite migration set.
/// </remarks>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<NiewebDbContext>
{
    public NiewebDbContext CreateDbContext(string[] args)
    {
        var dbPath = Environment.GetEnvironmentVariable("NIEWEB_DESIGNTIME_DB")
            ?? Path.Combine(AppContext.BaseDirectory, "nieweb-designtime.db");

        var options = new DbContextOptionsBuilder<NiewebDbContext>()
            .UseSqlite(
                $"Data Source={dbPath}",
                b => b.MigrationsAssembly(typeof(SqliteDesignTimeFactory).Assembly.GetName().Name))
            .Options;

        return new NiewebDbContext(options);
    }
}
