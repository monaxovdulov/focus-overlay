# Active Context

## Current state

- Папка проекта создана.
- Стек и архитектурные решения зафиксированы.
- PRD и one-shot implementation plan подготовлены.
- Реализован рабочий WPF MVP в `src/FocusOverlay.*`, SQLite хранение и тестовый проект.
- Controller и карточки переработаны в светлый минималистичный content-first UI; служебный card chrome мягко проявляется поверх фиксированного layout и не сдвигает содержание.
- Controller использует символы, цветной status dot и числовой card count; единая кнопка `?` объясняет типы, цвета, режимы и hotkeys.
- Режим click-through называется «Не мешать»: синий status dot означает доступное управление, янтарный — клики сквозь карточки.
- Реализованы global hotkeys, clipboard classification, tray, image picker/preview, task completion и persistence tests.
- Image-карточка принимает `Ctrl+V` после клика по изображению и один image file через Explorer drag-and-drop; bitmap сохраняется в локальную папку приложения.
- Для Image доступен сохраняемый полноформатный режим `UniformToFill` без заголовка и полей; переключатель `⛶` / `↙` появляется при hover.
- Browser drag распознаёт bitmap, HTML `<img src>`, URI и Chromium/Firefox URL formats; remote image импортируется локально с MIME/decode/size/timeout проверками.
- Карточки связываются протягиванием hover-only порта `●`: гибкая цветная кривая поддерживает подпись и направления `—` / `→` / `←` / `↔`.
- Символ `⌁` включает Relation Lens: релевантные линии, порты и подписи отображаются полностью, остальные связи приглушаются до 30%; `Esc` закрывает lens.
- Линии имеют белый halo, тонкий синхронизированный цветной stroke и inset-наконечники; после создания показывается подсказка без автоматического открытия инспектора.
- Инспектор закрепляется рядом с карточкой, имеет размер 270 × 210 px и раскрывает одну выбранную связь из компактного списка.
- Устранены двойной controller и поток параллельных SQLite writes; работают single-instance, debounce, serialized writes и shutdown flush.
- Ограничения: rolling file log и полноценные automated UI tests не добавлены; интерактивные сценарии проверялись bounded UI smoke tests.

## Environment facts

На машине проверено:

- .NET SDK `10.0.200` установлен.
- `Microsoft.WindowsDesktop.App 10.0.4` установлен.
- WPF templates доступны через `dotnet new wpf`.
- Visual Studio Build Tools установлен в `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools`.

Полная Visual Studio для выполнения плана не требуется.

## Next action

Провести ручной прогон двух cross-window сценариев на пользовательских сайтах/раскладке: browser image drag и протягивание `●` между двумя карточками. MVP tasks `FOC-001..FOC-014` реализованы; automated tests проходят `10/10`.

## Known risks to validate early

- Стабильное включение/выключение `WS_EX_TRANSPARENT` у WPF window.
- Поведение редактирования и focus activation для нескольких topmost окон.
- Восстановление окон после смены monitor layout.
- Конфликты `Ctrl+Shift+F` и `Ctrl+Shift+Space` с другими приложениями.
- Корректное завершение приложения при скрытом controller.
