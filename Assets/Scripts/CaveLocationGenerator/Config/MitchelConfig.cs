using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MitchelConfig", menuName = "Scriptable Objects/MitchelConfig")]
public class MitchelConfig : ScriptableObject
{
    [Header("Количество точек")] 
    public int nodeCount;


    [Header("Минимальная дистанция")] 
    public float rMin = 6f;

    [Header("Качество выборки")] 
    public int candidatesPerPoint = 20;
    public int growPerPoint = 1;
    public int maxExtraBatches = 6;

    [Header("Настройка высоты (Y)")] 
    public float heightAmplitude = 3f;

    [Header("Якорные точки")] public List<GraphNode> anchorNodes;
}
