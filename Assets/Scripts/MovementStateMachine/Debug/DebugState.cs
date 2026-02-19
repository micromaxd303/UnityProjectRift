#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class DebugState : MonoBehaviour
{
    [SerializeField] private MovementStateMachine stateMachine;
    [SerializeField] private PlayerMotor motor;
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Key toggleKey = Key.F1;
    [SerializeField] private Key cycleModeKey = Key.F2;
    
    private bool showDebug = true;
    private int displayMode = 0;
    private const int DisplayModeCount = 4;
    
    // История состояний
    private List<string> stateHistory = new();
    private const int MaxHistory = 10;
    
    // График скорости
    private float[] speedHistory = new float[120];
    private int speedHistoryIndex = 0;
    private float maxRecordedSpeed = 0f;
    
    // Статистика бустов
    private float lastSlideEntrySpeed;
    private float lastSlideExitSpeed;
    private bool lastJumpWasSlideJump;
    
    // Таймеры состояний
    private float currentStateTime;
    private float lastSlideDuration;
    private float lastAirTime;
    
    // Скролл-отображение
    private float scrollDisplayTime;
    private string lastScrollDirection = "—";
    private const float ScrollDisplayDuration = 0.3f;
    
    // Кэшированные текстуры
    private Texture2D boxTexture;
    private Texture2D whiteTexture;
    
    // Кэшированные стили
    private GUIStyle cachedBoxStyle;
    private GUIStyle cachedHeaderStyle;
    private GUIStyle cachedLabelStyle;
    private GUIStyle cachedHistoryStyle;
    private GUIStyle cachedSectionStyle;
    private GUIStyle cachedKeyStyle;
    private GUIStyle cachedHintStyle;
    private bool stylesInitialized;
    
    private void Awake()
    {
        boxTexture = MakeTexture(new Color(0, 0, 0, 0.85f));
        whiteTexture = Texture2D.whiteTexture;
    }
    
    private void OnDestroy()
    {
        if (boxTexture != null)
            Destroy(boxTexture);
    }
    
    private void OnEnable()
    {
        if (stateMachine != null)
            stateMachine.OnStateChanged += OnStateChanged;
    }
    
    private void OnDisable()
    {
        if (stateMachine != null)
            stateMachine.OnStateChanged -= OnStateChanged;
    }
    
    private void OnStateChanged(MovementType from, MovementType to)
    {
        if (stateMachine == null || motor == null)
            return;
        
        stateHistory.Insert(0, $"{Time.time:F2}: {from} -> {to}");
        if (stateHistory.Count > MaxHistory)
            stateHistory.RemoveAt(stateHistory.Count - 1);
        
        if (from == MovementType.Sliding)
        {
            lastSlideDuration = currentStateTime;
            lastSlideExitSpeed = motor.Speed;
        }
        
        if (to == MovementType.Sliding)
        {
            lastSlideEntrySpeed = motor.Speed;
        }
        
        if (from == MovementType.AirControl || from == MovementType.Jumping)
        {
            lastAirTime = currentStateTime;
        }
        
        if (to == MovementType.Jumping && from == MovementType.Sliding)
        {
            lastJumpWasSlideJump = true;
        }
        else if (to == MovementType.Jumping)
        {
            lastJumpWasSlideJump = false;
        }
        
        currentStateTime = 0f;
    }
    
    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[toggleKey].wasPressedThisFrame)
                showDebug = !showDebug;
            
            if (kb[cycleModeKey].wasPressedThisFrame)
                displayMode = (displayMode + 1) % DisplayModeCount;
        }
        
        if (stateMachine == null || motor == null)
            return;
        
        currentStateTime += Time.deltaTime;
        
        if (showDebug && Time.frameCount % 2 == 0)
        {
            speedHistory[speedHistoryIndex] = motor.Speed;
            speedHistoryIndex = (speedHistoryIndex + 1) % speedHistory.Length;
            maxRecordedSpeed = Mathf.Max(maxRecordedSpeed, motor.Speed);
        }
    }
    
    #region GUI Styles
    
    private void InitStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;
        
        cachedBoxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = boxTexture }
        };
        
        cachedHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        
        cachedLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = Color.white }
        };
        
        cachedHistoryStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
        };
        
        cachedSectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.5f, 0.8f, 1f) }
        };
        
        cachedKeyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = Color.white }
        };
        
        cachedHintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
        };
    }
    
    #endregion
    
    #region OnGUI
    
    private void OnGUI()
    {
        if (!showDebug) 
            return;
        
        InitStyles();
        
        switch (displayMode)
        {
            case 0: DrawCompactPanel(); break;
            case 1: DrawFullPanel(); break;
            case 2: DrawGraphPanel(); break;
            case 3: DrawInputPanel(); break;
        }
        
        GUI.Label(new Rect(10, Screen.height - 25, 300, 20), 
            $"{toggleKey}=Toggle | {cycleModeKey}=Mode ({GetModeName()})", cachedHintStyle);
    }
    
    private string GetModeName() => displayMode switch
    {
        0 => "Compact",
        1 => "Full",
        2 => "Graph",
        3 => "Input",
        _ => "Unknown"
    };
    
    #endregion
    
    #region Panels
    
    private void DrawCompactPanel()
    {
        if (stateMachine == null || motor == null)
            return;
        
        GUI.Box(new Rect(10, 10, 200, 100), "", cachedBoxStyle);
        GUILayout.BeginArea(new Rect(20, 15, 180, 90));
        
        GUI.color = GetStateColor(stateMachine.CurrentStateType);
        GUILayout.Label($"[*] {stateMachine.CurrentStateType}", cachedHeaderStyle);
        GUI.color = Color.white;
        
        GUILayout.Label($"Speed: {motor.Speed:F1} | Y: {motor.Velocity.y:F1}", cachedLabelStyle);
        GUILayout.Label($"Ground: {(motor.IsGrounded ? "Yes" : "No")} | Angle: {motor.GroundAngle:F0}°", cachedLabelStyle);
        
        GUILayout.EndArea();
    }
    
    private void DrawFullPanel()
    {
        if (stateMachine == null || motor == null)
            return;
        
        GUI.Box(new Rect(10, 10, 300, 620), "", cachedBoxStyle);
        GUILayout.BeginArea(new Rect(20, 15, 280, 610));
        
        GUI.color = GetStateColor(stateMachine.CurrentStateType);
        GUILayout.Label($"[*] {stateMachine.CurrentStateType}", cachedHeaderStyle);
        GUI.color = Color.white;
        GUILayout.Label($"Duration: {currentStateTime:F2}s", cachedLabelStyle);
        
        GUILayout.Space(8);
        
        // Velocity
        GUILayout.Label("VELOCITY", cachedSectionStyle);
        
        float hSpeed = motor.Speed;
        float vSpeed = motor.Velocity.y;
        
        GUI.color = hSpeed > motor.SpeedThreshold ? Color.green : Color.white;
        GUILayout.Label($"Horizontal: {hSpeed:F2} m/s", cachedLabelStyle);
        GUI.color = Color.white;
        
        GUI.color = vSpeed > 0 ? Color.cyan : (vSpeed < -10 ? Color.red : Color.white);
        GUILayout.Label($"Vertical: {vSpeed:F2} m/s", cachedLabelStyle);
        GUI.color = Color.white;
        
        GUILayout.Label($"Max Recorded: {maxRecordedSpeed:F1} m/s", cachedLabelStyle);
        
        DrawSpeedBar(hSpeed, 30f, 260);
        
        GUILayout.Space(8);
        
        // Surface
        GUILayout.Label("SURFACE", cachedSectionStyle);
        GUILayout.Label($"Grounded: {(motor.IsGrounded ? "Yes" : "No")}", cachedLabelStyle);
        GUILayout.Label($"Angle: {motor.GroundAngle:F1}° | Normal: {motor.GroundNormal.y:F2}", cachedLabelStyle);
        GUILayout.Label($"Can Slide: {(motor.CanStartSlide() ? "Yes" : "No")}", cachedLabelStyle);
        
        GUILayout.Space(8);
        
        // Crouch
        GUILayout.Label("CROUCH", cachedSectionStyle);
        
        bool canStand = motor.CanStandUp();
        
        GUI.color = motor.IsCrouching ? new Color(1f, 0.5f, 0f) : Color.white;
        GUILayout.Label($"Crouching: {(motor.IsCrouching ? "Yes" : "No")} | Fully: {(motor.IsFullyCrouched ? "Yes" : "No")}", cachedLabelStyle);
        GUI.color = Color.white;
        
        GUI.color = canStand ? Color.green : Color.red;
        GUILayout.Label($"Can Stand: {(canStand ? "Yes" : "No")} | Under Ceiling: {(!canStand && motor.IsCrouching ? "Yes" : "No")}", cachedLabelStyle);
        GUI.color = Color.white;
        
        GUILayout.Label($"Height: {motor.CurrentHeight:F2} / {motor.OriginalHeight:F2}", cachedLabelStyle);
        
        DrawHeightBar(motor.CurrentHeight, motor.OriginalHeight, motor.TargetHeight, 260);
        
        GUILayout.Space(8);
        
        // Dash
        GUILayout.Label("DASH", cachedSectionStyle);
        
        GUI.color = motor.IsDashing ? Color.red : Color.white;
        GUILayout.Label($"Dashing: {(motor.IsDashing ? "Yes" : "No")} | Progress: {motor.DashProgress:P0}", cachedLabelStyle);
        GUI.color = Color.white;
        
        GUI.color = motor.CanDash ? Color.green : Color.gray;
        GUILayout.Label($"Can Dash: {(motor.CanDash ? "Yes" : "No")}", cachedLabelStyle);
        GUI.color = Color.white;
        
        GUILayout.Label($"Charges: {motor.CurrentDashCharges} / {motor.MaxDashCharges}", cachedLabelStyle);
        
        DrawDashChargesBar(motor.CurrentDashCharges, motor.MaxDashCharges, motor.NextChargeRegenProgress, 260);
        
        GUILayout.Space(8);
        
        // Boost Stats
        GUILayout.Label("BOOST STATS", cachedSectionStyle);
        
        GUI.color = lastJumpWasSlideJump ? Color.yellow : Color.white;
        GUILayout.Label($"Last Jump: {(lastJumpWasSlideJump ? "SLIDE-JUMP" : "Normal")}", cachedLabelStyle);
        GUI.color = Color.white;
        
        GUILayout.Label($"Slide Entry: {lastSlideEntrySpeed:F1} m/s", cachedLabelStyle);
        GUILayout.Label($"Slide Exit: {lastSlideExitSpeed:F1} m/s", cachedLabelStyle);
        GUILayout.Label($"Slide Duration: {lastSlideDuration:F2}s", cachedLabelStyle);
        GUILayout.Label($"Last Air Time: {lastAirTime:F2}s", cachedLabelStyle);
        
        GUILayout.Space(8);
        
        // History
        GUILayout.Label("HISTORY", cachedSectionStyle);
        int historyCount = Mathf.Min(5, stateHistory.Count);
        for (int i = 0; i < historyCount; i++)
        {
            GUILayout.Label(stateHistory[i], cachedHistoryStyle);
        }
        
        GUILayout.EndArea();
    }
    
    private void DrawGraphPanel()
    {
        if (stateMachine == null || motor == null)
            return;
        
        int graphWidth = 260;
        int graphHeight = 100;
        
        GUI.Box(new Rect(10, 10, graphWidth + 40, graphHeight + 80), "", cachedBoxStyle);
        GUILayout.BeginArea(new Rect(20, 15, graphWidth + 20, graphHeight + 70));
        
        GUI.color = GetStateColor(stateMachine.CurrentStateType);
        GUILayout.Label($"[*] {stateMachine.CurrentStateType} | {motor.Speed:F1} m/s", cachedHeaderStyle);
        GUI.color = Color.white;
        
        GUILayout.Space(5);
        
        Rect graphRect = GUILayoutUtility.GetRect(graphWidth, graphHeight);
        DrawSpeedGraph(graphRect);
        
        GUILayout.Label($"Max: {maxRecordedSpeed:F1} m/s | Threshold: {motor.SpeedThreshold:F1}", cachedLabelStyle);
        
        GUILayout.EndArea();
    }
    
    private void DrawInputPanel()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        
        GUI.Box(new Rect(10, 10, 320, 360), "", cachedBoxStyle);
        GUILayout.BeginArea(new Rect(20, 15, 300, 350));
        
        GUILayout.Label("[INPUT DEBUG]", cachedHeaderStyle);
        GUILayout.Space(3);
        
        // Move Vector
        if (inputManager != null)
        {
            Vector2 move = inputManager.MoveInput;
            bool moveActive = move.sqrMagnitude > 0.01f;
            GUI.color = moveActive ? Color.green : Color.gray;
            GUILayout.Label($"Move: ({move.x:F1}, {move.y:F1})", cachedLabelStyle);
            GUI.color = Color.white;
        }
        
        GUILayout.Space(3);
        
        // Две колонки с кнопками
        GUILayout.BeginHorizontal();
        
        // Левая колонка
        GUILayout.BeginVertical(GUILayout.Width(145));
        GUILayout.Label("MOVEMENT", cachedSectionStyle);
        if (kb != null)
        {
            DrawKey("W", kb.wKey.isPressed);
            DrawKey("A", kb.aKey.isPressed);
            DrawKey("S", kb.sKey.isPressed);
            DrawKey("D", kb.dKey.isPressed);
            DrawKey("Space", kb.spaceKey.isPressed);
            DrawKey("Shift", kb.leftShiftKey.isPressed);
            DrawKey("Ctrl", kb.leftCtrlKey.isPressed);
            DrawKey("F (Dash)", kb.fKey.isPressed);
        }
        GUILayout.EndVertical();
        
        // Правая колонка
        GUILayout.BeginVertical(GUILayout.Width(145));
        GUILayout.Label("ACTIONS", cachedSectionStyle);
        if (kb != null && mouse != null)
        {
            DrawKey("LMB", mouse.leftButton.isPressed);
            DrawKey("RMB", mouse.rightButton.isPressed);
            DrawKey("R", kb.rKey.isPressed);
            DrawKey("E", kb.eKey.isPressed);
            DrawKey("Q", kb.qKey.isPressed);
            DrawKey("V", kb.vKey.isPressed);
            DrawKey("1/2/3", kb.digit1Key.isPressed || kb.digit2Key.isPressed || kb.digit3Key.isPressed);
            DrawKey("Tab/Esc", kb.tabKey.isPressed || kb.escapeKey.isPressed);
        }
        GUILayout.EndVertical();
        
        GUILayout.EndHorizontal();
        
        GUILayout.Space(6);
        
        // Scroll
        if (mouse != null)
        {
            Vector2 scroll = mouse.scroll.ReadValue();
            
            if (scroll.y > 0.1f)
            {
                lastScrollDirection = "▲ Up";
                scrollDisplayTime = ScrollDisplayDuration;
            }
            else if (scroll.y < -0.1f)
            {
                lastScrollDirection = "▼ Down";
                scrollDisplayTime = ScrollDisplayDuration;
            }
            
            if (scrollDisplayTime > 0)
                scrollDisplayTime -= Time.deltaTime;
            
            bool scrollActive = scrollDisplayTime > 0;
            string displayText = scrollActive ? lastScrollDirection : "—";
            
            GUI.color = scrollActive ? Color.yellow : Color.gray;
            GUILayout.Label($"Scroll: {displayText}", cachedLabelStyle);
            GUI.color = Color.white;
        }
        
        // Mouse Delta
        if (mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            GUI.color = delta.sqrMagnitude > 1f ? Color.cyan : Color.gray;
            GUILayout.Label($"Mouse Δ: ({delta.x:F0}, {delta.y:F0})", cachedLabelStyle);
            GUI.color = Color.white;
        }
        
        GUILayout.EndArea();
    }
    
    #endregion
    
    #region Drawing Helpers
    
    private void DrawKey(string name, bool pressed)
    {
        GUI.color = pressed ? Color.green : new Color(0.4f, 0.4f, 0.4f);
        GUILayout.Label(pressed ? $"● {name}" : $"○ {name}", cachedKeyStyle);
        GUI.color = Color.white;
    }
    
    private void DrawSpeedBar(float speed, float maxSpeed, int width)
    {
        Rect barRect = GUILayoutUtility.GetRect(width, 8);
        
        GUI.color = new Color(0.2f, 0.2f, 0.2f);
        GUI.DrawTexture(barRect, whiteTexture);
        
        float fill = Mathf.Clamp01(speed / maxSpeed);
        GUI.color = Color.Lerp(Color.red, Color.green, fill);
        GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * fill, barRect.height), whiteTexture);
        
        float thresholdPos = motor.SpeedThreshold / maxSpeed;
        GUI.color = Color.yellow;
        GUI.DrawTexture(new Rect(barRect.x + barRect.width * thresholdPos - 1, barRect.y, 2, barRect.height), whiteTexture);
        
        GUI.color = Color.white;
    }
    
    private void DrawHeightBar(float current, float max, float target, int width)
    {
        Rect barRect = GUILayoutUtility.GetRect(width, 8);
        
        GUI.color = new Color(0.2f, 0.2f, 0.2f);
        GUI.DrawTexture(barRect, whiteTexture);
        
        float fill = Mathf.Clamp01(current / max);
        GUI.color = Color.Lerp(new Color(1f, 0.5f, 0f), Color.green, fill);
        GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * fill, barRect.height), whiteTexture);
        
        float targetPos = Mathf.Clamp01(target / max);
        GUI.color = Color.cyan;
        GUI.DrawTexture(new Rect(barRect.x + barRect.width * targetPos - 1, barRect.y, 2, barRect.height), whiteTexture);
        
        GUI.color = Color.white;
    }
    
    private void DrawDashChargesBar(int current, int max, float regenProgress, int width)
    {
        Rect barRect = GUILayoutUtility.GetRect(width, 12);
        
        float chargeWidth = barRect.width / max;
        float padding = 2f;
        
        for (int i = 0; i < max; i++)
        {
            Rect chargeRect = new Rect(
                barRect.x + i * chargeWidth + padding / 2f,
                barRect.y,
                chargeWidth - padding,
                barRect.height
            );
            
            GUI.color = new Color(0.2f, 0.2f, 0.2f);
            GUI.DrawTexture(chargeRect, whiteTexture);
            
            if (i < current)
            {
                GUI.color = Color.cyan;
                GUI.DrawTexture(chargeRect, whiteTexture);
            }
            else if (i == current && regenProgress > 0)
            {
                GUI.color = new Color(0.3f, 0.6f, 0.8f);
                Rect fillRect = new Rect(chargeRect.x, chargeRect.y, chargeRect.width * regenProgress, chargeRect.height);
                GUI.DrawTexture(fillRect, whiteTexture);
            }
        }
        
        GUI.color = Color.white;
    }
    
    private void DrawSpeedGraph(Rect rect)
    {
        GUI.color = new Color(0.1f, 0.1f, 0.1f);
        GUI.DrawTexture(rect, whiteTexture);
        
        float maxVal = Mathf.Max(maxRecordedSpeed, 1f);
        float thresholdY = rect.y + rect.height * (1 - motor.SpeedThreshold / maxVal);
        GUI.color = new Color(1f, 1f, 0f, 0.3f);
        GUI.DrawTexture(new Rect(rect.x, thresholdY, rect.width, 1), whiteTexture);
        
        GUI.color = Color.green;
        float pointWidth = rect.width / speedHistory.Length;
        
        for (int i = 0; i < speedHistory.Length - 1; i++)
        {
            int idx = (speedHistoryIndex + i) % speedHistory.Length;
            int nextIdx = (speedHistoryIndex + i + 1) % speedHistory.Length;
            
            float x1 = rect.x + i * pointWidth;
            float y1 = rect.y + rect.height * (1 - speedHistory[idx] / maxVal);
            float y2 = rect.y + rect.height * (1 - speedHistory[nextIdx] / maxVal);
            
            if (speedHistory[idx] > 0 || speedHistory[nextIdx] > 0)
            {
                GUI.DrawTexture(new Rect(x1, Mathf.Min(y1, y2), 2, Mathf.Abs(y2 - y1) + 2), whiteTexture);
            }
        }
        
        GUI.color = Color.white;
    }
    
    #endregion
    
    #region Utility
    
    private Color GetStateColor(MovementType type) => type switch
    {
        MovementType.Idle => Color.gray,
        MovementType.Walking => Color.green,
        MovementType.Sprinting => Color.yellow,
        MovementType.Jumping => Color.cyan,
        MovementType.AirControl => new Color(0.3f, 0.5f, 1f),
        MovementType.Dashing => Color.red,
        MovementType.Sliding => Color.magenta,
        MovementType.Crouching => new Color(1f, 0.5f, 0f),
        _ => Color.white
    };
    
    private Texture2D MakeTexture(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
    
    #endregion
}
#endif
