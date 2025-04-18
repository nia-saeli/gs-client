using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GagSpeak.PlayerData.Data;
using GagSpeak.Services.Mediator;
using GagSpeak.UpdateMonitoring;
using GagspeakAPI.Data;
using GagspeakAPI.Extensions;
using Microsoft.Extensions.Hosting;

namespace GagSpeak.Services.Textures;

// Friendly Reminder, this is a scoped service, and IDalamudTextureWraps will only return values on the framework thread.
// Attempting to use or access this class to obtain information outside the framework draw update thread will result in a null return.
public class CosmeticService : IHostedService, IDisposable
{
    private readonly ILogger<CosmeticService> _logger;
    private readonly OnFrameworkService _frameworkUtils;
    private readonly ITextureProvider _textures;
    private readonly IDalamudPluginInterface _pi;
    public CosmeticService(ILogger<CosmeticService> logger, GagspeakMediator mediator,
        OnFrameworkService frameworkUtils, IDalamudPluginInterface pi, ITextureProvider tp)
    {
        _logger = logger;
        _frameworkUtils = frameworkUtils;
        _textures = tp;
        _pi = pi;
    }

    private Dictionary<string           , IDalamudTextureWrap> InternalCosmeticCache = [];
    public  Dictionary<CoreTexture, IDalamudTextureWrap> CoreTextures    = [];
    public  Dictionary<CoreEmoteTexture , IDalamudTextureWrap> CoreEmoteTextures     = [];

    // MUST ensure ALL images are disposed of or else we will leak a very large amount of memory.
    public void Dispose()
    {
        _logger.LogInformation("GagSpeak Profile Cosmetic Cache Disposing.");
        foreach (var texture in CoreTextures.Values)
            texture?.Dispose();
        foreach (var texture in CoreEmoteTextures.Values)
            texture?.Dispose();
        foreach (var texture in InternalCosmeticCache.Values)
            texture?.Dispose();

        // clear the dictionary, erasing all disposed textures.
        CoreTextures.Clear();
        InternalCosmeticCache.Clear();
    }

    public void LoadAllCoreEmoteTextures()
    {
        foreach (var label in CosmeticLabels.ChatEmoteTextures)
        {
            var key = label.Key;
            var path = label.Value;
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogError("Emote Key: " + key + " Texture Path is Empty.");
                return;
            }

            _logger.LogDebug("Renting Emote image to store in Cache: " + key, LoggerType.Textures);
            if (TryRentImageFromFile(path, out var texture))
                CoreEmoteTextures[key] = texture;
        }
        _logger.LogInformation("GagSpeak Profile Emote Image Cache Fetched all NecessaryImages!", LoggerType.Cosmetics);
    }

    public void LoadAllCoreTextures()
    {
        foreach (var label in CosmeticLabels.NecessaryImages)
        {
            var key = label.Key;
            var path = label.Value;
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogError("Cosmetic Key: " + key + " Texture Path is Empty.");
                return;
            }

            _logger.LogDebug("Renting image to store in Cache: " + key, LoggerType.Textures);
            if(TryRentImageFromFile(path, out var texture))
                CoreTextures[key] = texture;
        }
        _logger.LogInformation("GagSpeak Profile Cosmetic Cache Fetched all NecessaryImages!", LoggerType.Cosmetics);
    }

    public void LoadAllCosmetics()
    {
        // load in all the images to the dictionary by iterating through all public const strings stored in the cosmetic labels
        // and appending them as new texture wraps that should be stored into the cache.
        foreach (var label in CosmeticLabels.CosmeticTextures)
        {
            var key = label.Key;
            var path = label.Value;
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogError("Cosmetic Key: " + key + " Texture Path is Empty.");
                return;
            }

            _logger.LogDebug("Renting image to store in Cache: " + key, LoggerType.Textures);
            if (TryRentImageFromFile(path, out var texture))
                InternalCosmeticCache[key] = texture;
        }
        _logger.LogInformation("GagSpeak Profile Cosmetic Cache Fetched all Cosmetic Images!", LoggerType.Cosmetics);
    }

    public IDalamudTextureWrap? GagImageFromType(GagType gagType)
    {
        var stringToSearch = $"GagImages\\{gagType.GagName()}.png";
        return _textures.GetFromFile(Path.Combine(_pi.AssemblyLocation.DirectoryName!, "Assets", stringToSearch)).GetWrapOrDefault();
    }

    public IDalamudTextureWrap? PadlockImageFromType(Padlocks padlock)
    {
        var stringToSearch = $"PadlockImages\\{padlock}.png";
        return _textures.GetFromFile(Path.Combine(_pi.AssemblyLocation.DirectoryName!, "Assets", stringToSearch)).GetWrapOrDefault();
    }

    /// <summary>
    /// Grabs the texture from GagSpeak Cosmetic Cache Service, if it exists.
    /// </summary>
    /// <returns>True if the texture is valid, false otherwise. If returning false, the wrap WILL BE NULL. </returns>
    public bool TryGetBackground(ProfileComponent section, ProfileStyleBG style, out IDalamudTextureWrap value)
    {
        // See if the item exists in our GagSpeak Cache Service.
        if(InternalCosmeticCache.TryGetValue(section.ToString() + "_Background_" + style.ToString(), out var texture))
        {
            value = texture;
            return true;
        }
        // not valid, so return false.
        value = null!;
        return false;
    }

    /// <summary>
    /// Grabs the texture from GagSpeak Cosmetic Cache Service, if it exists.
    /// </summary>
    /// <returns>True if the texture is valid, false otherwise. If returning false, the wrap WILL BE NULL. </returns>
    public bool TryGetBorder(ProfileComponent section, ProfileStyleBorder style, out IDalamudTextureWrap value)
    {
        if(InternalCosmeticCache.TryGetValue(section.ToString() + "_Border_" + style.ToString(), out var texture))
        {
            value = texture;
            return true;
        }
        value = null!;
        return false;
    }

    /// <summary>
    /// Grabs the texture from GagSpeak Cosmetic Cache Service, if it exists.
    /// </summary>
    /// <returns>True if the texture is valid, false otherwise. If returning false, the wrap WILL BE NULL. </returns>
    public bool TryGetOverlay(ProfileComponent section, ProfileStyleOverlay style, out IDalamudTextureWrap value)
    {
        if(InternalCosmeticCache.TryGetValue(section.ToString() + "_Overlay_" + style.ToString(), out var texture))
        {
            value = texture;
            return true;
        }
        value = null!;
        return false;
    }

    public (IDalamudTextureWrap? SupporterWrap, string Tooltip) GetSupporterInfo(UserData userData)
    {
        IDalamudTextureWrap? supporterWrap = null;
        var tooltipString = string.Empty;

        switch (userData.Tier)
        {
            case CkSupporterTier.ServerBooster:
                supporterWrap = CoreTextures[CoreTexture.TierBoosterIcon];
                tooltipString = userData.AliasOrUID + " is supporting the discord with a server Boost!";
                break;

            case CkSupporterTier.IllustriousSupporter:
                supporterWrap = CoreTextures[CoreTexture.Tier1Icon];
                tooltipString = userData.AliasOrUID + " is supporting CK as an Illustrious Supporter";
                break;

            case CkSupporterTier.EsteemedPatron:
                supporterWrap = CoreTextures[CoreTexture.Tier2Icon];
                tooltipString = userData.AliasOrUID + " is supporting CK as an Esteemed Patron";
                break;

            case CkSupporterTier.DistinguishedConnoisseur:
                supporterWrap = CoreTextures[CoreTexture.Tier3Icon];
                tooltipString = userData.AliasOrUID + " is supporting CK as a Distinguished Connoisseur";
                break;

            case CkSupporterTier.KinkporiumMistress:
                supporterWrap = CoreTextures[CoreTexture.Tier4Icon];
                tooltipString = userData.AliasOrUID + " is the Shop Mistress of CK, and the Dev of GagSpeak.";
                break;

            default:
                tooltipString = userData.AliasOrUID + " has an unknown supporter tier.";
                break;
        }

        return (supporterWrap, tooltipString);
    }


    public IDalamudTextureWrap GetImageFromAssetsFolder(string path)
        => _textures.GetFromFile(Path.Combine(_pi.AssemblyLocation.DirectoryName!, "Assets", path)).GetWrapOrEmpty();
    public IDalamudTextureWrap? GetImageFromThumbnailPath(string path)
        => _textures.GetFromFile(Path.Combine(_pi.AssemblyLocation.DirectoryName!, "Assets\\Thumbnails", path)).GetWrapOrDefault();
    public IDalamudTextureWrap GetProfilePicture(byte[] imageData)
        => _textures.CreateFromImageAsync(imageData).Result;

    public IDalamudTextureWrap? GetImageFromBytes(byte[] imageData)
    {
        try
        {
            return _textures.CreateFromImageAsync(imageData).Result;
        }
        catch (Exception e)
        {
            _logger.LogError("Failed to load image from bytes: " + e);
            return null;
        }
    }

    private bool TryRentImageFromFile(string path, out IDalamudTextureWrap fileTexture)
    {
        try
        {
            var image = _textures.GetFromFile(Path.Combine(_pi.AssemblyLocation.DirectoryName!, "Assets", path)).RentAsync().Result;
            fileTexture = image;
            return true;
        }
        catch (Exception)
        {
            // TODO: Remove surpression once we have defined proper images.
            //_logger.LogWarning($"Failed to load texture from path: {path}");
            fileTexture = null!;
            return false;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GagSpeak Profile Cosmetic Cache Started.");
        LoadAllCoreTextures();
        LoadAllCoreEmoteTextures();
        LoadAllCosmetics();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GagSpeak Profile Cosmetic Cache Stopped.");
        return Task.CompletedTask;
    }

}
