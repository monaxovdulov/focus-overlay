# Progress

Реализованы Core-модель, SQLite CRUD с параметризованными запросами, controller, отдельные frameless topmost card windows, редактирование текста, delete, восстановление, Win32 click-through, global hotkeys, clipboard capture, tray, Task checkbox и Image picker/preview.

Проверки 2026-08-18:

- `dotnet build FocusOverlay.slnx -c Release` — успешно, 2 предупреждения о скрытии Window.Title/Content.
- `dotnet test FocusOverlay.slnx -c Release` — успешно, 2 теста (domain defaults + SQLite CRUD/geometry round-trip).
- `dotnet build FocusOverlay.slnx -c Release` после hotkeys/tray/image изменений — успешно, 0 warnings/0 errors.
- `dotnet publish src/FocusOverlay.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64` — успешно.

Ограничения: rolling file log и automated UI interaction smoke test не добавлены; startup smoke test выполняется bounded вручную через процесс.

## 2026-08-18 — visual and performance polish

- Исправлен двойной controller: удалён `StartupUri`, добавлена single-instance mutex.
- Controller сокращён до компактного минималистичного layout.
- Карточки переведены на светлую paper-like поверхность; текст снова отображается после удаления некорректного custom TextBox template.
- Постоянные toolbar и opacity slider удалены; edit chrome появляется только при hover.
- «Фокус» переименован в «Не мешать» / «Вернуть управление» с явным описанием click-through semantics.
- Карточки сохраняют видимый контур в режиме «Не мешать».
- Добавлен 420-мс save debounce, serialized repository writer, WAL, busy timeout и shutdown flush.
- Удалён пустой повреждённый `FocusOverlay.sln`; рабочим solution остаётся `FocusOverlay.slnx`.
- `dotnet build FocusOverlay.slnx -c Release` — успешно, 0 warnings / 0 errors.
- `dotnet test FocusOverlay.slnx -c Release --no-build` — успешно, 3/3 tests.
- `dotnet publish src/FocusOverlay.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64` — успешно.
- Повторный запуск проверен: второй процесс завершается, активным остаётся один экземпляр.
- Регрессионный UI smoke test режима «Не мешать»: переключение заняло 15 мс, процесс остался жив, появилась кнопка «Вернуть», обратное переключение и Exit прошли корректно.
- Точно идентифицированная тестовая карточка, созданная во время визуального QA, удалена из локальной базы; остальные локальные карточки сохранены.

## 2026-08-18 — symbolic minimal UI

- Controller уменьшен до заголовка, синей/янтарной status dot, символических действий и числового card count.
- Текстовые действия заменены системой `✓` Task, `✎` Note, `▧` Image, `⊘` «Не мешать», `↩` возврат, `×` выход.
- Добавлена единая кнопка `?` с компактной справкой по символам, цветам, режиму и global hotkeys.
- Edit chrome карточки больше не участвует в layout: title rectangle до, во время и после hover остался `87;105;334;33`.
- Hover-анимация использует мягкий `160 ms` fade-in и `240 ms` fade-out с easing.
- UI smoke test режима после редизайна: процесс остался жив, действие сменилось на «Вернуть управление».
- `dotnet build FocusOverlay.slnx -c Release --no-restore` — успешно, 0 warnings / 0 errors.
- `dotnet test FocusOverlay.slnx -c Release --no-build` — успешно, 3/3 tests.
- `dotnet publish src/FocusOverlay.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64` — успешно.

## 2026-08-18 — image paste, drop and edge-to-edge mode

- Image-карточка получает focus по клику и принимает `Ctrl+V` через `ApplicationCommands.Paste`, не перехватывая paste в title editor.
- Clipboard bitmap сохраняется как PNG в `%LOCALAPPDATA%\FocusOverlay\images`; file-drop clipboard и Explorer drop используют один проверенный image path.
- Добавлены поддержка `png/jpg/jpeg/bmp/gif/webp`, оранжевый drop cue и краткие fade-hints для результата/ошибки.
- Hover-only `⛶` переключает `Uniform` preview на edge-to-edge `UniformToFill`; title/type marker скрываются, `↙` возвращает обычный layout.
- `ImageFill` сохраняется в SQLite; существующая пользовательская база успешно мигрирована добавлением `image_fill` с default `0`.
- UI smoke test: image surface получила keyboard focus, bitmap paste создал PNG размером `228133` bytes, image path и `image_fill=1` сохранились, Exit завершил процесс штатно.
- Все точные QA rows и созданный smoke-test PNG удалены; две остальные локальные карточки сохранены.
- `dotnet build FocusOverlay.slnx -c Release --no-restore` — успешно, 0 warnings / 0 errors.
- `dotnet test FocusOverlay.slnx -c Release --no-build` — успешно, 3/3 tests, включая `ImageFill` persistence round-trip.
- `dotnet publish src/FocusOverlay.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64` — успешно.

## 2026-08-18 — browser image drag compatibility

- Добавлено распознавание bitmap, HTML `<img src>`, `text/uri-list`, Firefox URL formats и Windows/Chromium URL formats.
- Remote image сохраняется без cookies с timeout 15 секунд, лимитом 25 MB, MIME allowlist и обязательным WPF decode.
- Обычные page URLs отклоняются до drop/import; drag-over не выполняет сетевые запросы.
- Добавлены browser payload tests: HTML data image import, relative HTML image source и rejection обычной страницы.
- `dotnet build FocusOverlay.slnx -c Release --no-restore` — успешно, 0 warnings / 0 errors.
- `dotnet test FocusOverlay.slnx -c Release --no-build` — успешно, 6/6 tests.
- `dotnet publish src/FocusOverlay.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64` — успешно.
- Реальный cross-window drag из Chrome не удалось автоматизировать: Windows Computer Use остановился, не сумев достоверно определить URL вкладки. Ручная проверка остаётся обязательной.
