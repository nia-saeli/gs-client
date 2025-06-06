using GagSpeak.Interop.Ipc;
using GagSpeak.PlayerData.Handlers;
using GagSpeak.Services.Mediator;
using GagSpeak.UpdateMonitoring;
using GagspeakAPI.Network;
using Microsoft.Extensions.Hosting;

namespace GagSpeak.PlayerData.Factories;

public class PairHandlerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly GagspeakMediator _mediator;
    private readonly OnFrameworkService _frameworkUtils;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IpcManager _ipcManager;
    public PairHandlerFactory(ILoggerFactory loggerFactory, GagspeakMediator mediator,
        GameObjectHandlerFactory objFactory, IpcManager ipcManager,
        OnFrameworkService frameworkUtils, IHostApplicationLifetime appLife)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _gameObjectHandlerFactory = objFactory;
        _ipcManager = ipcManager;
        _frameworkUtils = frameworkUtils;
        _hostApplicationLifetime = appLife;
    }

    /// <summary> This create method in the pair handler factory will create a new pair handler object.</summary>
    /// <param name="OnlineKinkster">The online user to create a pair handler for</param>
    /// <returns> A new PairHandler object </returns>
    public PairHandler Create(OnlineKinkster OnlineKinkster)
    {
        return new PairHandler(OnlineKinkster, _loggerFactory.CreateLogger<PairHandler>(), _mediator,
            _gameObjectHandlerFactory, _ipcManager, _frameworkUtils, _hostApplicationLifetime);
    }
}
