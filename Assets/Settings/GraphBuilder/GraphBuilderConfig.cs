using System;
using UnityEngine;

[CreateAssetMenu(fileName = "GraphBuilderConfig", menuName = "Scriptable Objects/GraphBuilderConfig")]
public class GraphBuilderConfig : ScriptableObject
{
    public MitchelSamplerSettings mitchelSamplerSettings;
}

[Serializable]
public class MitchelSamplerSettings
{
    public int desiredNodesCount = 10;
    public int sampleCount = 10;
    public int sampleGrowth = 1;
        
    [Header("Height")]
    public float heightAmplitude = 20f;
    public float noiseScale = 0.1f;
}
