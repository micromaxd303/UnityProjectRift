using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemySpawner : MonoBehaviour, IDamageable
{
    [Header("Prefab врага")]
    public GameObject prefabToSpawn;

    [Header("Волны")]
    public int waveCount = 3;
    public int enemiesPerWave = 5;

    [Header("Настройки спавна")]
    public float spawnRadius = 10f;

    [Header("Poisson Disk")]
    public float minDistanceBetweenObjects = 2f;
    public int maxPoissonAttempts = 30;

    [Header("Raycast")]
    public LayerMask groundMask;
    public float raycastHeight = 30f;
    public float raycastDistance = 60f;

    [Header("NavMesh")]
    public float navMeshSearchDistance = 2f;
    public int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Препятствия")]
    public LayerMask obstacleMask;

    [Header("Поверхность")]
    public float maxSlopeAngle = 45f;

    [Header("Здоровье спавнера")]
    public float maxHealth = 500f;

    [Header("Управление")]
    public KeyCode respawnKey = KeyCode.R;

    private float currentHealth;

    private int currentWave = 0;

    private bool waveInProgress = false;

    private bool spawnerCanBeDestroyed = false;

    private List<GameObject> spawnedObjects =
        new List<GameObject>();

    private List<Vector3> spawnPoints =
        new List<Vector3>();


    // =========================================================
    // START
    // =========================================================

    private void Start()
    {
        currentHealth = maxHealth;

        StartNextWave();
    }


    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        // R полностью запускает всё заново
        if (Input.GetKeyDown(respawnKey))
        {
            RespawnSpawner();
        }

        // Если волна закончилась
        if (waveInProgress)
        {
            CheckWaveCompleted();
        }
    }


    // =========================================================
    // ЗАПУСК СЛЕДУЮЩЕЙ ВОЛНЫ
    // =========================================================

    private void StartNextWave()
    {
        currentWave++;

        if (currentWave > waveCount)
        {
            Victory();

            return;
        }

        Debug.Log(
            "========== ВОЛНА " +
            currentWave +
            " / " +
            waveCount +
            " =========="
        );

        SpawnWave();

        waveInProgress = true;
    }


    // =========================================================
    // СПАВН ВОЛНЫ
    // =========================================================

    private void SpawnWave()
    {
        spawnPoints.Clear();

        GeneratePoissonPoints();

        int spawnedCount = 0;

        foreach (Vector3 point in spawnPoints)
        {
            if (spawnedCount >= enemiesPerWave)
                break;

            if (SpawnEnemyAtPoint(point))
            {
                spawnedCount++;
            }
        }

        Debug.Log(
            "Появилось врагов: " +
            spawnedCount +
            " / " +
            enemiesPerWave
        );

        if (spawnedCount == 0)
        {
            Debug.LogWarning(
                "Не удалось создать ни одного врага!"
            );
        }
    }


    // =========================================================
    // POISSON DISK
    // =========================================================

    private void GeneratePoissonPoints()
    {
        List<Vector2> points =
            new List<Vector2>();

        int maxAttempts =
            enemiesPerWave * maxPoissonAttempts;

        int attempts = 0;

        while (
            points.Count < enemiesPerWave &&
            attempts < maxAttempts
        )
        {
            attempts++;

            Vector2 candidate =
                Random.insideUnitCircle *
                spawnRadius;

            bool valid = true;

            foreach (Vector2 point in points)
            {
                float distance =
                    Vector2.Distance(
                        candidate,
                        point
                    );

                if (
                    distance <
                    minDistanceBetweenObjects
                )
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            points.Add(candidate);
        }

        foreach (Vector2 point in points)
        {
            Vector3 worldPoint =
                transform.position +
                new Vector3(
                    point.x,
                    0f,
                    point.y
                );

            spawnPoints.Add(worldPoint);
        }
    }


    // =========================================================
    // СОЗДАНИЕ ОДНОГО ВРАГА
    // =========================================================

    private bool SpawnEnemyAtPoint(Vector3 point)
    {
        if (prefabToSpawn == null)
        {
            Debug.LogError(
                "Prefab врага не назначен!"
            );

            return false;
        }

        // -----------------------------------------------------
        // RAYCAST
        // -----------------------------------------------------

        Vector3 rayStart =
            point +
            Vector3.up * raycastHeight;

        if (!Physics.Raycast(
            rayStart,
            Vector3.down,
            out RaycastHit hit,
            raycastDistance,
            groundMask
        ))
        {
            return false;
        }

        // -----------------------------------------------------
        // НАКЛОН
        // -----------------------------------------------------

        float slopeAngle =
            Vector3.Angle(
                hit.normal,
                Vector3.up
            );

        if (slopeAngle > maxSlopeAngle)
        {
            return false;
        }

        // -----------------------------------------------------
        // NAVMESH
        // -----------------------------------------------------

        if (!NavMesh.SamplePosition(
            hit.point,
            out NavMeshHit navHit,
            navMeshSearchDistance,
            navMeshAreaMask
        ))
        {
            return false;
        }

        // -----------------------------------------------------
        // СОЗДАЁМ ВРАГА
        // -----------------------------------------------------

        GameObject enemy =
            Instantiate(
                prefabToSpawn,
                navHit.position,
                Quaternion.identity
            );

        Collider enemyCollider =
            enemy.GetComponentInChildren<Collider>();

        if (enemyCollider == null)
        {
            Debug.LogError(
                "У врага нет Collider!"
            );

            Destroy(enemy);

            return false;
        }

        // -----------------------------------------------------
        // СТАВИМ НИЖНИЙ КРАЙ НА ЗЕМЛЮ
        // -----------------------------------------------------

        Physics.SyncTransforms();

        float bottomOffset =
            enemy.transform.position.y -
            enemyCollider.bounds.min.y;

        enemy.transform.position +=
            Vector3.up * bottomOffset;

        Physics.SyncTransforms();

        // -----------------------------------------------------
        // ПРОВЕРКА СТОЛКНОВЕНИЙ
        // -----------------------------------------------------

        if (!IsEnemyPositionFree(enemy))
        {
            Destroy(enemy);

            return false;
        }

        // -----------------------------------------------------
        // ДОБАВЛЯЕМ В СПИСОК
        // -----------------------------------------------------

        spawnedObjects.Add(enemy);

        return true;
    }


    // =========================================================
    // ПРОВЕРКА ПОЗИЦИИ
    // =========================================================

    private bool IsEnemyPositionFree(
        GameObject enemy
    )
    {
        Collider enemyCollider =
            enemy.GetComponentInChildren<Collider>();

        if (enemyCollider == null)
            return false;

        Bounds bounds =
            enemyCollider.bounds;

        // Проверяем препятствия
        Collider[] obstacles =
            Physics.OverlapBox(
                bounds.center,
                bounds.extents,
                enemy.transform.rotation,
                obstacleMask
            );

        foreach (Collider obstacle in obstacles)
        {
            if (
                obstacle.transform.IsChildOf(
                    enemy.transform
                )
            )
            {
                continue;
            }

            return false;
        }

        // Проверяем уже созданных врагов
        foreach (GameObject other in spawnedObjects)
        {
            if (other == null)
                continue;

            Collider otherCollider =
                other.GetComponentInChildren<Collider>();

            if (otherCollider == null)
                continue;

            if (
                enemyCollider.bounds.Intersects(
                    otherCollider.bounds
                )
            )
            {
                return false;
            }
        }

        return true;
    }


    // =========================================================
    // ПРОВЕРКА ОКОНЧАНИЯ ВОЛНЫ
    // =========================================================

    private void CheckWaveCompleted()
    {
        // Удаляем null из списка
        spawnedObjects.RemoveAll(
            obj => obj == null
        );

        // Если врагов больше нет
        if (spawnedObjects.Count == 0)
        {
            waveInProgress = false;

            Debug.Log(
                "ВОЛНА " +
                currentWave +
                " ЗАВЕРШЕНА!"
            );

            // Запускаем следующую
            StartNextWave();
        }
    }


    // =========================================================
    // ПОБЕДА
    // =========================================================

    private void Victory()
    {
        waveInProgress = false;

        spawnerCanBeDestroyed = true;

        Debug.Log(
            "================================"
        );

        Debug.Log(
            "ПОБЕДА!"
        );

        Debug.Log(
            "Все волны уничтожены!"
        );

        Debug.Log(
            "Теперь спавнер можно уничтожить!"
        );

        Debug.Log(
            "================================"
        );
    }


    // =========================================================
    // УРОН СПАВНЕРУ
    // =========================================================

    public void TakeDamage(in DamageContext context)
    {
        // Пока не уничтожены все волны,
        // спавнер не получает урон
        if (!spawnerCanBeDestroyed)
        {
            Debug.Log(
                "Спавнер защищён! " +
                "Сначала уничтожьте всех врагов."
            );

            return;
        }

        float damage =
            context.Damage.Total;

        currentHealth -= damage;

        Debug.Log(
            "Спавнер получил " +
            damage +
            " урона. HP: " +
            currentHealth
        );

        if (currentHealth <= 0f)
        {
            DestroySpawner();
        }
    }


    // =========================================================
    // УНИЧТОЖЕНИЕ СПАВНЕРА
    // =========================================================

    private void DestroySpawner()
    {
        Debug.Log(
            "СПАВНЕР УНИЧТОЖЕН!"
        );

        Destroy(gameObject);
    }


    // =========================================================
    // ПЕРЕЗАПУСК
    // =========================================================

    private void RespawnSpawner()
    {
        // Удаляем врагов
        foreach (GameObject obj in spawnedObjects)
        {
            if (obj != null)
            {
                Destroy(obj);
            }
        }

        spawnedObjects.Clear();

        currentWave = 0;

        currentHealth = maxHealth;

        spawnerCanBeDestroyed = false;

        waveInProgress = false;

        StartNextWave();

        Debug.Log(
            "Спавнер перезапущен!"
        );
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;

        Gizmos.DrawWireSphere(
            transform.position,
            spawnRadius
        );

        Gizmos.color = Color.green;

        foreach (Vector3 point in spawnPoints)
        {
            Gizmos.DrawSphere(
                point,
                0.15f
            );
        }
    }
}