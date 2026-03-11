using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ChunkCuller : MonoBehaviour
{
    [SerializeField, Range(0.05f, 0.5f)]
    private float _occlusionInterval = 0.2f;
    
    [SerializeField]
    private float _maxDistance = 200f;

    [SerializeField, Range(1f, 20f)]
    private float _frustumBuffer = 5f;

    [SerializeField]
    private LayerMask _cullingMask = ~0;

    private Camera _camera;
    private Dictionary<Vector3Int, ChunkState> _chunks = new();
    private float _nextOcclusionUpdate;

    private struct ChunkState
    {
        public GameObject Go;
        public MeshRenderer Renderer;
        public bool OcclusionVisible;
    }

    public void RegisterChunks(Dictionary<Vector3Int, GameObject> chunks)
    {
        _chunks.Clear();

        foreach (var (coord, go) in chunks)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) continue;

            _chunks[coord] = new ChunkState
            {
                Go = go,
                Renderer = renderer,
                OcclusionVisible = true
            };

            SetChunkVisible(renderer, true);
        }
    }

    private void Start()
    {
        _camera = Camera.main;
    }

    private void Update()
    {
        if (_chunks.Count == 0)
            return;

        bool updateOcclusion = Time.time >= _nextOcclusionUpdate;
        if (updateOcclusion)
            _nextOcclusionUpdate = Time.time + _occlusionInterval;

        Vector3 camPos = _camera.transform.position;
        var planes = GeometryUtility.CalculateFrustumPlanes(_camera);

        for (int i = 0; i < planes.Length; i++)
            planes[i].distance += _frustumBuffer;

        var keys = new List<Vector3Int>(_chunks.Keys);

        foreach (var coord in keys)
        {
            var state = _chunks[coord];
            if (state.Go == null) continue;

            Bounds bounds = state.Renderer.bounds;

            if (Vector3.Distance(camPos, bounds.center) > _maxDistance)
            {
                SetChunkVisible(state.Renderer, false);
                continue;
            }

            if (!GeometryUtility.TestPlanesAABB(planes, bounds))
            {
                SetChunkVisible(state.Renderer, false);
                continue;
            }

            if (updateOcclusion)
            {
                state.OcclusionVisible = IsChunkVisible(camPos, bounds, state.Go);
                _chunks[coord] = state;
            }

            SetChunkVisible(state.Renderer, state.OcclusionVisible);
        }
    }

    private void SetChunkVisible(MeshRenderer renderer, bool visible)
    {
        if (visible)
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }
        else
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
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

            if (Physics.Raycast(camPos, dir.normalized, out RaycastHit hit, dist, _cullingMask))
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

        return new[]
        {
            c,
            c + Vector3.up * e.y,
            c - Vector3.up * e.y,
            c + Vector3.right * e.x,
            c - Vector3.right * e.x,
            c + Vector3.forward * e.z,
            c - Vector3.forward * e.z,
            c + new Vector3(e.x, e.y, e.z),
            c + new Vector3(-e.x, e.y, e.z),
            c + new Vector3(e.x, -e.y, e.z),
            c + new Vector3(-e.x, -e.y, e.z),
            c + new Vector3(e.x, e.y, -e.z),
            c + new Vector3(-e.x, e.y, -e.z),
            c + new Vector3(e.x, -e.y, -e.z),
            c + new Vector3(-e.x, -e.y, -e.z)
        };
    }
}