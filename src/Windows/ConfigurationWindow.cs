using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace ReadyCheck.Windows;

public sealed class ConfigurationWindow : IDisposable
{
    private static readonly Vector4 Heading = new(1f, 0.8f, 0.3f, 1f);

    private readonly Plugin _plugin;
    private Configuration Config => _plugin.Config;

    private bool _isVisible;
    public bool IsVisible { get => _isVisible; set => _isVisible = value; }

    public ConfigurationWindow(Plugin plugin)
    {
        _plugin = plugin;
    }

    public void Draw()
    {
        if (!IsVisible) return;

        ImGui.SetNextWindowSize(new Vector2(460, 400), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Ready Check###ReadyCheckSettings", ref _isVisible))
        {
            ImGui.End();
            return;
        }

        Toggle("Enable plugin", Config.Enabled, v => Config.Enabled = v);

        ImGui.BeginDisabled(!Config.Enabled);

        Toggle("Announce in party chat", Config.AnnounceToParty, v => Config.AnnounceToParty = v);

        Section("Message");
        Text(Config.MessageFormat, v => Config.MessageFormat = v, "##format");
        ImGui.TextDisabled("{duty} becomes the duty name, the list, or the count.");

        Section("What to announce");
        Toggle("List every selected duty", Config.ListMultipleDuties, v => Config.ListMultipleDuties = v);
        ImGui.TextDisabled(Config.ListMultipleDuties
            ? "  Ready check: The Aurum Vale, Haukke Manor, Brayflox's Longstop"
            : "  Ready check: 3 duties selected");

        Toggle("Announce roulettes", Config.AnnounceRoulettes, v => Config.AnnounceRoulettes = v);
        ImGui.TextDisabled("  Ready check: Leveling");

        Toggle("Level and item level", Config.AnnounceRequirements, v => Config.AnnounceRequirements = v);
        ImGui.TextDisabled("  Ready check: The Aurum Vale (Lv. 47)");

        Toggle("Adventurer in Need", Config.AnnounceAdventurerInNeed, v => Config.AnnounceAdventurerInNeed = v);
        ImGui.TextDisabled("  Ready check: Leveling (Lv. 16, healer in need)");
        ImGui.TextDisabled("  Roulettes only, and only where the game shows one.");

        Section("Your job");
        Toggle("Name the job you are on", Config.AnnounceOwnJob, v => Config.AnnounceOwnJob = v);
        if (Config.AnnounceOwnJob)
        {
            Toggle("  Say the role too", Config.AnnounceOwnRole, v => Config.AnnounceOwnRole = v);
            ImGui.TextDisabled(Config.AnnounceOwnRole
                ? "  Ready check: Leveling (Lv. 16, healer in need) — on WHM (healer)"
                : "  Ready check: Leveling (Lv. 16, healer in need) — on WHM");
            ImGui.TextDisabled("  Tells the party which slot is taken. Put {job} in the");
            ImGui.TextDisabled("  message to place it yourself.");
        }

        Section("When the duty cannot be determined");
        Toggle("Announce anyway", Config.AnnounceWithoutDuty, v => Config.AnnounceWithoutDuty = v);
        if (Config.AnnounceWithoutDuty)
            Text(Config.FallbackMessage, v => Config.FallbackMessage = v, "##fallback");
        else
            ImGui.TextDisabled("  Stay quiet rather than say something misleading.");

        Section("Debug");
        Toggle("Log what was detected and sent", Config.DebugMode, v => Config.DebugMode = v);
        ImGui.TextDisabled("  Writes to the Dalamud log (/xllog).");

        ImGui.EndDisabled();

        Section("Test");
        if (ImGui.Button("Dry run"))
            _plugin.DryRun();
        ImGui.TextDisabled("  Prints what a ready check would send, to your log only.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled("Announcement only — never answers a ready check for you.");

        ImGui.End();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static void Section(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(Heading, label);
        ImGui.Separator();
    }

    private void Toggle(string label, bool value, Action<bool> write)
    {
        var local = value;
        if (!ImGui.Checkbox(label, ref local)) return;
        write(local);
        _plugin.SaveConfig();
    }

    private void Text(string value, Action<string> write, string id)
    {
        var local = value;
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.InputText(id, ref local, 256)) return;
        write(local);
        _plugin.SaveConfig();
    }

    public void Dispose() { }
}
