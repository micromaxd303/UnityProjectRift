using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public sealed class MeshBuilder : IDisposable
{
    private readonly MeshBuilderConfig _settings;
    private readonly Transform _parent;
    private readonly Dictionary<Vector3Int, Chunk> _chunks = new();
    private readonly List<Vector3Int> _stale = new();
    private int _layer;
    

    private sealed class Chunk
    {
        public GameObject Go;
        public MeshRenderer Renderer;
        public MeshCollider Collider;
        public Mesh Mesh;
        public MeshData? Source; // сырой выход MC, хранится только при LiveTuning
    }

    public MeshBuilder(MeshBuilderConfig settings, Transform parent)
    {
        _settings = settings;
        _parent = parent;
        _layer = ResolveLayer();
    }

    /// Строит новые чанки или пересобирает существующие на месте.
    public Dictionary<Vector3Int, GameObject> Build(Dictionary<Vector3Int, MeshData> rawChunks)
    {
        var result = new Dictionary<Vector3Int, GameObject>(rawChunks.Count);

        foreach (var (coord, raw) in rawChunks)
        {
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                if (raw.Triangles.Length < 3)
                    continue;

                chunk = CreateChunk(coord);
                _chunks.Add(coord, chunk);
            }

            chunk.Source = _settings.LiveTuning ? raw : null;

            if (Rebuild(chunk, raw))
                result[coord] = chunk.Go;
        }
        
        _stale.Clear();
        foreach (var coord in _chunks.Keys)
            if (!rawChunks.ContainsKey(coord))
                _stale.Add(coord);

        foreach (var coord in _stale)
        {
            var c = _chunks[coord];
            SafeDestroy(c.Mesh);
            SafeDestroy(c.Go);
            _chunks.Remove(coord);
        }

        return result;
    }

    /// Смена Raw/Smooth, порога сварки, итераций сглаживания — без повторного MC.
    public void Reprocess()
    {
        foreach (var chunk in _chunks.Values)
            if (chunk.Source is { } raw)
                Rebuild(chunk, raw);
    }

    public void ApplyRenderSettings()
    {
        _layer = ResolveLayer();
        foreach (var c in _chunks.Values)
        {
            c.Renderer.sharedMaterial = _settings.Material;
            c.Go.layer = _layer;
        }
    }

    public void ApplyPhysicsSettings()
    {
        foreach (var c in _chunks.Values)
            if (c.Go.activeSelf)
                UpdateCollider(c);
    }

    /// Выбросить сырые данные и CPU-копии мешей. Меш остаётся только на GPU,
    /// коллайдер уже запечён и продолжает работать.
    public void ReleaseSourceData()
    {
        foreach (var c in _chunks.Values)
        {
            c.Source = null;
            if (c.Mesh.isReadable)
                c.Mesh.UploadMeshData(markNoLongerReadable: true);
        }
    }

    public void Clear()
    {
        foreach (var c in _chunks.Values)
        {
            SafeDestroy(c.Mesh); // Mesh — нативный объект, GC его не освободит
            SafeDestroy(c.Go);
        }
        _chunks.Clear();
    }

    public void Dispose() => Clear();

    // ------------------------------------------------------------------

    private Chunk CreateChunk(Vector3Int coord)
    {
        var go = new GameObject($"Chunk_{coord.x}_{coord.y}_{coord.z}");
        go.layer = _layer;
        go.transform.SetParent(_parent, false);

        var mesh = new Mesh { name = go.name };
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _settings.Material;

        return new Chunk { Go = go, Renderer = renderer, Mesh = mesh };
    }

    private bool Rebuild(Chunk c, MeshData raw)
    {
        if (!c.Mesh.isReadable)
        {
            // CPU-данные выгружены — для полной перегенерации они не нужны,
            // просто заменяем меш новым
            SafeDestroy(c.Mesh);
            c.Mesh = new Mesh { name = c.Go.name };
            c.Go.GetComponent<MeshFilter>().sharedMesh = c.Mesh;
        }

        MeshData data = Process(raw);

        bool hasGeometry = data.Triangles.Length >= 3;
        c.Go.SetActive(hasGeometry);
        if (!hasGeometry)
            return false;

        Mesh mesh = c.Mesh;
        mesh.Clear();
        mesh.indexFormat = data.Vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(data.Vertices);
        mesh.SetTriangles(data.Triangles, 0);
        mesh.RecalculateNormals(); // Raw -> плоские нормали, Smooth -> гладкие

        UpdateCollider(c); // коллайдер печётся ДО выгрузки CPU-данных

        if (!_settings.LiveTuning)
            mesh.UploadMeshData(markNoLongerReadable: true);

        return true;
    }

    private MeshData Process(MeshData raw)
    {
        if (_settings.Mode == MeshMode.Raw)
            return raw;

        MeshData welded = MeshOptimizer.WeldVertices(raw, _settings.WeldThreshold);

        return _settings.SmoothIterations > 0
            ? MeshOptimizer.LaplacianSmooth(welded, _settings.SmoothIterations, _settings.SmoothStrength)
            : welded;
    }

    private void UpdateCollider(Chunk c)
    {
        if (!_settings.GenerateColliders)
        {
            if (c.Collider != null)
            {
                SafeDestroy(c.Collider);
                c.Collider = null;
            }
            return;
        }

        if (!c.Mesh.isReadable)
        {
            if (c.Collider == null)
                Debug.LogWarning($"{c.Go.name}: нельзя создать коллайдер для выгруженного меша.");
            return;
        }

        if (c.Collider == null)
        {
            c.Collider = c.Go.AddComponent<MeshCollider>();
            c.Collider.convex = false; // пещера должна быть полой
        }
        
        c.Collider.sharedMesh = null;
        c.Collider.sharedMesh = c.Mesh;
    }

    private int ResolveLayer()
    {
        int layer = LayerMask.NameToLayer(_settings.ChunkLayer);
        if (layer < 0)
        {
            Debug.LogWarning($"Слой '{_settings.ChunkLayer}' не найден, используется Default.");
            return 0;
        }
        return layer;
    }

    private static void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }
}