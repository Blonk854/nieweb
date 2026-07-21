using System.Collections;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Nieweb.DataSources.Sql.Tests;

/// <summary>
/// Verifies that the AOI credential bridge translates the flat env-var
/// contract (as documented in <c>.env.example</c> / <c>probe-schema.ps1</c>)
/// into the structured <c>Nieweb:Aoi:*</c> config section that
/// <see cref="AoiSourceServiceCollectionExtensions.AddNiewebAoiSources"/>
/// binds against.
/// </summary>
public sealed class AoiEnvironmentConfigurationExtensionsTests
{
    [Fact]
    public void BuildMap_Empty_ReturnsEmptyDictionary()
    {
        var map = AoiEnvironmentConfigurationExtensions.BuildMap(new Hashtable());
        Assert.Empty(map);
    }

    [Fact]
    public void BuildMap_PostreflowOnly_MapsOnlyThatSubsection()
    {
        var env = new Hashtable
        {
            ["AOI_POSTREFLOW_SERVER"] = "HLYMSSQL2",
            ["AOI_POSTREFLOW_DATABASE"] = "HLYAOI2024",
            ["AOI_POSTREFLOW_USER"] = "svc_hlyaoiprod",
            ["AOI_POSTREFLOW_PASSWORD"] = "P@ss",
        };

        var map = AoiEnvironmentConfigurationExtensions.BuildMap(env);

        Assert.Equal("HLYMSSQL2", map["Nieweb:Aoi:Postreflow:Server"]);
        Assert.Equal("HLYAOI2024", map["Nieweb:Aoi:Postreflow:Database"]);
        Assert.Equal("svc_hlyaoiprod", map["Nieweb:Aoi:Postreflow:User"]);
        Assert.Equal("P@ss", map["Nieweb:Aoi:Postreflow:Password"]);
        Assert.DoesNotContain(map, kv => kv.Key.StartsWith("Nieweb:Aoi:Prereflow", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildMap_BothPopulated_MapsBothSubsections()
    {
        var env = new Hashtable
        {
            ["AOI_POSTREFLOW_SERVER"] = "HLYMSSQL2",
            ["AOI_POSTREFLOW_DATABASE"] = "HLYAOI2024",
            ["AOI_POSTREFLOW_USER"] = "svc",
            ["AOI_POSTREFLOW_PASSWORD"] = "pw",
            ["AOI_PREREFLOW_SERVER"] = "HLYMSSQL1",
            ["AOI_PREREFLOW_DATABASE"] = "MEAOI",
            ["AOI_PREREFLOW_USER"] = "meaoiprodinq",
            ["AOI_PREREFLOW_PASSWORD"] = "pw2",
        };

        var map = AoiEnvironmentConfigurationExtensions.BuildMap(env);

        Assert.Equal("HLYMSSQL2", map["Nieweb:Aoi:Postreflow:Server"]);
        Assert.Equal("HLYMSSQL1", map["Nieweb:Aoi:Prereflow:Server"]);
        Assert.Equal("MEAOI", map["Nieweb:Aoi:Prereflow:Database"]);
        Assert.Equal("meaoiprodinq", map["Nieweb:Aoi:Prereflow:User"]);
    }

    [Fact]
    public void BuildMap_SharedTimeouts_ApplyToBothSubsections()
    {
        var env = new Hashtable
        {
            ["AOI_POSTREFLOW_SERVER"] = "s1",
            ["AOI_CONNECT_TIMEOUT"] = "20",
            ["AOI_QUERY_TIMEOUT"] = "45",
        };

        var map = AoiEnvironmentConfigurationExtensions.BuildMap(env);

        Assert.Equal("20", map["Nieweb:Aoi:Postreflow:ConnectTimeoutSeconds"]);
        Assert.Equal("20", map["Nieweb:Aoi:Prereflow:ConnectTimeoutSeconds"]);
        Assert.Equal("45", map["Nieweb:Aoi:Postreflow:QueryTimeoutSeconds"]);
        Assert.Equal("45", map["Nieweb:Aoi:Prereflow:QueryTimeoutSeconds"]);
    }

    [Fact]
    public void BuildMap_IgnoresBlankValues()
    {
        var env = new Hashtable
        {
            ["AOI_POSTREFLOW_SERVER"] = "HLYMSSQL2",
            ["AOI_POSTREFLOW_DATABASE"] = "HLYAOI2024",
            ["AOI_POSTREFLOW_USER"] = "",       // blank -> not mapped
            ["AOI_POSTREFLOW_PASSWORD"] = "   ", // whitespace-only -> not mapped
        };

        var map = AoiEnvironmentConfigurationExtensions.BuildMap(env);

        Assert.True(map.ContainsKey("Nieweb:Aoi:Postreflow:Server"));
        Assert.True(map.ContainsKey("Nieweb:Aoi:Postreflow:Database"));
        Assert.False(map.ContainsKey("Nieweb:Aoi:Postreflow:User"));
        Assert.False(map.ContainsKey("Nieweb:Aoi:Postreflow:Password"));
    }

    [Fact]
    public void FindEnvFile_ReturnsNull_WhenAbsent()
    {
        using var tmp = new TempDirectory();
        // No .env anywhere in the temp tree.
        Assert.Null(AoiEnvironmentConfigurationExtensions.FindEnvFile(tmp.Path));
    }

    [Fact]
    public void FindEnvFile_LocatesEnvInParent()
    {
        using var tmp = new TempDirectory();
        var envPath = Path.Combine(tmp.Path, ".env");
        File.WriteAllText(envPath, "AOI_POSTREFLOW_SERVER=x\n");

        var nested = Path.Combine(tmp.Path, "src", "Nieweb.Api");
        Directory.CreateDirectory(nested);

        var found = AoiEnvironmentConfigurationExtensions.FindEnvFile(nested);

        Assert.NotNull(found);
        Assert.Equal(
            Path.GetFullPath(envPath),
            Path.GetFullPath(found!));
    }

    [Fact]
    public void LoadEnvFile_ParsesKeyValueLines_AndSkipsCommentsAndBlanks()
    {
        using var tmp = new TempDirectory();
        var envPath = Path.Combine(tmp.Path, ".env");
        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var keyA = $"NIEWEB_TEST_A_{uniqueSuffix}";
        var keyB = $"NIEWEB_TEST_B_{uniqueSuffix}";
        var keyC = $"NIEWEB_TEST_C_{uniqueSuffix}";
        var keyD = $"NIEWEB_TEST_D_{uniqueSuffix}";

        File.WriteAllText(envPath,
            $"""
            # comment line, ignored

            {keyA}=plain-value
            {keyB}="double quoted"
            {keyC}='single quoted'
            {keyD}=value=with=equals
            """);

        try
        {
            AoiEnvironmentConfigurationExtensions.LoadEnvFile(envPath);

            Assert.Equal("plain-value", Environment.GetEnvironmentVariable(keyA));
            Assert.Equal("double quoted", Environment.GetEnvironmentVariable(keyB));
            Assert.Equal("single quoted", Environment.GetEnvironmentVariable(keyC));
            // Only the first '=' separates key from value.
            Assert.Equal("value=with=equals", Environment.GetEnvironmentVariable(keyD));
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyA, null);
            Environment.SetEnvironmentVariable(keyB, null);
            Environment.SetEnvironmentVariable(keyC, null);
            Environment.SetEnvironmentVariable(keyD, null);
        }
    }

    [Fact]
    public void LoadEnvFile_DoesNotOverwriteExistingEnvVar()
    {
        using var tmp = new TempDirectory();
        var envPath = Path.Combine(tmp.Path, ".env");
        var key = $"NIEWEB_TEST_PREEXISTING_{Guid.NewGuid():N}";

        Environment.SetEnvironmentVariable(key, "from-real-env");
        File.WriteAllText(envPath, $"{key}=from-file\n");

        try
        {
            AoiEnvironmentConfigurationExtensions.LoadEnvFile(envPath);

            // Real environment must win.
            Assert.Equal("from-real-env", Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void AddNiewebAoiEnvironment_RoundTrip_LoadsFileAndResolvesSource()
    {
        // Full pipeline: temp .env -> AddNiewebAoiEnvironment -> config ->
        // AddNiewebAoiSources -> HlyaoiSource resolves.
        using var tmp = new TempDirectory();
        var envPath = Path.Combine(tmp.Path, ".env");
        var suffix = Guid.NewGuid().ToString("N");
        // Use unique per-test keys so parallel test runs cannot collide.
        // We control the key names by writing them ourselves; the extension
        // only reads AOI_POSTREFLOW_* / AOI_PREREFLOW_*, so we set those.
        var keys = new[]
        {
            "AOI_POSTREFLOW_SERVER",
            "AOI_POSTREFLOW_DATABASE",
            "AOI_POSTREFLOW_USER",
            "AOI_POSTREFLOW_PASSWORD",
        };

        // Snapshot existing values so we can restore them after the test.
        var snapshot = keys.ToDictionary(
            k => k,
            k => Environment.GetEnvironmentVariable(k),
            StringComparer.Ordinal);
        // Clear so LoadEnvFile actually writes the temp values.
        foreach (var k in keys)
        {
            Environment.SetEnvironmentVariable(k, null);
        }

        try
        {
            File.WriteAllText(envPath,
                $"""
                AOI_POSTREFLOW_SERVER=temphost-{suffix}
                AOI_POSTREFLOW_DATABASE=HLYAOI2024
                AOI_POSTREFLOW_USER=svc
                AOI_POSTREFLOW_PASSWORD=pw
                """);

            var configuration = new ConfigurationBuilder()
                .AddNiewebAoiEnvironment(envPath)
                .Build();

            var services = new ServiceCollection();
            services.AddNiewebAoiSources(configuration);

            using var provider = services.BuildServiceProvider();
            var hly = provider.GetService<HlyaoiSource>();
            Assert.NotNull(hly);
            Assert.Equal("postreflow", hly!.Descriptor.Id);
            Assert.Null(provider.GetService<MeaoiSource>());
        }
        finally
        {
            foreach (var kv in snapshot)
            {
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }
    }

    /// <summary>Scoped temp directory: created in ctor, recursively deleted on dispose.</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Nieweb-Aoi-Env-Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }
}
