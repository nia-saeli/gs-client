using GagSpeak.Achievements;
using GagSpeak.PlayerClient;
using GagSpeak.Services.Configs;
using GagSpeak.Services.Mediator;
using GagSpeak.State.Listeners;
using GagSpeak.State.Managers;
using GagSpeak.WebAPI;

namespace GagSpeak.Services;

/// <summary> A class that helps ensure all client data is synced with the currently connected user.
/// <para> The intention here is to make it so that there is no desync with information between logins </para>
/// </summary>
/// <remarks> Helps update config folder locations, update stored data, and update achievement data status. </remarks>
public sealed class ConnectionSyncService : DisposableMediatorSubscriberBase
{
    private readonly GlobalPermissions _globals;
    private readonly GagRestrictionManager _gags;
    private readonly RestrictionManager _restrictions;
    private readonly RestraintManager _restraints;
    private readonly CursedLootManager _cursedLoot;
    private readonly PuppeteerManager _puppeteer;
    private readonly AlarmManager _alarms;
    private readonly TriggerManager _triggers;
    private readonly VisualStateListener _visualState;

    private readonly ConfigFileProvider _fileNames;
    private readonly AchievementManager _achievements;

    public ConnectionSyncService(ILogger<ConnectionSyncService> logger, GagspeakMediator mediator,
        GlobalPermissions globals, GagRestrictionManager gags, RestrictionManager restrictions,
        RestraintManager restraints, CursedLootManager cursedLoot, PuppeteerManager puppeteer,
        AlarmManager alarms, TriggerManager triggers, VisualStateListener visualListener,
        ConfigFileProvider fileNames, AchievementManager achievements)
        : base(logger, mediator)
    {
        _globals = globals;
        _gags = gags;
        _restrictions = restrictions;
        _restraints = restraints;
        _cursedLoot = cursedLoot;
        _puppeteer = puppeteer;
        _alarms = alarms;
        _triggers = triggers;
        _visualState = visualListener;
        _fileNames = fileNames;
        _achievements = achievements;

        Mediator.Subscribe<OnlinePairsLoadedMessage>(this, _ => SetClientDataForProfile());
        Mediator.Subscribe<DalamudLogoutMessage>(this, _ => ClearClientDataForProfile());
    }

    private void ClearClientDataForProfile()
    {
        // Clear the folder paths for the configs, and then set the valid configs variable to false.
        _fileNames.ClearUidConfigs();
    }


    private async void SetClientDataForProfile()
    {
        // if the ConnectionResponse for whatever reason was null, dont process any of this.
        if (MainHub.ConnectionResponse is not { } connectionInfo)
            return;

        // 1. Update the Config File Provider with the current UID.
        Logger.LogInformation("Updating Configs for UID: " + MainHub.UID);
        _fileNames.UpdateConfigs(MainHub.UID);

        // 2. Load in the updated config storages for the profile.
        Logger.LogInformation("Loading in Configs for UID: " + MainHub.UID);
        _gags.Load();
        _restrictions.Load();
        _restraints.Load();
        _cursedLoot.Load();
        _puppeteer.Load();
        _alarms.Load();
        _triggers.Load();

        // 3. Load in the data from the server into our storages.
        Logger.LogInformation("Syncing Data with Connection DTO");

        // temp setup for permissions if they are null. They wont be in full release.
        _globals.ApplyFullDataChange(connectionInfo.GlobalPerms);
        await _visualState.SyncServerData(connectionInfo);

        // 4. Update the achievement manager with the latest UID and the latest data.
        Logger.LogInformation("Loading in Achievement Data for UID: " + MainHub.UID);
        _achievements.OnServerConnection(connectionInfo.UserAchievements);
    }
}
