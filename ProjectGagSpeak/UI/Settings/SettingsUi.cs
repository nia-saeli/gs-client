using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using GagSpeak.ChatMessages;
using GagSpeak.CkCommons.GarblerCore;
using GagSpeak.Interop.Ipc;
using GagSpeak.Localization;
using GagSpeak.PlayerData.Data;
using GagSpeak.PlayerData.Pairs;
using GagSpeak.Services;
using GagSpeak.Services.Configs;
using GagSpeak.Services.Mediator;
using GagSpeak.UpdateMonitoring;
using GagSpeak.UpdateMonitoring.SpatialAudio.Managers;
using GagSpeak.UpdateMonitoring.SpatialAudio.Spawner;
using GagSpeak.WebAPI;
using ImGuiNET;
using OtterGui;
using OtterGui.Text;

namespace GagSpeak.UI;

public class SettingsUi : WindowMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly AccountManagerTab _accountsTab;
    private readonly DebugTab _debugTab;
    private readonly GlobalData _global;
    private readonly IpcManager _ipcManager;
    private readonly OnFrameworkService _frameworkUtil;
    private readonly PairManager _pairManager;
    private readonly GagspeakConfigService _mainConfig;
    private readonly ServerConfigurationManager _serverConfigs;
    private readonly SettingsHardcore _hardcoreSettingsUI;
    private readonly PiShockProvider _shockProvider;
    private readonly AvfxManager _avfxManager;
    private readonly VfxSpawns _vfxSpawns;
    private bool ThemePushed = false;

    public SettingsUi(ILogger<SettingsUi> logger, GagspeakMediator mediator,
        MainHub hub, AccountManagerTab accounts, DebugTab debug, GagspeakConfigService config,
        PairManager pairManager, GlobalData global, PiShockProvider shockProvider,
        AvfxManager avfxManager, VfxSpawns vfxSpawns, ServerConfigurationManager serverConfigs,
        IpcManager ipcManager, SettingsHardcore hardcoreSettingsUI, CkGui uiShared,
        OnFrameworkService frameworkUtil) : base(logger, mediator, "GagSpeak Settings")
    {
        _hub = hub;
        _accountsTab = accounts;
        _debugTab = debug;
        _global = global;
        _pairManager = pairManager;
        _mainConfig = config;
        _serverConfigs = serverConfigs;
        _shockProvider = shockProvider;
        _avfxManager = avfxManager;
        _vfxSpawns = vfxSpawns;
        _ipcManager = ipcManager;
        _frameworkUtil = frameworkUtil;
        _hardcoreSettingsUI = hardcoreSettingsUI;

        Flags = ImGuiWindowFlags.NoScrollbar;
        AllowClickthrough = false;
        AllowPinning = false;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(625, 400),
            MaximumSize = new Vector2(800, 2000),
        };

        Mediator.Subscribe<OpenSettingsUiMessage>(this, (_) => Toggle());
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
    }

    protected override void PreDrawInternal()
    {
        if (!ThemePushed)
        {
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.331f, 0.081f, 0.169f, .803f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.579f, 0.170f, 0.359f, 0.828f));
            ThemePushed = true;
        }
    }

    protected override void PostDrawInternal()
    {
        if (ThemePushed)
        {
            ImGui.PopStyleColor(2);
            ThemePushed = false;
        }
    }


    protected override void DrawInternal()
    {
        CkGui.DrawOptionalPlugins();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(GSLoc.Settings.AccountClaimText);
        ImGui.SameLine();
        if (ImGui.Button("CK Discord"))
        {
            Util.OpenLink("https://discord.gg/kinkporium");
        }
        // draw out the tab bar for us.
        if (ImGui.BeginTabBar("mainTabBar"))
        {

            if (MainHub.IsConnected)
            {
                if (ImGui.BeginTabItem(GSLoc.Settings.TabsGlobal))
                {
                    DrawGlobalSettings();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(GSLoc.Settings.TabsHardcore))
                {
                    _hardcoreSettingsUI.DrawHardcoreSettings();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(GSLoc.Settings.TabsPreferences))
                {
                    DrawPreferences();
                    ImGui.EndTabItem();
                }
            }

            if (ImGui.BeginTabItem(GSLoc.Settings.TabsAccounts))
            {
                _accountsTab.DrawManager();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Debug"))
            {
                _debugTab.DrawDebugMain();
                ImGui.EndTabItem();
            }

#if DEBUG
            if (ImGui.BeginTabItem("Dev"))
            {
                _debugTab.DrawDevDebug();
                ImGui.EndTabItem();
            }
#endif
            ImGui.EndTabBar();
        }
    }

    private DateTime _lastRefresh = DateTime.MinValue;
    private void DrawGlobalSettings()
    {
        var liveChatGarblerActive = _global.GlobalPerms!.ChatGarblerActive;
        var liveChatGarblerLocked = _global.GlobalPerms.ChatGarblerLocked;
        bool removeGagOnLockExpiration = _mainConfig.Config.RemoveRestrictionOnTimerExpire;

        var wardrobeEnabled = _global.GlobalPerms.WardrobeEnabled;
        var gagVisuals = _global.GlobalPerms.GagVisuals;
        var restrictionVisuals = _global.GlobalPerms.RestrictionVisuals;
        var restraintSetVisuals = _global.GlobalPerms.RestraintSetVisuals;
        bool cursedDungeonLoot = _mainConfig.Config.CursedLootPanel;
        bool mimicsApplyTraits = _mainConfig.Config.CursedItemsApplyTraits;

        var puppeteerEnabled = _global.GlobalPerms.PuppeteerEnabled;
        var globalTriggerPhrase = _global.GlobalPerms.TriggerPhrase;
        var globalPuppetPerms = _global.GlobalPerms.PuppetPerms;

        var toyboxEnabled = _global.GlobalPerms.ToyboxEnabled;
        bool intifaceAutoConnect = _mainConfig.Config.IntifaceAutoConnect;
        string intifaceConnectionAddr = _mainConfig.Config.IntifaceConnectionSocket;
        var spatialVibratorAudio = _global.GlobalPerms.SpatialAudio;

        // pishock stuff.
        string piShockApiKey = _mainConfig.Config.PiShockApiKey;
        string piShockUsername = _mainConfig.Config.PiShockUsername;

        var globalPiShockShareCode = _global.GlobalPerms.GlobalShockShareCode;
        var allowGlobalShockShockCollar = _global.GlobalPerms.AllowShocks;
        var allowGlobalVibrateShockCollar = _global.GlobalPerms.AllowVibrations;
        var allowGlobalBeepShockCollar = _global.GlobalPerms.AllowBeeps;
        var maxGlobalShockCollarIntensity = _global.GlobalPerms.MaxIntensity;
        var maxGlobalShockDuration = _global.GlobalPerms.GetTimespanFromDuration();
        var maxGlobalVibrateDuration = (int)_global.GlobalPerms.ShockVibrateDuration.TotalSeconds;

        CkGui.GagspeakBigText(GSLoc.Settings.MainOptions.HeaderGags);
        using (ImRaii.Disabled(liveChatGarblerLocked))
        {
            if (ImGui.Checkbox(GSLoc.Settings.MainOptions.LiveChatGarbler, ref liveChatGarblerActive))
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.ChatGarblerActive), liveChatGarblerActive), UpdateDir.Own)).ConfigureAwait(false);
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.LiveChatGarblerTT);
        }

        if (ImGui.Checkbox(GSLoc.Settings.MainOptions.GagGlamours, ref gagVisuals))
            _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                new KeyValuePair<string, object>(nameof(_global.GlobalPerms.GagVisuals), gagVisuals), UpdateDir.Own)).ConfigureAwait(false);
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.GagGlamoursTT);

        if (ImGui.Checkbox(GSLoc.Settings.MainOptions.GagPadlockTimer, ref removeGagOnLockExpiration))
        {
            _mainConfig.Config.RemoveRestrictionOnTimerExpire = removeGagOnLockExpiration;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.GagPadlockTimerTT);

        ImGui.Separator();
        CkGui.GagspeakBigText(GSLoc.Settings.MainOptions.HeaderWardrobe);

        if (ImGui.Checkbox(GSLoc.Settings.MainOptions.WardrobeActive, ref wardrobeEnabled))
        {
            _ = _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                new KeyValuePair<string, object>(nameof(_global.GlobalPerms.WardrobeEnabled), wardrobeEnabled), UpdateDir.Own));
            if (wardrobeEnabled is false)
            {
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.RestrictionVisuals), false), UpdateDir.Own)).ConfigureAwait(false);
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData,
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.RestraintSetVisuals), false), UpdateDir.Own)).ConfigureAwait(false);
                _mainConfig.Config.CursedLootPanel = false;
                _mainConfig.Save();
            }
        }
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.WardrobeActiveTT);

        using (ImRaii.Disabled(!wardrobeEnabled))
        {
            if (ImGui.Checkbox(GSLoc.Settings.MainOptions.RestraintSetGlamour, ref restrictionVisuals))
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData,
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.RestrictionVisuals), restrictionVisuals), UpdateDir.Own)).ConfigureAwait(false);
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.RestraintSetGlamourTT);

            if (ImGui.Checkbox(GSLoc.Settings.MainOptions.RestraintSetGlamour, ref restraintSetVisuals))
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.RestraintSetVisuals), restraintSetVisuals), UpdateDir.Own)).ConfigureAwait(false);
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.RestraintSetGlamourTT);

            if (ImGui.Checkbox(GSLoc.Settings.MainOptions.CursedLootActive, ref cursedDungeonLoot))
            {
                _mainConfig.Config.CursedLootPanel = cursedDungeonLoot;
                _mainConfig.Save();
            }
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.CursedLootActiveTT);

            if (ImGui.Checkbox(GSLoc.Settings.MainOptions.MimicsApplyTraits, ref mimicsApplyTraits))
            {
                _mainConfig.Config.CursedItemsApplyTraits = mimicsApplyTraits;
                _mainConfig.Save();
            }
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.MimicsApplyTraitsTT);
        }

        CkGui.GagspeakBigText(GSLoc.Settings.MainOptions.HeaderPuppet);
        if (ImGui.Checkbox(GSLoc.Settings.MainOptions.PuppeteerActive, ref puppeteerEnabled))
            _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData,
                new KeyValuePair<string, object>(nameof(_global.GlobalPerms.PuppeteerEnabled), puppeteerEnabled), UpdateDir.Own)).ConfigureAwait(false);
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.PuppeteerActiveTT);

        using (ImRaii.Disabled(!puppeteerEnabled))
        {
            using var indent = ImRaii.PushIndent();

            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputText(GSLoc.Settings.MainOptions.GlobalTriggerPhrase, ref globalTriggerPhrase, 100, ImGuiInputTextFlags.EnterReturnsTrue))
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.TriggerPhrase), globalTriggerPhrase), UpdateDir.Own)).ConfigureAwait(false);
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.GlobalTriggerPhraseTT);

            // Correct these!
            var refSits = (globalPuppetPerms & PuppetPerms.Sit) == PuppetPerms.Sit;
            if (ImGui.Checkbox(GSLoc.Settings.MainOptions.GlobalSit, ref refSits))
            {
                PuppetPerms newPerms = globalPuppetPerms ^ PuppetPerms.Sit;
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.PuppetPerms), newPerms), UpdateDir.Own)).ConfigureAwait(false);
            }
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.GlobalSitTT);

            var refEmotes = (globalPuppetPerms & PuppetPerms.Emotes) == PuppetPerms.Emotes;
            if (ImGui.Checkbox(GSLoc.Settings.MainOptions.GlobalMotion, ref refEmotes))
            {
                PuppetPerms newPerms = globalPuppetPerms ^ PuppetPerms.Emotes;
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.PuppetPerms), newPerms), UpdateDir.Own)).ConfigureAwait(false);
            }
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.GlobalMotionTT);

            var refAlias = (globalPuppetPerms & PuppetPerms.Alias) == PuppetPerms.Alias;
            if (ImGui.Checkbox(GSLoc.Settings.MainOptions.GlobalAlias, ref refAlias))
            {
                PuppetPerms newPerms = globalPuppetPerms ^ PuppetPerms.Alias;
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.PuppetPerms), newPerms), UpdateDir.Own)).ConfigureAwait(false);
            }
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.GlobalAliasTT);

            var refAllPerms = (globalPuppetPerms & PuppetPerms.All) == PuppetPerms.All;
            if (ImGui.Checkbox(GSLoc.Settings.MainOptions.GlobalAll, ref refAllPerms))
            {
                PuppetPerms newPerms = globalPuppetPerms ^ PuppetPerms.All;
                _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                    new KeyValuePair<string, object>(nameof(_global.GlobalPerms.PuppetPerms), newPerms), UpdateDir.Own)).ConfigureAwait(false);
            }
            CkGui.DrawHelpText(GSLoc.Settings.MainOptions.GlobalAllTT);
        }

        ImGui.Separator();
        CkGui.GagspeakBigText(GSLoc.Settings.MainOptions.HeaderToybox);

        if (ImGui.Checkbox(GSLoc.Settings.MainOptions.ToyboxActive, ref toyboxEnabled))
            _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                new KeyValuePair<string, object>(nameof(_global.GlobalPerms.ToyboxEnabled), toyboxEnabled), UpdateDir.Own)).ConfigureAwait(false);
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.ToyboxActiveTT);


        if (ImGui.Checkbox(GSLoc.Settings.MainOptions.IntifaceAutoConnect, ref intifaceAutoConnect))
        {
            _mainConfig.Config.IntifaceAutoConnect = intifaceAutoConnect;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.IntifaceAutoConnectTT);

        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputTextWithHint($"Server Address##ConnectionWSaddr", "Leave blank for default...", ref intifaceConnectionAddr, 100))
        {
            if (!intifaceConnectionAddr.Contains("ws://"))
            {
                intifaceConnectionAddr = "ws://localhost:12345";
            }
            else
            {
                _mainConfig.Config.IntifaceConnectionSocket = intifaceConnectionAddr;
                _mainConfig.Save();
            }
        }
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.IntifaceAddressTT);

        if (ImGui.Checkbox(GSLoc.Settings.MainOptions.SpatialAudioActive, ref spatialVibratorAudio))
            _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData,
                new KeyValuePair<string, object>(nameof(_global.GlobalPerms.SpatialAudio), spatialVibratorAudio), UpdateDir.Own)).ConfigureAwait(false);
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.SpatialAudioActiveTT);

        ImGui.Spacing();

        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("PiShock API Key", ref piShockApiKey, 100, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _mainConfig.Config.PiShockApiKey = piShockApiKey;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.PiShockKeyTT);

        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("PiShock Username", ref piShockUsername, 100, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _mainConfig.Config.PiShockUsername = piShockUsername;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.PiShockUsernameTT);


        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale - CkGui.IconTextButtonSize(FontAwesomeIcon.Sync, "Refresh") - ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGui.InputText("##Global PiShock Share Code", ref globalPiShockShareCode, 100, ImGuiInputTextFlags.EnterReturnsTrue))
            _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, 
                new KeyValuePair<string, object>(nameof(_global.GlobalPerms.GlobalShockShareCode), globalPiShockShareCode), UpdateDir.Own)).ConfigureAwait(false);

        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FontAwesomeIcon.Sync, "Refresh", null, false, DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(5)))
        {
            _lastRefresh = DateTime.UtcNow;
            // Send Mediator Event to grab updated settings for pair.
            Task.Run(async () =>
            {
                if (_global.GlobalPerms is null)
                    return;

                var newPerms = await _shockProvider.GetPermissionsFromCode(_global.GlobalPerms.GlobalShockShareCode);
                // set the new permissions, without affecting the original.
                var newGlobalPerms = _global.GlobalPerms with
                {
                    AllowShocks = newPerms.AllowShocks,
                    AllowVibrations = newPerms.AllowVibrations,
                    AllowBeeps = newPerms.AllowBeeps,
                    MaxDuration = newPerms.MaxDuration,
                    MaxIntensity = newPerms.MaxIntensity,
                };
                await _hub.UserPushAllGlobalPerms(new(MainHub.PlayerUserData, MainHub.PlayerUserData, newGlobalPerms, UpdateDir.Own));
            });
        }
        CkGui.AttachToolTip(GSLoc.Settings.MainOptions.PiShockShareCodeRefreshTT);

        ImUtf8.SameLineInner();
        ImGui.TextUnformatted(GSLoc.Settings.MainOptions.PiShockShareCode);
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.PiShockShareCodeTT);

        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt(GSLoc.Settings.MainOptions.PiShockVibeTime, ref maxGlobalVibrateDuration, 0, 30))
        {
            _global.GlobalPerms.ShockVibrateDuration = TimeSpan.FromSeconds(maxGlobalVibrateDuration);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            // Convert TimeSpan to ticks and send as UInt64
            var ticks = (ulong)_global.GlobalPerms.ShockVibrateDuration.Ticks;
            _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData,
                new KeyValuePair<string, object>(nameof(_global.GlobalPerms.ShockVibrateDuration), ticks), UpdateDir.Own)).ConfigureAwait(false);
        }
        CkGui.DrawHelpText(GSLoc.Settings.MainOptions.PiShockVibeTimeTT);

        // make this section readonly
        CkGui.ColorText(GSLoc.Settings.MainOptions.PiShockPermsLabel, ImGuiColors.ParsedGold);
        using (ImRaii.Disabled(true))
        {
            using (ImRaii.Group())
            {
                ImGui.Checkbox(GSLoc.Settings.MainOptions.PiShockAllowShocks, ref allowGlobalShockShockCollar);
                ImGui.SameLine();
                ImGui.Checkbox(GSLoc.Settings.MainOptions.PiShockAllowVibes, ref allowGlobalVibrateShockCollar);
                ImGui.SameLine();
                ImGui.Checkbox(GSLoc.Settings.MainOptions.PiShockAllowBeeps, ref allowGlobalBeepShockCollar);
            }
            ImGui.TextUnformatted(GSLoc.Settings.MainOptions.PiShockMaxShockIntensity);
            ImGui.SameLine();
            CkGui.ColorText(maxGlobalShockCollarIntensity.ToString() + "%", ImGuiColors.ParsedGold);

            ImGui.TextUnformatted(GSLoc.Settings.MainOptions.PiShockMaxShockDuration);
            ImGui.SameLine();
            CkGui.ColorText(maxGlobalShockDuration.Seconds.ToString() + "." + maxGlobalShockDuration.Milliseconds.ToString() + "s", ImGuiColors.ParsedGold);
        }
    }

    private void DrawChannelPreferences()
    {
        // do not draw the preferences if the globalpermissions are null.
        if(_global.GlobalPerms is null)
            return;

        var width = ImGui.GetContentRegionAvail().X / 2;
        ImGui.Columns(2, "PreferencesColumns", true);
        ImGui.SetColumnWidth(0, width);

        CkGui.GagspeakBigText("Live Chat Garbler");
        using (ImRaii.Group())
        {
            var i = 0;
            foreach (var e in ChatChannel.GetOrderedChannels())
            {
                var enabled = e.IsChannelEnabled(_global.GlobalPerms.ChatGarblerChannelsBitfield);
                if (i != 0 && (i == 4 || i == 7 || i == 11 || i == 15 || i == 19))
                    ImGui.NewLine();

                if (ImGui.Checkbox($"{e}", ref enabled))
                {
                    var newBitfield = e.SetChannelState(_global.GlobalPerms.ChatGarblerChannelsBitfield, enabled);
                    _hub.UserUpdateOwnGlobalPerm(new(MainHub.PlayerUserData, MainHub.PlayerUserData, new KeyValuePair<string, object>
                        (nameof(_global.GlobalPerms.ChatGarblerChannelsBitfield), newBitfield), UpdateDir.Own)).ConfigureAwait(false);
                }

                ImGui.SameLine();
                i++;
            }

            ImGui.NewLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(GSLoc.Settings.Preferences.LangDialectLabel);
            ImGui.SameLine();

            // voodoo magic from old code i cant be asked to polish.
            if(ImGuiUtil.GenericEnumCombo("##Language", 65, _mainConfig.Config.Language, out GarbleCoreLang newLang, i => i.ToName()))
            {
                if(newLang != _mainConfig.Config.Language)
                    _mainConfig.Config.LanguageDialect = newLang.GetDialects().First();

                _mainConfig.Config.Language = newLang;
                _mainConfig.Save();
            }
            CkGui.AttachToolTip(GSLoc.Settings.Preferences.LangTT);

            ImGui.SameLine();
            if(ImGuiUtil.GenericEnumCombo("##Dialect", 55, _mainConfig.Config.LanguageDialect, out GarbleCoreDialect newDialect,
                _mainConfig.Config.Language.GetDialects(), i => i.ToName()))
            {
                _mainConfig.Config.LanguageDialect = newDialect;
                _mainConfig.Save();
            }
            CkGui.AttachToolTip(GSLoc.Settings.Preferences.DialectTT);
        }
    }
    private void DrawPreferences()
    {
        DrawChannelPreferences();

        ImGui.NextColumn();
        CkGui.GagspeakBigText(GSLoc.Settings.Preferences.HeaderPuppet);
        using (ImRaii.Group())
        {
            var j = 0;
            foreach (var e in ChatChannel.GetOrderedChannels())
            {
                var enabled = e.IsChannelEnabled(_mainConfig.Config.PuppeteerChannelsBitfield);
                if (j != 0 && (j == 4 || j == 7 || j == 11 || j == 15 || j == 19))
                    ImGui.NewLine();

                if (ImGui.Checkbox($"{e}##{e}puppeteer", ref enabled))
                {
                    var newBitfield = e.SetChannelState(_mainConfig.Config.PuppeteerChannelsBitfield, enabled);
                    _mainConfig.Config.PuppeteerChannelsBitfield = newBitfield;
                    _mainConfig.Save();
                }

                ImGui.SameLine();
                j++;
            }
        }
        ImGui.Columns(1);

        ImGui.Separator();
        CkGui.GagspeakBigText(GSLoc.Settings.Preferences.HeaderUiPrefs);

        var showMainUiOnStart = _mainConfig.Config.OpenMainUiOnStartup;

        var enableDtrEntry = _mainConfig.Config.EnableDtrEntry;
        var dtrPrivacyRadar = _mainConfig.Config.ShowPrivacyRadar;
        var dtrActionNotifs = _mainConfig.Config.ShowActionNotifs;
        var dtrVibeStatus = _mainConfig.Config.ShowVibeStatus;

        var preferThreeCharaAnonName = _mainConfig.Config.PreferThreeCharaAnonName;
        var preferNicknamesInsteadOfName = _mainConfig.Config.PreferNicknamesOverNames;
        var showVisibleSeparate = _mainConfig.Config.ShowVisibleUsersSeparately;
        var showOfflineSeparate = _mainConfig.Config.ShowOfflineUsersSeparately;

        var showProfiles = _mainConfig.Config.ShowProfiles;
        var profileDelay = _mainConfig.Config.ProfileDelay;
        var showContextMenus = _mainConfig.Config.ShowContextMenus;

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.ShowMainUiOnStartLabel, ref showMainUiOnStart))
        {
            _mainConfig.Config.OpenMainUiOnStartup = showMainUiOnStart;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.ShowMainUiOnStartTT);

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.EnableDtrLabel, ref enableDtrEntry))
        {
            _mainConfig.Config.EnableDtrEntry = enableDtrEntry;
            if (enableDtrEntry is false)
            {
                _mainConfig.Config.ShowPrivacyRadar = false;
                _mainConfig.Config.ShowActionNotifs = false;
                _mainConfig.Config.ShowVibeStatus = false;
            }
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.EnableDtrTT);

        using (ImRaii.Disabled(!enableDtrEntry))
        {
            ImGui.Indent();
            if (ImGui.Checkbox(GSLoc.Settings.Preferences.PrivacyRadarLabel, ref dtrPrivacyRadar))
            {
                _mainConfig.Config.ShowPrivacyRadar = dtrPrivacyRadar;
                _mainConfig.Save();
            }
            CkGui.DrawHelpText(GSLoc.Settings.Preferences.PrivacyRadarTT);

            if (ImGui.Checkbox(GSLoc.Settings.Preferences.ActionsNotifLabel, ref dtrActionNotifs))
            {
                _mainConfig.Config.ShowActionNotifs = dtrActionNotifs;
                _mainConfig.Save();
            }
            CkGui.DrawHelpText(GSLoc.Settings.Preferences.ActionsNotifTT);

            if (ImGui.Checkbox(GSLoc.Settings.Preferences.VibeStatusLabel, ref dtrVibeStatus))
            {
                _mainConfig.Config.ShowVibeStatus = dtrVibeStatus;
                _mainConfig.Save();
            }
            CkGui.DrawHelpText(GSLoc.Settings.Preferences.VibeStatusTT);
            ImGui.Unindent();
        }

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.ShowVisibleSeparateLabel, ref showVisibleSeparate))
        {
            _mainConfig.Config.ShowVisibleUsersSeparately = showVisibleSeparate;
            _mainConfig.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.ShowVisibleSeparateTT);

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.ShowOfflineSeparateLabel, ref showOfflineSeparate))
        {
            _mainConfig.Config.ShowOfflineUsersSeparately = showOfflineSeparate;
            _mainConfig.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.ShowOfflineSeparateTT);

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.PrefThreeCharaAnonName, ref preferThreeCharaAnonName))
        {
            _mainConfig.Config.PreferThreeCharaAnonName = preferThreeCharaAnonName;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.PrefThreeCharaAnonNameTT);

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.PreferNicknamesLabel, ref preferNicknamesInsteadOfName))
        {
            _mainConfig.Config.PreferNicknamesOverNames = preferNicknamesInsteadOfName;
            _mainConfig.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.PreferNicknamesTT);

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.ShowProfilesLabel, ref showProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _mainConfig.Config.ShowProfiles = showProfiles;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.ShowProfilesTT);

        using (ImRaii.Disabled(!showProfiles))
        {
            ImGui.Indent();
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat(GSLoc.Settings.Preferences.ProfileDelayLabel, ref profileDelay, 0.3f, 5))
            {
                _mainConfig.Config.ProfileDelay = profileDelay;
                _mainConfig.Save();
            }
            CkGui.DrawHelpText(GSLoc.Settings.Preferences.ProfileDelayTT);
            ImGui.Unindent();
        }

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.ContextMenusLabel, ref showContextMenus))
        {
            _mainConfig.Config.ShowContextMenus = showContextMenus;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.ContextMenusTT);

        /* --------------- Separator for moving onto the Notifications Section ----------- */
        ImGui.Separator();
        CkGui.GagspeakBigText(GSLoc.Settings.Preferences.HeaderNotifications);

        var liveGarblerZoneChangeWarn = _mainConfig.Config.LiveGarblerZoneChangeWarn;
        var serverConnectionNotifs = _mainConfig.Config.NotifyForServerConnections;
        var onlineNotifs = _mainConfig.Config.NotifyForOnlinePairs;
        var onlineNotifsNickLimited = _mainConfig.Config.NotifyLimitToNickedPairs;

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.ZoneChangeWarnLabel, ref liveGarblerZoneChangeWarn))
        {
            _mainConfig.Config.LiveGarblerZoneChangeWarn = liveGarblerZoneChangeWarn;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.ZoneChangeWarnTT);

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.ConnectedNotifLabel, ref serverConnectionNotifs))
        {
            _mainConfig.Config.NotifyForServerConnections = serverConnectionNotifs;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.ConnectedNotifTT);

        if (ImGui.Checkbox(GSLoc.Settings.Preferences.OnlineNotifLabel, ref onlineNotifs))
        {
            _mainConfig.Config.NotifyForOnlinePairs = onlineNotifs;
            if (!onlineNotifs) _mainConfig.Config.NotifyLimitToNickedPairs = false;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText(GSLoc.Settings.Preferences.OnlineNotifTT);

        using (ImRaii.Disabled(!onlineNotifs))
        {
            if (ImGui.Checkbox(GSLoc.Settings.Preferences.LimitForNicksLabel, ref onlineNotifsNickLimited))
            {
                _mainConfig.Config.NotifyLimitToNickedPairs = onlineNotifsNickLimited;
                _mainConfig.Save();
            }
            CkGui.DrawHelpText(GSLoc.Settings.Preferences.LimitForNicksTT);
        }

        if(ImGuiUtil.GenericEnumCombo("Info Location##notifInfo", 125f, _mainConfig.Config.InfoNotification, out NotificationLocation newInfo, i => i.ToString()))
        {
            _mainConfig.Config.InfoNotification = newInfo;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText("The location where \"Info\" notifications will display."
                      + Environment.NewLine + "'Nowhere' will not show any Info notifications"
                      + Environment.NewLine + "'Chat' will print Info notifications in chat"
                      + Environment.NewLine + "'Toast' will show Warning toast notifications in the bottom right corner"
                      + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        if (ImGuiUtil.GenericEnumCombo("Warning Location##notifWarn", 125f, _mainConfig.Config.WarningNotification, out NotificationLocation newWarn, i => i.ToString()))
        {
            _mainConfig.Config.WarningNotification = newWarn;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText("The location where \"Warning\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Warning notifications"
                              + Environment.NewLine + "'Chat' will print Warning notifications in chat"
                              + Environment.NewLine + "'Toast' will show Warning toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        if (ImGuiUtil.GenericEnumCombo("Error Location##notifError", 125f, _mainConfig.Config.ErrorNotification, out NotificationLocation newError, i => i.ToString()))
        {
            _mainConfig.Config.ErrorNotification = newError;
            _mainConfig.Save();
        }
        CkGui.DrawHelpText("The location where \"Error\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Error notifications"
                              + Environment.NewLine + "'Chat' will print Error notifications in chat"
                              + Environment.NewLine + "'Toast' will show Error toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");
    }
}
