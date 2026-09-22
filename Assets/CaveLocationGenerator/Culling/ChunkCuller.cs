using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ChunkCuller : MonoBehaviour
{
    [SerializeField] private float _maxDistance = 200f;
    [SerializeField, Range(1f, 20f)] private float _frustumBuffer = 5f;
    [SerializeField, Range(0, 10)] private int _hysteresisFrames = 3;

    private Camera _camera;
    private List<ChunkState> _chunks = new();
    private Plane[] _planes = new Plane[6];

    private struct ChunkState
    {
        public GameObject Go;
        public MeshRenderer Renderer;
        public int InvisibleFrames;
        public ShadowCastingMode CurrentMode;
    }

    public void RegisterChunks(Dictionary<Vector3Int, GameObject> chunks)
    {
        _chunks.Clear();

        foreach (var (_, go) in chunks)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) continue;

            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;

            _chunks.Add(new ChunkState
            {
                Go = go,
                Renderer = renderer,
                InvisibleFrames = 0,
                CurrentMode = ShadowCastingMode.On
            });
        }
    }

    private void Start()
    {
        _camera = Camera.main;
    }

    private void Update()
    {
        if (_chunks.Count == 0 || _camera == null)
            return;

        Vector3 camPos = _camera.transform.position;
        GeometryUtility.CalculateFrustumPlanes(_camera, _planes);

        for (int i = 0; i < _planes.Length; i++)
            _planes[i].distance += _frustumBuffer;

        for (int i = 0; i < _chunks.Count; i++)
        {
            var state = _chunks[i];
            if (state.Go == null) continue;

            Bounds bounds = state.Renderer.bounds;
            float dist = Vector3.Distance(camPos, bounds.ClosestPoint(camPos));

            bool inFrustum = dist <= _maxDistance
                             && GeometryUtility.TestPlanesAABB(_planes, bounds);

            if (inFrustum)
            {
                state.InvisibleFrames = 0;
                ApplyMode(ref state, ShadowCastingMode.On);
            }
            else
            {
                state.InvisibleFrames++;

                if (state.InvisibleFrames > _hysteresisFrames)
                    ApplyMode(ref state, ShadowCastingMode.ShadowsOnly);
            }

            _chunks[i] = state;
        }
    }

    private void ApplyMode(ref ChunkState state, ShadowCastingMode mode)
    {
        if (state.CurrentMode == mode)
            return;

        state.Renderer.enabled = true;
        state.Renderer.shadowCastingMode = mode;
        state.CurrentMode = mode;
    }
}
