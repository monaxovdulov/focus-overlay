# One-shot Implementation Plan

## Outcome

За один агентский проход получить запускаемый локальный прототип Focus Overlay, а не только scaffolding. Вертикальный путь должен работать полностью: создать карточку → изменить → перейти в Focus mode → работать через неё → перезапустить → увидеть восстановленную карточку.

## Phase 0 — preflight

1. Прочитать `AGENTS.md`, memory bank и PRD.
2. Проверить `dotnet --list-sdks`, рабочую директорию и отсутствие неожиданных пользовательских файлов.
3. Инициализировать git repository только если его ещё нет.
4. Создать `.gitignore` для Visual Studio/.NET, не игнорируя docs и SQLite migrations.

Exit condition: контекст прочитан, рабочее дерево понятно, .NET 10 доступен.

## Phase 1 — solution and startup skeleton

1. Создать solution и проекты App/Core/Infrastructure/Tests.
2. Настроить project references и NuGet dependencies.
3. Добавить DI composition root в `App.xaml.cs`.
4. Настроить `ShutdownMode=OnExplicitShutdown`.
5. Создать минимальный controller, который запускается без database logic.
6. Сразу выполнить restore/build.

Exit condition: controller запускается, закрытие не оставляет бесконтрольных процессов, Release build проходит.

## Phase 2 — domain and persistence vertical slice

1. Реализовать `FocusCard`, `CardType`, repository contract и normalization rules.
2. Создать SQLite database/migration v1 в `%LOCALAPPDATA%\FocusOverlay`.
3. Реализовать CRUD/settings и recovery при некорректной отдельной записи.
4. Добавить тесты repository на временной database.
5. Создать через controller Note, сохранить её и вывести вновь после restart.

Exit condition: одна Note проходит полный create/save/load/delete путь.

## Phase 3 — native card window manager

1. Реализовать `CardWindowManager` с одним окном на card id.
2. Создать frameless topmost CardWindow с `WindowChrome` resize и drag-zone.
3. Связать geometry с model и throttled persistence.
4. Реализовать safe restore/clamp к доступным monitor work areas.
5. Удаление карточки должно закрывать window и удалять строку SQLite.

Exit condition: несколько Note windows независимо перемещаются, resize работают, layout восстанавливается.

## Phase 4 — all MVP card types

1. Task editor/display: title, description, completed.
2. Note editor/display: title, multiline content.
3. Image: file picker, supported image validation, Uniform rendering, missing-file placeholder.
4. Добавить debounced autosave текстовых изменений.
5. Добавить hover-only edit chrome: delete, visual drag handle и image picker.

Exit condition: все три типа создаются и восстанавливаются без потери данных.

## Phase 5 — global modes and clipboard capture

1. Зарегистрировать `Ctrl+Shift+F` и `Ctrl+Shift+Space` через Win32.
2. Реализовать обработку `WM_HOTKEY` и корректный unregister.
3. Edit mode показывает chrome и принимает mouse input.
4. Focus mode скрывает chrome и применяет click-through ко всем card HWND.
5. Реализовать clipboard classification по PRD.
6. Показать warning в controller при конфликте hotkey.

Exit condition: Focus/Edit переключаются без мыши, а clipboard hotkey создаёт Note/Image.

## Phase 6 — controller, tray, lifecycle

1. Довести controller до предусмотренных PRD действий и status display.
2. Добавить tray: Show controller, Toggle mode, Exit.
3. Закрытие controller скрывает его.
4. Exit сбрасывает pending saves, unregister hotkeys и закрывает приложение.
5. Проверить повторные show/hide и отсутствие duplicate card windows.

Exit condition: приложение нормально живёт в tray и завершается только явной командой Exit.

## Phase 7 — diagnostics and verification

1. Добавить локальный rolling log без сетевых sinks.
2. Добавить domain tests: normalization, card defaults, mode behavior where applicable.
3. Добавить persistence tests: migration, CRUD, settings, delete, invalid type recovery.
4. Выполнить форматирование.
5. Выполнить:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet publish src/FocusOverlay.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

6. Запустить development build и published exe для bounded smoke tests. Убедиться, что процесс не падает сразу; после проверки завершить только процесс Focus Overlay.
7. Записать точные результаты команд в `07-progress.md`.

Exit condition: build, tests, publish и startup smoke test успешны.

## Phase 8 — SDD closeout

1. Отметить фактически выполненные задачи в PRD.
2. Для каждой завершённой задачи добавить `**Implemented:**` bullets.
3. Переписать верхнюю часть PRD как фактическое руководство пользователя.
4. Обновить `05-active-context.md`.
5. Обновить `07-progress.md` и перечислить реальные ограничения.
6. Сделать один осмысленный commit, если git доступен и рабочее дерево содержит только изменения проекта.

## Acceptance checklist

- [ ] Приложение запускается без прав администратора.
- [ ] Создаются Task, Note и Image.
- [ ] Каждая карточка — отдельное topmost frameless окно.
- [ ] Move, resize и delete работают.
- [ ] Text edits сохраняются автоматически.
- [ ] `Ctrl+Shift+F` переключает click-through всех карточек.
- [ ] `Ctrl+Shift+Space` создаёт карточку из clipboard.
- [ ] Hotkey conflict не вызывает crash.
- [ ] Restart восстанавливает содержимое и layout.
- [ ] Недоступное изображение показывает placeholder.
- [ ] Потерянная за пределами экранов карточка возвращается в visible area.
- [ ] Controller можно скрыть/вернуть через tray.
- [ ] Exit корректно завершает процесс.
- [ ] Release build, tests и self-contained publish проходят.
- [ ] PRD и memory bank соответствуют фактической реализации.

## Time-saving rules

- Не строить design system; достаточно светлой paper-like темы и читаемой типографики.
- Не внедрять generic event bus, mediator или ORM.
- Не делать installer и signing.
- Не реализовывать timer/workspaces/autostart/updater.
- Не тратить проход на pixel-perfect animations.
- Если tray library создаёт dependency blocker, использовать `System.Windows.Forms.NotifyIcon`.
- Сначала завершать вертикальный сценарий Note, затем обобщать на Task/Image.
