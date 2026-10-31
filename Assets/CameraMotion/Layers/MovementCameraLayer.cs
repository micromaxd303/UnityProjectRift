using UnityEngine;

namespace Rift.CameraMotion
{
    /// <summary>
    /// Анимация камеры от передвижения: покачивание в такт шагам, дыхание в покое,
    /// FOV/крен/высота по состоянию, наклон при стрейфе, толчки при прыжке,
    /// приземлении, рывке и входе в слайд.
    ///
    /// Кладётся на объект CinemachineCamera рядом с PlayerCameraMotion.
    /// </summary>
    [AddComponentMenu("RIFT/Camera Motion/Movement Camera Layer")]
    [DisallowMultipleComponent]
    public class MovementCameraLayer : MonoBehaviour, ICameraMotionLayer
    {
        [SerializeField] private MovementCameraProfile profile;

        [Tooltip("Если пусто — ищется в корне префаба, затем в сцене.")]
        [SerializeField] private PlayerMotor motor;
        [Tooltip("Если пусто — ищется в корне префаба, затем в сцене.")]
        [SerializeField] private MovementStateMachine stateMachine;

        [SerializeField] private bool affectedByIntensity = true;

        public bool IsLayerActive => isActiveAndEnabled && profile != null && motor != null && stateMachine != null;
        public bool AffectedByIntensity => affectedByIntensity;
        public MovementCameraProfile Profile { get => profile; set => profile = value; }

        // Плавно смешанные параметры текущего состояния
        private float fov, roll, pitch, height, bobAmp, bobFreq;

        // Покачивание
        private float bobPhase;
        private float bobWeight;
        private float breathTime;
        private float breathWeight;
        private float lean;

        // Толчки
        private Spring3 positionSpring;
        private Spring3 rotationSpring;
        private Spring1 fovSpring;

        // Приземление
        private bool wasGrounded = true;
        private float minAirVelocityY;

        private bool initialized;

        private void Awake()
        {
            // Камера лежит рядом с Player под общим корнем префаба, поэтому ищем от корня.
            if (motor == null) motor = transform.root.GetComponentInChildren<PlayerMotor>(true);
            if (motor == null) motor = FindFirstObjectByType<PlayerMotor>();
            if (stateMachine == null && motor != null) stateMachine = motor.GetComponent<MovementStateMachine>();
            if (stateMachine == null) stateMachine = transform.root.GetComponentInChildren<MovementStateMachine>(true);
            if (stateMachine == null) stateMachine = FindFirstObjectByType<MovementStateMachine>();
        }

        private void OnEnable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged += HandleStateChanged;
            if (motor != null) motor.OnDashStarted += HandleDashStarted;
        }

        private void OnDisable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged -= HandleStateChanged;
            if (motor != null) motor.OnDashStarted -= HandleDashStarted;
        }

        public void ResetLayer()
        {
            positionSpring.Reset();
            rotationSpring.Reset();
            fovSpring.Reset();
            bobPhase = 0f;
            bobWeight = 0f;
            breathWeight = 0f;
            lean = 0f;
            minAirVelocityY = 0f;
            wasGrounded = motor == null || motor.IsGrounded;
            initialized = false;
        }

        public CameraOffset Evaluate(float dt)
        {
            var p = profile;
            var s = p.Get(stateMachine.CurrentStateType);
            bool grounded = motor.IsGrounded;
            float speed = motor.Speed;

            // --- 1. Параметры состояния с плавным переходом ---
            if (!initialized)
            {
                fov = s.FovOffset; roll = s.Roll; pitch = s.Pitch; height = s.Height;
                bobAmp = s.BobAmplitude; bobFreq = s.BobFrequency;
                initialized = true;
            }
            float t = s.BlendInTime;
            fov     = Damp.Towards(fov, s.FovOffset, t, dt);
            roll    = Damp.Towards(roll, s.Roll, t, dt);
            pitch   = Damp.Towards(pitch, s.Pitch, t, dt);
            height  = Damp.Towards(height, s.Height, t, dt);
            bobAmp  = Damp.Towards(bobAmp, s.BobAmplitude, t, dt);
            bobFreq = Damp.Towards(bobFreq, s.BobFrequency, t, dt);

            // --- 2. Покачивание в такт шагам ---
            float speedFactor = Mathf.Clamp01(speed / p.BobFullSpeed);
            float bobTarget = grounded ? bobAmp * speedFactor : 0f;
            bobWeight = Damp.Towards(bobWeight, bobTarget, p.BobFadeTime, dt);

            // Полный цикл синуса = два шага (левая и правая нога).
            if (grounded)
                bobPhase += speed / p.StepLength * bobFreq * Mathf.PI * dt;
            bobPhase %= Mathf.PI * 2f;

            float side = Mathf.Sin(bobPhase);                       // влево-вправо, раз в два шага
            float step = (1f - Mathf.Cos(bobPhase * 2f)) * 0.5f;    // 0..1, пик на каждом шаге

            Vector3 bobPos = new Vector3(side * p.BobPosition.x, -step * p.BobPosition.y, 0f) * bobWeight;
            Vector3 bobRot = new Vector3(
                step * p.BobRotation.x,
                side * p.BobRotation.y,
                -side * p.BobRotation.z) * bobWeight;

            // --- 3. Дыхание в покое ---
            breathTime += dt;
            float breathTarget = grounded ? Mathf.Clamp01(1f - speedFactor * 2f) : 0f;
            breathWeight = Damp.Towards(breathWeight, breathTarget, 0.5f, dt);
            float bw = 2f * Mathf.PI * p.IdleBreathFrequency * breathTime;
            Vector3 breathRot = new Vector3(
                Mathf.Sin(bw) * p.IdleBreathRotation.x,
                Mathf.Sin(bw * 0.5f + 1.3f) * p.IdleBreathRotation.y,
                Mathf.Sin(bw * 0.7f + 0.4f) * p.IdleBreathRotation.z) * breathWeight;

            // --- 4. Наклон при движении боком (и «руление» в слайде) ---
            float lateral = Vector3.Dot(motor.Velocity, transform.right) / p.StrafeLeanFullSpeed;
            lateral = Mathf.Clamp(lateral, -1f, 1f);
            float leanAngle = stateMachine.CurrentStateType == MovementType.Sliding ? p.SlideSteerRoll : p.StrafeLeanAngle;
            lean = Damp.Towards(lean, -lateral * leanAngle, p.StrafeLeanTime, dt);

            // --- 5. Присед ---
            float crouch = p.FollowCrouchHeight
                ? -(motor.OriginalHeight - motor.CurrentHeight) * p.CrouchHeightFollow
                : 0f;

            // --- 6. Приземление ---
            if (!grounded)
            {
                minAirVelocityY = Mathf.Min(minAirVelocityY, motor.Velocity.y);
            }
            else if (!wasGrounded)
            {
                OnLanded(-minAirVelocityY);
                minAirVelocityY = 0f;
            }
            wasGrounded = grounded;

            // --- 7. Пружины толчков ---
            Vector3 kickPos = positionSpring.Update(Vector3.zero, p.PositionSpring, dt);
            Vector3 kickRot = rotationSpring.Update(Vector3.zero, p.RotationSpring, dt);
            float kickFov = fovSpring.Update(0f, p.FovSpring, dt);

            return new CameraOffset(
                new Vector3(0f, height + crouch, 0f) + bobPos + kickPos,
                new Vector3(pitch, 0f, roll + lean) + bobRot + breathRot + kickRot,
                fov + kickFov);
        }

        // ----- События -----

        private void OnLanded(float fallSpeed)
        {
            var p = profile;
            if (p == null) return;
            float k = Mathf.InverseLerp(p.LandMinFallSpeed, p.LandMaxFallSpeed, fallSpeed);
            if (k <= 0f) return;
            k = Mathf.SmoothStep(0f, 1f, k);

            positionSpring.AddImpulse(Vector3.down * SpringKick.VelocityForPeak(p.LandMaxDip * k, p.PositionSpring));
            float rollSign = Random.value < 0.5f ? -1f : 1f;
            rotationSpring.AddImpulse(SpringKick.VelocityForPeak(
                new Vector3(p.LandMaxPitch * k, 0f, rollSign * p.LandMaxRoll * k), p.RotationSpring));
        }

        private void HandleStateChanged(MovementType from, MovementType to)
        {
            var p = profile;
            if (p == null) return;

            switch (to)
            {
                case MovementType.Jumping:
                {
                    float m = from == MovementType.Sliding ? p.SlideJumpKickMultiplier : 1f;
                    rotationSpring.AddImpulse(SpringKick.VelocityForPeak(new Vector3(p.JumpKick.x * m, 0f, 0f), p.RotationSpring));
                    positionSpring.AddImpulse(SpringKick.VelocityForPeak(new Vector3(0f, p.JumpKick.y * m, 0f), p.PositionSpring));
                    break;
                }
                case MovementType.Sliding:
                    positionSpring.AddImpulse(SpringKick.VelocityForPeak(new Vector3(0f, p.SlideEnterDip, 0f), p.PositionSpring));
                    break;
            }
        }

        private void HandleDashStarted()
        {
            var p = profile;
            if (p == null) return;

            fovSpring.AddImpulse(SpringKick.VelocityForPeak(p.DashFovPunch, p.FovSpring));

            Vector2 dir = stateMachine != null && stateMachine.Input != null
                ? stateMachine.Input.Movement.Move
                : Vector2.up;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            rotationSpring.AddImpulse(SpringKick.VelocityForPeak(
                new Vector3(dir.y * p.DashPitchPunch, 0f, -dir.x * p.DashRollPunch), p.RotationSpring));
        }
    }
}
