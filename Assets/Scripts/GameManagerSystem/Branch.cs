using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "Branch", menuName = "Scriptable Objects/Branch")]
public class Branch : ScriptableObject
{
    [Tooltip("Активна ли ветка")]
    public bool isEnable = false;

    [Tooltip("Название ветки")]
    public new string name;

    [Tooltip("Описание ветик")]
    public string description;

    [Tooltip("Количество уровней в ветке")]
    public int levelCount;

    [Tooltip("Название сцены для загрузки")]
    public string sceneName;

    [Tooltip("Список всех возможных миссий на ветке")]
    public List<Mission> mission;

    [System.NonSerialized]
    public LevelController levelController;
}
