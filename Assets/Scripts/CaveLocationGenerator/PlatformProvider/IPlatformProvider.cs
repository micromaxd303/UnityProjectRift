using UnityEngine;
using UnityEngine.Rendering;

public interface IPlatformProvider
{
    RuntimePlatform GetPlatform();
    GraphicsDeviceType GetGraphicsAPI();
    bool SupportsComputeShader();
    
    bool SupportsJobSystem();
    bool IsGraphicsAPISupported(MarchingCubesConfig.GraphicsAPIType apiType);
}