using UnityEngine;

namespace Rift.CameraMotion
{
    /// <summary>Отдача и движения камеры для одного оружия. Ссылка кладётся в WeaponConfig.cameraRecoil.</summary>
    [CreateAssetMenu(fileName = "CameraRecoilProfile", menuName = "Scriptable Objects/Camera/Camera Recoil Profile")]
    public class CameraRecoilProfile : ScriptableObject
    {
        [Header("Визуальная отдача (прицел не сдвигается)")]
        [Tooltip("Подброс вверх, градусы: случайно между x и y.")]
        public Vector2 PitchKick = new(0.8f, 1.2f);
        [Tooltip("Рыскание влево/вправо, градусы: случайно от -x до x.")]
        public float YawKick = 0.3f;
        [Tooltip("Крен, градусы: случайно от -x до x.")]
        public float RollKick = 0.6f;
        [Tooltip("Камеру толкает назад, метры.")]
        public float BackKick = 0.02f;
        [Tooltip("Короткий всплеск FOV, градусы (+ шире, - приближение).")]
        public float FovKick = 0.8f;

        [Header("Реальная отдача (сдвигает прицел через PanTilt)")]
        [Tooltip("Прицел уходит вверх на столько градусов за выстрел. 0 — выключено.")]
        public Vector2 AimPitchKick = Vector2.zero;
        [Tooltip("Прицел уходит вбок, градусы: случайно от -x до x.")]
        public float AimYawKick = 0f;
        [Tooltip("Доля реальной отдачи, которая возвращается сама, когда игрок перестаёт стрелять. 0 — не возвращается.")]
        [Range(0f, 1f)] public float AimRecovery = 0f;
        [Tooltip("Скорость возврата прицела, градусы/с.")]
        public float AimRecoverySpeed = 10f;

        [Header("Тряска через Cinemachine Impulse")]
        [Tooltip("Сила импульса на выстрел. 0 — не использовать. Нужны CinemachineImpulseSource и Listener.")]
        public float ImpulseForce = 0f;

        [Header("Пружины")]
        public SpringSettings RotationSpring = new(9f, 0.55f);
        public SpringSettings PositionSpring = new(10f, 0.7f);
        public SpringSettings FovSpring = new(8f, 0.75f);

        [Header("Перезарядка")]
        [Tooltip("Клип камеры на перезарядку. Пусто — без движения камеры.")]
        public CameraMotionClip ReloadClip;
        [Tooltip("Растянуть клип на время перезарядки из WeaponConfig.rechargeTime.")]
        public bool FitReloadClipToReloadTime = true;
    }
}
