using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nieweb.Data;

/// <summary>
/// Constructs a <see cref="NiewebDbContext"/> for <c>dotnet ef</c> tooling
/// (migrations, scripts, database update). Not used at runtime - the API
/// host wires the context up through DI with the real connection string
/// selected by <c>Nieweb:Db:Provider</c> in configuration.
/// </summary>
/// <remarks>
/// Uses SQLite because it needs no server, so migrations can be generated
/// from any machine. The <c>NIEWEB_DESIGNTIME_DB</c> environment variable
/// overrides the default path if a developer wants to point at their own
/// dev database. The Npgsql design-time factory arrives with D2.
/// </remarks>
public sealed class NiewebDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NiewebDbContext>
{
    public NiewebDbContext CreateDbContext(string[] args)
    {
        var dbPath = Environment.GetEnvironmentVariable("NIEWEB_DESIGNTIME_DB")
            ?? Path.Combine(AppContext.BaseDirectory, "nieweb-designtime.db");

        var options = new DbContextOptionsBuilder<NiewebDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new NiewebDbContext(options);
    }
}
