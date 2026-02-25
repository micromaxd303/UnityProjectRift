using UnityEngine;

[CreateAssetMenu(fileName = "Mission", menuName = "Scriptable Objects/Mission")]
public class Mission : ScriptableObject
{
    [Tooltip("Название миссии")]
    public new string name;

    [Tooltip("Описание миссии")]
    public string description;

    [Tooltip("Тип миссии")]
    public Type type;

    [System.NonSerialized]
    public bool isComplete;

    public enum Type { Main, Additional, Unlocking }
}
