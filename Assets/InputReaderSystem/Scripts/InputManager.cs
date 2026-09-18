using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Состояние одной кнопки. Обёртка над <see cref="InputAction"/>: своего состояния не хранит,
/// каждый раз спрашивает его у Input System, поэтому копии структуры всегда равноценны.
/// </summary>
/// <remarks>
/// <para><see cref="Pressed"/> и <see cref="Released"/> привязаны к кадрам - читайте их в <c>Update</c>.
/// В <c>FixedUpdate</c> нажатие можно пропустить (в кадре не было физического тика)
/// или получить дважды (тиков было несколько).</para>
/// <para>Создавайте только через конструктор: у <c>default(InputButton)</c> внутри null,
/// и любое обращение к свойствам бросит <see cref="NullReferenceException"/>.</para>
/// </remarks>
public readonly struct InputButton
{
    private readonly InputAction action;

    /// <param name="action">Действие из сгенерированного класса <c>InputActions</c>.</param>
    public InputButton(InputAction action) => this.action = action;

    /// <summary>
    /// true ровно один кадр - тот, в котором кнопку нажали. Для разовых действий: прыжок, рывок, выстрел полуавтомата.
    /// </summary>
    /// <remarks>
    /// Срабатывает в момент физического нажатия, даже если на действии висит interaction (Hold, Tap).
    /// Остаётся true до конца кадра, даже если карту действий отключили в этом же кадре.
    /// </remarks>
    public bool Pressed => action.WasPressedThisFrame();

    /// <summary>true ровно один кадр - тот, в котором кнопку отпустили.</summary>
    public bool Released => action.WasReleasedThisFrame();

    /// <summary>
    /// true всё время, пока кнопка зажата. Для непрерывных действий: спринт, автоматический огонь.
    /// </summary>
    /// <remarks>
    /// Сбрасывается в false при отключении карты действий (см. <see cref="InputManager.SetMode"/>).
    /// После повторного включения карты уже зажатая клавиша не подхватывается -
    /// значение станет true только после нового нажатия.
    /// </remarks>
    public bool Held => action.IsPressed();

}

/// <summary>
/// Ввод перемещения игрока. Активна только в режиме <see cref="InputMode.Gameplay"/>.
/// </summary>
public class MovementInput
{
    private readonly InputActions.PlayerActions actions;

    /// <summary>Кнопки перемещения: прыжок, рывок, спринт, присед.</summary>
    public readonly InputButton Jump, Dash, Sprint, Crouch;

    public MovementInput(InputActions.PlayerActions actions)
    {
        this.actions = actions;
        Jump = new InputButton(actions.Jump);
        Dash = new InputButton(actions.Dash);
        Sprint = new InputButton(actions.Sprint);
        Crouch = new InputButton(actions.Crouch);
    }
        
    /// <summary>
    /// Направление движения: x - влево/вправо, y - назад/вперёд. <see cref="Vector2.zero"/>, если ввода нет
    /// или карта отключена.
    /// </summary>
    public Vector2 Move => actions.Move.ReadValue<Vector2>();
}

/// <summary>
/// Боевой ввод: стрельба, перезарядка, лечение, снаряжение, выбор оружия.
/// Активна только в режиме <see cref="InputMode.Gameplay"/>.
/// </summary>
public class CombatInput
{
    private readonly InputActions.PlayerActions actions;

    /// <summary>Боевые кнопки.</summary>
    public readonly InputButton Fire, Reload, Heal, Equipment1, Equipment2;

    public CombatInput(InputActions.PlayerActions actions)
    {
        this.actions = actions;
        Fire = new InputButton(actions.Fire);
        Reload = new InputButton(actions.Reload);
        Heal = new InputButton(actions.Heal);
        Equipment1 = new InputButton(actions.Equipment1);
        Equipment2 = new InputButton(actions.Equipment2);
    }

    /// <summary>
    /// Прокрутка оружия в этом кадре: +1 - следующее, -1 - предыдущее, 0 - прокрутки не было.
    /// </summary>
    /// <remarks>
    /// Отлично от нуля только в кадре, когда значение изменилось, поэтому зажатая кнопка
    /// (например, бампер геймпада) не листает оружие каждый кадр.
    /// </remarks>
    public int CycleDirection => actions.WeaponScroll.WasPerformedThisFrame()
        ? Math.Sign(actions.WeaponScroll.ReadValue<float>())
        : 0;
        
    /// <summary>
    /// Номер слота оружия, выбранного в этом кадре: 1, 2 или 3. Если слот не выбирали: -1.
    /// </summary>
    /// <remarks>
    /// Нумерация с единицы, как на клавиатуре. Для индекса массива используйте <c>SelectedSlot - 1</c>.
    /// Если в одном кадре нажато несколько клавиш слотов, побеждает слот с меньшим номером.
    /// </remarks>
    public int SelectedSlot
    {
        get
        {
            if (actions.WeaponSlot1.WasPressedThisFrame()) return 1;
            if (actions.WeaponSlot2.WasPressedThisFrame()) return 2;
            if (actions.WeaponSlot3.WasPressedThisFrame()) return 3;
            return -1;
        }
    }
}

/// <summary>
/// Кнопки взаимодействия и открытия меню: использовать, карта, инвентарь, пауза.
/// </summary>
/// <remarks>
/// Группа построена на карте Player, поэтому активна только в режиме <see cref="InputMode.Gameplay"/>.
/// Этими кнопками можно ОТКРЫТЬ меню, но не закрыть: после <c>SetMode(InputMode.UI)</c> они перестают
/// срабатывать. Закрывайте меню через <see cref="UIInput.Cancel"/>.
/// </remarks>
public class MenuInput
{
    private readonly InputActions.PlayerActions actions;

    /// <summary>Кнопки взаимодействия и меню.</summary>
    public readonly InputButton Interact, Map, Inventory, Pause;
        
    public MenuInput(InputActions.PlayerActions actions)
    {
        this.actions = actions;
        Interact = new InputButton(actions.Interact);
        Map = new InputButton(actions.Map);
        Inventory = new InputButton(actions.Inventory);
        Pause = new InputButton(actions.Pause);
    }
}

/// <summary>
/// Ввод для интерфейса: подтверждение, отмена, клики, указатель, навигация, прокрутка.
/// Активна только в режиме <see cref="InputMode.UI"/>.
/// </summary>
public class UIInput 
{
    private readonly InputActions.UIActions actions;

    /// <summary>Кнопки интерфейса.</summary>
    public readonly InputButton Cancel, Submit, LeftClick, MiddleClick, RightClick;

    public UIInput(InputActions.UIActions actions)
    {
        this.actions = actions;
        Cancel = new InputButton(actions.Cancel);
        Submit = new InputButton(actions.Submit);
        LeftClick = new InputButton(actions.Click);
        MiddleClick = new InputButton(actions.MiddleClick);
        RightClick = new InputButton(actions.RightClick);
    }
        
    /// <summary>Направление прокрутки колеса: +1 - вверх, -1 - вниз, 0 - нет прокрутки.</summary>
    public int ScrollDirection => Math.Sign(actions.ScrollWheel.ReadValue<Vector2>().y);

    /// <summary>Навигация по элементам интерфейса стрелками или стиком.</summary>
    public Vector2 ArrowNavigation => actions.Navigate.ReadValue<Vector2>();

    /// <summary>Позиция указателя в экранных координатах (пиксели, начало - левый нижний угол).</summary>
    public Vector2 Pointer => actions.Point.ReadValue<Vector2>();
}

/// <summary>Режим ввода: определяет, какая карта действий сейчас включена.</summary>
public enum InputMode
{
    /// <summary>Включена карта Player: работают <see cref="MovementInput"/>, <see cref="CombatInput"/>, <see cref="MenuInput"/>.</summary>
    Gameplay,

    /// <summary>Включена карта UI: работает только <see cref="UIInput"/>.</summary>
    UI
}

/// <summary>
/// Единая точка доступа к вводу. Доступен отовсюду через <c>GameServices.Input</c>.
/// Примеры использования - в InputSystem.md.
/// </summary>
/// <remarks>
/// Обычный класс, не MonoBehaviour: на сцену его добавлять не нужно, создаётся один раз
/// в <c>GameServices</c> до загрузки первой сцены. Ввод читается опросом (polling) -
/// менеджер ничего не хранит и не сбрасывает, состояние живёт внутри Input System.
/// </remarks>
public class InputManager : IDisposable
{
    private readonly InputActions actions;

    /// <summary>Перемещение. Активна в режиме <see cref="InputMode.Gameplay"/>.</summary>
    public MovementInput Movement { get;}

    /// <summary>Бой и выбор оружия. Активна в режиме <see cref="InputMode.Gameplay"/>.</summary>
    public CombatInput Combat { get;}

    /// <summary>Взаимодействие и открытие меню. Активна в режиме <see cref="InputMode.Gameplay"/>.</summary>
    public MenuInput Menu { get;} 

    /// <summary>Интерфейс. Активна в режиме <see cref="InputMode.UI"/>.</summary>
    public UIInput UI { get;}
    
    /// <summary>Текущий режим. Меняется только через <see cref="SetMode"/>.</summary>
    public InputMode CurrentMode { get; private set; }
    
    /// <summary>Создаёт действия и группы и включает режим <see cref="InputMode.Gameplay"/>.</summary>
    public InputManager()
    {
        actions = new InputActions();
        
        Movement = new MovementInput(actions.Player);
        Combat = new CombatInput(actions.Player);
        Menu = new MenuInput(actions.Player);
        UI = new UIInput(actions.UI);

        SetMode(InputMode.Gameplay);
    }

    /// <summary>
    /// Переключает режим ввода: отключает все карты действий и включает одну, соответствующую режиму.
    /// </summary>
    /// <remarks>
    /// Все <see cref="InputButton.Held"/> отключённой карты сразу становятся false, <c>Move</c> - нулём.
    /// Метод управляет только вводом: курсор, <c>Time.timeScale</c> и окна меню - забота вызывающего кода.
    /// </remarks>
    public void SetMode(InputMode mode)
    {
        CurrentMode = mode;
        actions.Disable();
        switch (mode)
        {  
            case InputMode.Gameplay:
                actions.Player.Enable();
                break;
            case InputMode.UI:
                actions.UI.Enable();
                break;
        }
    }

    /// <summary>
    /// Отключает и освобождает действия. Вызывается из <c>GameServices</c> при выходе из приложения;
    /// после этого экземпляром пользоваться нельзя.
    /// </summary>
    public void Dispose()
    {
        actions.Disable();
        actions.Dispose();
    }
}