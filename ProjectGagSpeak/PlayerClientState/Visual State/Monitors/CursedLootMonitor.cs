using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GagSpeak.CkCommons.Helpers;
using GagSpeak.PlayerData.Data;
using GagSpeak.PlayerData.Pairs;
using GagSpeak.PlayerState.Models;
using GagSpeak.Services.Configs;
using GagSpeak.Services.Mediator;
using GagSpeak.UpdateMonitoring;
using GagSpeak.Utils;
using GagSpeak.WebAPI;
using GagspeakAPI.Data;
using GagspeakAPI.Extensions;
using GagspeakAPI.Hub;
using GagspeakAPI.Network;
using GameObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;

namespace GagSpeak.PlayerState.Visual;

/// <summary> Monitored for interactions with dungeon coffers, and rolls the dice to apply cursed loot. </summary>
/// <remarks> Monitor classes are self-contained and allowed to use managers or appliers. </remarks>
public class CursedLootMonitor : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly GlobalData _globals;
    private readonly GagspeakConfigService _mainConfig;
    private readonly PairManager _pairs;
    private readonly GagRestrictionManager _gags;
    private readonly RestrictionManager _restrictions;
    private readonly CursedLootManager _manager;
    private readonly ClientMonitor _clientMonitor;
    private readonly OnFrameworkService _frameworkUtils;

    // SHOULD fire whenever we interact with any object thing.
    internal Hook<TargetSystem.Delegates.InteractWithObject> ItemInteractedHook;

    public CursedLootMonitor(ILogger<CursedLootMonitor> logger, GagspeakMediator mediator,
        MainHub hub, GlobalData globals, GagspeakConfigService config, PairManager pairs,
        GagRestrictionManager gags, CursedLootManager manager, ClientMonitor client,
        OnFrameworkService frameworkUtils, IGameInteropProvider interop) : base(logger, mediator)
    {
        _hub = hub;
        _globals = globals;
        _mainConfig = config;
        _pairs = pairs;
        _gags = gags;
        _manager = manager;
        _clientMonitor = client;
        _frameworkUtils = frameworkUtils;

        unsafe
        {
            ItemInteractedHook = interop.HookFromAddress<TargetSystem.Delegates.InteractWithObject>((nint)TargetSystem.MemberFunctionPointers.InteractWithObject, ItemInteractedDetour);
            ItemInteractedHook.Enable();
        }
    }


    /// <summary> Stores last interacted chestId so we dont keep spam opening the same chest. </summary>
    /// <remarks> This is static so we can send it to mediator calls and update it. </remarks>
    private static ulong LastOpenedTreasureId = 0;
    private Task? _openTreasureTask = null;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        ItemInteractedHook?.Disable();
        ItemInteractedHook?.Dispose();
        ItemInteractedHook = null!;
    }

    private unsafe ulong ItemInteractedDetour(TargetSystem* thisPtr, GameObject* obj, bool checkLineOfSight)
    {
        try
        {
            Logger.LogTrace("Object ID: " + obj->GetGameObjectId().ObjectId);
            Logger.LogTrace("Object Kind: " + obj->ObjectKind);
            Logger.LogTrace("Object SubKind: " + obj->SubKind);
            Logger.LogTrace("Object Name: " + obj->NameString.ToString());
            if (obj->EventHandler is not null)
            {
                Logger.LogTrace("Object EventHandler ID: " + obj->EventHandler->Info.EventId.Id);
                Logger.LogTrace("Object EventHandler Entry ID: " + obj->EventHandler->Info.EventId.EntryId);
                Logger.LogTrace("Object EventHandler Content Id: " + obj->EventHandler->Info.EventId.ContentId);
            }

            // dont bother if cursed dungeon loot isnt enabled, or if there are no inactive items in the pool.
            if (!_mainConfig.Config.CursedLootPanel || !_manager.Storage.InactiveItemsInPool.Any() || !MainHub.IsServerAlive)
                return ItemInteractedHook.Original(thisPtr, obj, checkLineOfSight);

            // if the object is not a treasure of event object dont worry about processing it.
            if (obj->ObjectKind is not GameObjectKind.Treasure && !AchievementHelpers.IsDeepDungeonCoffer(obj))
            {
                Logger.LogTrace("Interacted with GameObject that was not a Treasure Chest or Deep Dungeon Coffer.", LoggerType.CursedLoot);
                return ItemInteractedHook.Original(thisPtr, obj, checkLineOfSight);
            }

            // if we the item interacted with is the same as the last opened chest, return.
            if (obj->GetGameObjectId().ObjectId == LastOpenedTreasureId)
            {
                Logger.LogTrace("Interacted with GameObject that was the last opened chest.", LoggerType.CursedLoot);
                return ItemInteractedHook.Original(thisPtr, obj, checkLineOfSight);
            }

            // Dont process if our current treasure task is running
            if (_openTreasureTask != null && !_openTreasureTask.IsCompleted)
                return ItemInteractedHook.Original(thisPtr, obj, checkLineOfSight);

            // The chest is a valid chest at this point, but we need to determine what type it is.
            if (AchievementHelpers.IsDeepDungeonCoffer(obj))
            {
                // its a Deep Dungeon Coffer.
                Logger.LogTrace("Attempting to open Deep Dungeon coffer, checking on next second", LoggerType.CursedLoot);
                _openTreasureTask = CheckDeepDungeonCoffers(obj->GetGameObjectId().ObjectId);
            }
            else
            {
                // It's a normal Coffer.
                // Make sure we are opening it. If we were not the first, it will exist in here.
                if (_clientMonitor.PartySize is not 1)
                {
                    foreach (var item in Loot.Instance()->Items)
                    {
                        if (item.ChestObjectId == obj->GetGameObjectId().ObjectId)
                        {
                            Logger.LogTrace("This treasure was already opened!", LoggerType.CursedLoot);
                            return ItemInteractedHook.Original(thisPtr, obj, checkLineOfSight);
                        }
                    }
                }

                Logger.LogTrace("Attempting to open coffer, checking on next second", LoggerType.CursedLoot);
                _openTreasureTask = CheckLootTables(obj->GetGameObjectId().ObjectId);
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to log object information.");
        }
        return ItemInteractedHook.Original(thisPtr, obj, checkLineOfSight);
    }

    private async Task CheckLootTables(ulong objectInteractedWith)
    {
        try
        {
            await Task.Delay(1000);
            Logger.LogInformation("Checking tables!", LoggerType.CursedLoot);
            await _frameworkUtils.RunOnFrameworkThread(() =>
            {
                unsafe
                {
                    var valid = _clientMonitor.InSoloParty ? true : Loot.Instance()->Items.ToArray().Any(x => x.ChestObjectId == objectInteractedWith);
                    if (valid)
                    {
                        Logger.LogTrace("we satisfy valid condition.", LoggerType.CursedLoot);
                        if (objectInteractedWith != LastOpenedTreasureId)
                        {
                            Logger.LogTrace("we just attempted to open a dungeon chest.", LoggerType.CursedLoot);
                            LastOpenedTreasureId = objectInteractedWith;
                            ApplyCursedLoot().ConfigureAwait(false);
                            return;
                        }
                    }
                    else
                    {
                        Logger.LogTrace("No loot items are the nearest treasure", LoggerType.CursedLoot);
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _openTreasureTask = null;
        }
    }

    private async Task CheckDeepDungeonCoffers(ulong objectInteractedWith)
    {
        try
        {
            await Task.Delay(1000);
            Logger.LogInformation("Checking tables!", LoggerType.CursedLoot);
            await _frameworkUtils.RunOnFrameworkThread(() =>
            {
                unsafe
                {
                    if (objectInteractedWith != LastOpenedTreasureId)
                    {
                        Logger.LogTrace("we just attempted to open a deep dungeon chest.", LoggerType.CursedLoot);
                        LastOpenedTreasureId = objectInteractedWith;
                        ApplyCursedLoot().ConfigureAwait(false);
                        return;
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _openTreasureTask = null;
        }
    }

    private async Task ApplyCursedLoot()
    {
        // get the percent change to apply
        var percentChange = _manager.LockChance;
        var randomValue = new Random().Next(0, 101);
        if (_manager.LockChance <= 0 || randomValue > percentChange)
            return;

        // aquire the items we can apply to.
        var inactiveInPoolCnt = _manager.Storage.InactiveItemsInPool.Count;
        if (inactiveInPoolCnt <= 0)
            return;

        // Attempt a first application with gags in account.
        var randomIndex = new Random().Next(0, inactiveInPoolCnt);
        var chosenItem = _manager.Storage.InactiveItemsInPool[randomIndex];
        var lockTimeGag = Generators.GetRandomTimeSpan(_manager.LockRangeLower, _manager.LockRangeUpper);
        if (await HandleGagApplication(chosenItem, lockTimeGag))
        {
            _manager.ActivateCursedItem(chosenItem, DateTimeOffset.UtcNow.Add(lockTimeGag));
            Logger.LogInformation("Cursed Loot Applied!", LoggerType.CursedLoot);
            UnlocksEventManager.AchievementEvent(UnlocksEvent.CursedDungeonLootFound);
            return;
        }

        // gag failed to apply, now we need to apply a valid restriction. Restriction must be unoccupied.
        var itemsToRoll = _manager.Storage.InactiveItemsInPool
            .Where(item => item.RestrictionRef is not GarblerRestriction 
                && _restrictions.OccupiedRestrictions.Any(x => x.Item.Identifier == item.Identifier));
        // If no items, abort.
        if (!itemsToRoll.Any())
        {
            Logger.LogWarning("No items to apply restrictions to. Skipping.");
            return;
        }

        // Roll for the chosen item.
        randomIndex = new Random().Next(0, itemsToRoll.Count());
        chosenItem = itemsToRoll.ElementAt(randomIndex);
        if (await HandleRestrictionApplication(chosenItem, lockTimeGag))
        {
            _manager.ActivateCursedItem(chosenItem, DateTimeOffset.UtcNow.Add(lockTimeGag));
            Logger.LogInformation("Cursed Loot Applied!", LoggerType.CursedLoot);
            UnlocksEventManager.AchievementEvent(UnlocksEvent.CursedDungeonLootFound);
            return;
        }
    }

    private async Task<bool> HandleGagApplication(CursedItem item, TimeSpan lockTime)
    {
        if (_gags.ServerGagData is not { } gagData)
            return false;

        var Idx = gagData.FindFirstUnused();
        if (Idx is - 1 || item.RestrictionRef is not GarblerRestriction gag)
            return false;

        // Apply the gag restriction to that player.
        Logger.LogInformation("Applying a cursed Gag Item (" + gag.GagType + ") to layer " + Idx, LoggerType.CursedLoot);
        var interactedItem = new LightCursedItem(item.Identifier, item.Label, gag.GagType, Guid.Empty, DateTimeOffset.UtcNow.Add(lockTime));
        var newInfo = new PushClientCursedLootUpdate(_pairs.GetOnlineUserDatas(), _manager.Storage.ActiveIds, interactedItem);

        var result = await _hub.UserPushDataCursedLoot(newInfo);
        if (result.ErrorCode is GagSpeakApiEc.Success)
        {
            Logger.LogInformation($"Cursed Loot Applied & Locked!", LoggerType.CursedLoot);
            var message = new SeStringBuilder().AddItalics("As the coffer opens, cursed loot spills forth, silencing your mouth with a Gag now strapped on tight!").BuiltString;
            Mediator.Publish(new NotifyChatMessage(message, NotificationType.Error));

            if (_globals.GlobalPerms is not null && _globals.GlobalPerms.ChatGarblerActive)
                Mediator.Publish(new NotificationMessage("Chat Garbler", "LiveChatGarbler Is Active and you were just Gagged! Be cautious of chatting around strangers!", NotificationType.Warning));

            // Update the cursed items offset time.
            if(_manager.Storage.TryGetLoot(item.Identifier, out var loot))
                loot.ReleaseTime = DateTimeOffset.UtcNow.Add(lockTime);

            return true;
        }
        else
        {
            Logger.LogError("Failed to apply gag restriction to player. Error Code: " + result.ErrorCode);
            return false;
        }
    }

    private async Task<bool> HandleRestrictionApplication(CursedItem cursedItem, TimeSpan lockTime)
    {
        if (_restrictions.AppliedRestrictions is not { } restrictionData)
            return false;

        // If the attached restriction item is already in the container of active restrictions, return false.
        if(restrictionData.Any(x => x.Identifier == cursedItem.Identifier))
            return false;

        // Get the first unused restriction index.
        if(cursedItem.RestrictionRef is not IRestrictionItem restriction)
            return false;

        // Apply the restriction to that player.
        Logger.LogInformation("Applying a cursed Item (" + cursedItem.Label + ") to you!", LoggerType.CursedLoot);
        var item = new LightCursedItem(cursedItem.Identifier, cursedItem.Label, GagType.None, restriction.Identifier, DateTimeOffset.UtcNow.Add(lockTime));
        var newInfo = new PushClientCursedLootUpdate(_pairs.GetOnlineUserDatas(), _manager.Storage.ActiveIds, item);

        var result = await _hub.UserPushDataCursedLoot(newInfo);
        if (result.ErrorCode is GagSpeakApiEc.Success)
        {
            Mediator.Publish(new NotifyChatMessage(new SeStringBuilder().AddItalics("As the coffer opens, cursed loot spills " +
                "forth, binding you in an inescapable restraint!").BuiltString, NotificationType.Error));

            // Update the items release time if successful.
            if (_manager.Storage.TryGetLoot(cursedItem.Identifier, out var loot))
                loot.ReleaseTime = DateTimeOffset.UtcNow.Add(lockTime);

            return true;
        }
        else
        {
            Logger.LogError("Failed to apply restriction to player. Error Code: " + result.ErrorCode);
            return false;
        }
    }


}
