using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record StationServicesAcceptanceReport(
    bool Passed,
    string Result,
    int EconomyTypes,
    int Factions,
    int Npcs,
    int DialogueOptions,
    int Quests,
    int QuestNodes,
    int TradableItems,
    bool PriceFormula,
    bool DeterministicDaily,
    bool OfflineEconomy,
    bool SupplyDemandRepriced,
    bool BuySell,
    bool AtomicRejected,
    bool CreditConservation,
    bool QuestGraph,
    bool QuestFeasibility,
    bool QuestFlow,
    bool Reputation,
    bool ColdRestore,
    bool LegacyFallback,
    bool ExactRoundTrip,
    bool LogWritten,
    SaveDatabaseDiagnostics Diagnostics,
    double ElapsedMilliseconds);

public static class StationServicesAcceptanceRunner
{
    public const string NpcId = "npc.trader.ilia_voss";
    private const long FixedNowUnixSeconds = 1_800_000_000;

    public static async Task<StationServicesAcceptanceReport> RunAsync(
        string databasePath,
        string slotId,
        GameContentCatalog contentCatalog,
        StationServicesCatalog servicesCatalog,
        CraftingRecipeDefinition repairRecipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(contentCatalog);
        ArgumentNullException.ThrowIfNull(servicesCatalog);
        ArgumentNullException.ThrowIfNull(repairRecipe);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DeleteTestArtifacts(databasePath);
            SaveDatabase.RegisterKnownInventoryDefinitions(
                contentCatalog.Items.Keys);
            StarterRepairSession session = new(repairRecipe);
            session.GrantInventory("resource.ice_water", 2);
            StationServicesRuntime runtime = new(
                contentCatalog,
                servicesCatalog,
                NpcId,
                saveData: null,
                nowUnixSeconds: FixedNowUnixSeconds);
            MarketServiceDefinition market = servicesCatalog.GetMarket(
                servicesCatalog.GetNpc(NpcId).MarketId);

            MarketPriceQuote[] quotes = contentCatalog.Items.Keys
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(runtime.Quote)
                .ToArray();
            bool priceFormula = quotes.Length == contentCatalog.Items.Count &&
                quotes.All(quote =>
                {
                    double finalBase = quote.BasePrice *
                        quote.SystemEconomyModifier *
                        quote.SupplyDemandModifier *
                        quote.FactionModifier *
                        quote.ReputationModifier *
                        quote.RandomDailyModifier;
                    int expectedBuy = Math.Max(
                        1,
                        (int)Math.Ceiling(finalBase * market.BuyMarkup));
                    int expectedSell = Math.Max(
                        1,
                        (int)Math.Floor(finalBase * market.SellMarkdown));
                    if (expectedSell >= expectedBuy)
                    {
                        expectedSell = Math.Max(1, expectedBuy - 1);
                    }

                    return quote.BasePrice > 0.0 &&
                        quote.SystemEconomyModifier ==
                            market.SystemEconomyModifier &&
                        quote.SupplyDemandModifier > 0.0 &&
                        quote.FactionModifier == market.FactionModifier &&
                        quote.ReputationModifier > 0.0 &&
                        quote.RandomDailyModifier is >= 0.95 and <= 1.05 &&
                        quote.BuyPrice == expectedBuy &&
                        quote.SellPrice == expectedSell &&
                        quote.BuyPrice > quote.SellPrice &&
                        quote.SellPrice > 0;
                });
            MarketPriceQuote deterministicA = runtime.Quote(
                "resource.ice_water");
            MarketPriceQuote deterministicB = runtime.Quote(
                "resource.ice_water");
            bool sameDayDeterministic = deterministicA == deterministicB;
            long appliedDays = runtime.RefreshEconomy(
                FixedNowUnixSeconds + StationServicesRuntime.EconomyDaySeconds);
            MarketPriceQuote nextDay = runtime.Quote("resource.ice_water");
            bool deterministicDaily = sameDayDeterministic &&
                appliedDays == 1 &&
                runtime.DayIndex == 1 &&
                nextDay.RandomDailyModifier !=
                    deterministicA.RandomDailyModifier;
            bool offlineEconomy = runtime.LastEconomyUpdateUnixSeconds ==
                FixedNowUnixSeconds + StationServicesRuntime.EconomyDaySeconds;

            bool questGraph = servicesCatalog.Quests.Values.All(quest =>
                quest.Nodes.Count > 0 &&
                quest.Nodes.Select(node => node.NodeId)
                    .Distinct(StringComparer.Ordinal).Count() == quest.Nodes.Count &&
                IsAcyclicAndReachable(quest));
            bool questFeasibility = servicesCatalog.Quests.Values
                .SelectMany(quest => quest.Nodes)
                .All(node => IsFeasible(node, contentCatalog));

            foreach (StationServiceQuestView quest in runtime.Quests)
            {
                if (!runtime.TryAcceptQuest(
                    quest.Definition.QuestId,
                    out string acceptResult))
                {
                    throw new InvalidOperationException(acceptResult);
                }
            }

            int creditsBeforeRejected = runtime.PlayerCredits;
            int merchantBeforeRejected = runtime.MerchantCredits;
            int stockBeforeRejected = runtime.Quote(
                "resource.ice_water").Stock;
            StationServiceTradeResult stockRejected = runtime.TryBuy(
                "resource.ice_water",
                stockBeforeRejected + 1,
                session);
            string expensiveId = quotes
                .OrderByDescending(quote => quote.BuyPrice)
                .First().DefinitionId;
            StationServiceTradeResult fundsRejected = runtime.TryBuy(
                expensiveId,
                runtime.Quote(expensiveId).Stock,
                session);
            bool atomicRejected =
                !stockRejected.Succeeded &&
                !fundsRejected.Succeeded &&
                runtime.PlayerCredits == creditsBeforeRejected &&
                runtime.MerchantCredits == merchantBeforeRejected &&
                runtime.Quote("resource.ice_water").Stock == stockBeforeRejected;

            int totalCreditsBefore = checked(
                runtime.PlayerCredits + runtime.MerchantCredits);
            MarketPriceQuote ferricBefore = runtime.Quote(
                "resource.ferric_ore");
            StationServiceTradeResult bought = runtime.TryBuy(
                "resource.ferric_ore",
                1,
                session);
            MarketPriceQuote ferricAfter = runtime.Quote(
                "resource.ferric_ore");
            StationServiceTradeResult sold = runtime.TrySell(
                "resource.ice_water",
                1,
                session);
            bool buySell = bought.Succeeded && sold.Succeeded &&
                session.GetAvailableQuantity("resource.ferric_ore") == 1 &&
                session.GetAvailableQuantity("resource.ice_water") == 1;
            bool supplyDemandRepriced =
                ferricAfter.Stock == ferricBefore.Stock - 1 &&
                ferricAfter.SupplyDemandModifier >
                    ferricBefore.SupplyDemandModifier;
            bool creditConservation = checked(
                runtime.PlayerCredits + runtime.MerchantCredits) ==
                totalCreditsBefore;

            runtime.RecordObjective(
                StationServiceObjectiveType.CollectResource,
                "resource.ferric_ore",
                2);
            runtime.RecordObjective(
                StationServiceObjectiveType.CraftItem,
                "material.refined_ferrite",
                1);
            bool readyToClaim = runtime.Quests.All(
                quest => quest.Status ==
                    StationServiceQuestStatus.ReadyToClaim);
            foreach (StationServiceQuestView quest in runtime.Quests)
            {
                if (!runtime.TryClaimQuest(
                    quest.Definition.QuestId,
                    out string claimResult))
                {
                    throw new InvalidOperationException(claimResult);
                }
            }

            bool questFlow = readyToClaim &&
                runtime.CompletedQuestCount == servicesCatalog.Quests.Count;
            bool reputation = runtime.Reputation == servicesCatalog.Quests.Values
                .Sum(quest => quest.ReputationReward);

            SaveGameSnapshot expected = StarterRepairSnapshotFactory.Create(
                slotId,
                revision: 1,
                session,
                playerPositionX: 1.0,
                playerPositionY: 2.0,
                playerPositionZ: 3.0,
                stationServices: runtime.CreateSaveData());
            using SaveDatabase database = new(databasePath);
            using SaveAutosaveCoordinator autosave = new(
                database,
                new DomainEventBus(),
                TimeSpan.FromMilliseconds(60.0));
            await database.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            await database.ResetSlotAsync(slotId, cancellationToken)
                .ConfigureAwait(false);
            await autosave.FlushAsync(
                AutosaveTrigger.QuestCompleted,
                expected,
                cancellationToken).ConfigureAwait(false);
            SaveGameSnapshot? loaded = await database.LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            bool exactRoundTrip = SaveDatabase.SnapshotsEqual(
                expected,
                loaded,
                out string mismatch);
            StationServicesRuntime restored = new(
                contentCatalog,
                servicesCatalog,
                NpcId,
                loaded?.StationServices,
                FixedNowUnixSeconds +
                    StationServicesRuntime.EconomyDaySeconds);
            StationServicesSaveData expectedServices = runtime.CreateSaveData();
            StationServicesSaveData restoredServices = restored.CreateSaveData();
            bool coldRestore = loaded?.StationServices is not null &&
                expectedServices.PlayerCredits == restoredServices.PlayerCredits &&
                expectedServices.MerchantCredits == restoredServices.MerchantCredits &&
                expectedServices.Reputation == restoredServices.Reputation &&
                expectedServices.DayIndex == restoredServices.DayIndex &&
                expectedServices.LastEconomyUpdateUnixSeconds ==
                    restoredServices.LastEconomyUpdateUnixSeconds &&
                expectedServices.Stock.SequenceEqual(restoredServices.Stock) &&
                expectedServices.Quests.SequenceEqual(restoredServices.Quests);
            StationServicesRuntime legacy = new(
                contentCatalog,
                servicesCatalog,
                NpcId,
                saveData: null,
                nowUnixSeconds: FixedNowUnixSeconds);
            bool legacyFallback =
                legacy.PlayerCredits == market.PlayerStartingCredits &&
                legacy.MerchantCredits == market.MerchantStartingCredits &&
                legacy.DayIndex == 0 &&
                legacy.Quests.All(quest =>
                    quest.Status == StationServiceQuestStatus.Offered &&
                    string.Equals(
                        quest.CurrentNodeId,
                        quest.Definition.StartNodeId,
                        StringComparison.Ordinal));

            SaveDatabaseDiagnostics diagnostics =
                await database.ReadDiagnosticsAsync(
                    slotId,
                    cancellationToken).ConfigureAwait(false);
            string logText = File.Exists(autosave.AutosaveLogPath)
                ? File.ReadAllText(autosave.AutosaveLogPath)
                : string.Empty;
            bool logWritten = logText.Contains(
                "AUTOSAVE_COMPLETED",
                StringComparison.Ordinal) &&
                logText.Contains(
                    nameof(AutosaveTrigger.QuestCompleted),
                    StringComparison.Ordinal);
            bool integrityOk = string.Equals(
                diagnostics.IntegrityResult,
                "ok",
                StringComparison.OrdinalIgnoreCase);
            int dialogueOptions = servicesCatalog.Dialogues.Values.Sum(
                dialogue => dialogue.Options.Count);
            int questNodes = servicesCatalog.Quests.Values.Sum(
                quest => quest.Nodes.Count);
            bool passed =
                servicesCatalog.EconomyTypes.Count == 6 &&
                servicesCatalog.Factions.Count == 3 &&
                servicesCatalog.Npcs.Count == 1 &&
                dialogueOptions == 3 &&
                servicesCatalog.Quests.Count == 3 &&
                questNodes == 3 &&
                runtime.TradableItemCount == contentCatalog.Items.Count &&
                priceFormula && deterministicDaily && offlineEconomy &&
                supplyDemandRepriced && buySell && atomicRejected &&
                creditConservation && questGraph && questFeasibility &&
                questFlow && reputation && coldRestore && legacyFallback &&
                exactRoundTrip && logWritten &&
                diagnostics.MaximumConcurrentWriters == 1 && integrityOk;
            List<string> failures = new();
            if (servicesCatalog.EconomyTypes.Count != 6) failures.Add("economies=0");
            if (servicesCatalog.Factions.Count != 3) failures.Add("factions=0");
            if (servicesCatalog.Npcs.Count != 1) failures.Add("npcs=0");
            if (dialogueOptions != 3) failures.Add("dialogue=0");
            if (servicesCatalog.Quests.Count != 3 || questNodes != 3)
                failures.Add("questBaseline=0");
            if (runtime.TradableItemCount != contentCatalog.Items.Count)
                failures.Add("marketCoverage=0");
            if (!priceFormula) failures.Add("priceFormula=0");
            if (!deterministicDaily) failures.Add("daily=0");
            if (!offlineEconomy) failures.Add("offlineEconomy=0");
            if (!supplyDemandRepriced) failures.Add("supplyDemand=0");
            if (!buySell) failures.Add("buySell=0");
            if (!atomicRejected) failures.Add("atomicRejected=0");
            if (!creditConservation) failures.Add("credits=0");
            if (!questGraph) failures.Add("questGraph=0");
            if (!questFeasibility) failures.Add("feasibility=0");
            if (!questFlow) failures.Add("quests=0");
            if (!reputation) failures.Add("reputation=0");
            if (!coldRestore) failures.Add("restore=0");
            if (!legacyFallback) failures.Add("fallback=0");
            if (!exactRoundTrip) failures.Add($"roundTrip={mismatch}");
            if (!logWritten) failures.Add("logWritten=0");
            if (diagnostics.MaximumConcurrentWriters != 1)
                failures.Add($"maxWriters={diagnostics.MaximumConcurrentWriters}");
            if (!integrityOk)
                failures.Add($"integrity={diagnostics.IntegrityResult}");

            stopwatch.Stop();
            return new StationServicesAcceptanceReport(
                passed,
                passed
                    ? "six economy types, three factions, one template-dialogue " +
                      "trader, catalog-wide deterministic market pricing, atomic " +
                      "trade and three feasible persistent quest graphs completed " +
                      "exactly"
                    : "station services criteria failed: " +
                      string.Join(", ", failures),
                servicesCatalog.EconomyTypes.Count,
                servicesCatalog.Factions.Count,
                servicesCatalog.Npcs.Count,
                dialogueOptions,
                servicesCatalog.Quests.Count,
                questNodes,
                runtime.TradableItemCount,
                priceFormula,
                deterministicDaily,
                offlineEconomy,
                supplyDemandRepriced,
                buySell,
                atomicRejected,
                creditConservation,
                questGraph,
                questFeasibility,
                questFlow,
                reputation,
                coldRestore,
                legacyFallback,
                exactRoundTrip,
                logWritten,
                diagnostics,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new StationServicesAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                servicesCatalog.EconomyTypes.Count,
                servicesCatalog.Factions.Count,
                servicesCatalog.Npcs.Count,
                servicesCatalog.Dialogues.Values.Sum(
                    dialogue => dialogue.Options.Count),
                servicesCatalog.Quests.Count,
                servicesCatalog.Quests.Values.Sum(quest => quest.Nodes.Count),
                contentCatalog.Items.Count,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                new SaveDatabaseDiagnostics(
                    0, "unknown", false, 0, 0, "not-run", 0, 0, 0, 0, 0, 0),
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static bool IsFeasible(
        QuestNodeServiceDefinition node,
        GameContentCatalog contentCatalog)
    {
        return node.ObjectiveType switch
        {
            StationServiceObjectiveType.CollectResource =>
                contentCatalog.Resources.Values.Any(resource => string.Equals(
                    resource.ItemDefinitionId,
                    node.TargetDefinitionId,
                    StringComparison.Ordinal)),
            StationServiceObjectiveType.CraftItem =>
                contentCatalog.Recipes.Values.Any(recipe =>
                    recipe.RuntimeEnabled && recipe.Outputs.Any(output =>
                        string.Equals(
                            output.DefinitionId,
                            node.TargetDefinitionId,
                            StringComparison.Ordinal))),
            StationServiceObjectiveType.TradeItem =>
                contentCatalog.Items.ContainsKey(node.TargetDefinitionId),
            _ => false
        };
    }

    private static bool IsAcyclicAndReachable(QuestServiceDefinition quest)
    {
        HashSet<string> visiting = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);
        bool Visit(string nodeId)
        {
            if (visited.Contains(nodeId))
            {
                return true;
            }

            if (!visiting.Add(nodeId))
            {
                return false;
            }

            QuestNodeServiceDefinition node = quest.GetNode(nodeId);
            foreach (string nextNodeId in node.NextNodeIds)
            {
                if (!Visit(nextNodeId))
                {
                    return false;
                }
            }

            visiting.Remove(nodeId);
            visited.Add(nodeId);
            return true;
        }

        return Visit(quest.StartNodeId) && visited.Count == quest.Nodes.Count;
    }

    private static void DeleteTestArtifacts(string databasePath)
    {
        foreach (string path in new[]
        {
            databasePath,
            databasePath + "-wal",
            databasePath + "-shm",
            databasePath + ".backup",
            databasePath + ".backup-wal",
            databasePath + ".backup-shm"
        })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            string logPath = Path.Combine(
                directory,
                "logs",
                $"{Path.GetFileNameWithoutExtension(databasePath)}.autosave.log");
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }
}
