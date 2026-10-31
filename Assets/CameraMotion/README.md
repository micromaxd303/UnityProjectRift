# RIFT Camera Motion

Процедурная анимация камеры поверх Cinemachine 3 (CinemachineCamera).

## Как это устроено

`PlayerCameraMotion` — расширение CinemachineCamera. Каждый кадр оно опрашивает все слои
(компоненты с `ICameraMotionLayer` на том же объекте), складывает их смещения и добавляет
к камере после стадии Noise. Смещения идут в PositionCorrection/OrientationCorrection, поэтому:

- трансформ объекта **CinemachineCamera** остаётся «чистым» (только PanTilt): от него считаются
  направление движения (PlayerMotor) и луч выстрела (Weapon.aimOrigin);
- **MainCamera** (и Weapons внутри неё) получает итоговую картинку с покачиванием.

Слои:

| Слой | Что делает |
|---|---|
| `MovementCameraLayer` | покачивание в такт шагам, дыхание в покое, FOV/крен/высота по состоянию, наклон при стрейфе, толчки: прыжок, приземление, рывок, вход в слайд |
| `WeaponCameraLayer` | отдача на выстрел (визуальная и, по желанию, реальная через PanTilt), клип на перезарядку, Impulse |
| `CameraClipLayer` | проигрывает `CameraMotionClip` — для снаряжения и любых разовых «жестов» |

Настройки лежат в ScriptableObject:

- `MovementCameraProfile` — по одному на игрока, список состояний `MovementType` + общие параметры;
- `CameraRecoilProfile` — по одному на оружие, ссылка в `WeaponConfig.cameraRecoil`;
- `CameraMotionClip` — кривые для перезарядки/снаряжения (в инспекторе есть пункт контекстного
  меню «Заполнить примером»).

## Установка

1. Create → Scriptable Objects → Camera → **Movement Camera Profile**, **Camera Recoil Profile**,
   **Camera Motion Clip** (для перезарядки: ⋮ → «Заполнить примером: перезарядка»).
   В Recoil Profile укажи Reload Clip.
2. На объект **CinemachineCamera** добавь:
   - `RIFT Player Camera Motion` (Add Extension в инспекторе CinemachineCamera или Add Component);
   - `Movement Camera Layer` → поле Profile = Movement Camera Profile;
   - `Weapon Camera Layer` → Default Profile = Camera Recoil Profile;
   - `Camera Clip Layer` (необязательно, добавится сам при первом клипе).
   PlayerMotor, MovementStateMachine и ShootingController найдутся сами в корне префаба.
3. На оружии (WeaponRaycast) в поле **Aim Origin** перетащи объект CinemachineCamera.
4. (Тряска) Для взрывов/попаданий: на CinemachineCamera добавь `CinemachineImpulseListener`,
   на источник — `CinemachineImpulseSource`, вызывай `GenerateImpulse()`. Для выстрела укажи
   Impulse Source в Weapon Camera Layer и Impulse Force в Recoil Profile.

## Присед

Камера лежит в «Камера холдер», а не внутри Player, поэтому сама при приседе может не опускаться.
Если так — включи `Follow Crouch Height` в Movement Camera Profile: камера будет опускаться
вместе с капсулой (`PlayerMotor.CurrentHeight`).

## Снаряжение и другие эффекты

```csharp
using Rift.CameraMotion;

[SerializeField] CameraMotionClip useClip;

var handle = PlayerCameraMotion.Instance.PlayClip(useClip);          // длительность из клипа
var handle2 = PlayerCameraMotion.Instance.PlayClip(useClip, 0.4f);   // своя длительность
handle.Stop();                                                       // плавно прервать
```

## Опция «уменьшить покачивание»

`PlayerCameraMotion.GlobalIntensity = 0..1` из меню настроек. Влияет на все «косметические» слои,
не влияет на реальную отдачу прицела.

## Какие файлы ShootingSystem изменены

- `Weapon.cs` — события `Shot`, `ReloadStarted`, `ReloadFinished`, поле `aimOrigin`.
- `WeaponRaycast.cs` — вызывает события, луч из `aimOrigin` вместо `Camera.main`.
- `WeaponConfig.cs` — поле `cameraRecoil`.
