using System;
using Unity.Cinemachine;
using UnityEngine;

public class PlayerMotor : MonoBehaviour
{
    
    [Header("Controller")]
    [SerializeField] private CharacterController controller;
    
    [Header("Config")]
    public MovementConfig config;
    
    [Header("Crouch Settings")]
    [SerializeField] private Transform meshRoot;
    
    [Header("Camera")]
    [SerializeField] private GameObject cinemachineCamera;
    public Vector3 Velocity { get; private set; }
    public GameObject CameraObject => cinemachineCamera;
    private CinemachinePanTilt PanTilt;
    
    private float lastGroundedTime;
    private float lastTrueGroundedTime;
    private float originalHeight;
    private Vector3 originalCenter;
    private float originalCameraY;
    private float originalMeshY;
    
    // Crouch state
    private float targetHeight;
    private float currentHeight;
    private bool wantsToCrouch;
    
    // Кэш информации о земле (обновляется раз за кадр)
    private bool wasGroundedLastFrame;
    private Vector3 cachedGroundNormal = Vector3.up;
    private float cachedGroundAngle;
    private bool cachedIsGroundedResult;
    private int lastGroundCheckFrame = -1;
    
    // Отдельный покадровый кэш для GetGroundInfo (прямой Raycast, точные нормали)
    private Vector3 cachedPreciseNormal = Vector3.up;
    private float cachedPreciseAngle;
    private bool cachedGroundInfoResult;
    private int lastGroundInfoFrame = -1;
    
    // Предаллоцированные буферы для физ-запросов (избегаем GC аллокации)
    private readonly RaycastHit[] sphereHitBuffer = new RaycastHit[8];
    private readonly RaycastHit[] rayHitBuffer = new RaycastHit[8];
    private readonly Collider[] overlapBuffer = new Collider[8];
    
    // Отслеживание столкновений со стенами (через OnControllerColliderHit)
    private bool hitWallThisFrame;
    private Vector3 lastWallNormal;
    
    // Dash state
    private int currentDashCharges;
    private float[] chargeRegenTimers;
    private float lastDashTime;
    private bool isDashing;
    private float dashTimer;
    private Vector3 dashDirection;
    private float dashSpeed;
    
    // Dash Events
    public event Action<int, int> OnDashChargesChanged; // current, max
    public event Action OnDashStarted;
    public event Action OnDashEnded;
    
    // --- Public Properties ---
    
    public bool IsGrounded
    {
        get
        {
            if (Time.frameCount != lastGroundCheckFrame)
            {
                cachedIsGroundedResult = CheckGrounded();
                lastGroundCheckFrame = Time.frameCount;
            }
            return cachedIsGroundedResult;
        }
    }
    
    public bool IsCrouching { get; private set; }
    public bool IsFullyCrouched => IsCrouching && Mathf.Abs(currentHeight - targetHeight) < 0.01f;
    public bool CanJump => Time.time - lastGroundedTime < config.CoyoteTimeDuration;
    public float TimeSinceGrounded => Time.time - lastGroundedTime;
    public float Speed => new Vector3(Velocity.x, 0, Velocity.z).magnitude;
    public float GroundAngle
    {
        get
        {
            EnsurePreciseGroundInfo();
            return cachedPreciseAngle;
        }
    }
    
    public Vector3 GroundNormal
    {
        get
        {
            EnsurePreciseGroundInfo();
            return cachedPreciseNormal;
        }
    }
    
    // Crouch debug properties TODO: (УБРАТЬ)
    public float CurrentHeight => currentHeight;
    public float OriginalHeight => originalHeight;
    public float TargetHeight => targetHeight;
    
    public float CrouchProgress
    {
        get
        {
            float range = originalHeight * (1f - config.CrouchHeightMultiplier);
            if (range < 0.001f) return 1f;
            return Mathf.Clamp01(1f - (currentHeight - targetHeight) / range);
        }
    }
    
    // Dash properties
    public int CurrentDashCharges => currentDashCharges;
    public int MaxDashCharges => config.MaxDashCharges;
    public bool IsDashing => isDashing;
    public float DashProgress => isDashing ? 1f - (dashTimer / config.DashDuration) : 0f;
    public bool CanDash => currentDashCharges > 0 && Time.time - lastDashTime >= config.DashCooldownBetweenUses && !isDashing;
    
    /// <summary>
    /// Возвращает прогресс восстановления следующего заряда (0-1)
    /// </summary>
    public float NextChargeRegenProgress
    {
        get
        {
            if (currentDashCharges >= config.MaxDashCharges) return 1f;
            
            float maxProgress = 0f;
            for (int i = 0; i < config.MaxDashCharges - currentDashCharges; i++)
            {
                if (i < chargeRegenTimers.Length)
                {
                    float progress = chargeRegenTimers[i] / config.DashChargeRegenTime;
                    if (progress > maxProgress) maxProgress = progress;
                }
            }
            return maxProgress;
        }
    }
    
    private void Awake()
    {
        if (controller == null)
        {
            Debug.LogError("[PlayerMotor] CharacterController не назначен!", this);
            enabled = false;
            return;
        }
        
        if (CameraObject == null)
        {
            Debug.LogError("[PlayerMotor] CameraObject не назначен!", this);
            
        }
        
        PanTilt = CameraObject.GetComponent<CinemachinePanTilt>();
        
        originalHeight = controller.height;
        originalCenter = controller.center;
        
        currentHeight = originalHeight;
        targetHeight = originalHeight;
        
        if (CameraObject != null && CameraObject.transform.parent == transform)
        {
            originalCameraY = CameraObject.transform.localPosition.y;
        }
        
        if (meshRoot != null)
        {
            originalMeshY = meshRoot.localPosition.y;
        }
        
        InitializeDashSystem();
    }
    
    private void Update()
    {
        if (IsGrounded)
            lastGroundedTime = Time.time;
        
        UpdateCrouchTransition();
        UpdateDashChargeRegen();
    }
    
    /// <summary>
    /// Вызывается CharacterController при столкновении во время Move().
    /// Используем для точного определения стен — нормаль почти горизонтальна (normal.y мал).
    /// Склоны и полы имеют normal.y > 0.3, поэтому не попадают сюда.
    /// Столкновения ниже stepOffset игнорируются — это ступеньки,
    /// которые CharacterController перешагивает автоматически.
    /// </summary>
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y > -0.3f && hit.normal.y < 0.3f)
        {
            // Ступеньки: точка контакта ниже stepOffset от основания капсулы — пропускаем
            float hitHeight = hit.point.y - transform.position.y;
            if (hitHeight < controller.stepOffset)
                return;
            
            hitWallThisFrame = true;
            lastWallNormal = hit.normal;
        }
    }
    
    #region Velocity Accessors
    
    /// <summary>
    /// Устанавливает горизонтальную скорость в заданном направлении.
    /// Направление нормализуется и проецируется на горизонталь автоматически.
    /// </summary>
    public void SetHorizontalVelocity(Vector3 direction, float speed)
    {
        direction.y = 0;
        if (direction.sqrMagnitude < 0.001f) return;
        direction.Normalize();
        
        var vel = Velocity;
        vel.x = direction.x * speed;
        vel.z = direction.z * speed;
        Velocity = vel;
    }
    
    /// <summary>
    /// Добавляет горизонтальный импульс в заданном направлении.
    /// </summary>
    public void AddHorizontalImpulse(Vector3 direction, float force)
    {
        direction.y = 0;
        if (direction.sqrMagnitude < 0.001f) return;
        direction.Normalize();
        
        var vel = Velocity;
        vel.x += direction.x * force;
        vel.z += direction.z * force;
        Velocity = vel;
    }
    
    /// <summary>
    /// Устанавливает вертикальную составляющую скорости.
    /// </summary>
    public void SetVerticalVelocity(float y)
    {
        var vel = Velocity;
        vel.y = y;
        Velocity = vel;
    }
    
    /// <summary>
    /// Полностью обнуляет скорость.
    /// </summary>
    public void ResetVelocity()
    {
        Velocity = Vector3.zero;
    }
    
    #endregion
    
    #region Dash System
    
    private void InitializeDashSystem()
    {
        currentDashCharges = config.MaxDashCharges;
        chargeRegenTimers = new float[config.MaxDashCharges];
    }
    
    /// <summary>
    /// Изменяет максимальное количество зарядов дэша
    /// </summary>
    public void SetMaxDashCharges(int newMax)
    {
        //if (newMax < 1) newMax = 1;
        
        int oldMax = config.MaxDashCharges;
        config.MaxDashCharges = newMax;
        
        float[] oldTimers = chargeRegenTimers;
        chargeRegenTimers = new float[newMax];
        
        for (int i = 0; i < Mathf.Min(oldTimers.Length, newMax); i++)
        {
            chargeRegenTimers[i] = oldTimers[i];
        }
        
        if (newMax > oldMax)
        {
            currentDashCharges += (newMax - oldMax);
        }
        else if (currentDashCharges > newMax)
        {
            currentDashCharges = newMax;
        }
        
        OnDashChargesChanged?.Invoke(currentDashCharges, config.MaxDashCharges);
    }
    
    /// <summary>
    /// Восстанавливает заряды уклонения
    /// </summary>
    /// <param name="amount">Количество</param>
    public void AddDashCharges(int amount)
    {
        int oldCharges = currentDashCharges;
        currentDashCharges = Mathf.Min(currentDashCharges + amount, config.MaxDashCharges);
        
        if (currentDashCharges != oldCharges)
        {
            int chargesToClear = currentDashCharges - oldCharges;
            for (int i = 0; i < chargesToClear && i < chargeRegenTimers.Length; i++)
            {
                chargeRegenTimers[i] = 0f;
            }
            
            OnDashChargesChanged?.Invoke(currentDashCharges, config.MaxDashCharges);
        }
    }
    
    /// <summary>
    /// Восстанавливает все заряды уклонения
    /// </summary>
    public void RefillDashCharges()
    {
        currentDashCharges = config.MaxDashCharges;
        for (int i = 0; i < chargeRegenTimers.Length; i++)
        {
            chargeRegenTimers[i] = 0f;
        }
        OnDashChargesChanged?.Invoke(currentDashCharges, config.MaxDashCharges);
    }
    
    private void UpdateDashChargeRegen()
    {
        if (currentDashCharges >= config.MaxDashCharges) return;
        
        chargeRegenTimers[0] += Time.deltaTime;
        
        while (currentDashCharges < config.MaxDashCharges && chargeRegenTimers[0] >= config.DashChargeRegenTime)
        {
            currentDashCharges++;
            
            for (int j = 0; j < chargeRegenTimers.Length - 1; j++)
            {
                chargeRegenTimers[j] = chargeRegenTimers[j + 1];
            }
            chargeRegenTimers[chargeRegenTimers.Length - 1] = 0f;
            
            OnDashChargesChanged?.Invoke(currentDashCharges, config.MaxDashCharges);
        }
    }
    
    /// <summary>
    /// Начинает дэш в указанном направлении. Возвращает true если дэш начался.
    /// </summary>
    public bool StartDash(Vector2 inputDirection)
    {
        if (!CanDash) return false;
        
        Vector3 direction;
        if (inputDirection.magnitude > 0.1f)
        {
            direction = GetInputDirection(inputDirection);
        }
        else
        {
            return false;
        }
        
        return StartDash(direction);
    }
    
    /// <summary>
    /// Начинает дэш в указанном мировом направлении
    /// </summary>
    public bool StartDash(Vector3 worldDirection)
    {
        if (!CanDash) return false;
        
        currentDashCharges--;
        lastDashTime = Time.time;
        
        int activeTimers = config.MaxDashCharges - currentDashCharges;
        int newTimerSlot = activeTimers - 1;
        if (newTimerSlot >= 0 && newTimerSlot < chargeRegenTimers.Length)
        {
            chargeRegenTimers[newTimerSlot] = 0f;
        }
        
        OnDashChargesChanged?.Invoke(currentDashCharges, config.MaxDashCharges);
        
        // Начинаем дэш
        isDashing = true;
        dashTimer = config.DashDuration;
        dashSpeed = config.DashDistance / config.DashDuration;
        
        // Безопасная нормализация направления (защита от нулевого вектора)
        dashDirection = worldDirection;
        dashDirection.y = 0;
        if (dashDirection.sqrMagnitude < 0.001f)
            dashDirection = transform.forward;
        else
            dashDirection.Normalize();
        
        // Обнуляем вертикальную скорость для резкости
        SetVerticalVelocity(0f);
        
        OnDashStarted?.Invoke();
        
        return true;
    }
    
    /// <summary>
    /// Обновляет состояние дэша. Вызывается из DashingState.Update()
    /// </summary>
    public void UpdateDash()
    {
        if (!isDashing) return;
        
        dashTimer -= Time.deltaTime;
        
        Vector3 dashVelocity = dashDirection * dashSpeed;
        controller.Move(dashVelocity * Time.deltaTime);
        
        SetVerticalVelocity(0f);
        
        if (dashTimer <= 0)
        {
            EndDash();
        }
    }
    
    /// <summary>
    /// Принудительно завершает дэш с сохранением импульса
    /// </summary>
    public void EndDash()
    {
        if (!isDashing) return;
        
        isDashing = false;
        dashTimer = 0;
        
        SetHorizontalVelocity(dashDirection, dashSpeed * config.DashMomentumPreserve);
        
        OnDashEnded?.Invoke();
    }
    
    /// <summary>
    /// Отменяет дэш без сохранения импульса
    /// </summary>
    public void CancelDash()
    {
        if (!isDashing) return;
        
        isDashing = false;
        dashTimer = 0;
        
        OnDashEnded?.Invoke();
    }
    
    /// <summary>
    /// Возвращает информацию о восстановлении всех зарядов
    /// </summary>
    public (int current, int max, float[] regenProgress) GetDashChargeInfo()
    {
        int regenCount = config.MaxDashCharges - currentDashCharges;
        float[] progress = new float[regenCount];
        
        for (int i = 0; i < regenCount; i++)
        {
            progress[i] = chargeRegenTimers[i] / config.DashChargeRegenTime;
        }
        
        return (currentDashCharges, config.MaxDashCharges, progress);
    }
    
    #endregion
    
    #region Ground Detection
    
    private bool CheckGrounded()
    {
        // Ранний выход: если летим вверх — точно не на земле
        if (Velocity.y > 0.5f)
            return false;
        
        bool raycastGrounded = CheckGroundedByRaycast();
        bool controllerGrounded = controller.isGrounded;
        
        if (controllerGrounded || raycastGrounded)
        {
            lastTrueGroundedTime = Time.time;
            return true;
        }
        
        if (Time.time - lastTrueGroundedTime < config.GroundedGraceTime)
        {
            return true;
        }
        
        return false;
    }
    
    private bool CheckGroundedByRaycast()
    {
        float radius = controller.radius * 0.9f;
        float checkDistance = config.GroundCheckDistance + controller.skinWidth;
        Vector3 sphereOrigin = transform.position + Vector3.up * (radius + 0.1f);
        
        // === Шаг 1: SphereCast — широкая проверка наличия земли ===
        bool sphereFoundGround = false;
        
        int hitCount = Physics.SphereCastNonAlloc(
            sphereOrigin, radius, Vector3.down, sphereHitBuffer,
            checkDistance, config.groundMask, QueryTriggerInteraction.Ignore
        );
        
        for (int i = 0; i < hitCount; i++)
        {
            ref var hit = ref sphereHitBuffer[i];
            
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform)
                continue;
            if (hit.collider == controller)
                continue;
            
            sphereFoundGround = true;
            break;
        }
        
        if (!sphereFoundGround)
        {
            cachedGroundNormal = Vector3.up;
            cachedGroundAngle = 0f;
            return false;
        }
        
        // === Шаг 2: Raycast — точная нормаль поверхности ===
        // SphereCast.hit.normal возвращает нормаль контактной точки на сфере,
        // а не нормаль поверхности. Raycast даёт точную нормаль.
        Vector3 rayOrigin = transform.position + Vector3.up * 0.15f;
        
        int rayCount = Physics.RaycastNonAlloc(
            rayOrigin, Vector3.down, rayHitBuffer,
            checkDistance + 0.2f, config.groundMask, QueryTriggerInteraction.Ignore
        );
        
        for (int i = 0; i < rayCount; i++)
        {
            ref var hit = ref rayHitBuffer[i];
            
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform)
                continue;
            if (hit.collider == controller)
                continue;
            
            cachedGroundAngle = Vector3.Angle(Vector3.up, hit.normal);
            
            if (cachedGroundAngle < 2f)
            {
                cachedGroundNormal = Vector3.up;
                cachedGroundAngle = 0f;
            }
            else
            {
                cachedGroundNormal = hit.normal;
            }
            
            return cachedGroundAngle <= config.MaxSlopeAngle;
        }
        
        // SphereCast нашёл землю, но Raycast промахнулся
        // (например, стоим на краю). Считаем grounded с дефолтной нормалью.
        cachedGroundNormal = Vector3.up;
        cachedGroundAngle = 0f;
        return true;
    }
    
    /// <summary>
    /// Гарантирует, что точные данные о поверхности актуальны для текущего кадра.
    /// Вызывается лениво из свойств GroundAngle / GroundNormal.
    /// </summary>
    private void EnsurePreciseGroundInfo()
    {
        if (Time.frameCount != lastGroundInfoFrame)
        {
            GetGroundInfo(out _, out _);
        }
    }
    
    /// <summary>
    /// Возвращает точную информацию о поверхности под игроком.
    /// Берёт ближайшее попадание.
    /// </summary>
    public bool GetGroundInfo(out Vector3 normal, out float angle)
    {
        if (Time.frameCount == lastGroundInfoFrame)
        {
            normal = cachedPreciseNormal;
            angle = cachedPreciseAngle;
            return cachedGroundInfoResult;
        }
        
        lastGroundInfoFrame = Time.frameCount;
        
        normal = Vector3.up;
        angle = 0f;
        
        Vector3 origin = transform.position + Vector3.up * 0.15f;
        
        int hitCount = Physics.RaycastNonAlloc(
            origin, Vector3.down, rayHitBuffer,
            5f, config.groundMask, QueryTriggerInteraction.Ignore
        );
        
        // Ищем ближайший валидный хит
        float closestDist = float.MaxValue;
        int closestIdx = -1;
        
        for (int i = 0; i < hitCount; i++)
        {
            ref var hit = ref rayHitBuffer[i];
            
            // Пропускаем себя
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform)
                continue;
            if (hit.collider == controller)
                continue;
            
            // Пропускаем потолки и стены: нормаль должна смотреть вверх (это пол)
            if (hit.normal.y <= 0.01f)
                continue;
            
            // Пропускаем хиты выше игрока (origin внутри коллайдера потолка)
            if (hit.point.y > transform.position.y + 0.1f)
                continue;
            
            if (hit.distance < closestDist)
            {
                closestDist = hit.distance;
                closestIdx = i;
            }
        }
        
        if (closestIdx < 0)
        {
            cachedPreciseNormal = Vector3.up;
            cachedPreciseAngle = 0f;
            cachedGroundInfoResult = false;
            return false;
        }
        
        ref var closest = ref rayHitBuffer[closestIdx];
        angle = Vector3.Angle(Vector3.up, closest.normal);
        
        if (angle < 2f)
        {
            normal = Vector3.up;
            angle = 0f;
        }
        else
        {
            normal = closest.normal;
        }
        
        cachedGroundNormal = normal;
        cachedGroundAngle = angle;
        
        cachedPreciseNormal = normal;
        cachedPreciseAngle = angle;
        cachedGroundInfoResult = true;
        return true;
    }
    
    #endregion
    
    #region Movement
    
    public void Move(Vector3 direction, float speed, float acceleration)
    {
        if (CameraObject == null) return;
        
        var cameraForward = CameraObject.transform.forward;
        cameraForward.y = 0;
        cameraForward.Normalize();
        
        var cameraRight = CameraObject.transform.right;
        cameraRight.y = 0;
        cameraRight.Normalize();
        
        var targetDirection = cameraForward * direction.z + cameraRight * direction.x;
        
        var currentHorizontal = new Vector3(Velocity.x, 0, Velocity.z);
        var targetVelocity = targetDirection * speed;
        var newVelocity = Vector3.MoveTowards(currentHorizontal, targetVelocity, acceleration * Time.deltaTime);
        
        var vel = Velocity;
        vel.x = newVelocity.x;
        vel.z = newVelocity.z;
        Velocity = vel;
    }

    public void AirMove(Vector3 direction, float airSpeed, float airAcceleration)
    {
        if (CameraObject == null) return;
        
        var cameraForward = CameraObject.transform.forward;
        var cameraRight = CameraObject.transform.right;

        cameraForward.y = 0;
        cameraRight.y = 0;
        cameraForward.Normalize();
        cameraRight.Normalize();

        var currentHorizontal = new Vector3(Velocity.x, 0, Velocity.z);
    
        if (direction.magnitude < 0.1f)
            return;

        var wishDirection = (cameraForward * direction.z + cameraRight * direction.x).normalized;
        
        float currentSpeed = currentHorizontal.magnitude;
        float targetSpeed = Mathf.Max(currentSpeed, airSpeed);
    
        var targetVelocity = wishDirection * targetSpeed;
        
        var newVelocity = Vector3.MoveTowards(
            currentHorizontal, 
            targetVelocity, 
            airAcceleration * Time.deltaTime
        );

        var vel = Velocity;
        vel.x = newVelocity.x;
        vel.z = newVelocity.z;
        Velocity = vel;
    }

    public void SlideMove(Vector3 input, float speed, float acceleration, 
        float slopeAccelMultiplier = 2f, float frictionMultiplier = 0.5f, float maxSpeedMultiplier = 1.5f,
        float steerStrength = 2f, float maxSteerAngle = 45f)
    {
        GetGroundInfo(out Vector3 normal, out float angle);
        
        var currentHorizontal = new Vector3(Velocity.x, 0, Velocity.z);
        float currentSpeed = currentHorizontal.magnitude;
        
        if (currentSpeed < 0.5f)
        {
            if (input.magnitude > 0.1f)
            {
                Vector3 wishDir = GetWorldInputDirection(input);
                var vel = Velocity;
                vel.x = wishDir.x * speed * 0.5f;
                vel.z = wishDir.z * speed * 0.5f;
                Velocity = vel;
            }
            return;
        }
        
        Vector3 currentDirection = currentHorizontal.normalized;
        
        // Руление
        if (input.magnitude > 0.1f)
        {
            Vector3 wishDir = GetWorldInputDirection(input);
            
            Vector3 right = Vector3.Cross(Vector3.up, currentDirection);
            float steerInput = Vector3.Dot(wishDir, right);
            
            float speedFactor = Mathf.Clamp01(speed / Mathf.Max(currentSpeed, 0.01f));
            float steerAmount = steerInput * steerStrength * speedFactor * Time.deltaTime;
            
            float maxSteerThisFrame = maxSteerAngle * Time.deltaTime;
            steerAmount = Mathf.Clamp(steerAmount, -maxSteerThisFrame, maxSteerThisFrame);
            
            currentDirection = Quaternion.AngleAxis(steerAmount * Mathf.Rad2Deg, Vector3.up) * currentDirection;
        }
        
        // Гравитация склона
        float slopeSteepness = Mathf.Clamp01(angle / 90f);
        Vector3 downSlope = GetDownSlopeDirection(normal);
        float slopeAlignment = downSlope != Vector3.zero ? Vector3.Dot(currentDirection, downSlope) : 0f;
        
        float baseMaxSpeed = speed * maxSpeedMultiplier;
        float slopeMaxSpeed = baseMaxSpeed * (1f + slopeSteepness * slopeAlignment);
        
        if (slopeSteepness > 0.01f)
        {
            float gravityForce = slopeSteepness * acceleration * slopeAccelMultiplier;
            currentSpeed += gravityForce * slopeAlignment * Time.deltaTime;
            
            float frictionForce;
            if (slopeAlignment > 0.1f)
            {
                frictionForce = acceleration * frictionMultiplier * 0.05f;
                
                if (currentSpeed > slopeMaxSpeed)
                {
                    float excessSpeed = currentSpeed - slopeMaxSpeed;
                    float dragForce = excessSpeed * 2f;
                    currentSpeed -= dragForce * Time.deltaTime;
                    currentSpeed = Mathf.Max(currentSpeed, slopeMaxSpeed);
                }
            }
            else if (slopeAlignment < -0.1f)
            {
                frictionForce = acceleration * frictionMultiplier * 0.5f * (1f + slopeSteepness);
            }
            else
            {
                frictionForce = acceleration * frictionMultiplier * 0.1f;
            }
            
            currentSpeed -= frictionForce * Time.deltaTime;
        }
        else
        {
            currentSpeed -= acceleration * frictionMultiplier * 0.3f * Time.deltaTime;
        }
        
        currentSpeed = Mathf.Max(currentSpeed, 0f);
        
        var slideVel = Velocity;
        slideVel.x = currentDirection.x * currentSpeed;
        slideVel.z = currentDirection.z * currentSpeed;
        Velocity = slideVel;
    }
    
    #endregion
    
    #region Gravity & Physics
    
    public void ApplyGravity()
    {
        if (isDashing) return;
        
        var vel = Velocity;
        
        if (IsGrounded && vel.y < 0)
        {
            float stickForce = config.GroundStickForce;
            
            if (cachedGroundAngle > 5f)
            {
                float slopeFactor = cachedGroundAngle / config.MaxSlopeAngle;
                stickForce *= (1f + slopeFactor * 2f);
            }
            
            if (IsCrouching)
            {
                stickForce *= 3f;
            }
            
            float speedFactor = Mathf.Clamp01(Speed / 20f);
            stickForce *= (1f + speedFactor);
            
            vel.y = -stickForce;
        }
        else
        {
            vel.y -= config.Gravity * Time.deltaTime;
        }
        
        Velocity = vel;
    }
    
    public void ApplyMovement()
    {
        if (isDashing) return;
        
        Vector3 motion = Velocity * Time.deltaTime;
        
        if (IsGrounded && Velocity.y <= 0)
        {
            motion = ProjectOnGround(motion);
            SnapToGround();
        }
        
        // Сбрасываем флаг перед Move — OnControllerColliderHit установит его при столкновении
        hitWallThisFrame = false;
        
        CollisionFlags flags = controller.Move(motion);
        
        var vel = Velocity;
        
        // Потолок: обнуляем вертикальную скорость при столкновении сверху
        if ((flags & CollisionFlags.Above) != 0 && vel.y > 0)
        {
            vel.y = 0f;
        }
        
        // Стены: проецируем скорость на плоскость стены.
        // OnControllerColliderHit отфильтровал только реальные стены (normal.y < 0.3),
        // склоны и полы сюда не попадают.
        if (hitWallThisFrame)
        {
            Vector3 horizVel = new Vector3(vel.x, 0, vel.z);
            Vector3 wallNormalH = new Vector3(lastWallNormal.x, 0, lastWallNormal.z);
            
            if (wallNormalH.sqrMagnitude > 0.001f)
            {
                wallNormalH.Normalize();
                
                // Корректируем только если движемся В стену
                if (Vector3.Dot(horizVel, wallNormalH) < 0)
                {
                    Vector3 corrected = Vector3.ProjectOnPlane(horizVel, wallNormalH);
                    vel.x = corrected.x;
                    vel.z = corrected.z;
                }
            }
        }
        
        Velocity = vel;
        wasGroundedLastFrame = IsGrounded;
    }
    
    private Vector3 ProjectOnGround(Vector3 motion)
    {
        if (cachedGroundAngle < 0.1f)
            return motion;
        
        Vector3 horizontal = new Vector3(motion.x, 0, motion.z);
        Vector3 projected = Vector3.ProjectOnPlane(horizontal, cachedGroundNormal);
        
        float originalMagnitude = horizontal.magnitude;
        if (projected.magnitude > 0.001f)
        {
            projected = projected.normalized * originalMagnitude;
        }
        
        projected.y -= config.GroundStickForce * Time.deltaTime;
        
        return projected;
    }
    
    private void SnapToGround()
    {
        if (Velocity.y > 0.1f)
            return;
        
        if (!wasGroundedLastFrame && TimeSinceGrounded > 0.15f)
            return;
        
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        
        // Используем NonAlloc + фильтрация себя
        int hitCount = Physics.RaycastNonAlloc(
            origin, Vector3.down, rayHitBuffer,
            config.SnapToGroundDistance, config.groundMask
        );
        
        for (int i = 0; i < hitCount; i++)
        {
            ref var hit = ref rayHitBuffer[i];
            
            // Фильтруем себя
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform)
                continue;
            if (hit.collider == controller)
                continue;
            
            float angle = Vector3.Angle(Vector3.up, hit.normal);
            if (angle > config.MaxSlopeAngle)
                continue;
            
            float distanceToGround = hit.distance - 0.1f;
            if (distanceToGround > 0.01f && distanceToGround < config.SnapToGroundDistance)
            {
                controller.Move(Vector3.down * distanceToGround);
                return;
            }
        }
    }
    
    public void Jump(float force)
    {
        SetVerticalVelocity(force);
        lastGroundedTime = -1f;
        lastTrueGroundedTime = -1f;
        
        // Инвалидируем кэши, чтобы следующий вызов не вернул устаревший результат
        lastGroundCheckFrame = -1;
        lastGroundInfoFrame = -1;
    }
    
    #endregion
    
    #region Crouch
    
    public float GetHeight() => controller.height;
    
    /// <summary>
    /// Начать приседание. Безопасно вызывать каждый кадр.
    /// </summary>
    public void Crouch()
    {
        if (IsCrouching) return;
        
        IsCrouching = true;
        wantsToCrouch = true;
        targetHeight = originalHeight * config.CrouchHeightMultiplier;
    }
    
    /// <summary>
    /// Попытаться встать. Проверяет наличие места над головой.
    /// </summary>
    public void ResetHeight()
    {
        if (!IsCrouching) return;
        
        wantsToCrouch = false;
        
        if (CanStandUp())
        {
            IsCrouching = false;
            targetHeight = originalHeight;
        }
    }
    
    /// <summary>
    /// Принудительно встать.
    /// </summary>
    public void ForceResetHeight()
    {
        IsCrouching = false;
        wantsToCrouch = false;
        targetHeight = originalHeight;
        currentHeight = originalHeight;
        
        controller.height = originalHeight;
        controller.center = originalCenter;
        
        if (meshRoot != null)
        {
            meshRoot.localPosition = new Vector3(
                meshRoot.localPosition.x,
                originalMeshY,
                meshRoot.localPosition.z
            );
        }
    }
    
    private void UpdateCrouchTransition()
    {
        if (!wantsToCrouch && IsCrouching && CanStandUp())
        {
            IsCrouching = false;
            targetHeight = originalHeight;
        }
        
        if (Mathf.Abs(currentHeight - targetHeight) > 0.001f)
        {
            currentHeight = Mathf.MoveTowards(currentHeight, targetHeight, config.CrouchTransitionSpeed * Time.deltaTime);
            ApplyHeight(currentHeight);
        }
    }
    
    private void ApplyHeight(float height)
    {
        controller.height = height;
        
        float heightDiff = originalHeight - height;
        
        if (meshRoot != null)
        {
            float targetY = originalMeshY - heightDiff / 2f;
            meshRoot.localPosition = new Vector3(
                meshRoot.localPosition.x,
                targetY,
                meshRoot.localPosition.z
            );
        }
    }
    
    /// <summary>
    /// Проверяет, достаточно ли места для полного роста над игроком.
    /// Работает независимо от текущего состояния приседания.
    /// </summary>
    public bool CanStandUp()
    {
        if (!IsCrouching) return true;
        
        float heightDifference = originalHeight - currentHeight;
        if (heightDifference < 0.01f)
        {
            // Уже в полный рост — проверяем, не застряли ли внутри чего-то
            float checkRadius = controller.radius * 0.9f;
            Vector3 top = transform.position + originalCenter + Vector3.up * (originalHeight / 2f - checkRadius);
            
            int overlapCount = Physics.OverlapSphereNonAlloc(
                top, checkRadius, overlapBuffer,
                config.groundMask, QueryTriggerInteraction.Ignore
            );
            
            for (int i = 0; i < overlapCount; i++)
            {
                var col = overlapBuffer[i];
                if (col.transform == transform) continue;
                if (col.transform.IsChildOf(transform)) continue;
                if (col == controller) continue;
                return false;
            }
            return true;
        }

        float capsuleRadius = controller.radius * 1f;
        
        Vector3 currentTop = transform.position + controller.center + Vector3.up * (currentHeight / 2f);
        Vector3 targetTop = transform.position + originalCenter + Vector3.up * (originalHeight / 2f);
        
        Vector3 point1 = currentTop + Vector3.up * capsuleRadius;
        Vector3 point2 = targetTop - Vector3.up * capsuleRadius;
        
        int capsuleOverlapCount = Physics.OverlapCapsuleNonAlloc(
            point1,
            point2,
            capsuleRadius,
            overlapBuffer,
            config.groundMask,
            QueryTriggerInteraction.Ignore
        );
        
        for (int i = 0; i < capsuleOverlapCount; i++)
        {
            var col = overlapBuffer[i];
            if (col.transform == transform) continue;
            if (col.transform.IsChildOf(transform)) continue;
            if (col == controller) continue;
            
            return false;
        }
        
        return true;
    }
    
    #endregion
    
    #region Utility
    
    public Vector3 GetMoveDirection()
    {
        return new Vector3(Velocity.x, 0, Velocity.z).normalized;
    }
    
    public Vector3 GetInputDirection(Vector2 input)
    {
        if (CameraObject == null) return Vector3.forward;
        
        Vector3 inputDirection = new Vector3(input.x, 0, input.y);
        
        if (inputDirection.magnitude < 0.1f)
            return Vector3.zero;
        
        inputDirection.Normalize();
        
        var cameraForward = CameraObject.transform.forward;
        cameraForward.y = 0;
        cameraForward.Normalize();
    
        var cameraRight = CameraObject.transform.right;
        cameraRight.y = 0;
        cameraRight.Normalize();
    
        return (cameraForward * inputDirection.z + cameraRight * inputDirection.x).normalized;
    }
    
    public bool CanStartSlide()
    {
        if (!GetGroundInfo(out Vector3 normal, out float angle))
            return false;
        
        Vector3 slopeRight = Vector3.Cross(normal, Vector3.up);
        if (slopeRight.sqrMagnitude < 0.001f)
            return true;
    
        Vector3 downSlope = Vector3.Cross(slopeRight, normal).normalized;
        if (downSlope.y > 0)
            downSlope = -downSlope;
    
        Vector3 moveDir = GetMoveDirection();
        if (moveDir.sqrMagnitude < 0.01f)
            return true;
        
        float alignment = Vector3.Dot(moveDir, downSlope);
        return alignment >= -0.1f;
    }
    
    private Vector3 GetWorldInputDirection(Vector3 input)
    {
        if (CameraObject == null) return Vector3.forward;
        
        Vector3 cameraForward = CameraObject.transform.forward;
        Vector3 cameraRight = CameraObject.transform.right;
        
        cameraForward.y = 0;
        cameraRight.y = 0;
        cameraForward.Normalize();
        cameraRight.Normalize();
        
        return (cameraForward * input.z + cameraRight * input.x).normalized;
    }

    private Vector3 GetDownSlopeDirection(Vector3 normal)
    {
        Vector3 slopeRight = Vector3.Cross(normal, Vector3.up);
        if (slopeRight.sqrMagnitude < 0.001f)
            return Vector3.zero;
        
        slopeRight.Normalize();
        Vector3 downSlope = Vector3.Cross(slopeRight, normal);
        
        return downSlope.y > 0 ? -downSlope : downSlope;
    }
    
    #endregion
    
    #region Teleport

    private bool isTeleporting;
    private Vector3 teleportTarget;
    private Vector2? teleportRotation;
    private bool teleportPreserveVelocity;

    public void Teleport(Vector3 position)
    {
        Teleport(position, false);
    }

    public void Teleport(Vector3 position, bool preserveVelocity)
    {
        isTeleporting = true;
        teleportTarget = position;
        teleportRotation = null;
        teleportPreserveVelocity = preserveVelocity;
    }
    /// <summary>
    /// 
    /// BUG: Не работает...
    /// 
    /// </summary>
    /// <param name="position"></param>
    /// <param name="rotation"></param>
    public void Teleport(Vector3 position, Vector2 rotation)
    {
        isTeleporting = true;
        teleportTarget = position;
        teleportRotation = rotation;
        teleportPreserveVelocity = false;
    }

    public bool ProcessTeleport()
    {
        if (!isTeleporting)
            return false;
    
        isTeleporting = false;
    
        controller.enabled = false;
    
        transform.position = teleportTarget;
        
        

        if (!teleportPreserveVelocity)
            ResetVelocity();
    
        controller.enabled = true;
    
        lastGroundedTime = -1f;
        lastTrueGroundedTime = -1f;
        lastGroundCheckFrame = -1;
        lastGroundInfoFrame = -1;
        wasGroundedLastFrame = false;
        
        ForceResetHeight();
        
        if (isDashing)
            CancelDash();
    
        return true;
    }

    #endregion
}