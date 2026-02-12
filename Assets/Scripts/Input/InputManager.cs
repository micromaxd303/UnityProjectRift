using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour, InputActions.IPlayerActions, InputActions.IUIActions
{
    private InputActions inputActions;
    
    // Значения
    public Vector2 MoveInput { get; private set; }
    public Vector2 NavigateInput { get; private set; }
    public Vector2 PointPosition { get; private set; }
    
    // Нажатия (сбрасываются каждый кадр)
    public bool JumpPressed { get; private set; }
    public bool DashPressed { get; private set; }
    public bool CrouchPressed { get; private set; }
    public bool FirePressed { get; private set; }
    public bool ReloadPressed { get; private set; }
    public bool InteractPressed { get; private set; }
    public bool HealPressed { get; private set; }
    public bool Equipment1Pressed { get; private set; }
    public bool Equipment2Pressed { get; private set; }
    public bool MapPressed { get; private set; }
    public bool InventoryPressed { get; private set; }
    public bool PausePressed { get; private set; }
    
    // UI нажатия (сбрасываются каждый кадр)
    public bool SubmitPressed { get; private set; }
    public bool CancelPressed { get; private set; }
    public bool ClickPressed { get; private set; }
    public bool RightClickPressed { get; private set; }
    public bool MiddleClickPressed { get; private set; }
    
    // UI зажатые
    public bool ClickHeld { get; private set; }
    public bool RightClickHeld { get; private set; }
    
    // UI скролл
    public Vector2 ScrollWheelDelta { get; private set; }
    
    // Оружие
    public int WeaponSlotPressed { get; private set; } = -1; // -1 = ничего, 0-2 = слоты, 3 = следующее, 4 = предыдущее
    
    // Зажатые кнопки
    public bool SprintHeld { get; private set; }
    public bool CrouchHeld { get; private set; }
    public bool FireHeld { get; private set; }

    private void Awake()
    {
        inputActions = new InputActions();
        
        //inputActions = SettingManager.Instance.InputSettings.GetInputActions(); Вернуть потом!
        
        inputActions.Player.SetCallbacks(this);
        inputActions.UI.SetCallbacks(this);
    }

    public void UIEnable()
    {
        inputActions.UI.Enable();
        inputActions.Player.Disable();
    }
    
    public void UIDisable()
    {
        inputActions.UI.Disable();
        inputActions.Player.Enable();
    }
    
    private void OnEnable() => inputActions.Player.Enable();
    private void OnDisable() => inputActions.Disable();
    private void OnDestroy() => inputActions?.Dispose();

    private void LateUpdate()
    {
        // Player
        JumpPressed = false;
        DashPressed = false;
        CrouchPressed = false;
        FirePressed = false;
        ReloadPressed = false;
        InteractPressed = false;
        HealPressed = false;
        Equipment1Pressed = false;
        Equipment2Pressed = false;
        MapPressed = false;
        InventoryPressed = false;
        PausePressed = false;
        WeaponSlotPressed = -1;
        
        // UI
        SubmitPressed = false;
        CancelPressed = false;
        ClickPressed = false;
        RightClickPressed = false;
        MiddleClickPressed = false;
        ScrollWheelDelta = Vector2.zero;
    }

    #region Player Actions

    public void OnMove(InputAction.CallbackContext context)
    {
        MoveInput = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        Debug.Log($"OnJump: phase={context.phase}");
        if (context.performed)
            JumpPressed = true;
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (context.performed)
            SprintHeld = true;
        else if (context.canceled)
            SprintHeld = false;
    }

    public void OnCrouch(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            CrouchPressed = true;
            CrouchHeld = true;
        }
        else if (context.canceled)
            CrouchHeld = false;
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        if (context.performed)
            DashPressed = true;
    }

    public void OnFire(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            FirePressed = true;
            FireHeld = true;
        }
        else if (context.canceled)
            FireHeld = false;
    }

    public void OnReload(InputAction.CallbackContext context)
    {
        if (context.performed)
            ReloadPressed = true;
    }

    public void OnWeaponSlot1(InputAction.CallbackContext context)
    {
        if (context.performed)
            WeaponSlotPressed = 0;
    }

    public void OnWeaponSlot2(InputAction.CallbackContext context)
    {
        if (context.performed)
            WeaponSlotPressed = 1;
    }

    public void OnWeaponSlot3(InputAction.CallbackContext context)
    {
        if (context.performed)
            WeaponSlotPressed = 2;
    }

    public void OnWeaponScroll(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
    
        float scroll = context.ReadValue<float>();
    
        if (scroll > 0) WeaponSlotPressed = 3;      // Следующее
        else if (scroll < 0) WeaponSlotPressed = 4;  // Предыдущее
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (context.performed)
            InteractPressed = true;
    }

    public void OnHeal(InputAction.CallbackContext context)
    {
        if (context.performed)
            HealPressed = true;
    }

    public void OnEquipment1(InputAction.CallbackContext context)
    {
        if (context.performed)
            Equipment1Pressed = true;
    }

    public void OnEquipment2(InputAction.CallbackContext context)
    {
        if (context.performed)
            Equipment2Pressed = true;
    }

    public void OnMap(InputAction.CallbackContext context)
    {
        if (context.performed)
            MapPressed = true;
    }

    public void OnInventory(InputAction.CallbackContext context)
    {
        if (context.performed)
            InventoryPressed = true;
    }

    public void OnPause(InputAction.CallbackContext context)
    {
        if (context.performed)
            PausePressed = true;
    }

    #endregion

    #region UI Actions

    public void OnCancel(InputAction.CallbackContext context)
    {
        if (context.performed)
            CancelPressed = true;
    }

    public void OnSubmit(InputAction.CallbackContext context)
    {
        if (context.performed)
            SubmitPressed = true;
    }

    public void OnClick(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            ClickPressed = true;
            ClickHeld = true;
        }
        else if (context.canceled)
            ClickHeld = false;
    }

    public void OnMiddleClick(InputAction.CallbackContext context)
    {
        if (context.performed)
            MiddleClickPressed = true;
    }

    public void OnRightClick(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            RightClickPressed = true;
            RightClickHeld = true;
        }
        else if (context.canceled)
            RightClickHeld = false;
    }

    public void OnScrollWheel(InputAction.CallbackContext context)
    {
        ScrollWheelDelta = context.ReadValue<Vector2>();
    }

    public void OnPoint(InputAction.CallbackContext context)
    {
        PointPosition = context.ReadValue<Vector2>();
    }

    public void OnNavigate(InputAction.CallbackContext context)
    {
        NavigateInput = context.ReadValue<Vector2>();
    }

    #endregion
}