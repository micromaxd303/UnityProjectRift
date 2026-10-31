using UnityEngine;

namespace Rift.CameraMotion
{
    /// <summary>
    /// Короткая «анимация» камеры кривыми: перезарядка, использование снаряжения, взмах, удар.
    /// Время по оси X кривых нормализовано 0..1, значения — реальные смещения (метры / градусы).
    /// Проигрывается через PlayerCameraMotion.Instance.PlayClip(clip).
    /// </summary>
    [CreateAssetMenu(fileName = "CameraMotionClip", menuName = "Scriptable Objects/Camera/Camera Motion Clip")]
    public class CameraMotionClip : ScriptableObject
    {
        [Tooltip("Длительность по умолчанию, секунды. PlayClip может её переопределить.")]
        [Min(0.01f)] public float Duration = 1f;

        [Tooltip("Плавный вход/выход, секунды. Позволяет оборвать клип без рывка.")]
        [Min(0f)] public float BlendIn = 0.08f;
        [Min(0f)] public float BlendOut = 0.15f;

        [Header("Позиция, метры (x вправо, y вверх, z вперёд)")]
        public AnimationCurve PositionX = Flat();
        public AnimationCurve PositionY = Flat();
        public AnimationCurve PositionZ = Flat();

        [Header("Поворот, градусы (тангаж + вниз, рыскание, крен + влево)")]
        public AnimationCurve Pitch = Flat();
        public AnimationCurve Yaw = Flat();
        public AnimationCurve Roll = Flat();

        [Header("FOV, градусы")]
        public AnimationCurve Fov = Flat();

        public CameraOffset Evaluate(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            return new CameraOffset(
                new Vector3(PositionX.Evaluate(t), PositionY.Evaluate(t), PositionZ.Evaluate(t)),
                new Vector3(Pitch.Evaluate(t), Yaw.Evaluate(t), Roll.Evaluate(t)),
                Fov.Evaluate(t));
        }

        private static AnimationCurve Flat() => AnimationCurve.Constant(0f, 1f, 0f);

        private static AnimationCurve Bump(float peak, float upEnd, float holdEnd) =>
            new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(upEnd, peak),
                new Keyframe(holdEnd, peak),
                new Keyframe(1f, 0f));

        [ContextMenu("Заполнить примером: перезарядка")]
        private void FillReloadExample()
        {
            Duration = 1.5f;
            Pitch = Bump(3f, 0.25f, 0.7f);
            Roll = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.25f, 2.5f), new Keyframe(0.55f, 1.5f),
                new Keyframe(0.62f, -1f), new Keyframe(0.75f, 0.5f), new Keyframe(1f, 0f));
            PositionY = Bump(-0.02f, 0.25f, 0.7f);
            PositionX = Flat(); PositionZ = Flat(); Yaw = Flat(); Fov = Flat();
        }

        [ContextMenu("Заполнить примером: использование снаряжения")]
        private void FillGadgetExample()
        {
            Duration = 0.6f;
            Pitch = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 2f), new Keyframe(0.45f, -1.5f), new Keyframe(1f, 0f));
            PositionZ = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.45f, 0.03f), new Keyframe(1f, 0f));
            Fov = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.45f, -2f), new Keyframe(1f, 0f));
            PositionX = Flat(); PositionY = Flat(); Yaw = Flat(); Roll = Flat();
        }
    }
}
