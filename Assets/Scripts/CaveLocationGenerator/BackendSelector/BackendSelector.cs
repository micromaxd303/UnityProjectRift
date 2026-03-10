using System;

public class BackendSelector
{
    private readonly IPlatformProvider _platformProvider;
    
    public BackendSelector(IPlatformProvider platformProvider)
    {
        _platformProvider = platformProvider;
    }
    
    public BackendRecommendation Recommend(MarchingCubesConfig config)
    {
        var (backend, reason) = ResolveBackend(config);

        var api = backend == MarchingCubesConfig.BackendType.ComputeShader
            ? ResolveGraphicsAPI(config)
            : MarchingCubesConfig.GraphicsAPIType.Auto;
        
        return new BackendRecommendation(backend, api, reason);
    }

    private (MarchingCubesConfig.BackendType backendType, string reason) ResolveBackend(MarchingCubesConfig config)
    {
        if(config.PreferredBackend == MarchingCubesConfig.BackendType.JobSystem)
            return (MarchingCubesConfig.BackendType.JobSystem, "Job System is user choice");
        
        if(!_platformProvider.SupportsComputeShader())
            return (MarchingCubesConfig.BackendType.JobSystem, "Compute shader not supported");

        if (config.PreferredBackend == MarchingCubesConfig.BackendType.Auto)
        {
            if (config.TotalChunkVoxels <= config.RecommendationThreshold)
                return (MarchingCubesConfig.BackendType.JobSystem, "Compute shader not effective at small sizes");

            return (MarchingCubesConfig.BackendType.ComputeShader, "Balance choice");
        }

        return (MarchingCubesConfig.BackendType.ComputeShader, "Compute shader is user choice");

    }

    private MarchingCubesConfig.GraphicsAPIType ResolveGraphicsAPI(MarchingCubesConfig config)
    {
        if (config.PreferredGraphicsAPI != MarchingCubesConfig.GraphicsAPIType.Auto // Проверяем сначала выбранный
            && _platformProvider.IsGraphicsAPISupported(config.PreferredGraphicsAPI))
            return config.PreferredGraphicsAPI;

        foreach (MarchingCubesConfig.GraphicsAPIType api in Enum.GetValues(typeof(MarchingCubesConfig.GraphicsAPIType))) // Ищем первый подходящий
        {
            if (api == MarchingCubesConfig.GraphicsAPIType.Auto)
                continue;

            if (_platformProvider.IsGraphicsAPISupported(api))
                return api;
        }
        
        throw new InvalidOperationException("No supported graphics API found."); 
    }
}

public readonly struct BackendRecommendation
{
    public readonly MarchingCubesConfig.BackendType BackendType;
    public readonly MarchingCubesConfig.GraphicsAPIType GraphicsAPI;
    public readonly string Reason;
    
    public BackendRecommendation(MarchingCubesConfig.BackendType backendType, MarchingCubesConfig.GraphicsAPIType graphicsAPI, string reason)
    {
        BackendType = backendType;
        GraphicsAPI = graphicsAPI;
        Reason = reason;
    }
}
