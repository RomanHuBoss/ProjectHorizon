using System;
using System.Collections.Generic;
using Godot;

public partial class SalvageRepairSlice
{
    private readonly DomainEventBus _domainEventBus = new();
    private readonly List<IDisposable> _domainEventSubscriptions = new();
    private string _task142AcceptanceHud = "READY";
    private int _task142PublishedEvents;
    private int _task142ResourceQuestUpdates;
    private int _task142ProceduralQuestUpdates;
    private readonly SystemFrequencyGate _backgroundEconomyGate =
        new(SystemFrequencyPolicy.DefaultBackgroundEconomyHz);
    private readonly SystemFrequencyGate _telemetryFlushGate =
        new(SystemFrequencyPolicy.TelemetryFlushHz);

    private IDomainEventBus DomainEvents => _domainEventBus;

    private void InitializeArchitectureRuntime()
    {
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<ResourceMined>(OnResourceMined));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<ItemAdded>(OnItemAdded));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<ItemRemoved>(OnItemRemoved));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<PlanetEntered>(OnPlanetEntered));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<PlanetExited>(OnPlanetExited));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<SystemDiscovered>(OnSystemDiscovered));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<QuestAccepted>(OnQuestAccepted));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<QuestCompleted>(OnQuestCompleted));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<ShipDamaged>(OnShipDamaged));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<BaseModulePlaced>(OnBaseModulePlaced));
        _domainEventSubscriptions.Add(DomainEvents.Subscribe<SaveRequested>(OnSaveRequested));

        GD.Print(
            "TASK-142 architecture runtime READY: " +
            $"typedEvents=11; subscriptions={DomainEvents.SubscriptionCount}; " +
            $"physicsHz={SystemFrequencyPolicy.PhysicsHz:0}; " +
            $"nearbyAiHz={SystemFrequencyPolicy.NearbyAiHz:0}; " +
            $"distantAiHz={SystemFrequencyPolicy.DistantAiHz:0}; " +
            $"backgroundEconomyHz={SystemFrequencyPolicy.DefaultBackgroundEconomyHz:0.0}; " +
            "eventBus=domain-only; godotSignals=scene-local; F5=acceptance.");
    }

    private void UpdateArchitectureRuntime(double deltaSeconds)
    {
        if (_backgroundEconomyGate.Consume(deltaSeconds))
        {
            long economyDays = StationServices.RefreshEconomy(
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (economyDays > 0)
            {
                QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
                if (_stationServicesOpen)
                {
                    UpdateStationServicesPanel();
                }
            }
        }

        if (_telemetryFlushGate.Consume(deltaSeconds))
        {
            StructuredGameLogger.FlushPending();
        }
    }

    private void DisposeArchitectureRuntime()
    {
        StructuredGameLogger.FlushPending();
        foreach (IDisposable subscription in _domainEventSubscriptions)
        {
            subscription.Dispose();
        }
        _domainEventSubscriptions.Clear();
    }

    private void PublishDomainEvent<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent
    {
        _task142PublishedEvents++;
        DomainEvents.Publish(domainEvent);
    }

    private void OnResourceMined(ResourceMined domainEvent)
    {
        _task142ResourceQuestUpdates = StationServices.RecordObjective(
            StationServiceObjectiveType.CollectResource,
            domainEvent.DefinitionId,
            domainEvent.Quantity);
        _task142ProceduralQuestUpdates = RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.CollectResource,
            domainEvent.DefinitionId,
            domainEvent.Quantity);
        PublishDomainEvent(new ItemAdded(
            domainEvent.DefinitionId,
            domainEvent.Quantity,
            "resource-mining",
            domainEvent.OccurredAtUtc));
        _lastDomainEvent =
            $"ResourceMined({domainEvent.ResourceNodeId},{domainEvent.DefinitionId},{domainEvent.Quantity})";
    }

    private void OnItemAdded(ItemAdded domainEvent)
    {
        _lastDomainEvent =
            $"ItemAdded({domainEvent.DefinitionId},{domainEvent.Quantity},{domainEvent.Source})";
    }

    private void OnItemRemoved(ItemRemoved domainEvent)
    {
        _lastDomainEvent =
            $"ItemRemoved({domainEvent.DefinitionId},{domainEvent.Quantity},{domainEvent.Source})";
    }

    private void OnPlanetEntered(PlanetEntered domainEvent)
    {
        RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.ExplorePlanet,
            domainEvent.PlanetId,
            1,
            queueAutosave: false);
        _lastDomainEvent = $"PlanetEntered({domainEvent.PlanetId})";
        QueueCurrentSnapshot(AutosaveTrigger.Landing);
    }

    private void OnPlanetExited(PlanetExited domainEvent)
    {
        _lastDomainEvent = $"PlanetExited({domainEvent.PlanetId})";
        QueueCurrentSnapshot(AutosaveTrigger.Takeoff);
    }

    private void OnSystemDiscovered(SystemDiscovered domainEvent)
    {
        RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.ExploreSystem,
            domainEvent.SystemId,
            1,
            queueAutosave: false);
        _lastDomainEvent = $"SystemDiscovered({domainEvent.SystemId})";
        QueueCurrentSnapshot(AutosaveTrigger.Hyperspace);
    }

    private void OnQuestAccepted(QuestAccepted domainEvent)
    {
        _lastDomainEvent = $"QuestAccepted({domainEvent.QuestId},{domainEvent.QuestSource})";
        AutosaveTrigger trigger = string.Equals(
            domainEvent.QuestSource,
            "procedural",
            StringComparison.Ordinal)
            ? AutosaveTrigger.QuestCompleted
            : AutosaveTrigger.BaseChanged;
        QueueCurrentSnapshot(trigger);
    }

    private void OnQuestCompleted(QuestCompleted domainEvent)
    {
        _lastDomainEvent = $"QuestCompleted({domainEvent.QuestId},{domainEvent.QuestSource})";
        QueueCurrentSnapshot(AutosaveTrigger.QuestCompleted);
    }

    private void OnShipDamaged(ShipDamaged domainEvent)
    {
        _lastDomainEvent =
            $"ShipDamaged({domainEvent.SystemId},{domainEvent.Damage:0.#},{domainEvent.RemainingHealth:0.#})";
        QueueCurrentSnapshot(AutosaveTrigger.ShipChanged);
    }

    private void OnBaseModulePlaced(BaseModulePlaced domainEvent)
    {
        RecordProceduralQuestObjective(
            ProceduralQuestObjectiveType.BuildModule,
            domainEvent.ModuleId,
            1,
            queueAutosave: false);
        _lastDomainEvent =
            $"BaseModulePlaced({domainEvent.InstanceId},{domainEvent.ModuleId})";
        QueueCurrentSnapshot(AutosaveTrigger.BaseChanged);
    }

    private void OnSaveRequested(SaveRequested domainEvent)
    {
        StructuredGameLogger.Log(
            GameLogLevel.Debug,
            GameLogCategory.SAVE,
            "domain save request",
            fields: new Dictionary<string, object?>
            {
                ["slot"] = domainEvent.SlotId,
                ["revision"] = domainEvent.Revision,
                ["trigger"] = domainEvent.Trigger
            });
    }

    private void RunArchitectureAcceptance()
    {
        _task142AcceptanceHud = "RUNNING";
        try
        {
            DomainEventBus probeBus = new();
            int handled = 0;
            using IDisposable itemAdded = probeBus.Subscribe<ItemAdded>(_ => handled++);
            using IDisposable itemRemoved = probeBus.Subscribe<ItemRemoved>(_ => handled++);
            using IDisposable resourceMined = probeBus.Subscribe<ResourceMined>(_ => handled++);
            using IDisposable planetEntered = probeBus.Subscribe<PlanetEntered>(_ => handled++);
            using IDisposable planetExited = probeBus.Subscribe<PlanetExited>(_ => handled++);
            using IDisposable systemDiscovered = probeBus.Subscribe<SystemDiscovered>(_ => handled++);
            using IDisposable questAccepted = probeBus.Subscribe<QuestAccepted>(_ => handled++);
            using IDisposable questCompleted = probeBus.Subscribe<QuestCompleted>(_ => handled++);
            using IDisposable shipDamaged = probeBus.Subscribe<ShipDamaged>(_ => handled++);
            using IDisposable baseModulePlaced = probeBus.Subscribe<BaseModulePlaced>(_ => handled++);
            using IDisposable saveRequested = probeBus.Subscribe<SaveRequested>(_ => handled++);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            probeBus.Publish(new ItemAdded("item.test", 1, "acceptance", now));
            probeBus.Publish(new ItemRemoved("item.test", 1, "acceptance", now));
            probeBus.Publish(new ResourceMined("resource.test", "item.test", 1, now));
            probeBus.Publish(new PlanetEntered("planet.test", now));
            probeBus.Publish(new PlanetExited("planet.test", now));
            probeBus.Publish(new SystemDiscovered("system.test", 2, now));
            probeBus.Publish(new QuestAccepted("quest.test", "acceptance", now));
            probeBus.Publish(new QuestCompleted("quest.test", "acceptance", now));
            probeBus.Publish(new ShipDamaged("ship.system.test", 1.0, 99.0, now));
            probeBus.Publish(new BaseModulePlaced("base.test", "module.test", 0, 0, now));
            probeBus.Publish(new SaveRequested("slot.test", 1, "Acceptance", now));

            SystemFrequencyGate nearby = new(SystemFrequencyPolicy.NearbyAiHz);
            SystemFrequencyGate distant = new(SystemFrequencyPolicy.DistantAiHz);
            int nearbyTicks = 0;
            int distantTicks = 0;
            const double sampleDelta = 1.0 / SystemFrequencyPolicy.PhysicsHz;
            for (int frame = 0; frame < 600; frame++)
            {
                if (nearby.Consume(sampleDelta)) nearbyTicks++;
                if (distant.Consume(sampleDelta)) distantTicks++;
            }

            bool eventContract = handled == 11 && probeBus.SubscriptionCount == 11;
            bool frequencyContract = nearbyTicks >= 99 && nearbyTicks <= 101 &&
                distantTicks >= 19 && distantTicks <= 21 &&
                Math.Abs(EcologyRuntime.GetUpdateFrequencyHz(8.0) -
                    SystemFrequencyPolicy.NearbyAiHz) < 0.001 &&
                Math.Abs(EcologyRuntime.GetUpdateFrequencyHz(35.0) -
                    SystemFrequencyPolicy.DistantAiHz) < 0.001;
            bool liveContract = DomainEvents.SubscriptionCount == 11;
            bool passed = eventContract && frequencyContract && liveContract;
            _task142AcceptanceHud = passed ? "PASS" : "FAIL";
            string result =
                $"TASK-142 architecture acceptance {(passed ? "PASS" : "FAIL")}: " +
                $"typedEvents={handled}/11; liveSubscriptions={DomainEvents.SubscriptionCount}/11; " +
                $"nearbyTicks={nearbyTicks}/100; distantTicks={distantTicks}/20; " +
                $"physicsHz={SystemFrequencyPolicy.PhysicsHz:0}; playerHz={SystemFrequencyPolicy.PlayerControllerHz:0}; " +
                $"nearbyAiHz={SystemFrequencyPolicy.NearbyAiHz:0}; distantAiHz={SystemFrequencyPolicy.DistantAiHz:0}; " +
                $"backgroundEconomyHz={SystemFrequencyPolicy.DefaultBackgroundEconomyHz:0.0}; " +
                $"publishedRuntime={_task142PublishedEvents}; eventBus={(eventContract ? 1 : 0)}; " +
                $"frequencyPolicy={(frequencyContract ? 1 : 0)}; result=section-38-architecture-runtime.";
            GD.Print(result);
            if (!passed)
            {
                GD.PushError(result);
            }
        }
        catch (Exception exception)
        {
            _task142AcceptanceHud = "FAIL";
            GD.PushError($"TASK-142 architecture acceptance FAIL: {exception}");
        }
    }
    private void RunPlatformArchitectureAcceptance()
    {
        try
        {
            string domainAssembly = typeof(IDomainEvent).Assembly.GetName().Name ?? string.Empty;
            string applicationAssembly = typeof(DomainEventBus).Assembly.GetName().Name ?? string.Empty;
            string clientAssembly = GetType().Assembly.GetName().Name ?? string.Empty;
            bool layers =
                string.Equals(domainAssembly, "Game.Domain", StringComparison.Ordinal) &&
                string.Equals(applicationAssembly, "Game.Application", StringComparison.Ordinal) &&
                string.Equals(clientAssembly, "Game.Client", StringComparison.Ordinal) &&
                !string.Equals(domainAssembly, applicationAssembly, StringComparison.Ordinal) &&
                !string.Equals(applicationAssembly, clientAssembly, StringComparison.Ordinal);
            RendererProfileSnapshot renderer = RendererProfileDiagnostics.Capture();
            bool passed = layers && renderer.IsValidForProfile;
            string result =
                $"TASK-144 platform architecture acceptance {(passed ? "PASS" : "FAIL")}: " +
                $"domainAssembly={domainAssembly}; applicationAssembly={applicationAssembly}; " +
                $"clientAssembly={clientAssembly}; layers={(layers ? 3 : 0)}/3; " +
                $"renderer={renderer.RenderingMethod}; driver={renderer.RenderingDriver}; " +
                $"compatibilityFeature={(renderer.CompatibilityExportFeature ? 1 : 0)}; " +
                $"rendererProfile={(renderer.IsValidForProfile ? 1 : 0)}.";
            GD.Print(result);
            if (!passed)
            {
                GD.PushError(result);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"TASK-144 platform architecture acceptance FAIL: {exception}");
        }
    }

}
