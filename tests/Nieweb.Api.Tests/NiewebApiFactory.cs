using System.Data.Common;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Nieweb.Data;

namespace Nieweb.Api.Tests;

/// <summary>
/// Boots Nieweb.Api in-process with an in-memory SQLite database so
/// tests can exercise the real endpoint pipeline (auth handshake,
/// Identity, JWT issuance) without touching the developer's disk DB.
/// </summary>
public class NiewebApiFactory : WebApplicationFactory<Program>
{
    // Keeping the connection open for the factory's lifetime keeps the
    // in-memory SQLite database alive across scopes.
    private readonly DbConnection _keepAlive;

    public NiewebApiFactory()
    {
        _keepAlive = new SqliteConnection("DataSource=:memory:");
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nieweb:Db:Provider"] = "Sqlite",
                // The DbContext registration reads this only to build the
                // connection string; we replace the provider below so the
                // literal value does not matter.
                ["ConnectionStrings:NiewebDb"] = "DataSource=:memory:",
                ["Nieweb:Auth:Jwt:Issuer"] = "https://nieweb.test",
                ["Nieweb:Auth:Jwt:Audience"] = "nieweb-api-test",
                ["Nieweb:Auth:Jwt:SigningKey"] = "nieweb-test-jwt-signing-key-must-be-32-plus-bytes",
                ["Nieweb:Auth:Jwt:AccessTokenLifetime"] = "00:05:00",
                // Cheap Argon2 parameters so tests aren't sluggish.
                ["Nieweb:Identity:Argon2id:MemoryKb"] = "8",
                ["Nieweb:Identity:Argon2id:Iterations"] = "1",
                ["Nieweb:Identity:Argon2id:DegreeOfParallelism"] = "1",
                ["Nieweb:Identity:Password:RequiredLength"] = "8",
                ["Nieweb:Identity:Password:RequireDigit"] = "false",
                ["Nieweb:Identity:Password:RequireLowercase"] = "false",
                ["Nieweb:Identity:Password:RequireUppercase"] = "false",
                ["Nieweb:Identity:Password:RequireNonAlphanumeric"] = "false",
                ["Nieweb:Identity:Password:RequiredUniqueChars"] = "1",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Drop the Program.cs Sqlite registration and replace it with
            // one that points at the shared in-memory connection.
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<NiewebDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<NiewebDbContext>(options =>
                options.UseSqlite(_keepAlive,
                    b => b.MigrationsAssembly("Nieweb.Data.Migrations.Sqlite")));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keepAlive.Dispose();
        }
        base.Dispose(disposing);
    }
}
