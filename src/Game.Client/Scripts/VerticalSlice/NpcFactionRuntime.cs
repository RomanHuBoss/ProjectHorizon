using System;
using System.Collections.Generic;
using System.Linq;

public sealed record NpcFactionAgentView(
    NpcFactionAgentDefinition Definition,
    double Health,
    bool Interacted,
    bool Defeated,
    int DefeatCount);

public sealed record NpcDialogueOutcome(
    bool Applied,
    string NpcId,
    string FactionId,
    string Action,
    int ReputationBefore,
    int ReputationAfter,
    int AppliedReputationDelta,
    bool FirstMeaningfulInteraction,
    string ConsequenceEn,
    string ConsequenceRu);

public sealed record NpcFactionCombatOutcome(
    string NpcId,
    double HealthBefore,
    double HealthAfter,
    bool DefeatedNow,
    bool Respawned,
    int DefeatCount,
    string FactionId,
    int ReputationBefore,
    int ReputationAfter,
    int AppliedReputationDelta);

public sealed class NpcFactionRuntime
{
    private sealed class MutableAgentState
    {
        public required NpcFactionAgentDefinition Definition { get; init; }
        public double Health { get; set; }
        public bool Interacted { get; set; }
        public bool Defeated { get; set; }
        public int DefeatCount { get; set; }
    }

    private readonly NpcFactionCatalog _catalog;
    private readonly Dictionary<string, int> _reputation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableAgentState> _states = new(StringComparer.Ordinal);

    public NpcFactionRuntime(
        NpcFactionCatalog catalog,
        NpcFactionSaveData? saveData = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        foreach (string factionId in catalog.Factions.Keys)
        {
            _reputation.Add(factionId, 0);
        }
        foreach (NpcFactionAgentDefinition agent in catalog.Agents.Values)
        {
            _states.Add(agent.NpcId, new MutableAgentState
            {
                Definition = agent,
                Health = agent.Health,
                Interacted = false,
                Defeated = false,
                DefeatCount = 0
            });
        }

        if (saveData is not null)
        {
            Restore(saveData);
        }
    }

    public int FactionCount => _reputation.Count;

    public int AgentCount => _states.Count;

    public int AliveCount => _states.Values.Count(state => !state.Defeated);

    public int DefeatedCount => _states.Values.Count(state => state.Defeated);

    public int TotalOpponentDefeats => _states.Values.Sum(state => state.DefeatCount);

    public IReadOnlyList<string> DefeatTargetIds => _catalog.DefeatTargetIds;

    public IReadOnlyList<string> ProtectTargetIds => _catalog.ProtectTargetIds;

    public IReadOnlyDictionary<string, int> FactionReputation => _reputation;

    public int GetFactionReputation(string factionId)
    {
        if (string.IsNullOrEmpty(factionId))
        {
            return 0;
        }
        return _reputation.TryGetValue(factionId, out int value)
            ? value
            : throw new KeyNotFoundException($"Unknown faction {factionId}.");
    }

    public int ApplyReputationDelta(string factionId, int delta)
    {
        if (string.IsNullOrWhiteSpace(factionId))
        {
            return 0;
        }
        if (!_reputation.TryGetValue(factionId, out int current))
        {
            throw new KeyNotFoundException($"Unknown faction {factionId}.");
        }
        int updated = Math.Clamp(checked(current + delta), -100, 100);
        _reputation[factionId] = updated;
        return updated;
    }

    public NpcFactionAgentView GetAgent(string npcId)
    {
        MutableAgentState state = GetState(npcId);
        return new NpcFactionAgentView(
            state.Definition,
            state.Health,
            state.Interacted,
            state.Defeated,
            state.DefeatCount);
    }

    public IReadOnlyList<NpcDialogueOptionDefinition> GetAvailableDialogueOptions(
        string npcId)
    {
        MutableAgentState state = GetState(npcId);
        NpcDialogueDefinition dialogue = _catalog.GetDialogue(
            state.Definition.DialogueId);
        int reputation = GetFactionReputation(state.Definition.FactionId);
        return dialogue.Options
            .Where(option => reputation >= option.MinimumReputation)
            .ToArray();
    }

    public NpcDialogueOutcome ChooseDialogueOption(
        string npcId,
        string optionId)
    {
        MutableAgentState state = GetState(npcId);
        NpcDialogueDefinition dialogue = _catalog.GetDialogue(
            state.Definition.DialogueId);
        NpcDialogueOptionDefinition option = dialogue.Options.FirstOrDefault(
            value => string.Equals(value.OptionId, optionId, StringComparison.Ordinal)) ??
            throw new KeyNotFoundException(
                $"Dialogue {dialogue.DialogueId} has no option {optionId}.");
        int before = GetFactionReputation(state.Definition.FactionId);
        if (before < option.MinimumReputation)
        {
            return new NpcDialogueOutcome(
                false,
                npcId,
                state.Definition.FactionId,
                option.Action,
                before,
                before,
                0,
                false,
                $"Requires reputation {option.MinimumReputation}.",
                $"Требуется репутация {option.MinimumReputation}.");
        }

        bool meaningful = !string.Equals(option.Action, "Close", StringComparison.Ordinal);
        bool firstMeaningful = meaningful && !state.Interacted;
        int appliedDelta = firstMeaningful ? option.ReputationDelta : 0;
        int after = string.IsNullOrEmpty(state.Definition.FactionId)
            ? 0
            : ApplyReputationDelta(state.Definition.FactionId, appliedDelta);
        if (meaningful)
        {
            state.Interacted = true;
        }
        return new NpcDialogueOutcome(
            true,
            npcId,
            state.Definition.FactionId,
            option.Action,
            before,
            after,
            appliedDelta,
            firstMeaningful,
            option.ConsequenceEn,
            option.ConsequenceRu);
    }

    public NpcFactionCombatOutcome ApplyDamage(
        string npcId,
        double damage)
    {
        if (!double.IsFinite(damage) || damage <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(damage),
                "NPC damage must be finite and positive.");
        }
        MutableAgentState state = GetState(npcId);
        int repBefore = GetFactionReputation(state.Definition.FactionId);
        if (state.Defeated)
        {
            return new NpcFactionCombatOutcome(
                npcId,
                state.Health,
                state.Health,
                false,
                false,
                state.DefeatCount,
                state.Definition.FactionId,
                repBefore,
                repBefore,
                0);
        }

        double before = state.Health;
        state.Health = Math.Max(0.0, state.Health - damage);
        bool defeatedNow = state.Health <= 0.0;
        int delta = 0;
        bool respawned = false;
        if (!state.Definition.Hostile && !string.IsNullOrEmpty(state.Definition.FactionId))
        {
            delta -= 2;
        }
        if (defeatedNow)
        {
            state.DefeatCount = checked(state.DefeatCount + 1);
            if (!state.Definition.Hostile && !string.IsNullOrEmpty(state.Definition.FactionId))
            {
                delta -= 10;
            }
            if (state.Definition.Respawnable)
            {
                state.Health = state.Definition.Health;
                state.Defeated = false;
                respawned = true;
            }
            else
            {
                state.Defeated = true;
            }
        }
        int repAfter = string.IsNullOrEmpty(state.Definition.FactionId)
            ? 0
            : ApplyReputationDelta(state.Definition.FactionId, delta);
        return new NpcFactionCombatOutcome(
            npcId,
            before,
            state.Health,
            defeatedNow,
            respawned,
            state.DefeatCount,
            state.Definition.FactionId,
            repBefore,
            repAfter,
            delta);
    }

    public NpcFactionSaveData CreateSaveData()
    {
        NpcFactionReputationSaveData[] reputation = _reputation
            .Where(pair => pair.Value != 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new NpcFactionReputationSaveData(pair.Key, pair.Value))
            .ToArray();
        NpcFactionAgentStateSaveData[] agents = _states.Values
            .Where(state =>
                Math.Abs(state.Health - state.Definition.Health) > 0.0001 ||
                state.Interacted ||
                state.Defeated ||
                state.DefeatCount > 0)
            .OrderBy(state => state.Definition.NpcId, StringComparer.Ordinal)
            .Select(state => new NpcFactionAgentStateSaveData(
                state.Definition.NpcId,
                state.Health,
                state.Interacted,
                state.Defeated,
                state.DefeatCount))
            .ToArray();
        return new NpcFactionSaveData(
            _catalog.WorldSeed,
            _catalog.RegionKey,
            reputation,
            agents);
    }

    private void Restore(NpcFactionSaveData saveData)
    {
        if (saveData.WorldSeed != _catalog.WorldSeed ||
            !string.Equals(saveData.RegionKey, _catalog.RegionKey, StringComparison.Ordinal) ||
            saveData.Reputations is null ||
            saveData.Agents is null)
        {
            throw new InvalidOperationException(
                "NPC/faction save seed or region does not match the content catalogue.");
        }
        HashSet<string> reputationIds = new(StringComparer.Ordinal);
        foreach (NpcFactionReputationSaveData entry in saveData.Reputations)
        {
            if (!_reputation.ContainsKey(entry.FactionId) ||
                !reputationIds.Add(entry.FactionId) ||
                entry.Reputation is < -100 or > 100 ||
                entry.Reputation == 0)
            {
                throw new InvalidOperationException(
                    $"Invalid NPC faction reputation delta {entry.FactionId}.");
            }
            _reputation[entry.FactionId] = entry.Reputation;
        }

        HashSet<string> stateIds = new(StringComparer.Ordinal);
        foreach (NpcFactionAgentStateSaveData entry in saveData.Agents)
        {
            if (!_states.TryGetValue(entry.NpcId, out MutableAgentState? state) ||
                !stateIds.Add(entry.NpcId) ||
                !double.IsFinite(entry.Health) ||
                entry.Health < 0.0 || entry.Health > state.Definition.Health + 0.0001 ||
                entry.DefeatCount < 0 ||
                (entry.Defeated && (entry.Health > 0.0001 || state.Definition.Respawnable)) ||
                (!entry.Defeated && entry.Health <= 0.0))
            {
                throw new InvalidOperationException(
                    $"Invalid NPC agent delta {entry.NpcId}.");
            }
            state.Health = entry.Health;
            state.Interacted = entry.Interacted;
            state.Defeated = entry.Defeated;
            state.DefeatCount = entry.DefeatCount;
        }
    }

    private MutableAgentState GetState(string npcId) =>
        _states.TryGetValue(npcId, out MutableAgentState? value)
            ? value
            : throw new KeyNotFoundException($"Unknown NPC {npcId}.");
}
