using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using ReadyCheck.Windows;

namespace ReadyCheck;

public sealed class Plugin : IDalamudPlugin
{
    // ── Injected services ─────────────────────────────────────────────────────
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager  { get; private set; } = null!;
    [PluginService] internal static IDataManager            DataManager     { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider    GameInterop     { get; private set; } = null!;
    [PluginService] internal static IPartyList              PartyList       { get; private set; } = null!;
    [PluginService] internal static IChatGui                ChatGui         { get; private set; } = null!;

    internal Configuration Config { get; }

    private readonly ReadyCheckAnnouncer  _announcer;
    private readonly ConfigurationWindow  _configWindow;

    private const string CmdConfig = "/readycheckcfg";

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _configWindow = new ConfigurationWindow(this);
        _announcer    = new ReadyCheckAnnouncer(Config, GameInterop, Log, PartyList);

        CommandManager.AddHandler(CmdConfig, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open Ready Check settings. \"/readycheckcfg test\" reports what a ready check would send.",
        });

        PluginInterface.UiBuilder.Draw         += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfig;
        PluginInterface.UiBuilder.OpenMainUi   += OnOpenConfig;

        Log.Info("ReadyCheck: Plugin loaded.");
    }

    private void OnConfigCommand(string cmd, string args)
    {
        if (args.Trim().Equals("test", System.StringComparison.OrdinalIgnoreCase))
            DryRun();
        else
            _configWindow.IsVisible = !_configWindow.IsVisible;
    }

    /// <summary>
    /// Prints what a ready check would do, to your own chat log only. The real path
    /// needs a party, so this is the only way to check the plugin works before using
    /// it in front of one.
    /// </summary>
    internal void DryRun()
    {
        foreach (var line in _announcer.DryRun())
            ChatGui.Print($"[Ready Check] {line}");
    }

    private void OnOpenConfig()                           => _configWindow.IsVisible = true;
    private void OnDraw()                                 => _configWindow.Draw();

    internal void SaveConfig() => PluginInterface.SavePluginConfig(Config);

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw         -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfig;
        PluginInterface.UiBuilder.OpenMainUi   -= OnOpenConfig;

        CommandManager.RemoveHandler(CmdConfig);

        _announcer.Dispose();
        _configWindow.Dispose();

        Log.Info("ReadyCheck: Plugin unloaded.");
    }
}
