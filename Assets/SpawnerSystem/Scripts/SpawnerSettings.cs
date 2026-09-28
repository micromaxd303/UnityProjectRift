
using UnityEngine;

[CreateAssetMenu(fileName = "SpawnerSettings", menuName = "Spawner/Settings")]
public class SpawnerSettings : ScriptableObject
{
    [Header("Количество врагов")]
    [Min(1)]
    public int totalEnemies = 15;

    [Min(1)]
    public int maxAliveEnemies = 5;

    [Header("Радиус")]
    [Min(0.5f)]
    public float spawnRadius = 10f;

    [Header("Спавн")]
    public GameObject enemyPrefab;

    [Min(1f)]
    public float raycastHeight = 20f;

    [Header("Поверхность")]
    public LayerMask groundMask;

    [Header("Безопасность")]
    [Min(0.1f)]
    public float minDistanceBetweenEnemies = 1.5f;
}

