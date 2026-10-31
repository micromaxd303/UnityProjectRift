using Unity.Cinemachine;
using UnityEngine;

namespace Rift.CameraMotion
{
    /// <summary>
    /// Движения камеры от оружия: отдача на выстрел, клип на перезарядку.
    /// Следит за ShootingController.weapon и сам переподписывается при смене оружия.
    /// Профиль берётся из WeaponConfig.cameraRecoil, иначе — defaultProfile.
    /// </summary>
    [AddComponentMenu("RIFT/Camera Motion/Weapon Camera Layer")]
    [DisallowMultipleComponent]
    public class WeaponCameraLayer : MonoBehaviour, ICameraMotionLayer
    {
        [Tooltip("Если пусто — ищется в корне префаба.")]
        [SerializeField] private ShootingController shooting;
        [Tooltip("Профиль для оружия, у которого в WeaponConfig не задан cameraRecoil.")]
        [SerializeField] private CameraRecoilProfile defaultProfile;
        [Tooltip("Источник импульса для тряски (необязательно).")]
        [SerializeField] private CinemachineImpulseSource impulseSource;
        [SerializeField] private bool affectedByIntensity = true;

        private CinemachinePanTilt panTilt;
        private PlayerCameraMotion cameraMotion;

        private Weapon subscribed;
        private CameraClipHandle reloadHandle;

        private Spring3 rotationSpring;
        private Spring3 positionSpring;
        private Spring1 fovSpring;
        private CameraRecoilProfile springProfile;   // чьи настройки пружин сейчас используются

        private Vector2 aimDebt;          // сколько реальной отдачи ещё можно вернуть (x — тангаж, y — рыскание)
        private float lastShotTime = -999f;

        public bool IsLayerActive => isActiveAndEnabled;
        public bool AffectedByIntensity => affectedByIntensity;

        private void Awake()
        {
            panTilt = GetComponent<CinemachinePanTilt>();
            cameraMotion = GetComponent<PlayerCameraMotion>();
            if (shooting == null) shooting = transform.root.GetComponentInChildren<ShootingController>(true);
        }

        private void OnDisable() => Subscribe(null);

        private Weapon CurrentWeapon => shooting != null ? shooting.weapon : null;

        private CameraRecoilProfile ProfileFor(Weapon w)
        {
            if (w != null && w.weaponConfig != null && w.weaponConfig.cameraRecoil != null)
                return w.weaponConfig.cameraRecoil;
            return defaultProfile;
        }

        private void Subscribe(Weapon w)
        {
            if (subscribed == w) return;
            if (subscribed != null)
            {
                subscribed.Shot -= HandleShot;
                subscribed.ReloadStarted -= HandleReloadStarted;
                subscribed.ReloadFinished -= HandleReloadFinished;
            }
            reloadHandle.Stop();
            subscribed = w;
            if (subscribed != null)
            {
                subscribed.Shot += HandleShot;
                subscribed.ReloadStarted += HandleReloadStarted;
                subscribed.ReloadFinished += HandleReloadFinished;
            }
        }

        public void ResetLayer()
        {
            rotationSpring.Reset();
            positionSpring.Reset();
            fovSpring.Reset();
            aimDebt = Vector2.zero;
        }

        public CameraOffset Evaluate(float dt)
        {
            Subscribe(CurrentWeapon);

            var p = springProfile != null ? springProfile : ProfileFor(subscribed);
            if (p == null) return CameraOffset.Zero;

            RecoverAim(p, dt);

            Vector3 rot = rotationSpring.Update(Vector3.zero, p.RotationSpring, dt);
            Vector3 pos = positionSpring.Update(Vector3.zero, p.PositionSpring, dt);
            float fov = fovSpring.Update(0f, p.FovSpring, dt);
            return new CameraOffset(pos, rot, fov);
        }

        private void HandleShot(Weapon weapon)
        {
            var p = ProfileFor(weapon);
            if (p == null) return;
            springProfile = p;
            lastShotTime = Time.time;

            // Визуальная отдача — толчки пружин.
            Vector3 kickRot = new Vector3(
                -Random.Range(p.PitchKick.x, p.PitchKick.y),
                Random.Range(-p.YawKick, p.YawKick),
                Random.Range(-p.RollKick, p.RollKick));
            rotationSpring.AddImpulse(SpringKick.VelocityForPeak(kickRot, p.RotationSpring));
            positionSpring.AddImpulse(SpringKick.VelocityForPeak(new Vector3(0f, 0f, -p.BackKick), p.PositionSpring));
            fovSpring.AddImpulse(SpringKick.VelocityForPeak(p.FovKick, p.FovSpring));

            // Реальная отдача — двигаем оси PanTilt, прицел действительно уходит.
            if (panTilt != null && (p.AimPitchKick.y > 0f || p.AimYawKick > 0f))
            {
                float up = Random.Range(p.AimPitchKick.x, p.AimPitchKick.y);
                float side = Random.Range(-p.AimYawKick, p.AimYawKick);
                RotateAim(-up, side);
                aimDebt += new Vector2(up, side) * p.AimRecovery;
            }

            if (impulseSource != null && p.ImpulseForce > 0f)
                impulseSource.GenerateImpulseWithForce(p.ImpulseForce);
        }

        private void RecoverAim(CameraRecoilProfile p, float dt)
        {
            if (panTilt == null || aimDebt == Vector2.zero) return;
            // Возврат начинается, когда стрельба прекратилась.
            float cooldown = subscribed != null && subscribed.weaponConfig != null
                ? subscribed.weaponConfig.timeCooldownShoot + 0.05f : 0.1f;
            if (Time.time - lastShotTime < cooldown) return;

            Vector2 step = Vector2.MoveTowards(aimDebt, Vector2.zero, p.AimRecoverySpeed * dt);
            Vector2 delta = aimDebt - step;
            aimDebt = step;
            RotateAim(delta.x, -delta.y);
        }

        private void RotateAim(float tiltDelta, float panDelta)
        {
            // У PanTilt положительный Tilt — взгляд вниз.
            panTilt.TiltAxis.Value = panTilt.TiltAxis.ClampValue(panTilt.TiltAxis.Value + tiltDelta);
            panTilt.PanAxis.Value = panTilt.PanAxis.ClampValue(panTilt.PanAxis.Value + panDelta);
        }

        private void HandleReloadStarted(Weapon weapon)
        {
            var p = ProfileFor(weapon);
            if (p == null || p.ReloadClip == null) return;
            if (cameraMotion == null) cameraMotion = GetComponent<PlayerCameraMotion>();
            if (cameraMotion == null) return;

            float duration = p.FitReloadClipToReloadTime && weapon.weaponConfig != null
                ? weapon.weaponConfig.rechargeTime
                : -1f;
            reloadHandle.Stop();
            reloadHandle = cameraMotion.PlayClip(p.ReloadClip, duration);
        }

        private void HandleReloadFinished(Weapon weapon)
        {
            // Клип растянут на время перезарядки и завершится сам; если перезарядку прервали — плавно гасим.
            reloadHandle.Stop();
        }
    }
}
