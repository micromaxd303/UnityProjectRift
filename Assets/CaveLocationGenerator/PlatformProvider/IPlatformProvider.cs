using UnityEngine;
using UnityEngine.Rendering;

public interface IPlatformProvider
{
    RuntimePlatform GetPlatform();
    GraphicsDeviceType GetGraphicsAPI();
    bool SupportsComputeShader();
    bool SupportsJobSystem();
    MarchingCubesConfig.GraphicsAPIType GetCurrentGraphicsAPIType();
}