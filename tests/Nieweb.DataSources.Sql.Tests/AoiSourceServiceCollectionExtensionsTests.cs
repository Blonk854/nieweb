using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Nieweb.DataSources.Sql.Tests;

public sealed class AoiSourceServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNiewebAoiSources_RegistersNothing_WhenConfigEmpty()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddNiewebAoiSources(config);

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<HlyaoiSource>());
        Assert.Null(provider.GetService<MeaoiSource>());
    }

    [Fact]
    public void AddNiewebAoiSources_RegistersOnlyPopulatedSection()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nieweb:Aoi:Postreflow:Server"] = "server1",
                ["Nieweb:Aoi:Postreflow:Database"] = "HLYAOI",
                ["Nieweb:Aoi:Postreflow:User"] = "svc",
                ["Nieweb:Aoi:Postreflow:Password"] = "pw",
                // Prereflow left blank -> skipped.
            })
            .Build();

        services.AddNiewebAoiSources(config);

        using var provider = services.BuildServiceProvider();
        var hly = provider.GetService<HlyaoiSource>();
        Assert.NotNull(hly);
        Assert.Equal("postreflow", hly!.Descriptor.Id);
        Assert.Equal("5.0", hly.Descriptor.SchemaVersion);
        Assert.Null(provider.GetService<MeaoiSource>());
    }

    [Fact]
    public void AddNiewebAoiSources_RegistersBoth_WhenBothPopulated()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nieweb:Aoi:Postreflow:Server"] = "server1",
                ["Nieweb:Aoi:Postreflow:Database"] = "HLYAOI",
                ["Nieweb:Aoi:Postreflow:User"] = "svc",
                ["Nieweb:Aoi:Postreflow:Password"] = "pw",
                ["Nieweb:Aoi:Prereflow:Server"] = "server2",
                ["Nieweb:Aoi:Prereflow:Database"] = "MEAOI",
                ["Nieweb:Aoi:Prereflow:User"] = "svc",
                ["Nieweb:Aoi:Prereflow:Password"] = "pw",
            })
            .Build();

        services.AddNiewebAoiSources(config);

        using var provider = services.BuildServiceProvider();
        var hly = provider.GetService<HlyaoiSource>();
        var me = provider.GetService<MeaoiSource>();
        Assert.NotNull(hly);
        Assert.NotNull(me);
        Assert.Equal("prereflow", me!.Descriptor.Id);
        Assert.Equal("4.3.1", me.Descriptor.SchemaVersion);
    }

    [Fact]
    public void AddNiewebAoiSources_AlsoExposesEachSourceAsIAoiSource()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nieweb:Aoi:Postreflow:Server"] = "server1",
                ["Nieweb:Aoi:Postreflow:Database"] = "HLYAOI",
                ["Nieweb:Aoi:Postreflow:User"] = "svc",
                ["Nieweb:Aoi:Postreflow:Password"] = "pw",
                ["Nieweb:Aoi:Prereflow:Server"] = "server2",
                ["Nieweb:Aoi:Prereflow:Database"] = "MEAOI",
                ["Nieweb:Aoi:Prereflow:User"] = "svc",
                ["Nieweb:Aoi:Prereflow:Password"] = "pw",
            })
            .Build();

        services.AddNiewebAoiSources(config);

        using var provider = services.BuildServiceProvider();
        var all = provider.GetServices<IAoiSource>().ToList();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.Descriptor.Id == "postreflow");
        Assert.Contains(all, s => s.Descriptor.Id == "prereflow");

        // The IAoiSource resolution must return the SAME instance as the
        // concrete-type resolution (both are AddSingleton, but the second
        // registration is a lambda that could accidentally construct a
        // second instance if not written carefully).
        var hly = provider.GetRequiredService<HlyaoiSource>();
        Assert.Same(hly, all.Single(s => s.Descriptor.Id == "postreflow"));
    }
}
