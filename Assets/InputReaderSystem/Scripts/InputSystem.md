# Система ввода

Обёртка над Unity Input System. Весь ввод игры читается через одну точку — `GameServices.Input`.

```csharp
if (GameServices.Input.Movement.Jump.Pressed) Jump();
```

Файлы:

| Файл | Что внутри |
|---|---|
| `InputManager.cs` | `InputButton`, группы (`MovementInput`, `CombatInput`, `MenuInput`, `UIInput`), `InputMode`, `InputManager` |
| `GameServices.cs` | Статическая точка доступа к сервисам, создаёт `InputManager` |
| `InputActions` (.inputactions + сгенерированный класс) | Биндинги клавиш. Карты действий: `Player` и `UI` |
| `DebugState.cs` | Отладочная панель (только в редакторе) |

## Как это устроено

```
GameServices.Input  (InputManager)
 ├─ Movement   Jump, Dash, Sprint, Crouch, Move          ┐
 ├─ Combat     Fire, Reload, Heal, Equipment1/2,         ├─ карта Player → режим Gameplay
 │             SelectedSlot, CycleDirection              │
 ├─ Menu       Interact, Map, Inventory, Pause           ┘
 └─ UI         Submit, Cancel, клики, Pointer,           ── карта UI → режим UI
               ArrowNavigation, ScrollDirection
```

Три идеи, на которых всё держится:

1. **Опрос (polling), а не события.** Код сам спрашивает «кнопка нажата?» в своём `Update`. Подписываться и отписываться не нужно.
2. **Менеджер ничего не хранит.** Состояние кнопок живёт внутри Input System; `InputButton` — тонкая обёртка, которая его читает. Поэтому нет флагов, которые надо сбрасывать, и нечему рассинхронизироваться.
3. **В каждый момент включена одна карта действий.** Режим `Gameplay` — карта `Player`, режим `UI` — карта `UI`. Группы выключенной карты молчат: кнопки возвращают `false`, векторы — ноль.

`InputManager` — обычный класс, не `MonoBehaviour`. На сцену его добавлять не нужно: `GameServices` создаёт его до загрузки первой сцены, так что ввод доступен уже в любом `Awake`.

## Быстрый старт

Если скрипт читает ввод каждый кадр, сохраните нужную группу в поле:

```csharp
public class PlayerMovement : MonoBehaviour
{
    private MovementInput input;

    private void Start()
    {
        input = GameServices.Input.Movement;
    }

    private void Update()
    {
        Vector2 move = input.Move;
        bool sprinting = input.Sprint.Held;

        if (input.Dash.Pressed)
            StartDash();
    }
}
```

Скрипт движения видит только `MovementInput` — про стрельбу и меню он ничего не знает и случайно их не тронет.

## Справочник

### InputButton

| Свойство | Когда `true` | Для чего |
|---|---|---|
| `Pressed` | Один кадр — в момент нажатия | Прыжок, рывок, одиночный выстрел, открытие меню |
| `Held` | Всё время, пока кнопка зажата | Спринт, автоматический огонь, присед удержанием |
| `Released` | Один кадр — в момент отпускания | Заряженный выстрел, отмена прицеливания |

### Группы

| Группа | Кнопки (`InputButton`) | Значения | Режим |
|---|---|---|---|
| `Movement` | `Jump`, `Dash`, `Sprint`, `Crouch` | `Vector2 Move` | Gameplay |
| `Combat` | `Fire`, `Reload`, `Heal`, `Equipment1`, `Equipment2` | `int SelectedSlot`, `int CycleDirection` | Gameplay |
| `Menu` | `Interact`, `Map`, `Inventory`, `Pause` | — | Gameplay |
| `UI` | `Submit`, `Cancel`, `LeftClick`, `RightClick`, `MiddleClick` | `Vector2 Pointer`, `Vector2 ArrowNavigation`, `int ScrollDirection` | UI |

Значения:

- `Move` — x: влево/вправо, y: назад/вперёд.
- `SelectedSlot` — слот, выбранный **в этом кадре**: `1`, `2`, `3`; если не выбирали: `-1`. Нумерация с единицы.
- `CycleDirection` — прокрутка оружия **в этом кадре**: `+1` следующее, `-1` предыдущее, `0` нет.
- `ScrollDirection` — колесо в UI: `+1` вверх, `-1` вниз, `0` нет.
- `Pointer` — позиция курсора в пикселях экрана.

### InputManager

| Член | Описание |
|---|---|
| `Movement`, `Combat`, `Menu`, `UI` | Группы ввода |
| `CurrentMode` | Текущий режим (только чтение) |
| `SetMode(InputMode mode)` | Переключить режим |
| `Dispose()` | Вызывается из `GameServices` при выходе. Самим вызывать не нужно |

## Примеры

### Стрельба: автомат и полуавтомат

```csharp
private void Update()
{
    var combat = GameServices.Input.Combat;

    // Автоматическое оружие: стреляем, пока зажато
    if (isAutomatic && combat.Fire.Held && Time.time >= nextShotTime)
        Shoot();

    // Полуавтомат: один выстрел на одно нажатие
    if (!isAutomatic && combat.Fire.Pressed)
        Shoot();

    if (combat.Reload.Pressed)
        StartReload();
}
```

### Заряженное действие (Pressed → Held → Released)

```csharp
private float charge;

private void Update()
{
    var fire = GameServices.Input.Combat.Fire;

    if (fire.Pressed)  charge = 0f;
    if (fire.Held)     charge += Time.deltaTime;
    if (fire.Released) ReleaseShot(charge);
}
```

### Смена оружия

```csharp
private void Update()
{
    var combat = GameServices.Input.Combat;

    int slot = combat.SelectedSlot;
    if (slot != -1)
    {
        EquipWeapon(slot - 1);          // слоты 1..3 → индексы массива 0..2
        return;
    }

    int direction = combat.CycleDirection;
    if (direction != 0)
        CycleWeapon(direction);         // +1 следующее, -1 предыдущее
}
```

Прямой выбор слота здесь важнее прокрутки. Порядок проверок — и есть приоритет.

### Прыжок, если движение считается в FixedUpdate

`Pressed` живёт один кадр, а `FixedUpdate` идёт в своём ритме: в кадре может не оказаться ни одного физического тика, и нажатие потеряется. Поэтому нажатие ловим в `Update`, а тратим в `FixedUpdate`:

```csharp
[SerializeField] private float jumpBufferTime = 0.15f;
private float jumpBufferTimer;

private void Update()
{
    if (input.Jump.Pressed)
        jumpBufferTimer = jumpBufferTime;
    else
        jumpBufferTimer -= Time.deltaTime;
}

private void FixedUpdate()
{
    if (jumpBufferTimer > 0f && isGrounded)
    {
        jumpBufferTimer = 0f;
        PerformJump();
    }
}
```

Бонус: получается jump buffering — нажатие чуть раньше приземления всё равно засчитывается.

`Held` и `Move` в `FixedUpdate` читать можно — это состояние, а не событие одного кадра.

### Меню паузы и переключение режимов

```csharp
public class PauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject window;
    private InputManager input;
    private bool isOpen;

    private void Start()
    {
        input = GameServices.Input;
    }

    private void Update()
    {
        if (!isOpen && input.Menu.Pause.Pressed)
            Open();
        else if (isOpen && input.UI.Cancel.Pressed)
            Close();
    }

    private void Open()
    {
        isOpen = true;
        window.SetActive(true);
        input.SetMode(InputMode.UI);
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
    }

    private void Close()
    {
        isOpen = false;
        window.SetActive(false);
        input.SetMode(InputMode.Gameplay);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
    }
}
```

Обратите внимание:

- **Открываем через `Menu.Pause`, закрываем через `UI.Cancel`.** После `SetMode(InputMode.UI)` карта `Player` выключена, и `Menu.Pause.Pressed` больше не сработает. Вариант `if (Menu.Pause.Pressed) Toggle();` откроет меню, но не закроет. То же относится к `Inventory` и `Map`.
- **`SetMode` управляет только вводом.** Курсор, `timeScale` и окна — ответственность того, кто переключает режим.
- При открытии меню все `Held` карты `Player` сами становятся `false`: игрок не продолжит стрелять сквозь паузу.

### Ввод в UI

```csharp
private void Update()
{
    var ui = GameServices.Input.UI;

    if (ui.Submit.Pressed) Confirm();

    Vector2 cursor = ui.Pointer;
    if (ui.LeftClick.Pressed) BeginDrag(cursor);
    if (ui.LeftClick.Released) EndDrag(cursor);

    int scroll = ui.ScrollDirection;
    if (scroll != 0) ScrollList(scroll);
}
```

Стандартные элементы Unity UI (кнопки, слайдеры) работают через EventSystem и в этом коде не нуждаются. Группа `UI` — для собственной логики: перетаскивание, горячие клавиши, нестандартные списки.

## Подводные камни

**`Pressed` и `Released` — только в `Update`.** В `FixedUpdate` нажатия теряются или дублируются. Решение — в примере с прыжком.

**`SelectedSlot` — с единицы.** Для индекса массива: `SelectedSlot - 1`. «Ничего не выбрано» — это `-1`, а не `0`.

**Группа `Menu` не работает в режиме UI.** См. пример с паузой. Если понадобится закрывать инвентарь той же клавишей, что открывает, — действие нужно продублировать в карте `UI` (в .inputactions) и добавить в `UIInput`.

**Зажатая клавиша после возврата из меню.** Игрок держит Shift, открывает и закрывает меню, не отпуская. `Sprint.Held` останется `false` до повторного нажатия: действия типа Button не подхватывают уже зажатую клавишу при включении карты. Для стрельбы это хорошо, для спринта может мешать — тогда смените тип действия Sprint на Value в .inputactions.

**Значения «этого кадра» читайте один раз.** `SelectedSlot` и `CycleDirection` — свойства, которые опрашивают Input System при каждом обращении. Сохраните результат в переменную, как в примерах, а не обращайтесь к свойству дважды.

**`OnGUI` вызывается несколько раз за кадр.** Таймеры и счётчики, завязанные на ввод, считайте в `Update`. `Held` и `Pressed` читать в `OnGUI` безопасно, но «посчитать нажатия» там не получится.

**Выход из приложения.** При выходе `GameServices` освобождает `InputManager`. Не обращайтесь к вводу из `OnDisable` / `OnDestroy` — при опросе это и не требуется.

**Assembly Definitions.** Скрипты в сборке с `.asmdef` не видят код из `Assembly-CSharp`. `InputManager`, `GameServices` и сгенерированный `InputActions` должны лежать в сборке, на которую ссылаются остальные, а у неё самой должна быть ссылка на `Unity.InputSystem`.

## Как расширять

### Новая кнопка

1. Добавьте действие в `.inputactions` (карта `Player` или `UI`), назначьте клавиши, сохраните. Класс `InputActions` перегенерируется сам.
2. Добавьте поле и строку в конструктор подходящей группы:

```csharp
public readonly InputButton Jump, Dash, Sprint, Crouch, Slide;   // + Slide

// в конструкторе
Slide = new InputButton(actions.Slide);
```

3. По желанию — строку `DrawKey("Slide", input.Movement.Slide.Held);` в `DebugState.DrawInputPanel`.

Больше ничего: ни колбэков, ни сброса флагов.

### Новое значение (ось, вектор)

Свойство, которое читает действие напрямую:

```csharp
public Vector2 Look => actions.Look.ReadValue<Vector2>();
```

Тип в `ReadValue<T>` должен совпадать с типом действия в .inputactions. Несовпадение (например, `float` для Vector2-действия) скомпилируется, но бросит исключение в игре.

### Новый режим

Например, диалоги или катсцены:

1. Добавьте значение в `enum InputMode`.
2. Добавьте `case` в `InputManager.SetMode` и включите в нём нужные карты.

Если режиму нужны собственные кнопки — заведите для него отдельную карту в .inputactions и свою группу по образцу существующих.

### Новая группа

Класс с конструктором, принимающим карту действий; свойство в `InputManager`; создание в его конструкторе. Группируйте по потребителю: что читает один и тот же скрипт — то и лежит вместе.

## Отладка

`DebugState` (работает только в редакторе):

| Клавиша | Действие |
|---|---|
| F1 | Показать / скрыть панель |
| F2 | Следующая панель: Compact → Full → Graph → Input |
| F3 | Переключить режим ввода Gameplay ↔ UI |

Панель **Input** показывает текущий режим, вектор `Move`, состояние `Held` всех кнопок по группам, последний выбранный слот, скролл и дельту мыши. Панель показывает **действия, а не клавиши**: если клавиша нажата, а индикатор не горит — отключена карта (проверьте строку `Mode`) или сбит биндинг.

F3 переключает только карты действий — курсор и пауза остаются как были. Этого достаточно, чтобы проверить, что группы включаются и отключаются правильно.

## Частые вопросы

**Ввод не работает вообще.** Проверьте `Mode` на панели Input. Затем — Project Settings → Input System Package → Update Mode: должен быть Dynamic Update, иначе покадровые `Pressed` не совпадут с `Update`.

**`NullReferenceException` при обращении к кнопке.** Кнопка создана не через конструктор (`default(InputButton)`) или добавлено поле в группу, но забыта строка в конструкторе.

**`The name 'GameServices' does not exist`.** Скрипт лежит в другой сборке — см. раздел про Assembly Definitions. Если ошибка только в IDE, а консоль Unity чистая: Preferences → External Tools → Regenerate project files.

**Действие срабатывает много раз за одно нажатие.** Используется `Held` там, где нужен `Pressed`.
