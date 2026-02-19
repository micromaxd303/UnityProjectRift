using System;
using UnityEngine;

public class PlayerMotor : MonoBehaviour
{
    [SerializeField] private CharacterController controller;
    
    [Header("Settings")]
    [SerializeField] private float gravity = 20f;
    [SerializeField] private float groundStickForce = 5f;
    [SerializeField] private float speedThreshold = 5.5f;
    
    [Header("Ground Detection")]
    [SerializeField] private float groundCheckDistance = 0.3f;
    [SerializeField] private float maxSlopeAngle = 45f;
    [SerializeField] private float snapToGroundDistance = 0.5f;
    [SerializeField] private float groundedGraceTime = 0.1f;
    [SerializeField] private LayerMask groundMask = ~0;
    
    [Header("Coyote Time")]
    [SerializeField] private float coyoteTimeDuration = 0.15f;
    
    [Header("Crouch Settings")]
    [SerializeField] private float crouchHeightMultiplier = 0.5f;
    [SerializeField] private float crouchTransitionSpeed = 10f;
    [SerializeField] private Transform meshRoot; // Корень модели персонажа
    
    [Header("Dash Settings")]
    [SerializeField] private int maxDashCharges = 2;
    [SerializeField] private float dashChargeRegenTime = 2f;
    [SerializeField] private float dashDistance = 8f;
    [SerializeField] private float dashDuration = 0.1f; // Короткий для резкости
    [SerializeField] private float dashCooldownBetweenUses = 0.1f; // Минимальная пауза между дэшами
    [SerializeField] private float dashMomentumPreserve = 0.3f; // Сколько импульса сохраняется после дэша (0-1)
    
    public Vector3 Velocity;
    public GameObject CameraObject;
    
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
    
    // Кэш информации о земле
    private bool wasGroundedLastFrame;
    private Vector3 cachedGroundNormal = Vector3.up;
    private float cachedGroundAngle;
    
    // Dash state
    private int currentDashCharges;
    private float[] chargeRegenTimers; // Индивидуальные таймеры для каждого заряда
    private float lastDashTime;
    private bool isDashing;
    private float dashTimer;
    private Vector3 dashDirection;
    private float dashSpeed;
    
    // Dash Events
    public event Action<int, int> OnDashChargesChanged; // current, max
    public event Action OnDashStarted;
    public event Action OnDashEnded;
    
    public bool IsGrounded => CheckGrounded();
    public bool IsCrouching { get; private set; }
    public bool IsFullyCrouched => IsCrouching && Mathf.Abs(currentHeight - targetHeight) < 0.01f;
    public bool CanJump => Time.time - lastGroundedTime < coyoteTimeDuration;
    public float TimeSinceGrounded => Time.time - lastGroundedTime;
    public float Speed => new Vector3(Velocity.x, 0, Velocity.z).magnitude;
    public float SpeedThreshold => speedThreshold;
    public float GroundAngle => cachedGroundAngle;
    public Vector3 GroundNormal => cachedGroundNormal;
    
    // Crouch debug properties
    public float CurrentHeight => currentHeight;
    public float OriginalHeight => originalHeight;
    public float TargetHeight => targetHeight;
    public float CrouchProgress => Mathf.Clamp01(1f - (currentHeight - targetHeight) / (originalHeight - originalHeight * crouchHeightMultiplier));
    
    // Dash properties
    public int CurrentDashCharges => currentDashCharges;
    public int MaxDashCharges => maxDashCharges;
    public bool IsDashing => isDashing;
    public float DashProgress => isDashing ? 1f - (dashTimer / dashDuration) : 0f;
    public bool CanDash => currentDashCharges > 0 && Time.time - lastDashTime >= dashCooldownBetweenUses && !isDashing;
    
    /// <summary>
    /// Возвращает прогресс восстановления следующего заряда (0-1)
    /// </summary>
    public float NextChargeRegenProgress
    {
        get
        {
            if (currentDashCharges >= maxDashCharges) return 1f;
            
            // Находим заряд который восстанавливается дольше всего (ближе всего к завершению)
            float maxProgress = 0f;
            for (int i = 0; i < maxDashCharges - currentDashCharges; i++)
            {
                if (i < chargeRegenTimers.Length)
                {
                    float progress = chargeRegenTimers[i] / dashChargeRegenTime;
                    if (progress > maxProgress) maxProgress = progress;
                }
            }
            return maxProgress;
        }
    }
    
    private void Awake()
    {
        originalHeight = controller.height;
        // Сохраняем оригинальный центр как он настроен в инспекторе
        originalCenter = controller.center;
        
        currentHeight = originalHeight;
        targetHeight = originalHeight;
        
        // Запоминаем позицию камеры если она дочерний объект
        if (CameraObject != null && CameraObject.transform.parent == transform)
        {
            originalCameraY = CameraObject.transform.localPosition.y;
        }
        
        // Запоминаем и сбрасываем позицию меша
        if (meshRoot != null)
        {
            originalMeshY = meshRoot.localPosition.y;
        }
        
        // Инициализация дэша
        InitializeDashSystem();
    }
    
    private void Update()
    {
        if (IsGrounded)
            lastGroundedTime = Time.time;
        
        // Плавный переход высоты
        UpdateCrouchTransition();
        
        // Восстановление зарядов дэша
        UpdateDashChargeRegen();
    }
    
    #region Dash System
    
    private void InitializeDashSystem()
    {
        currentDashCharges = maxDashCharges;
        chargeRegenTimers = new float[maxDashCharges];
    }
    
    /// <summary>
    /// Изменяет максимальное количество зарядов дэша
    /// </summary>
    public void SetMaxDashCharges(int newMax)
    {
        if (newMax < 1) newMax = 1;
        
        int oldMax = maxDashCharges;
        maxDashCharges = newMax;
        
        // Пересоздаём массив таймеров
        float[] oldTimers = chargeRegenTimers;
        chargeRegenTimers = new float[newMax];
        
        // Копируем существующие таймеры
        for (int i = 0; i < Mathf.Min(oldTimers.Length, newMax); i++)
        {
            chargeRegenTimers[i] = oldTimers[i];
        }
        
        // Если добавились заряды — даём их сразу
        if (newMax > oldMax)
        {
            currentDashCharges += (newMax - oldMax);
        }
        // Если убавились — ограничиваем текущие
        else if (currentDashCharges > newMax)
        {
            currentDashCharges = newMax;
        }
        
        OnDashChargesChanged?.Invoke(currentDashCharges, maxDashCharges);
    }
    
    /// <summary>
    /// Добавляет заряды дэша (например, от способности)
    /// </summary>
    public void AddDashCharges(int amount)
    {
        int oldCharges = currentDashCharges;
        currentDashCharges = Mathf.Min(currentDashCharges + amount, maxDashCharges);
        
        if (currentDashCharges != oldCharges)
        {
            // Убираем таймеры восстановления для добавленных зарядов
            int chargesToClear = currentDashCharges - oldCharges;
            for (int i = 0; i < chargesToClear && i < chargeRegenTimers.Length; i++)
            {
                chargeRegenTimers[i] = 0f;
            }
            
            OnDashChargesChanged?.Invoke(currentDashCharges, maxDashCharges);
        }
    }
    
    /// <summary>
    /// Полностью восстанавливает заряды дэша
    /// </summary>
    public void RefillDashCharges()
    {
        currentDashCharges = maxDashCharges;
        for (int i = 0; i < chargeRegenTimers.Length; i++)
        {
            chargeRegenTimers[i] = 0f;
        }
        OnDashChargesChanged?.Invoke(currentDashCharges, maxDashCharges);
    }
    
    private void UpdateDashChargeRegen()
    {
        if (currentDashCharges >= maxDashCharges) return;
        
        // Тикает только первый таймер (самый старый, восстановится первым)
        chargeRegenTimers[0] += Time.deltaTime;
        
        // Проверяем первый таймер
        while (currentDashCharges < maxDashCharges && chargeRegenTimers[0] >= dashChargeRegenTime)
        {
            currentDashCharges++;
            
            for (int j = 0; j < chargeRegenTimers.Length - 1; j++)
            {
                chargeRegenTimers[j] = chargeRegenTimers[j + 1];
            }
            chargeRegenTimers[chargeRegenTimers.Length - 1] = 0f;
            
            OnDashChargesChanged?.Invoke(currentDashCharges, maxDashCharges);
        }
    }
    
    /// <summary>
    /// Начинает дэш в указанном направлении. Возвращает true если дэш начался.
    /// </summary>
    public bool StartDash(Vector2 inputDirection)
    {
        if (!CanDash) return false;
        
        // Определяем направление
        Vector3 direction;
        if (inputDirection.magnitude > 0.1f)
        {
            direction = GetInputDirection(inputDirection);
        }
        else
        {
            direction = transform.forward;
        }
        
        return StartDash(direction);
    }
    
    /// <summary>
    /// Начинает дэш в указанном мировом направлении
    /// </summary>
    public bool StartDash(Vector3 worldDirection)
    {
        if (!CanDash) return false;
        // Тратим заряд
        currentDashCharges--;
        lastDashTime = Time.time;
        
        // Добавляем новый таймер восстановления в конец очереди активных таймеров
        // Количество активных таймеров = количество недостающих зарядов
        int activeTimers = maxDashCharges - currentDashCharges;
        // Новый таймер идёт в позицию (activeTimers - 1), так как мы только что уменьшили currentDashCharges
        int newTimerSlot = activeTimers - 1;
        if (newTimerSlot >= 0 && newTimerSlot < chargeRegenTimers.Length)
        {
            chargeRegenTimers[newTimerSlot] = 0f;
        }
        
        OnDashChargesChanged?.Invoke(currentDashCharges, maxDashCharges);
        
        // Начинаем дэш
        isDashing = true;
        dashTimer = dashDuration;
        dashSpeed = dashDistance / dashDuration; // Пересчитываем для актуальных значений
        dashDirection = worldDirection.normalized;
        dashDirection.y = 0; // Горизонтальный дэш
        dashDirection.Normalize();
        
        // Обнуляем вертикальную скорость для резкости
        Velocity.y = 0;
        
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
        
        // Применяем движение дэша
        Vector3 dashVelocity = dashDirection * dashSpeed;
        controller.Move(dashVelocity * Time.deltaTime);
        
        // Держим вертикальную скорость на нуле во время дэша
        Velocity.y = 0;
        
        // Дэш завершён
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
        
        // Сохраняем импульс от дэша
        Velocity.x = dashDirection.x * dashSpeed * dashMomentumPreserve;
        Velocity.z = dashDirection.z * dashSpeed * dashMomentumPreserve;
        
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
        int regenCount = maxDashCharges - currentDashCharges;
        float[] progress = new float[regenCount];
        
        for (int i = 0; i < regenCount; i++)
        {
            progress[i] = chargeRegenTimers[i] / dashChargeRegenTime;
        }
        
        return (currentDashCharges, maxDashCharges, progress);
    }
    
    #endregion
    
    #region Ground Detection
    
    private bool CheckGrounded()
    {
        bool raycastGrounded = CheckGroundedByRaycast();
        
        if (Velocity.y > 0.5f)
            return false;
        
        bool controllerGrounded = controller.isGrounded;
        
        if (controllerGrounded || raycastGrounded)
        {
            lastTrueGroundedTime = Time.time;
            return true;
        }
        
        if (Time.time - lastTrueGroundedTime < groundedGraceTime)
        {
            return true;
        }
        
        return false;
    }
    
    private bool CheckGroundedByRaycast()
    {
        float radius = controller.radius * 0.9f;
        float checkDistance = groundCheckDistance + controller.skinWidth;
        Vector3 origin = transform.position + Vector3.up * (radius + 0.1f);
        
        // SphereCastAll чтобы пропустить свои коллайдеры
        var hits = Physics.SphereCastAll(origin, radius, Vector3.down, checkDistance, groundMask, QueryTriggerInteraction.Ignore);
        
        foreach (var hit in hits)
        {
            // Пропускаем себя
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
            
            return cachedGroundAngle <= maxSlopeAngle;
        }
        
        // Fallback на простой Raycast
        var rayHits = Physics.RaycastAll(transform.position + Vector3.up * 0.15f, Vector3.down, checkDistance + 0.2f, groundMask, QueryTriggerInteraction.Ignore);
        
        foreach (var hit in rayHits)
        {
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
            
            return cachedGroundAngle <= maxSlopeAngle;
        }
        
        cachedGroundNormal = Vector3.up;
        cachedGroundAngle = 0f;
        
        return false;
    }
    
    public bool GetGroundInfo(out Vector3 normal, out float angle)
    {
        normal = cachedGroundNormal;
        angle = cachedGroundAngle;
        
        // Raycast от позиции игрока вниз
        Vector3 origin = transform.position + Vector3.up * 0.15f;
        
        // Используем RaycastAll чтобы пропустить свои коллайдеры
        var hits = Physics.RaycastAll(origin, Vector3.down, 5f, groundMask, QueryTriggerInteraction.Ignore);
        
        foreach (var hit in hits)
        {
            // Пропускаем себя и своих детей
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform)
                continue;
            
            // Пропускаем CharacterController
            if (hit.collider == controller)
                continue;
            
            angle = Vector3.Angle(Vector3.up, hit.normal);
            
            if (angle < 2f)
            {
                normal = Vector3.up;
                angle = 0f;
            }
            else
            {
                normal = hit.normal;
            }
            
            cachedGroundNormal = normal;
            cachedGroundAngle = angle;
            return true;
        }
        
        return false;
    }
    
    #endregion
    
    #region Movement
    
    public void Move(Vector3 direction, float speed, float acceleration)
    {
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
        
        Velocity.x = newVelocity.x;
        Velocity.z = newVelocity.z;
    }

    public void AirMove(Vector3 direction, float airSpeed, float airAcceleration)
    {
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

        Velocity.x = newVelocity.x;
        Velocity.z = newVelocity.z;
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
                Velocity.x = wishDir.x * speed * 0.5f;
                Velocity.z = wishDir.z * speed * 0.5f;
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
            
            float speedFactor = Mathf.Clamp01(speed / currentSpeed);
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
        
        Velocity.x = currentDirection.x * currentSpeed;
        Velocity.z = currentDirection.z * currentSpeed;
    }
    
    #endregion
    
    #region Gravity & Physics
    
    public void ApplyGravity()
    {
        // Во время дэша гравитация не применяется
        if (isDashing) return;
        
        if (IsGrounded && Velocity.y < 0)
        {
            float stickForce = groundStickForce;
            
            if (cachedGroundAngle > 5f)
            {
                float slopeFactor = cachedGroundAngle / maxSlopeAngle;
                stickForce *= (1f + slopeFactor * 2f);
            }
            
            if (IsCrouching)
            {
                stickForce *= 3f;
            }
            
            float speedFactor = Mathf.Clamp01(Speed / 20f);
            stickForce *= (1f + speedFactor);
            
            Velocity.y = -stickForce;
        }
        else
        {
            Velocity.y -= gravity * Time.deltaTime;
        }
    }
    
    public void ApplyMovement()
    {
        // Во время дэша движение управляется через UpdateDash
        if (isDashing) return;
        
        Vector3 motion = Velocity * Time.deltaTime;
        
        if (IsGrounded && Velocity.y <= 0)
        {
            CheckGroundedByRaycast();
            
            motion = ProjectOnGround(motion);
            SnapToGround();
        }
        
        controller.Move(motion);
        
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
        
        projected.y -= groundStickForce * Time.deltaTime;
        
        return projected;
    }
    
    private void SnapToGround()
    {
        if (Velocity.y > 0.1f)
            return;
        
        if (!wasGroundedLastFrame && TimeSinceGrounded > 0.15f)
            return;
        
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, snapToGroundDistance, groundMask))
        {
            float angle = Vector3.Angle(Vector3.up, hit.normal);
            if (angle > maxSlopeAngle)
                return;
            
            float distanceToGround = hit.distance - 0.1f;
            if (distanceToGround > 0.01f && distanceToGround < snapToGroundDistance)
            {
                controller.Move(Vector3.down * distanceToGround);
            }
        }
    }
    
    public void Jump(float force)
    {
        Velocity.y = force;
        lastGroundedTime = -1f;
        lastTrueGroundedTime = -1f;
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
        targetHeight = originalHeight * crouchHeightMultiplier;
    }
    
    /// <summary>
    /// Попытаться встать. Проверяет наличие места над головой.
    /// </summary>
    public void ResetHeight()
    {
        if (!IsCrouching) return;
        
        wantsToCrouch = false;
        
        // Проверяем, можно ли встать
        if (CanStandUp())
        {
            IsCrouching = false;
            targetHeight = originalHeight;
        }
        // Если нельзя — остаёмся в приседе, попробуем в следующем кадре
    }
    
    /// <summary>
    /// Принудительно встать (для особых случаев, например респавна).
    /// </summary>
    public void ForceResetHeight()
    {
        IsCrouching = false;
        wantsToCrouch = false;
        targetHeight = originalHeight;
        currentHeight = originalHeight;
        
        controller.height = originalHeight;
        controller.center = originalCenter;
        
        // Восстанавливаем позицию меша
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
        // Если хотим встать, но не можем — продолжаем пытаться
        if (!wantsToCrouch && IsCrouching && CanStandUp())
        {
            IsCrouching = false;
            targetHeight = originalHeight;
        }
        
        // Плавный переход высоты
        if (Mathf.Abs(currentHeight - targetHeight) > 0.001f)
        {
            float previousHeight = currentHeight;
            currentHeight = Mathf.MoveTowards(currentHeight, targetHeight, crouchTransitionSpeed * Time.deltaTime);
            
            ApplyHeight(currentHeight);
            
            // Компенсируем изменение высоты, чтобы ноги оставались на месте
            // (CharacterController.center меняется, но transform.position — нет)
        }
    }
    
    private void ApplyHeight(float height)
    {
        controller.height = height;
        
        // Центр смещается пропорционально изменению высоты
        // При приседании центр опускается вниз
        float heightDiff = originalHeight - height;
        controller.center = new Vector3(
            originalCenter.x,
            originalCenter.y - heightDiff / 2f,
            originalCenter.z
        );
        
        // Смещаем меш вниз при приседании
        if (meshRoot != null)
        {
            float targetY = originalMeshY - heightDiff / 2f;
            meshRoot.localPosition = new Vector3(
                meshRoot.localPosition.x,
                targetY,
                meshRoot.localPosition.z
            );
        }
        
        // Двигаем камеру вместе с высотой
        if (CameraObject != null && CameraObject.transform.parent == transform)
        {
            float heightRatio = height / originalHeight;
            float newCameraY = originalCameraY * heightRatio;
            CameraObject.transform.localPosition = new Vector3(
                CameraObject.transform.localPosition.x,
                newCameraY,
                CameraObject.transform.localPosition.z
            );
        }
    }
    
    /// <summary>
    /// Проверяет, есть ли место чтобы встать.
    /// </summary>
    public bool CanStandUp()
    {
        if (!IsCrouching) return true;
        
        float heightDifference = originalHeight - currentHeight;
        if (heightDifference < 0.01f) return true;
        
        float checkRadius = controller.radius * 0.8f;
        
        // Проверяем от верха текущего коллайдера
        // center.y - это смещение центра от transform.position
        // верх коллайдера = transform.position + center + (height/2) * up
        Vector3 currentTop = transform.position + controller.center + Vector3.up * (currentHeight / 2f);
        Vector3 targetTop = transform.position + originalCenter + Vector3.up * (originalHeight / 2f);
        
        // Используем OverlapCapsule чтобы проверить всё пространство между текущей и целевой высотой
        // Нижняя точка капсулы = текущий верх, верхняя = целевой верх
        Vector3 point1 = currentTop + Vector3.up * checkRadius;
        Vector3 point2 = targetTop - Vector3.up * checkRadius;
        
        // Проверяем на все слои кроме игрока (слой 6)
        int layerMask = ~(1 << 6);
        
        Collider[] colliders = Physics.OverlapCapsule(
            point1,
            point2,
            checkRadius,
            layerMask,
            QueryTriggerInteraction.Ignore
        );
        
        // Фильтруем свои коллайдеры
        foreach (var col in colliders)
        {
            if (col.transform == transform) continue;
            if (col.transform.IsChildOf(transform)) continue;
            if (col == controller) continue;
            
            // Нашли препятствие
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Проверяет, находимся ли мы под низким потолком.
    /// </summary>
    public bool IsUnderLowCeiling()
    {
        return IsCrouching && !CanStandUp();
    }
    
    #endregion
    
    #region Utility
    
    public Vector3 GetMoveDirection()
    {
        return new Vector3(Velocity.x, 0, Velocity.z).normalized;
    }
    
    public Vector3 GetInputDirection(Vector2 input)
    {
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
    private Quaternion? teleportRotation;
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

    public void Teleport(Vector3 position, Quaternion rotation)
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
    
        if (teleportRotation.HasValue)
            transform.rotation = teleportRotation.Value;
    
        if (!teleportPreserveVelocity)
            Velocity = Vector3.zero;
    
        controller.enabled = true;
    
        lastGroundedTime = -1f;
        lastTrueGroundedTime = -1f;
        wasGroundedLastFrame = false;
        
        // Сбрасываем приседание при телепорте
        ForceResetHeight();
        
        // Отменяем дэш при телепорте
        if (isDashing)
            CancelDash();
    
        return true;
    }

    #endregion
    
}