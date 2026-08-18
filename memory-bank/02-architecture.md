# Architecture

## Технологии

- C# / .NET 10
- WPF (`net10.0-windows`, `UseWPF=true`)
- `CommunityToolkit.Mvvm`
- `Microsoft.Data.Sqlite`
- `Microsoft.Extensions.DependencyInjection`
- Serilog с локальным file sink
- xUnit
- Win32 interop для `RegisterHotKey`, `WM_HOTKEY` и extended window styles

## Предлагаемая solution structure

```text
FocusOverlay.sln
src/
  FocusOverlay.App/
    App.xaml
    Controller/
    Cards/
    Services/
    WindowsInterop/
    Assets/
  FocusOverlay.Core/
    Models/
    Repositories/
    Services/
  FocusOverlay.Infrastructure/
    Persistence/
tests/
  FocusOverlay.Tests/
docs/prd/
memory-bank/
```

Не нужно создавать дополнительные проекты без конкретной необходимости. WPF views остаются в App, domain не зависит от WPF, Infrastructure реализует repository.

## Runtime components

```text
App lifecycle
├── ControllerWindow
├── TrayService
├── HotkeyService
├── CardWindowManager
│   ├── CardWindow(Task)
│   ├── CardWindow(Note)
│   └── CardWindow(Image)
├── ConnectionOverlayWindow (click-through drawing only)
├── ConnectionEditorWindow
└── CardRepository(SQLite)
```

## Источник истины

SQLite хранит карточки. `CardWindowManager` держит соответствие `cardId → CardWindow`, но не является долговременным хранилищем. Изменения view model проходят через application service и repository, затем отражаются в окне.

## Window behavior

Card window:

- `WindowStyle=None`;
- `Topmost=true`;
- `ShowInTaskbar=false`;
- светлая непрозрачная paper-like поверхность с hardware-rendered WPF content;
- `WindowChrome` или эквивалент для resize;
- `DragMove()` из выделенной drag-зоны в Edit mode;
- минимальный размер 180×100 DIP;
- нормальный стартовый размер около 320×180 DIP.

Режим «Не мешать» добавляет к HWND extended style `WS_EX_TRANSPARENT`, не скрывая окно и не убирая его контур. Edit mode удаляет click-through style. Применение выполняется после получения HWND через `WindowInteropHelper` и должно быть идемпотентным. `AllowsTransparency=True` намеренно не используется для card windows, чтобы не ухудшать WPF rendering performance.

Исключение — единый `ConnectionOverlayWindow`: это простой click-through drawing surface без controls и содержимого карточек. Он использует transparency для линий в промежутках между независимыми окнами, не принимает input и перерисовывается только при изменении геометрии/данных либо во время короткой анимации.

## Hotkeys

Global hotkeys регистрируются один раз на HWND controller/hidden message source. `WM_HOTKEY` обрабатывается через `HwndSource.AddHook`. Ошибка регистрации превращается в пользовательское предупреждение, а не в crash.

## Persistence flow

- Database path: `%LOCALAPPDATA%\FocusOverlay\focus-overlay.db`.
- Logs: `%LOCALAPPDATA%\FocusOverlay\logs\`.
- Миграция v1 создаётся идемпотентно при startup.
- UI changes сохраняются с debounce примерно 300–500 мс.
- Геометрия сохраняется после завершения move/resize либо с throttling, а не на каждый pixel event.
- Все SQL statements параметризованы.

## Multi-monitor

Координаты WPF хранятся в device-independent units. При восстановлении нужно проверить пересечение сохранённого прямоугольника хотя бы с одной текущей working area. Если пересечения нет, карточка перемещается в рабочую область primary monitor с небольшим offset.

Mixed-DPI perfection не является условием MVP, но изменение монитора не должно терять карточку полностью.

## Shutdown

`ShutdownMode=OnExplicitShutdown`. Закрытие controller скрывает окно. Команда Exit:

1. запрещает новые операции;
2. сохраняет pending changes;
3. unregister hotkeys;
4. закрывает card windows и controller;
5. освобождает SQLite/logging;
6. вызывает `Application.Shutdown()`.
