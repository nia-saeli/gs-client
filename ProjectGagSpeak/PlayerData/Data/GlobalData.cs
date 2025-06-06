using GagSpeak.PlayerData.Pairs;
using GagSpeak.Services.Mediator;
using GagSpeak.WebAPI;
using GagspeakAPI.Data.Permissions;
using GagspeakAPI.Extensions;
using GagspeakAPI.Network;

namespace GagSpeak.PlayerData.Data;

/// <summary> Handles the Kinkster Requests, Global Permissions, and Global Data Updates. </summary>
public sealed class GlobalData : DisposableMediatorSubscriberBase
{
    public GlobalData(ILogger<GlobalData> logger, GagspeakMediator mediator) : base(logger, mediator)
    {
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => { GlobalPerms = null; });
    }

    public bool GlobalsValid => GlobalPerms is not null;
    public GlobalPerms? GlobalPerms { get; set; } = null;
    public HashSet<KinksterRequestEntry> CurrentRequests { get; set; } = new();
    public HashSet<KinksterRequestEntry> OutgoingRequests => CurrentRequests.Where(x => x.User.UID == MainHub.UID).ToHashSet();
    public HashSet<KinksterRequestEntry> IncomingRequests => CurrentRequests.Where(x => x.Target.UID == MainHub.UID).ToHashSet();

    public void AddPairRequest(KinksterRequestEntry dto)
    {
        CurrentRequests.Add(dto);
        Logger.LogInformation("New pair request added!", LoggerType.PairManagement);
        Mediator.Publish(new RefreshUiMessage());
    }

    public void RemovePairRequest(KinksterRequestEntry dto)
    {
        var res = CurrentRequests.RemoveWhere(x => x.User.UID == dto.User.UID && x.Target.UID == dto.Target.UID);
        Logger.LogInformation("Removed " + res + " pair requests.", LoggerType.PairManagement);
        Mediator.Publish(new RefreshUiMessage());
    }

    /// <summary> For permission updates done by ourselves. </summary>
    public void ChangeGlobalPermission(SingleChangeGlobal dto)
    {
        var changeType = UpdateGlobalPermission(dto);
        if (changeType is InteractionType.None)
            return;

        // if one did occur, we can log it.
        Mediator.Publish(new EventMessage(new("Self-Update", MainHub.UID, changeType, $"{dto.NewPerm.Key.ToString()} changed to [{dto.NewPerm.Value.ToString()}]")));

        // If the change was a hardcore action, log a warning.
        if(changeType is not InteractionType.None && changeType is not InteractionType.ForcedPermChange)
            Logger.LogWarning($"Hardcore action [{changeType.ToString()}] has changed, but should never happen!");
    }

    /// <summary> For permission updates done by another user. </summary>
    public void ChangeGlobalPermission(SingleChangeGlobal dto, Pair enactor)
    {
        var changeType = UpdateGlobalPermission(dto);
        if (changeType is InteractionType.None)
            return;

        if (changeType is InteractionType.ForcedPermChange)
        {
            Mediator.Publish(new EventMessage(new(enactor.GetNickAliasOrUid(), dto.Enactor.UID, InteractionType.ForcedPermChange,
                $"{dto.NewPerm.Key.ToString()} changed to [{dto.NewPerm.Value.ToString()}]")));
        }
        else
        {
            // would be a hardcore permission change in this case.
            Mediator.Publish(new EventMessage(new(enactor.GetNickAliasOrUid(), dto.Enactor.UID, changeType, $"{changeType.ToString()} changed to [{dto.NewPerm.Value.ToString()}]")));
            var newState = string.IsNullOrEmpty((string)dto.NewPerm.Value) ? NewState.Disabled : NewState.Enabled;
            Mediator.Publish(new HardcoreActionMessage(changeType, newState));
            UnlocksEventManager.AchievementEvent(UnlocksEvent.HardcoreAction, changeType, newState, dto.Enactor.UID, MainHub.UID);
        }
    }

    /// <summary> Updates our global permissions. </summary>
    /// <returns> The type of interaction performed by the update. </returns>
    private InteractionType UpdateGlobalPermission(SingleChangeGlobal changeDto)
    {
        if (GlobalPerms is null)
            return InteractionType.None;

        var propertyName = changeDto.NewPerm.Key;
        var newValue = changeDto.NewPerm.Value;
        var propertyInfo = typeof(GlobalPerms).GetProperty(propertyName);

        if (propertyInfo is null || !propertyInfo.CanWrite)
        {
            Logger.LogError($"Property '{propertyName}' not found or cannot be updated.");
            return InteractionType.None;
        }

        // Get the changed type.
        var interactedType = GlobalPerms.PermChangeType(propertyName, newValue?.ToString() ?? string.Empty);

        // Special conversions
        var convertedValue = propertyInfo.PropertyType switch
        {
            Type t when t.IsEnum =>
                newValue?.GetType() == Enum.GetUnderlyingType(t) 
                    ? Enum.ToObject(t, newValue)
                    : Convert.ChangeType(newValue, t), // If newValue type matches enum underlying type, convert it directly.
            Type t when t == typeof(TimeSpan) && newValue is ulong u => TimeSpan.FromTicks((long)u),
            Type t when t == typeof(char) && newValue is byte b => Convert.ToChar(b),
            _ => Convert.ChangeType(newValue, propertyInfo.PropertyType)
        };

        // Update value.
        propertyInfo.SetValue(GlobalPerms, convertedValue);
        return interactedType;
    }
}
