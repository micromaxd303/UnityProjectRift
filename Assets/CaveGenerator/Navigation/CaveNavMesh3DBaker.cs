using NavMesh3DSystem;
using UnityEngine;

/// <summary>
/// Этап 6 для летающих врагов: после генерации пещеры запекает NavMesh3D из поля вокселей.
/// Физика не нужна: проходимость берётся из EDT (расстояние до камня), поэтому толща
/// скалы не считается воздухом, как при OverlapSphere по оболочке MeshCollider.
/// Назначает себя в NavMesh3D.BakeSource, поэтому кнопка Bake и Bake() из кода тоже берут поле пещеры.
/// </summary>
public class CaveNavMesh3DBaker : MonoBehaviour, INavMesh3DBakeSource
{
    [SerializeField] private CaveGenerator generator;
    [SerializeField] private NavMesh3D navMesh;

    private void Awake()
    {
        // Физическое запекание на старте не нужно: сетку даст пещера
        if (navMesh != null)
            navMesh.bakeOnStart = false;
    }

    private void OnEnable()
    {
        if (generator != null)
            generator.LevelGenerated += OnLevelGenerated;
        if (navMesh != null)
            navMesh.BakeSource = this;
    }

    private void OnDisable()
    {
        if (generator != null)
            generator.LevelGenerated -= OnLevelGenerated;
        if (navMesh != null && ReferenceEquals(navMesh.BakeSource, this))
            navMesh.BakeSource = null;
    }

    private void OnLevelGenerated(LevelContext context) => Bake();

    [ContextMenu("Bake From Cave")]
    public void Bake()
    {
        if (navMesh != null)
            TryBake(navMesh);
    }

    public bool TryBake(NavMesh3D target)
    {
        var field = generator != null ? generator.Validation?.Field : null;
        if (field == null)
        {
            Debug.LogWarning("CaveNavMesh3DBaker: нет поля вокселей (включи проверку уровня, этап 4). Запекаю через физику.");
            return false;
        }

        // EDT считаем заново: Cleanup после анализа заливает карманы камнем
        var edt = LevelAnalyzer.ComputeEdt(field);

        target.BakeFromDistanceField(new NavMesh3DDistanceField
        {
            distance = edt,
            size = field.Size,
            spacing = field.VoxelSize,
            origin = field.ToLocal(0, 0, 0),
            surfaceBias = field.VoxelSize * 0.5f
        }, generator.transform);
        return true;
    }
}
