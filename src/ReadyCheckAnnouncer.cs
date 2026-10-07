using System;
using System.Collections.Generic;

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;

using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace ReadyCheck;

/// <summary>
/// Detects a ready check being started and posts the duty to party chat just before
/// the game sends the check itself.
///
/// <para>Dalamud has no ready-check event. It has <c>IDutyState</c>, but that fires
/// on duty start, and <c>AgentReadyCheck.ReadyCheckEntries</c> only fills in once the
/// check is already running — both are too late to satisfy "announce before the check",
/// and the second would also fire for checks other people started.</para>
///
/// <para>This hooks <c>AgentReadyCheck.InitiateReadyCheck</c>, which the party list's
/// Ready Check button, the Duty Finder's, and <c>/readycheck</c> all reach. The client
/// also calls it after receiving another player's check, so the detour distinguishes
/// that path by entries already awaiting a response and stays silent.</para>
///
/// <para>The detour sends first and calls the original second. Both are outbound
/// packets on the same frame, so ordering them this way is what puts the message in
/// the party's chat log above the ready-check prompt rather than below it. The
/// original is called from a <c>finally</c>: whatever this plugin gets wrong, the
/// game's own ready check still happens.</para>
/// </summary>
internal sealed unsafe class ReadyCheckAnnouncer : IDisposable
{
    /// <summary>
    /// Ignore a second initiation inside this window. The game only calls
    /// InitiateReadyCheck once per check, so this is insurance against a future
    /// client calling it twice (or another plugin driving it) rather than a known
    /// duplicate — cheap, and the alternative is double-posting to the party.
    /// </summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    private readonly Configuration                                    _config;
    private readonly IPluginLog                                       _log;
    private readonly IPartyList                                         _party;
    private readonly IPlayerState                                       _player;
    private readonly ICondition                                         _condition;
    private readonly Hook<AgentReadyCheck.Delegates.InitiateReadyCheck> _hook;

    private DateTime _lastAnnounced = DateTime.MinValue;

    public ReadyCheckAnnouncer(
        Configuration config,
        IGameInteropProvider interop,
        IPluginLog log,
        IPartyList party,
        IPlayerState player,
        ICondition condition)
    {
        _config    = config;
        _log       = log;
        _party     = party;
        _player    = player;
        _condition = condition;

        _hook = interop.HookFromAddress<AgentReadyCheck.Delegates.InitiateReadyCheck>(
            (nint)AgentReadyCheck.MemberFunctionPointers.InitiateReadyCheck,
            OnInitiateReadyCheck);
        _hook.Enable();
    }

    private void OnInitiateReadyCheck(AgentReadyCheck* agent)
    {
        try
        {
            if (HasActiveEntries(agent))
                _config.Debug(_log, "Ready check announcement skipped: check was received from another player.");
            else
                Announce();
        }
        catch (Exception ex)
        {
            // A throw here would propagate into game code. Swallow it, log it, and
            // let the ready check proceed untouched.
            _log.Error(ex, "Ready check announcement failed.");
        }
        finally
        {
            _hook.Original(agent);
        }
    }

    /// <summary>
    /// Only <see cref="ReadyCheckStatus.AwaitingResponse"/> marks a check in flight.
    /// The entries keep the previous check's Ready/NotReady results after it ends, so
    /// testing for any non-Unknown status mistakes every later self-started check for
    /// a received one.
    /// </summary>
    private static bool HasActiveEntries(AgentReadyCheck* agent)
    {
        foreach (ref var entry in agent->ReadyCheckEntries)
        {
            if (entry.ContentId != 0 && entry.Status == ReadyCheckStatus.AwaitingResponse)
                return true;
        }

        return false;
    }

    private void Announce()
    {
        if (!_config.Enabled || !_config.AnnounceToParty)
            return;

        if (IsInDuty())
        {
            _config.Debug(_log, "Ready check announcement skipped: already inside a duty.");
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastAnnounced < Debounce)
        {
            _config.Debug(_log, "Ready check announcement skipped: debounced.");
            return;
        }

        // /p prints "You are not in a party." locally rather than leaking to Say, but
        // there is no reason to make the game say it.
        if (_party.Length == 0)
        {
            _config.Debug(_log, "Ready check announcement skipped: not in a party.");
            return;
        }

        var selection = DutyFinder.Read();
        _config.Debug(_log, $"Duty Finder selection: {selection.Count} entr{(selection.Count == 1 ? "y" : "ies")}, roulette={selection.AnyRoulette}.");

        var message = Compose(selection);
        if (message == null)
            return;

        var sent = PartyChat.Send(message);
        if (sent == null)
        {
            _config.Debug(_log, "Ready check announcement produced no sendable text.");
            return;
        }

        _lastAnnounced = now;
        _config.Debug(_log, $"Sent: {sent}");
    }

    /// <summary>
    /// Everything a real ready check would do except send the packet: is the hook
    /// live, what is selected, and what would go out. Exists because the real path
    /// cannot be exercised alone — the game will not start a ready check without a
    /// party — so without this the first test of the plugin is also its first use in
    /// front of seven other people.
    /// </summary>
    public IEnumerable<string> DryRun()
    {
        yield return _hook.IsEnabled
            ? $"Hook installed and enabled at 0x{_hook.Address:X}."
            : "Hook is NOT enabled — announcements will not fire.";

        if (!_config.Enabled)
            yield return "Plugin is disabled in settings; a ready check would do nothing.";
        else if (!_config.AnnounceToParty)
            yield return "Party announcement is turned off in settings.";

        yield return _config.AnnounceOwnJob
            ? $"Own job: {OwnJob.Describe(_player, _config.AnnounceOwnRole) ?? "unreadable"}."
            : "Own job: not announced.";

        var selection = DutyFinder.Read();
        if (selection.IsEmpty)
        {
            yield return "Duty Finder: nothing selected.";
        }
        else
        {
            yield return $"Duty Finder: {selection.Count} selected.";
            // Per entry rather than a joined line: this is where you check that the
            // level and the Adventurer in Need match what the Duty Finder is showing.
            foreach (var e in selection.Entries)
                yield return $"  {e.Name} — roulette={e.IsRoulette}, Lv.{e.Level}, i{e.ItemLevel}, inNeed={e.InNeed}";
        }

        var message = Compose(selection);
        var line    = message == null || IsInDuty() ? null : PartyChat.BuildLine(message);

        if (IsInDuty())
            yield return "Already inside a duty, so a real ready check would stay silent.";

        yield return line == null
            ? "Would send: nothing."
            : $"Would send: {line}";

        // The party check is reported rather than short-circuiting, so the detection
        // above is still visible when testing alone — which is the whole point.
        if (_party.Length == 0)
            yield return "You are not in a party, so a real ready check would stay silent.";
    }

    /// <summary>
    /// Builds the line to send, or null to stay quiet. Every case that cannot name a
    /// duty honestly ends up at <see cref="Configuration.AnnounceWithoutDuty"/>, which
    /// is off — a ready check with no message is better than one that names the wrong
    /// duty or a duty that was never selected.
    /// </summary>
    private bool IsInDuty() =>
        _condition[ConditionFlag.BoundByDuty] ||
        _condition[ConditionFlag.BoundByDuty56] ||
        _condition[ConditionFlag.BoundByDuty95];

    private string? Compose(DutySelection selection)
    {
        if (selection.IsEmpty)
            return _config.AnnounceWithoutDuty ? _config.FallbackMessage : null;

        if (selection.AnyRoulette && !_config.AnnounceRoulettes)
            return _config.AnnounceWithoutDuty ? _config.FallbackMessage : null;

        var duty = selection.Count == 1
            ? Describe(selection.Entries[0])
            : _config.ListMultipleDuties
                ? string.Join(", ", selection.Names)
                : $"{selection.Count} duties selected";

        var line = _config.MessageFormat.Replace("{duty}", duty, StringComparison.Ordinal);
        return WithJob(line);
    }

    /// <summary>
    /// Puts the job into the line: where the format asks for it, or appended when it
    /// does not mention it. A format that places "{job}" itself wins, so someone who
    /// has written their own line is never given a second copy on the end.
    /// </summary>
    private string WithJob(string line)
    {
        var mentioned = line.Contains("{job}", StringComparison.Ordinal);
        if (!_config.AnnounceOwnJob)
            return mentioned ? line.Replace("{job}", "", StringComparison.Ordinal).TrimEnd() : line;

        var job = OwnJob.Describe(_player, _config.AnnounceOwnRole);
        if (job == null)
        {
            _config.Debug(_log, "Own job not announced: no character to read it from.");
            return mentioned ? line.Replace("{job}", "", StringComparison.Ordinal).TrimEnd() : line;
        }

        return mentioned
            ? line.Replace("{job}", job, StringComparison.Ordinal)
            : $"{line} — on {job}";
    }

    /// <summary>
    /// One duty with its requirements and Adventurer in Need appended, as the Duty
    /// Finder shows them. Only used for a single selection: hanging a level on each
    /// of eight names, or on a bare count, is noise rather than information.
    /// </summary>
    private string Describe(DutyEntry entry)
    {
        var detail = entry.Detail(_config.AnnounceRequirements, _config.AnnounceAdventurerInNeed);
        return detail == null ? entry.Name : $"{entry.Name} ({detail})";
    }

    public void Dispose()
    {
        _hook.Disable();
        _hook.Dispose();
    }
}
