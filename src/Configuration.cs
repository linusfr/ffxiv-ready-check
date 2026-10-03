using Dalamud.Configuration;
using Dalamud.Plugin.Services;

namespace ReadyCheck;

[System.Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// Master switch. Off leaves the game's ready check completely untouched — the
    /// hook stays installed but does nothing, so toggling needs no reload.
    public bool Enabled { get; set; } = true;

    /// Post the announcement to party chat. Off with Enabled on is only useful with
    /// Debug, to see what would have been sent.
    public bool AnnounceToParty { get; set; } = true;

    /// The line sent to party chat. "{duty}" is replaced with the duty name, the
    /// list of duties, or the count; "{job}" with the job you are on.
    public string MessageFormat { get; set; } = "Ready check: {duty}";

    /// Name the job you are on, so the party can see which slot is taken and decide
    /// what to switch to. Off by default: it is the one part of the line that is
    /// about you rather than about the duty. Appended as "— on WHM (healer)" unless
    /// MessageFormat places "{job}" itself.
    public bool AnnounceOwnJob { get; set; }

    /// Say the role beside the job — "WHM (healer)". On: the role is what someone
    /// reads to work out what is still missing, and not everyone knows every
    /// abbreviation.
    public bool AnnounceOwnRole { get; set; } = true;

    /// With several duties ticked, name them all rather than counting them. Off:
    /// eight duties is a wall of text in someone else's chat log.
    public bool ListMultipleDuties { get; set; } = false;

    /// Append the level and item level the duty requires: "(Lv. 100, i750)". Item
    /// level is omitted where the game states none, which is most content below 50.
    public bool AnnounceRequirements { get; set; } = true;

    /// Append the Adventurer in Need role for roulettes that have one, the way the
    /// Duty Finder shows it: "(Lv. 16, healer in need)". Worth saying — it is the one
    /// part of a ready check someone might actually act on by switching job.
    public bool AnnounceAdventurerInNeed { get; set; } = true;

    /// Announce Duty Roulette selections. On — "Leveling Roulette" is as much what
    /// the party is about to queue for as a named dungeon is.
    public bool AnnounceRoulettes { get; set; } = true;

    /// Send <see cref="FallbackMessage"/> when the duty cannot be determined. Off:
    /// a bare "Ready check" adds nothing the ready-check prompt does not already
    /// say, and the interesting case — nothing selected — is exactly when a message
    /// would be most misleading.
    public bool AnnounceWithoutDuty { get; set; } = false;

    /// What to send when the duty cannot be determined, if AnnounceWithoutDuty is on.
    public string FallbackMessage { get; set; } = "Ready check";

    /// Log what was detected and sent to the Dalamud log.
    public bool DebugMode { get; set; } = false;

    internal void Debug(IPluginLog log, string message)
    {
        if (DebugMode)
            log.Information(message);
    }
}
