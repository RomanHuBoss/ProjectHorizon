using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public sealed record MarketPriceQuote(
    string DefinitionId,
    double BasePrice,
    double SystemEconomyModifier,
    double SupplyDemandModifier,
    double FactionModifier,
    double ReputationModifier,
    double RandomDailyModifier,
    int BuyPrice,
    int SellPrice,
    int Stock);

public sealed record StationServiceTradeResult(
    bool Succeeded,
    string Result,
    string DefinitionId,
    int Quantity,
    int TotalCredits,
    bool IsBuy,
    int PlayerCredits,
    int MerchantCredits);

public sealed record StationServiceQuestView(
    QuestServiceDefinition Definition,
    StationServiceQuestStatus Status,
    string CurrentNodeId,
    int Progress)
{
    public QuestNodeServiceDefinition CurrentNode =>
        Definition.GetNode(CurrentNodeId);

    public int ClampedProgress => Math.Min(
        Progress,
        CurrentNode.RequiredQuantity);
}

public sealed class StationServicesRuntime
{
    public const long EconomyDaySeconds = 86_400;

    private sealed class MutableQuestState
    {
        public MutableQuestState(
            QuestServiceDefinition definition,
            StationServiceQuestStatus status,
            string currentNodeId,
            int progress)
        {
            Definition = definition;
            Status = status;
            CurrentNodeId = currentNodeId;
            Progress = progress;
        }

        public QuestServiceDefinition Definition { get; }

        public StationServiceQuestStatus Status { get; set; }

        public string CurrentNodeId { get; set; }

        public int Progress { get; set; }

        public QuestNodeServiceDefinition CurrentNode =>
            Definition.GetNode(CurrentNodeId);
    }

    private readonly GameContentCatalog _contentCatalog;
    private readonly MarketServiceDefinition _market;
    private readonly NpcServiceDefinition _npc;
    private readonly Dictionary<string, int> _stock =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableQuestState> _quests =
        new(StringComparer.Ordinal);

    public StationServicesRuntime(
        GameContentCatalog contentCatalog,
        StationServicesCatalog servicesCatalog,
        string npcId,
        StationServicesSaveData? saveData = null,
        long? nowUnixSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(contentCatalog);
        ArgumentNullException.ThrowIfNull(servicesCatalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(npcId);
        _contentCatalog = contentCatalog;
        _npc = servicesCatalog.GetNpc(npcId);
        _market = servicesCatalog.GetMarket(_npc.MarketId);

        foreach (string definitionId in contentCatalog.Items.Keys.OrderBy(
            id => id,
            StringComparer.Ordinal))
        {
            _stock.Add(definitionId, _market.InitialStockPerItem);
        }

        foreach (QuestServiceDefinition quest in servicesCatalog.Quests.Values
            .Where(quest => string.Equals(
                quest.GiverNpcId,
                _npc.NpcId,
                StringComparison.Ordinal))
            .OrderBy(quest => quest.QuestId, StringComparer.Ordinal))
        {
            _quests.Add(
                quest.QuestId,
                new MutableQuestState(
                    quest,
                    StationServiceQuestStatus.Offered,
                    quest.StartNodeId,
                    0));
        }

        long now = nowUnixSeconds ??
            DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUnixSeconds));
        }

        PlayerCredits = _market.PlayerStartingCredits;
        MerchantCredits = _market.MerchantStartingCredits;
        LastEconomyUpdateUnixSeconds = now;
        if (saveData is not null)
        {
            Restore(saveData);
            RefreshEconomy(now);
        }
    }

    public string MarketId => _market.MarketId;

    public string NpcId => _npc.NpcId;

    public string FactionId => _npc.FactionId;

    public int PlayerCredits { get; private set; }

    public int MerchantCredits { get; private set; }

    public int Reputation { get; private set; }

    public long DayIndex { get; private set; }

    public long LastEconomyUpdateUnixSeconds { get; private set; }

    public int CompletedQuestCount => _quests.Values.Count(
        quest => quest.Status == StationServiceQuestStatus.Completed);

    public int ActiveQuestCount => _quests.Values.Count(
        quest => quest.Status is StationServiceQuestStatus.Accepted or
            StationServiceQuestStatus.ReadyToClaim);

    public int TradableItemCount => _stock.Count;

    public IReadOnlyList<StationServiceQuestView> Quests => _quests.Values
        .OrderBy(quest => quest.Definition.QuestId, StringComparer.Ordinal)
        .Select(quest => new StationServiceQuestView(
            quest.Definition,
            quest.Status,
            quest.CurrentNodeId,
            quest.Progress))
        .ToArray();

    public IReadOnlyList<MarketPriceQuote> GetBuyOffers()
    {
        return _stock
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => Quote(pair.Key))
            .ToArray();
    }

    public IReadOnlyList<MarketPriceQuote> GetSellOffers(
        StarterRepairSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.AvailableInventory
            .Where(stack => stack.Quantity > 0 &&
                _contentCatalog.Items.ContainsKey(stack.DefinitionId))
            .OrderBy(stack => stack.DefinitionId, StringComparer.Ordinal)
            .Select(stack => Quote(stack.DefinitionId))
            .ToArray();
    }

    public MarketPriceQuote Quote(string definitionId)
    {
        GameItemDefinition item = _contentCatalog.Items.TryGetValue(
            definitionId,
            out GameItemDefinition? value) && value is not null
            ? value
            : throw new KeyNotFoundException(
                $"Unknown tradable item {definitionId}.");
        int stock = _stock.TryGetValue(definitionId, out int quantity)
            ? quantity
            : 0;
        double stockDelta = (_market.TargetStockPerItem - stock) /
            (double)_market.TargetStockPerItem;
        double supplyDemand = Math.Clamp(
            1.0 + stockDelta * 0.25,
            0.75,
            1.35);
        double reputation = Math.Clamp(
            1.0 - Reputation * 0.0025,
            0.85,
            1.10);
        double daily = ComputeDailyModifier(
            _market.DailySeed,
            DayIndex,
            definitionId);
        double finalBase = item.BasePrice *
            _market.SystemEconomyModifier *
            supplyDemand *
            _market.FactionModifier *
            reputation *
            daily;
        int buy = Math.Max(
            1,
            (int)Math.Ceiling(finalBase * _market.BuyMarkup));
        int sell = Math.Max(
            1,
            (int)Math.Floor(finalBase * _market.SellMarkdown));
        if (sell >= buy)
        {
            sell = Math.Max(1, buy - 1);
        }

        return new MarketPriceQuote(
            definitionId,
            item.BasePrice,
            _market.SystemEconomyModifier,
            supplyDemand,
            _market.FactionModifier,
            reputation,
            daily,
            buy,
            sell,
            stock);
    }

    public long RefreshEconomy(long nowUnixSeconds)
    {
        if (nowUnixSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUnixSeconds));
        }

        if (nowUnixSeconds <= LastEconomyUpdateUnixSeconds)
        {
            return 0;
        }

        long elapsed = nowUnixSeconds - LastEconomyUpdateUnixSeconds;
        long days = elapsed / EconomyDaySeconds;
        if (days <= 0)
        {
            return 0;
        }

        long appliedDays = Math.Min(days, 3650);
        DayIndex = checked(DayIndex + appliedDays);
        LastEconomyUpdateUnixSeconds = checked(
            LastEconomyUpdateUnixSeconds +
            appliedDays * EconomyDaySeconds);
        return appliedDays;
    }

    public StationServiceTradeResult TryBuy(
        string definitionId,
        int quantity,
        StarterRepairSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateTransactionArguments(definitionId, quantity);
        MarketPriceQuote quote = Quote(definitionId);
        int total = checked(quote.BuyPrice * quantity);
        if (quote.Stock < quantity)
        {
            return Failed(
                definitionId,
                quantity,
                total,
                isBuy: true,
                GameLocalizationService.Format("ui.station.trade.stock_short", ("quantity", quantity - quote.Stock)));
        }

        if (PlayerCredits < total)
        {
            return Failed(
                definitionId,
                quantity,
                total,
                isBuy: true,
                GameLocalizationService.Format("ui.station.trade.credits_short", ("credits", total - PlayerCredits)));
        }

        int updatedMerchantCredits = checked(MerchantCredits + total);
        _ = checked(session.GetAvailableQuantity(definitionId) + quantity);
        _stock[definitionId] -= quantity;
        PlayerCredits -= total;
        MerchantCredits = updatedMerchantCredits;
        session.GrantInventory(definitionId, quantity);
        RecordObjective(
            StationServiceObjectiveType.TradeItem,
            definitionId,
            quantity);
        return new StationServiceTradeResult(
            true,
            GameLocalizationService.Format("ui.station.trade.bought", ("quantity", quantity), ("item", definitionId), ("credits", total)),
            definitionId,
            quantity,
            total,
            true,
            PlayerCredits,
            MerchantCredits);
    }

    public StationServiceTradeResult TrySell(
        string definitionId,
        int quantity,
        StarterRepairSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateTransactionArguments(definitionId, quantity);
        MarketPriceQuote quote = Quote(definitionId);
        int total = checked(quote.SellPrice * quantity);
        if (MerchantCredits < total)
        {
            return Failed(
                definitionId,
                quantity,
                total,
                isBuy: false,
                GameLocalizationService.Format("ui.station.trade.merchant_short", ("credits", total - MerchantCredits)));
        }

        int updatedStock = checked(_stock[definitionId] + quantity);
        int updatedPlayerCredits = checked(PlayerCredits + total);
        if (!session.TryConsumeInventory(
            definitionId,
            quantity,
            out string inventoryResult))
        {
            return Failed(
                definitionId,
                quantity,
                total,
                isBuy: false,
                inventoryResult);
        }

        _stock[definitionId] = updatedStock;
        MerchantCredits -= total;
        PlayerCredits = updatedPlayerCredits;
        RecordObjective(
            StationServiceObjectiveType.TradeItem,
            definitionId,
            quantity);
        return new StationServiceTradeResult(
            true,
            GameLocalizationService.Format("ui.station.trade.sold", ("quantity", quantity), ("item", definitionId), ("credits", total)),
            definitionId,
            quantity,
            total,
            false,
            PlayerCredits,
            MerchantCredits);
    }

    public bool TryAcceptQuest(string questId, out string result)
    {
        MutableQuestState quest = GetQuest(questId);
        if (quest.Status != StationServiceQuestStatus.Offered)
        {
            result = GameLocalizationService.Format("ui.station.quest.cannot_accept", ("quest", questId), ("status", quest.Status));
            return false;
        }

        quest.Status = StationServiceQuestStatus.Accepted;
        quest.CurrentNodeId = quest.Definition.StartNodeId;
        quest.Progress = 0;
        result = GameLocalizationService.Format("ui.station.quest.accepted", ("quest", questId));
        return true;
    }

    public int RecordObjective(
        StationServiceObjectiveType objectiveType,
        string targetDefinitionId,
        int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDefinitionId);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        int updated = 0;
        foreach (MutableQuestState quest in _quests.Values)
        {
            QuestNodeServiceDefinition node = quest.CurrentNode;
            if (quest.Status != StationServiceQuestStatus.Accepted ||
                node.ObjectiveType != objectiveType ||
                !string.Equals(
                    node.TargetDefinitionId,
                    targetDefinitionId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            quest.Progress = Math.Min(
                node.RequiredQuantity,
                checked(quest.Progress + quantity));
            if (quest.Progress >= node.RequiredQuantity)
            {
                if (node.NextNodeIds.Count == 0)
                {
                    quest.Status = StationServiceQuestStatus.ReadyToClaim;
                }
                else
                {
                    quest.CurrentNodeId = node.NextNodeIds[0];
                    quest.Progress = 0;
                }
            }

            updated++;
        }

        return updated;
    }

    public bool TryClaimQuest(string questId, out string result)
    {
        MutableQuestState quest = GetQuest(questId);
        if (quest.Status != StationServiceQuestStatus.ReadyToClaim)
        {
            result = GameLocalizationService.Format("ui.station.quest.not_ready", ("quest", questId));
            return false;
        }

        quest.Status = StationServiceQuestStatus.Completed;
        PlayerCredits = checked(
            PlayerCredits + quest.Definition.RewardCredits);
        Reputation = Math.Clamp(
            checked(Reputation + quest.Definition.ReputationReward),
            -100,
            100);
        result = GameLocalizationService.Format(
            "ui.station.quest.claimed",
            ("quest", questId),
            ("credits", quest.Definition.RewardCredits),
            ("reputation", Reputation));
        return true;
    }

    public int ApplyReputationDelta(int delta)
    {
        Reputation = Math.Clamp(checked(Reputation + delta), -100, 100);
        return Reputation;
    }

    public string GrantExternalQuestReward(
        string factionId,
        int credits,
        int reputationReward)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factionId);
        if (credits < 0 || reputationReward < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(credits),
                "Quest rewards must not be negative.");
        }
        PlayerCredits = checked(PlayerCredits + credits);
        bool localFaction = string.Equals(
            factionId,
            _npc.FactionId,
            StringComparison.Ordinal);
        if (localFaction)
        {
            Reputation = Math.Clamp(
                checked(Reputation + reputationReward),
                -100,
                100);
        }
        return GameLocalizationService.Format(
            "ui.station.external_reward",
            ("credits", credits),
            ("faction", factionId),
            ("reputation", localFaction ? Reputation : 0));
    }

    public StationServicesSaveData CreateSaveData()
    {
        return new StationServicesSaveData(
            _market.MarketId,
            _npc.NpcId,
            PlayerCredits,
            MerchantCredits,
            Reputation,
            DayIndex,
            LastEconomyUpdateUnixSeconds,
            _stock
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new StationServiceStockSaveData(
                    pair.Key,
                    pair.Value))
                .ToArray(),
            _quests.Values
                .OrderBy(quest => quest.Definition.QuestId, StringComparer.Ordinal)
                .Select(quest => new StationServiceQuestSaveData(
                    quest.Definition.QuestId,
                    quest.Status,
                    quest.CurrentNodeId,
                    quest.Progress))
                .ToArray());
    }

    public string BuildSummary()
    {
        return $"credits={PlayerCredits} • rep={Reputation} • " +
            $"quests={CompletedQuestCount}/{_quests.Count} • " +
            $"market={_market.EconomyType} • day={DayIndex} • " +
            $"tradable={TradableItemCount}";
    }

    private void Restore(StationServicesSaveData saveData)
    {
        if (!string.Equals(
                saveData.MarketId,
                _market.MarketId,
                StringComparison.Ordinal) ||
            !string.Equals(
                saveData.NpcId,
                _npc.NpcId,
                StringComparison.Ordinal) ||
            saveData.PlayerCredits < 0 ||
            saveData.MerchantCredits < 0 ||
            saveData.Reputation is < -100 or > 100 ||
            saveData.DayIndex < 0 ||
            saveData.LastEconomyUpdateUnixSeconds < 0 ||
            saveData.Stock is null ||
            saveData.Quests is null)
        {
            throw new InvalidOperationException(
                "Station services save contains invalid market identity or values.");
        }

        HashSet<string> stockIds = new(StringComparer.Ordinal);
        foreach (StationServiceStockSaveData stock in saveData.Stock)
        {
            if (!_stock.ContainsKey(stock.DefinitionId) ||
                stock.Quantity < 0 ||
                !stockIds.Add(stock.DefinitionId))
            {
                throw new InvalidOperationException(
                    "Station services save contains invalid or duplicate stock.");
            }

            _stock[stock.DefinitionId] = stock.Quantity;
        }

        HashSet<string> questIds = new(StringComparer.Ordinal);
        foreach (StationServiceQuestSaveData savedQuest in saveData.Quests)
        {
            if (!_quests.TryGetValue(
                    savedQuest.QuestId,
                    out MutableQuestState? quest) ||
                quest is null ||
                !questIds.Add(savedQuest.QuestId) ||
                !Enum.IsDefined(savedQuest.Status) ||
                !quest.Definition.Nodes.Any(node => string.Equals(
                    node.NodeId,
                    savedQuest.CurrentNodeId,
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Station services save contains invalid quest identity or state.");
            }

            QuestNodeServiceDefinition node = quest.Definition.GetNode(
                savedQuest.CurrentNodeId);
            bool offeredInconsistent =
                savedQuest.Status == StationServiceQuestStatus.Offered &&
                (savedQuest.Progress != 0 ||
                 !string.Equals(
                     savedQuest.CurrentNodeId,
                     quest.Definition.StartNodeId,
                     StringComparison.Ordinal));
            bool acceptedInconsistent =
                savedQuest.Status == StationServiceQuestStatus.Accepted &&
                savedQuest.Progress >= node.RequiredQuantity;
            bool terminalInconsistent =
                (savedQuest.Status is StationServiceQuestStatus.ReadyToClaim or
                    StationServiceQuestStatus.Completed) &&
                (node.NextNodeIds.Count != 0 ||
                 savedQuest.Progress != node.RequiredQuantity);
            if (savedQuest.Progress < 0 ||
                savedQuest.Progress > node.RequiredQuantity ||
                offeredInconsistent ||
                acceptedInconsistent ||
                terminalInconsistent)
            {
                throw new InvalidOperationException(
                    "Station services save contains inconsistent quest progress.");
            }

            quest.Status = savedQuest.Status;
            quest.CurrentNodeId = savedQuest.CurrentNodeId;
            quest.Progress = savedQuest.Progress;
        }

        if (stockIds.Count != _stock.Count || questIds.Count != _quests.Count)
        {
            throw new InvalidOperationException(
                "Station services save is incomplete for the active catalog.");
        }

        PlayerCredits = saveData.PlayerCredits;
        MerchantCredits = saveData.MerchantCredits;
        Reputation = saveData.Reputation;
        DayIndex = saveData.DayIndex;
        LastEconomyUpdateUnixSeconds = saveData.LastEconomyUpdateUnixSeconds;
    }

    private MutableQuestState GetQuest(string questId)
    {
        return _quests.TryGetValue(questId, out MutableQuestState? quest) &&
            quest is not null
            ? quest
            : throw new KeyNotFoundException($"Unknown quest {questId}.");
    }

    private void ValidateTransactionArguments(
        string definitionId,
        int quantity)
    {
        if (!_contentCatalog.Items.ContainsKey(definitionId))
        {
            throw new ArgumentException(
                $"Unknown tradable item {definitionId}.",
                nameof(definitionId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }
    }

    private StationServiceTradeResult Failed(
        string definitionId,
        int quantity,
        int total,
        bool isBuy,
        string reason)
    {
        return new StationServiceTradeResult(
            false,
            reason,
            definitionId,
            quantity,
            total,
            isBuy,
            PlayerCredits,
            MerchantCredits);
    }

    private static double ComputeDailyModifier(
        int seed,
        long dayIndex,
        string definitionId)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            string value = seed.ToString(CultureInfo.InvariantCulture) + ":" +
                dayIndex.ToString(CultureInfo.InvariantCulture) + ":" +
                definitionId;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }

            int basisPoints = (int)(hash % 1001UL) - 500;
            return 1.0 + basisPoints / 10000.0;
        }
    }
}
