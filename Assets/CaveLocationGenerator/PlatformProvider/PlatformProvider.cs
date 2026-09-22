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
    
    public MarchingCubesConfig.GraphicsAPIType GetCurrentGraphicsAPIType()
    {
        if (ApiMapping.TryGetValue(GetGraphicsAPI(), out var apiType))
            return apiType;

        throw new InvalidOperationException($"Unsupported graphics API: {GetGraphicsAPI()}");
    }

    private static readonly Dictionary<GraphicsDeviceType, MarchingCubesConfig.GraphicsAPIType> ApiMapping = new()
    {
        [GraphicsDeviceType.Direct3D11] = MarchingCubesConfig.GraphicsAPIType.DX11,
        [GraphicsDeviceType.Direct3D12] = MarchingCubesConfig.GraphicsAPIType.DX12,
        [GraphicsDeviceType.Vulkan] = MarchingCubesConfig.GraphicsAPIType.Vulkan,
        [GraphicsDeviceType.Metal] = MarchingCubesConfig.GraphicsAPIType.Metal,
    };
}
