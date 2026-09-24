using UnityEngine;

public static class GraphBuilder
{
    public static void BuildGraph(MarchingCubesConfig MCconfig, MitchelConfig MITConfig, Vector3 worldCenter)
    {
        var nodes = MitchelSampler.Generate(MITConfig, MCconfig.TotalWorldSize, worldCenter);
        
        CaveGraphDebug.Show(nodes);
        
    }
}

