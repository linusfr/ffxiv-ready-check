using System;

using Dalamud.Plugin.Services;

namespace ReadyCheck;

/// <summary>
/// The job you are on, for the announcement. A ready check tells the party what is
/// being queued for; this tells them which slot is already taken, which is the part
/// someone acts on when they decide what to switch to.
/// </summary>
internal static class OwnJob
{
    /// <summary>
    /// "WHM (healer)", or null when there is no character to read — logged out, or
    /// between zones. Null means the announcement simply leaves the job out rather
    /// than naming a job that is not current.
    /// </summary>
    public static string? Describe(IPlayerState player, bool withRole)
    {
        if (!player.IsLoaded)
            return null;

        var job = player.ClassJob.ValueNullable;
        if (job is null)
            return null;

        var abbreviation = job.Value.Abbreviation.ExtractText();
        if (string.IsNullOrWhiteSpace(abbreviation))
            return null;

        var role = withRole ? Role(job.Value.Role) : null;
        return role == null ? abbreviation : $"{abbreviation} ({role})";
    }

    /// <summary>
    /// The sheet's role number, worded the way the Duty Finder's Adventurer in Need
    /// is worded elsewhere in the announcement. Melee and ranged are both DPS here:
    /// the question being answered is which of the four slots is filled.
    /// </summary>
    private static string? Role(byte role) => role switch
    {
        1 => "tank",
        2 or 3 => "DPS",
        4 => "healer",
        _ => null,
    };
}
