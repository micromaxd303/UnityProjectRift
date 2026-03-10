using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PlatformProvider : IPlatformProvider
{
    public RuntimePlatform GetPlatform()
    {
        return Application.platform;
    }
    public GraphicsDeviceType GetGraphicsAPI()
    {
        return SystemInfo.graphicsDeviceType;
    }
    public bool SupportsComputeShader()
    {
        return SystemInfo.supportsComputeShaders;
    }

    public bool SupportsJobSystem()
    {
        return true;
    }
    
    public bool IsGraphicsAPISupported(MarchingCubesConfig.GraphicsAPIType apiType)
    {
        return ApiChecks.TryGetValue(apiType, out var check) && check(GetGraphicsAPI(), GetPlatform());
    }

    private static readonly Dictionary<MarchingCubesConfig.GraphicsAPIType, Func<GraphicsDeviceType, RuntimePlatform, bool>>
        ApiChecks = new()
        {
            [MarchingCubesConfig.GraphicsAPIType.DX11] = (api, p) =>
                api == GraphicsDeviceType.Direct3D11 &&
                p is RuntimePlatform.WindowsPlayer or  RuntimePlatform.WindowsEditor,
            
            [MarchingCubesConfig.GraphicsAPIType.DX12] = (api, p) =>
                api == GraphicsDeviceType.Direct3D12 &&
                p is RuntimePlatform.WindowsPlayer or  RuntimePlatform.WindowsEditor,
            
            [MarchingCubesConfig.GraphicsAPIType.Vulkan] = (api, p) =>
                api == GraphicsDeviceType.Vulkan 
                && p is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor 
                    or RuntimePlatform.LinuxPlayer or RuntimePlatform.LinuxEditor,
        };
}
