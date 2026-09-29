using System;
using UnityEngine;

public enum MeshMode
{
    Raw,    // как вышло из Marching Cubes
    Smooth  // сварка вершин + сглаживание + гладкие нормали
}

[Flags]
public enum SettingsChange
{
    None        = 0,
    Extract     = 1 << 0, // нужно заново прогнать Marching Cubes
    PostProcess = 1 << 1, // пересобрать меш из сохранённого сырого выхода MC
    Render      = 1 << 2, // материал / слой
    Physics     = 1 << 3, // коллайдеры
    Memory      = 1 << 4  // выключили LiveTuning -> можно освободить исходные данные
}

[CreateAssetMenu(fileName = "MeshBuilderConfig", menuName = "RIFT/Mesh Builder Config")]
public sealed class MeshBuilderConfig : ScriptableObject
{
    [Header("Mesh")]
    [SerializeField] private MeshMode meshMode = MeshMode.Smooth;
    [SerializeField, Min(1e-6f)] private float weldThreshold = 1e-4f;
    [SerializeField, Range(0, 10)] private int smoothIterations = 2;
    [SerializeField, Range(0f, 1f)] private float smoothStrength = 0.5f;

    [Header("Rendering")]
    [SerializeField] private Material material;
    [SerializeField] private string chunkLayer = "CaveChunk";

    [Header("Physics")]
    [SerializeField] private bool generateColliders = true;

    [Header("Memory")]
    [Tooltip("Вкл: сырые данные MC и CPU-копии мешей хранятся, параметры можно менять на лету.\n" +
             "Выкл: после сборки память освобождается, пересборка невозможна до новой генерации.")]
    [SerializeField] private bool liveTuning = true;

    public event Action<SettingsChange> Changed;

    public MeshMode Mode
    {
        get => meshMode;
        set { meshMode = value; DetectChanges(); }
    }

    public float WeldThreshold => weldThreshold;
    public int SmoothIterations => smoothIterations;
    public float SmoothStrength => smoothStrength;
    public Material Material => material;
    public string ChunkLayer => chunkLayer;
    public bool GenerateColliders => generateColliders;
    public bool LiveTuning => liveTuning;

    // ---- определение того, ЧТО именно поменялось ----

    private struct Snapshot
    {
        public float Iso;
        public MeshMode Mode;
        public float Weld;
        public int Iterations;
        public float Strength;
        public Material Material;
        public string Layer;
        public bool Colliders;
        public bool LiveTuning;
    }

    [NonSerialized] private Snapshot _last;
    [NonSerialized] private bool _hasSnapshot;

    private Snapshot Capture() => new Snapshot
    {
        Mode = meshMode,
        Weld = weldThreshold,
        Iterations = smoothIterations,
        Strength = smoothStrength,
        Material = material,
        Layer = chunkLayer,
        Colliders = generateColliders,
        LiveTuning = liveTuning
    };

    private void OnEnable()
    {
        _last = Capture();
        _hasSnapshot = true;
    }

#if UNITY_EDITOR
    // Вызывается при изменении в инспекторе. Здесь только уведомляем -
    // тяжёлую работу подписчик должен делать в Update, а не внутри OnValidate.
    private void OnValidate() => DetectChanges();
#endif

    private void DetectChanges()
    {
        Snapshot now = Capture();
        if (!_hasSnapshot)
        {
            _last = now;
            _hasSnapshot = true;
            return;
        }

        SettingsChange change = SettingsChange.None;

        if (now.Iso != _last.Iso)
            change |= SettingsChange.Extract;

        if (now.Mode != _last.Mode || now.Weld != _last.Weld ||
            now.Iterations != _last.Iterations || now.Strength != _last.Strength)
            change |= SettingsChange.PostProcess;

        if (now.Material != _last.Material || now.Layer != _last.Layer)
            change |= SettingsChange.Render;

        if (now.Colliders != _last.Colliders)
            change |= SettingsChange.Physics;

        if (_last.LiveTuning && !now.LiveTuning)
            change |= SettingsChange.Memory;

        _last = now;

        if (change != SettingsChange.None)
            Changed?.Invoke(change);
    }
}