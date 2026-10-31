using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rift.CameraMotion
{
    /// <summary>Как камера ведёт себя в одном состоянии передвижения.</summary>
    [Serializable]
    public class StateCameraSettings
    {
        public MovementType State;

        [Tooltip("Добавка к FOV в этом состоянии, градусы. Например спринт +6, слайд +10.")]
        public float FovOffset;

        [Tooltip("Постоянный крен камеры, градусы (+ влево, - вправо). Например слайд 4.")]
        public float Roll;

        [Tooltip("Постоянный наклон вниз/вверх, градусы (+ вниз).")]
        public float Pitch;

        [Tooltip("Смещение камеры по высоте, метры. Например присед -0.1, если капсула опускает камеру не полностью.")]
        public float Height;

        [Tooltip("Множитель амплитуды покачивания при шаге. 0 — без покачивания (воздух, слайд, рывок).")]
        [Min(0f)] public float BobAmplitude = 1f;

        [Tooltip("Множитель частоты шагов. Спринт обычно чуть выше 1.")]
        [Min(0.01f)] public float BobFrequency = 1f;

        [Tooltip("Время перехода В это состояние, секунды.")]
        [Min(0f)] public float BlendInTime = 0.25f;
    }

    [CreateAssetMenu(fileName = "MovementCameraProfile", menuName = "Scriptable Objects/Camera/Movement Camera Profile")]
    public class MovementCameraProfile : ScriptableObject
    {
        [Header("Состояния")]
        [Tooltip("Состояние, которого нет в списке, использует Default.")]
        public StateCameraSettings Default = new() { State = MovementType.Idle, BobAmplitude = 0f };
        public List<StateCameraSettings> States = new()
        {
            new() { State = MovementType.Idle,       BobAmplitude = 0f,   BlendInTime = 0.35f },
            new() { State = MovementType.Walking,    BobAmplitude = 1f,   BlendInTime = 0.25f },
            new() { State = MovementType.Sprinting,  BobAmplitude = 1.4f, BobFrequency = 1.1f, FovOffset = 6f, BlendInTime = 0.3f },
            new() { State = MovementType.Crouching,  BobAmplitude = 0.6f, BobFrequency = 0.9f, BlendInTime = 0.2f },
            new() { State = MovementType.Sliding,    BobAmplitude = 0f,   FovOffset = 10f, Roll = 4f, Pitch = -1f, BlendInTime = 0.15f },
            new() { State = MovementType.Jumping,    BobAmplitude = 0f,   BlendInTime = 0.15f },
            new() { State = MovementType.AirControl, BobAmplitude = 0f,   BlendInTime = 0.2f },
            new() { State = MovementType.Dashing,    BobAmplitude = 0f,   FovOffset = 4f, BlendInTime = 0.05f },
        };

        [Header("Покачивание при шаге (head bob)")]
        [Tooltip("Сколько метров проходит игрок за один шаг. Частота покачивания = скорость / длина шага.")]
        [Min(0.1f)] public float StepLength = 1.0f;
        [Tooltip("Амплитуда смещения: x — вбок, y — вниз на каждом шаге, метры.")]
        public Vector2 BobPosition = new(0.025f, 0.035f);
        [Tooltip("Амплитуда поворота: x — кивок, y — рыскание, z — крен, градусы.")]
        public Vector3 BobRotation = new(0.6f, 0.3f, 0.5f);
        [Tooltip("Скорость, при которой покачивание выходит на полную амплитуду (м/с).")]
        [Min(0.1f)] public float BobFullSpeed = 5f;
        [Tooltip("Как быстро покачивание затухает/включается при остановке/старте, секунды.")]
        [Min(0f)] public float BobFadeTime = 0.15f;

        [Header("Дыхание в покое")]
        [Tooltip("Медленное покачивание, когда игрок стоит. Ноль — выключено.")]
        public Vector3 IdleBreathRotation = new(0.25f, 0.15f, 0f);
        [Min(0.01f)] public float IdleBreathFrequency = 0.25f;

        [Header("Наклон при стрейфе")]
        [Tooltip("Крен при движении боком на полной скорости, градусы.")]
        public float StrafeLeanAngle = 1.5f;
        [Tooltip("Скорость, при которой наклон максимальный, м/с.")]
        [Min(0.1f)] public float StrafeLeanFullSpeed = 6f;
        [Min(0f)] public float StrafeLeanTime = 0.2f;

        [Header("Присед")]
        [Tooltip("Опускать камеру вслед за капсулой при приседе. Включить, если камера НЕ дочерняя к meshRoot и сама не опускается.")]
        public bool FollowCrouchHeight = false;
        [Range(0f, 1f)] public float CrouchHeightFollow = 1f;

        [Header("Прыжок")]
        [Tooltip("Толчок при отрыве от земли, пиковая амплитуда: x — кивок, градусы (- вверх), y — высота, метры.")]
        public Vector2 JumpKick = new(-1.2f, -0.04f);
        [Tooltip("Дополнительный множитель для прыжка из слайда.")]
        public float SlideJumpKickMultiplier = 1.4f;

        [Header("Приземление")]
        [Tooltip("Скорость падения, с которой эффект начинается, м/с.")]
        public float LandMinFallSpeed = 3f;
        [Tooltip("Скорость падения, при которой эффект максимальный, м/с.")]
        public float LandMaxFallSpeed = 20f;
        [Tooltip("Насколько камера «проседает» при самом сильном приземлении, метры.")]
        public float LandMaxDip = 0.12f;
        [Tooltip("Кивок вниз при самом сильном приземлении, градусы.")]
        public float LandMaxPitch = 4f;
        [Tooltip("Случайный крен при самом сильном приземлении, градусы.")]
        public float LandMaxRoll = 1.5f;

        [Header("Рывок (dash)")]
        [Tooltip("Всплеск FOV в начале рывка, градусы (поверх FovOffset состояния Dashing).")]
        public float DashFovPunch = 8f;
        [Tooltip("Крен в сторону рывка при рывке вбок, градусы.")]
        public float DashRollPunch = 3f;
        [Tooltip("Кивок при рывке вперёд (+ вниз) / назад, градусы.")]
        public float DashPitchPunch = 1.5f;

        [Header("Слайд")]
        [Tooltip("Толчок вниз при входе в слайд, метры.")]
        public float SlideEnterDip = -0.08f;
        [Tooltip("Наклон слайда в сторону, куда игрок смещается. 0 — всегда Roll из состояния.")]
        public float SlideSteerRoll = 3f;

        [Header("Пружины")]
        public SpringSettings PositionSpring = new(4f, 0.65f);
        public SpringSettings RotationSpring = new(5f, 0.6f);
        public SpringSettings FovSpring = new(3f, 0.8f);

        private Dictionary<MovementType, StateCameraSettings> lookup;

        public StateCameraSettings Get(MovementType state)
        {
            if (lookup == null || lookup.Count != States.Count)
            {
                lookup = new Dictionary<MovementType, StateCameraSettings>();
                foreach (var s in States) lookup[s.State] = s;
            }
            return lookup.TryGetValue(state, out var result) ? result : Default;
        }

        private void OnValidate() => lookup = null;
    }
}
