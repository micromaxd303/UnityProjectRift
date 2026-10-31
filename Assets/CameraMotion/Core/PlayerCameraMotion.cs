using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Rift.CameraMotion
{
    /// <summary>
    /// Расширение CinemachineCamera, которое складывает все слои анимации камеры
    /// и добавляет результат поверх обычного положения камеры.
    ///
    /// Смещения пишутся в PositionCorrection / OrientationCorrection, поэтому:
    /// - трансформ самого объекта CinemachineCamera остаётся «чистым» (только PanTilt),
    ///   и движение (PlayerMotor) и прицел от него не плывут;
    /// - Unity-камера (Camera.main) получает итоговую картинку с покачиванием.
    /// </summary>
    [AddComponentMenu("Cinemachine/Procedural/Extensions/RIFT Player Camera Motion")]
    [DisallowMultipleComponent]
    public class PlayerCameraMotion : CinemachineExtension
    {
        /// <summary>Общий множитель для меню настроек («уменьшить покачивание камеры»). 0..1.</summary>
        public static float GlobalIntensity = 1f;

        /// <summary>Активная камера игрока. Нужна снаряжению и прочим системам, чтобы проиграть эффект.</summary>
        public static PlayerCameraMotion Instance { get; private set; }

        [Tooltip("Множитель всех «косметических» слоёв на этой камере.")]
        [Range(0f, 2f)] public float Intensity = 1f;

        [Tooltip("После какой стадии конвейера применять смещения. Noise — поверх шума и импульсов.")]
        public CinemachineCore.Stage ApplyAfter = CinemachineCore.Stage.Noise;

        [Tooltip("Ограничение суммарного смещения (защита от «улёта» при неудачных настройках).")]
        public float MaxPositionOffset = 0.5f;
        public float MaxAngleOffset = 25f;
        public float MaxFovOffset = 30f;

        private readonly List<ICameraMotionLayer> layers = new();
        private CameraClipLayer clipLayer;

        /// <summary>Итог последнего кадра — удобно смотреть в отладке.</summary>
        public CameraOffset LastOffset { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            CollectLayers();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Пересобрать список слоёв (если слои добавлялись в рантайме).</summary>
        public void CollectLayers()
        {
            layers.Clear();
            GetComponents(layers);
            clipLayer = GetComponent<CameraClipLayer>();
        }

        /// <summary>Проиграть клип камеры (перезарядка, снаряжение, катсцены-«жесты»).</summary>
        public CameraClipHandle PlayClip(CameraMotionClip clip, float duration = -1f, float weight = 1f)
        {
            if (clipLayer == null)
            {
                clipLayer = gameObject.AddComponent<CameraClipLayer>();
                CollectLayers();
            }
            return clipLayer.Play(clip, duration, weight);
        }

        public void ResetAllLayers()
        {
            foreach (var layer in layers) layer.ResetLayer();
            LastOffset = CameraOffset.Zero;
        }

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage,
            ref CameraState state,
            float deltaTime)
        {
            if (stage != ApplyAfter) return;

            // deltaTime < 0 — Cinemachine просит сбросить состояние (камера только что активировалась).
            if (deltaTime < 0f)
            {
                ResetAllLayers();
                return;
            }

            if (!Application.isPlaying) return;

            float intensity = Intensity * Mathf.Clamp01(GlobalIntensity);
            CameraOffset total = CameraOffset.Zero;

            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null || !layer.IsLayerActive) continue;

                CameraOffset o = layer.Evaluate(deltaTime);
                total += layer.AffectedByIntensity ? o * intensity : o;
            }

            total.Position = Vector3.ClampMagnitude(total.Position, MaxPositionOffset);
            total.Rotation = new Vector3(
                Mathf.Clamp(total.Rotation.x, -MaxAngleOffset, MaxAngleOffset),
                Mathf.Clamp(total.Rotation.y, -MaxAngleOffset, MaxAngleOffset),
                Mathf.Clamp(total.Rotation.z, -MaxAngleOffset, MaxAngleOffset));
            total.Fov = Mathf.Clamp(total.Fov, -MaxFovOffset, MaxFovOffset);
            LastOffset = total;

            Quaternion orientation = state.RawOrientation * state.OrientationCorrection;
            state.PositionCorrection += orientation * total.Position;
            state.OrientationCorrection *= Quaternion.Euler(total.Rotation);

            if (!state.Lens.Orthographic)
                state.Lens.FieldOfView = Mathf.Clamp(state.Lens.FieldOfView + total.Fov, 1f, 179f);
        }
    }
}
