using System.Collections.Generic;
using UnityEngine;

namespace Rift.CameraMotion
{
    /// <summary>Ссылка на запущенный клип: можно остановить или узнать, играет ли он.</summary>
    public readonly struct CameraClipHandle
    {
        private readonly CameraClipLayer layer;
        private readonly int id;

        internal CameraClipHandle(CameraClipLayer layer, int id)
        {
            this.layer = layer;
            this.id = id;
        }

        public bool IsPlaying => layer != null && layer.IsPlaying(id);

        /// <summary>Остановить с плавным выходом (BlendOut клипа).</summary>
        public void Stop() { if (layer != null) layer.Stop(id, false); }

        public void StopImmediate() { if (layer != null) layer.Stop(id, true); }
    }

    /// <summary>
    /// Слой, который проигрывает CameraMotionClip. Несколько клипов складываются.
    /// Обычно вызывается через PlayerCameraMotion.Instance.PlayClip(...).
    /// </summary>
    [AddComponentMenu("RIFT/Camera Motion/Camera Clip Layer")]
    [DisallowMultipleComponent]
    public class CameraClipLayer : MonoBehaviour, ICameraMotionLayer
    {
        [SerializeField] private bool affectedByIntensity = true;

        private struct Instance
        {
            public int Id;
            public CameraMotionClip Clip;
            public float Time;
            public float Duration;
            public float Weight;
            public float StopTime;   // < 0 — не остановлен
        }

        private readonly List<Instance> playing = new();
        private int nextId = 1;

        public bool IsLayerActive => isActiveAndEnabled && playing.Count > 0;
        public bool AffectedByIntensity => affectedByIntensity;

        public CameraClipHandle Play(CameraMotionClip clip, float duration = -1f, float weight = 1f)
        {
            if (clip == null) return default;
            int id = nextId++;
            playing.Add(new Instance
            {
                Id = id,
                Clip = clip,
                Time = 0f,
                Duration = duration > 0f ? duration : clip.Duration,
                Weight = weight,
                StopTime = -1f,
            });
            return new CameraClipHandle(this, id);
        }

        public bool IsPlaying(int id)
        {
            foreach (var i in playing) if (i.Id == id) return true;
            return false;
        }

        public void Stop(int id, bool immediate)
        {
            for (int k = 0; k < playing.Count; k++)
            {
                if (playing[k].Id != id) continue;
                if (immediate) { playing.RemoveAt(k); return; }
                var inst = playing[k];
                if (inst.StopTime < 0f) inst.StopTime = inst.Time;
                playing[k] = inst;
                return;
            }
        }

        public void StopAll()
        {
            for (int k = 0; k < playing.Count; k++)
            {
                var inst = playing[k];
                if (inst.StopTime < 0f) inst.StopTime = inst.Time;
                playing[k] = inst;
            }
        }

        public void ResetLayer() => playing.Clear();

        public CameraOffset Evaluate(float dt)
        {
            CameraOffset total = CameraOffset.Zero;

            for (int k = playing.Count - 1; k >= 0; k--)
            {
                var inst = playing[k];
                inst.Time += dt;

                var clip = inst.Clip;
                if (clip == null) { playing.RemoveAt(k); continue; }

                float envelope = 1f;
                if (clip.BlendIn > 0f) envelope = Mathf.Min(envelope, inst.Time / clip.BlendIn);
                if (clip.BlendOut > 0f) envelope = Mathf.Min(envelope, (inst.Duration - inst.Time) / clip.BlendOut);

                bool finished = inst.Time >= inst.Duration;
                if (inst.StopTime >= 0f)
                {
                    float fade = clip.BlendOut > 0f ? 1f - (inst.Time - inst.StopTime) / clip.BlendOut : 0f;
                    envelope = Mathf.Min(envelope, fade);
                    if (fade <= 0f) finished = true;
                }

                if (finished) { playing.RemoveAt(k); continue; }

                envelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(envelope));
                total += clip.Evaluate(inst.Time / inst.Duration) * (envelope * inst.Weight);
                playing[k] = inst;
            }

            return total;
        }
    }
}
