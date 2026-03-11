using System.Collections.Generic;
using UnityEngine;

public class ChunkCuller : MonoBehaviour
{
    [SerializeField, Range(0.1f, 1f)]
    private float _updateInterval = 0.1f;

    [SerializeField, Range(1, 8)]
    private int _raysPerChunk = 3;

    [SerializeField]
    private float _maxDistance = 200f;

    [SerializeField, Range(1f, 20f)]
    private float _frustumBuffer = 5f;

    private Camera _camera;
    private Dictionary<Vector3Int, GameObject> _chunkObjects = new();
    private float _nextUpdate;

    public void RegisterChunks(Dictionary<Vector3Int, GameObject> chunks)
    {
        _chunkObjects = chunks;

        foreach (var (_, go) in _chunkObjects)
            SetChunkVisible(go, true);
    }

    private void Start()
    {
        _camera = Camera.main;
    }

    private void Update()
    {
        if (_chunkObjects.Count == 0)
            return;

        UpdateFrustumCulling();

        if (Time.time >= _nextUpdate)
        {
            _nextUpdate = Time.time + _updateInterval;
            UpdateOcclusionCulling();
        }
    }

    private void UpdateFrustumCulling()
    {
        Vector3 camPos = _camera.transform.position;
        var planes = GeometryUtility.CalculateFrustumPlanes(_camera);

        for (int i = 0; i < planes.Length; i++)
            planes[i].distance += _frustumBuffer;

        foreach (var (_, go) in _chunkObjects)
        {
            if (go == null) continue;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) continue;

            bool inFrustum = GeometryUtility.TestPlanesAABB(planes, renderer.bounds)
                && Vector3.Distance(camPos, renderer.bounds.center) <= _maxDistance;

            if (!inFrustum)
                renderer.enabled = false;
            else if (!renderer.enabled)
                renderer.enabled = true;
        }
    }

    private void UpdateOcclusionCulling()
    {
        Vector3 camPos = _camera.transform.position;

        foreach (var (_, go) in _chunkObjects)
        {
            if (go == null) continue;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled) continue;

            if (!IsChunkVisible(camPos, renderer.bounds, go))
                renderer.enabled = false;
        }
    }

    private bool IsChunkVisible(Vector3 camPos, Bounds bounds, GameObject self)
    {
        Vector3[] testPoints = GetTestPoints(bounds);

        for (int i = 0; i < testPoints.Length; i++)
        {
            Vector3 dir = testPoints[i] - camPos;
            float dist = dir.magnitude;

            if (dist < 0.1f)
                return true;

            if (Physics.Raycast(camPos, dir.normalized, out RaycastHit hit, dist))
            {
                if (hit.collider.gameObject == self)
                    return true;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private Vector3[] GetTestPoints(Bounds b)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;

        return _raysPerChunk switch
        {
            1 => new[] { c },
            2 => new[] { c, c + Vector3.up * e.y },
            3 => new[] { c, c + Vector3.up * e.y, c - Vector3.up * e.y },
            4 => new[]
            {
                c,
                c + Vector3.up * e.y,
                c + Vector3.right * e.x,
                c + Vector3.forward * e.z
            },
            _ => new[]
            {
                c,
                c + Vector3.up * e.y,
                c - Vector3.up * e.y,
                c + Vector3.right * e.x,
                c + Vector3.forward * e.z
            }
        };
    }

    private void SetChunkVisible(GameObject go, bool visible)
    {
        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.enabled != visible)
            renderer.enabled = visible;
    }
}