using System.Collections.Generic;
using System.Linq;

using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

using Lumina.Text.ReadOnly;

namespace ReadyCheck;

/// <summary>The role the server is currently granting a bonus to, if any.</summary>
public enum RoleInNeed
{
    None,
    Tank,
    Healer,
    Dps,
}

/// <summary>One entry the Duty Finder has ticked, with everything worth announcing.</summary>
/// <param name="Name">The display name, in the client's language.</param>
/// <param name="Level">Required class/job level; 0 when the game states none.</param>
/// <param name="ItemLevel">Required item level; 0 for most content below level 50.</param>
/// <param name="InNeed">Adventurer in Need, for roulettes that have one.</param>
public readonly record struct DutyEntry(string Name, bool IsRoulette, int Level, int ItemLevel, RoleInNeed InNeed)
{
    /// <summary>
    /// The parenthetical after the duty name — "Lv. 100, i750, tank in need" — built
    /// from whichever parts the game has values for and the user asked for. Null when
    /// that leaves nothing, so the caller never prints empty parentheses.
    /// </summary>
    public string? Detail(bool requirements, bool inNeed)
    {
        var parts = new List<string>(3);

        if (requirements && Level > 0)
            parts.Add($"Lv. {Level}");
        // Item level is 0 for most content below level 50: the game states no
        // requirement there, so neither do we.
        if (requirements && ItemLevel > 0)
            parts.Add($"i{ItemLevel}");
        if (inNeed && InNeed != RoleInNeed.None)
            parts.Add($"{Label(InNeed)} in need");

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string Label(RoleInNeed role) => role switch
    {
        RoleInNeed.Tank   => "tank",
        RoleInNeed.Healer => "healer",
        RoleInNeed.Dps    => "DPS",
        _                 => "",
    };
}

/// <summary>What the Duty Finder currently has ticked.</summary>
public readonly record struct DutySelection(IReadOnlyList<DutyEntry> Entries)
{
    public static readonly DutySelection Empty = new(System.Array.Empty<DutyEntry>());

    // Entries is null on a defaulted struct. Tolerate it rather than trusting every
    // caller to have come through Empty.
    public int  Count       => Entries?.Count ?? 0;
    public bool IsEmpty     => Count == 0;
    public bool AnyRoulette => Entries?.Any(static e => e.IsRoulette) ?? false;

    public IEnumerable<string> Names => Entries?.Select(static e => e.Name) ?? Enumerable.Empty<string>();
}

/// <summary>
/// Reads the Duty Finder's selection out of the game.
///
/// <para>The authority is <c>AgentContentsFinder.SelectedContent</c>, a vector of
/// <c>ContentsId</c> — the entries the player has ticked. Each carries a
/// <c>ContentType</c> of <c>Regular</c> (a ContentFinderCondition row) or
/// <c>Roulette</c> (a ContentRoulette row), so roulettes and duties come out of the
/// same list and multi-selection is just a longer vector. There is no separate
/// "roulette mode" to special-case; <c>HasRouletteSelected</c> is only a summary of
/// what is already in the vector.</para>
///
/// <para>Names come from the agent's own <c>ContentList</c> first. Those are the
/// exact <c>Utf8String</c>s the Duty Finder is drawing, so they are already in the
/// client's language and already capitalised the way the game capitalises them.
/// Excel is the fallback for when the agent has been torn down — correct, but see
/// <see cref="CapitaliseFirst"/> for the one thing the sheet does not carry.</para>
///
/// <para>Levels come from Excel in every case rather than from the agent: the sheets
/// carry the same numbers, and reading them costs no struct offsets a patch could
/// move.</para>
/// </summary>
internal static unsafe class DutyFinder
{
    /// <summary>
    /// Reads the current selection, or an empty result if nothing is selected or the
    /// Duty Finder agent does not exist yet.
    /// </summary>
    /// <param name="includeQueued">
    /// When nothing is ticked, fall back to what is actually queued. Covers a ready
    /// check run after the party has already joined the queue, where the agent's
    /// selection has been handed over to <c>ContentsFinder.QueueInfo</c>.
    /// </param>
    public static DutySelection Read(bool includeQueued = true)
    {
        var agent = AgentContentsFinder.Instance();
        if (agent == null)
            return DutySelection.Empty;

        var ids = new List<ContentsId>();

        var selected = agent->SelectedContent;
        for (var i = 0; i < selected.Count; i++)
        {
            var id = selected[i];
            if (id.ContentType != ContentsType.None && id.Id != 0)
                ids.Add(id);
        }

        if (ids.Count == 0 && includeQueued)
        {
            var finder = ContentsFinder.Instance();
            if (finder != null)
            {
                foreach (var id in finder->QueueInfo.QueuedEntries)
                {
                    if (id.ContentType != ContentsType.None && id.Id != 0)
                        ids.Add(id);
                }
            }
        }

        if (ids.Count == 0)
            return DutySelection.Empty;

        var entries = new List<DutyEntry>(ids.Count);
        foreach (var id in ids)
        {
            var entry = Describe(agent, id);
            if (entry != null)
                entries.Add(entry.Value);
        }

        return new DutySelection(entries);
    }

    private static DutyEntry? Describe(AgentContentsFinder* agent, ContentsId id)
    {
        var onScreen = NameFromContentList(agent, id);

        if (id.ContentType == ContentsType.Roulette)
        {
            if (!TryGetRow<Lumina.Excel.Sheets.ContentRoulette>(id.Id, out var roulette))
                return Bare(onScreen, true);

            var name = onScreen ?? Fallback(roulette.Name.ExtractText());
            return name == null
                ? null
                : new DutyEntry(name, true, roulette.RequiredLevel, roulette.ItemLevelRequired, RoleInNeedFor(agent, roulette));
        }

        if (!TryGetRow<Lumina.Excel.Sheets.ContentFinderCondition>(id.Id, out var duty))
            return Bare(onScreen, false);

        var dutyName = onScreen ?? Fallback(duty.Name.ExtractText());
        return dutyName == null
            ? null
            : new DutyEntry(dutyName, false, duty.ClassJobLevelRequired, duty.ItemLevelRequired, RoleInNeed.None);
    }

    /// <summary>A name with no detail, for when the sheet lookup failed but the UI still had the row.</summary>
    private static DutyEntry? Bare(string? name, bool isRoulette)
        => name == null ? null : new DutyEntry(name, isRoulette, 0, 0, RoleInNeed.None);

    /// <summary>
    /// The Adventurer in Need for a roulette.
    ///
    /// <para>The server sends 11 role-bonus bytes on zone init and the agent mirrors
    /// them. <c>ContentRouletteRoleBonus</c> is an 11-row sheet, and each roulette's
    /// row reference into it is that roulette's slot in the array — verified against
    /// the game data: Leveling→1, High-level→2, Main Scenario→3, Guildhests→4,
    /// Expert→5, Trials→6, Level Cap→7, Mentor→8, Alliance→9, Normal Raids→10.</para>
    ///
    /// <para>Row 0 is the "no such thing" sentinel rather than a real slot — Frontline,
    /// Crystalline Conflict and the chocobo races all point at it. Every reference is
    /// 0..10, which is what keeps the index in bounds; the length check is belt and
    /// braces for a patch that adds a roulette before Dalamud catches up.</para>
    /// </summary>
    private static RoleInNeed RoleInNeedFor(AgentContentsFinder* agent, Lumina.Excel.Sheets.ContentRoulette roulette)
    {
        var slot = roulette.ContentRouletteRoleBonus.RowId;
        if (slot == 0)
            return RoleInNeed.None;

        var bonuses = agent->ContentRouletteRoleBonuses;
        if (slot >= (uint)bonuses.Length)
            return RoleInNeed.None;

        return bonuses[(int)slot] switch
        {
            ContentsRouletteRole.Tank   => RoleInNeed.Tank,
            ContentsRouletteRole.Healer => RoleInNeed.Healer,
            ContentsRouletteRole.Dps    => RoleInNeed.Dps,
            _                           => RoleInNeed.None,
        };
    }

    /// <summary>
    /// Finds the entry in the agent's own list of rows. This is the preferred source
    /// for the name: it is the literal text on screen, so it needs no localisation or
    /// casing work.
    /// </summary>
    private static string? NameFromContentList(AgentContentsFinder* agent, ContentsId id)
    {
        var list = agent->ContentList;
        for (var i = 0; i < list.Count; i++)
        {
            var entry = list[i].Value;
            if (entry == null)
                continue;
            if (entry->Id.Id != id.Id || entry->Id.ContentType != id.ContentType)
                continue;

            // Not Utf8String.ToString(): that raw-decodes the buffer, and duty names
            // are SeStrings. "the Thousand Maws of Toto-Rak" stores its hyphen as the
            // macro chunk 02 1F 01 03, which raw-decodes to four control characters
            // and then gets stripped on the way to chat — "TotoRak". ExtractText
            // resolves the macro to "-" the way the game's own renderer does.
            // Seven duties are affected, including Tam-Tara and the Whorleater.
            var name = new ReadOnlySeStringSpan(entry->Name.AsSpan()).ExtractText();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        return null;
    }

    private static bool TryGetRow<T>(uint rowId, out T row) where T : struct, Lumina.Excel.IExcelRow<T>
    {
        var sheet = Plugin.DataManager.GetExcelSheet<T>();
        if (sheet != null && sheet.TryGetRow(rowId, out row))
            return true;

        row = default;
        return false;
    }

    private static string? Fallback(string name)
        => string.IsNullOrWhiteSpace(name) ? null : CapitaliseFirst(name);

    /// <summary>
    /// ContentFinderCondition stores English names mid-sentence ("the Aurum Vale");
    /// the game capitalises them when it puts them at the start of a line. Only the
    /// Excel fallback needs this — names taken from the agent are already correct —
    /// and it is a no-op for languages that do not lead with a lowercase article.
    /// </summary>
    private static string CapitaliseFirst(string value)
        => char.IsLower(value[0]) ? char.ToUpperInvariant(value[0]) + value[1..] : value;
}
