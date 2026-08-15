using System;
using Xunit;

namespace ProjectHorizon.Tests.Architecture;

public sealed class Section38ArchitectureTests
{
    [Fact]
    public void DomainEventBusDispatchesTypedEventsAndUnsubscribes()
    {
        DomainEventBus bus = new();
        int itemEvents = 0;
        using IDisposable subscription = bus.Subscribe<ItemAdded>(evt =>
        {
            Assert.Equal("item.test", evt.DefinitionId);
            itemEvents++;
        });

        bus.Publish(new ItemAdded(
            "item.test",
            2,
            "unit-test",
            DateTimeOffset.UtcNow));
        Assert.Equal(1, itemEvents);
        Assert.Equal(1, bus.SubscriptionCount);

        subscription.Dispose();
        bus.Publish(new ItemAdded(
            "item.test",
            1,
            "unit-test",
            DateTimeOffset.UtcNow));
        Assert.Equal(1, itemEvents);
        Assert.Equal(0, bus.SubscriptionCount);
    }

    [Fact]
    public void FrequencyGateMatchesNearbyAndDistantAiBudgets()
    {
        SystemFrequencyGate nearby = new(SystemFrequencyPolicy.NearbyAiHz);
        SystemFrequencyGate distant = new(SystemFrequencyPolicy.DistantAiHz);
        int nearbyTicks = 0;
        int distantTicks = 0;
        const double delta = 1.0 / SystemFrequencyPolicy.PhysicsHz;

        for (int frame = 0; frame < 600; frame++)
        {
            if (nearby.Consume(delta)) nearbyTicks++;
            if (distant.Consume(delta)) distantTicks++;
        }

        Assert.InRange(nearbyTicks, 99, 101);
        Assert.InRange(distantTicks, 19, 21);
    }

    [Fact]
    public void EcologyUsesNormativeAiTiers()
    {
        Assert.Equal(SystemFrequencyPolicy.NearbyAiHz,
            EcologyRuntime.GetUpdateFrequencyHz(8.0), 6);
        Assert.Equal(SystemFrequencyPolicy.DistantAiHz,
            EcologyRuntime.GetUpdateFrequencyHz(35.0), 6);
        Assert.Equal(0.0, EcologyRuntime.GetUpdateFrequencyHz(80.0), 6);
    }
    [Fact]
    public void LayeredAssembliesHaveOneWayDependencies()
    {
        var domainAssembly = typeof(IDomainEvent).Assembly;
        var applicationAssembly = typeof(DomainEventBus).Assembly;
        var clientAssembly = typeof(EcologyRuntime).Assembly;

        Assert.Equal("Game.Domain", domainAssembly.GetName().Name);
        Assert.Equal("Game.Application", applicationAssembly.GetName().Name);
        Assert.Equal("Game.Client", clientAssembly.GetName().Name);
        Assert.DoesNotContain(domainAssembly.GetReferencedAssemblies(), reference =>
            reference.Name is "Game.Application" or "Game.Client" or "GodotSharp" or "Microsoft.Data.Sqlite");
        Assert.Contains(applicationAssembly.GetReferencedAssemblies(), reference =>
            reference.Name == "Game.Domain");
        Assert.DoesNotContain(applicationAssembly.GetReferencedAssemblies(), reference =>
            reference.Name is "Game.Client" or "GodotSharp" or "Microsoft.Data.Sqlite");
    }

}
