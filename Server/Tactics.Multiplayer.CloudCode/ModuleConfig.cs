using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;

namespace Tactics.Multiplayer.CloudCode;

public sealed class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config) => config.AddGameApiClient();
}
