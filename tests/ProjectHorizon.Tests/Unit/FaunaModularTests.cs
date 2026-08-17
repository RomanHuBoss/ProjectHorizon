using System;
using System.Linq;
using Godot;
using Xunit;

public sealed class FaunaModularTests
{
    [Fact]
    public void SixBodyPlansHaveFixedCompatibleSkeletonFamilies()
    {
        EcologyCatalog catalog = RepositoryFixture.Ecology;
        Assert.Equal(6, FaunaBodyPlanRuntime.BodyPlans.Count);
        Assert.Equal(6, catalog.Fauna.Values.Select(item => item.BodyPlan).Distinct().Count());
        foreach (EcologyFaunaDefinition definition in catalog.Fauna.Values)
        {
            FaunaMorphologyProfile profile = FaunaBodyPlanRuntime.Build(definition, "test.instance");
            Assert.True(FaunaBodyPlanRuntime.IsCompatible(profile));
            Assert.StartsWith(definition.BodyPlan.ToLowerInvariant() + ".", profile.HeadModule);
            Assert.Equal(FaunaBodyPlanRuntime.GetSkeleton(definition.BodyPlan).Joints.Count, profile.JointCount);
        }
    }

    [Fact]
    public void MorphologyIsDeterministicButVariesPerInstance()
    {
        EcologyFaunaDefinition definition = RepositoryFixture.Ecology.Fauna.Values.First();
        FaunaMorphologyProfile first = FaunaBodyPlanRuntime.Build(definition, "instance.a");
        FaunaMorphologyProfile repeat = FaunaBodyPlanRuntime.Build(definition, "instance.a");
        FaunaMorphologyProfile second = FaunaBodyPlanRuntime.Build(definition, "instance.b");
        Assert.Equal(first, repeat);
        Assert.True(!first.Modules.SequenceEqual(second.Modules) ||
            Math.Abs(first.WidthScale - second.WidthScale) > 0.001);
    }

    [Fact]
    public void HierarchicalUtilityCoversCombatNeedsSocialAndTerritory()
    {
        EcologyCatalog catalog = RepositoryFixture.Ecology;
        EcologyFaunaDefinition attacker = catalog.Fauna.Values.First(item =>
            item.Aggression >= 0.60 && item.Behaviors.Contains("Attack"));
        EcologyFaunaDefinition grazer = catalog.Fauna.Values.First(item =>
            item.Diet == "Herbivore" && item.Behaviors.Contains("Graze"));
        EcologyFaunaDefinition follower = catalog.Fauna.Values.First(item =>
            item.Behaviors.Contains("FollowGroup"));
        Assert.Equal("Attack", FaunaBehaviorRuntime.SelectBehavior(attacker,
            new EcologyBehaviorContext(4, .2, .2, .2, 4, 1, false, true)));
        Assert.Equal("Graze", FaunaBehaviorRuntime.SelectBehavior(grazer,
            new EcologyBehaviorContext(30, .9, .2, .2, 4, 1, false, false)));
        Assert.Equal("FollowGroup", FaunaBehaviorRuntime.SelectBehavior(follower,
            new EcologyBehaviorContext(30, .1, .1, .1, 20, 1, false, false)));
        Assert.Equal("ReturnToTerritory", FaunaBehaviorRuntime.SelectBehavior(grazer,
            new EcologyBehaviorContext(30, .1, .1, .1, 4, 200, false, false)));
    }

    [Fact]
    public void SimulationTiersAreTenFiveTwoThenStatistical()
    {
        Assert.Equal(10.0, FaunaBehaviorRuntime.GetDecisionFrequencyHz(8), 6);
        Assert.Equal(5.0, FaunaBehaviorRuntime.GetDecisionFrequencyHz(40), 6);
        Assert.Equal(2.0, FaunaBehaviorRuntime.GetDecisionFrequencyHz(100), 6);
        Assert.Equal(0.0, FaunaBehaviorRuntime.GetDecisionFrequencyHz(180), 6);
    }

    [Fact]
    public void FarPopulationRunsWithoutSceneEntities()
    {
        EcologyPlan plan = EcologyPlanner.Plan(RepositoryFixture.Ecology);
        FaunaStatisticalSimulationRuntime runtime = new(plan.SimplifiedFauna);
        runtime.Tick(4.1);
        FaunaStatisticalSnapshot snapshot = runtime.CreateSnapshot();
        Assert.True(snapshot.Ticks >= 2);
        Assert.Equal(plan.SimplifiedFauna.Count, snapshot.Population);
        Assert.True(snapshot.Species > 0);
    }

    [Fact]
    public void BoidsProducesGroupSteeringForCompatibleNeighbors()
    {
        FaunaFlockSample self = new("a", "fauna.test", Vector3.Zero, Vector3.Forward, true);
        FaunaFlockSteering steering = FaunaFlockRuntime.Compute(self, new[]
        {
            self,
            new FaunaFlockSample("b", "fauna.test", new Vector3(1,0,0), Vector3.Forward, true),
            new FaunaFlockSample("c", "fauna.other", new Vector3(1,0,0), Vector3.Forward, true)
        });
        Assert.Equal(1, steering.Neighbors);
        Assert.True(steering.Combined.LengthSquared() > 0.0001f);
    }
}
