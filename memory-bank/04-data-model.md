# Data Model

## Domain model

```csharp
public enum CardType
{
    Task,
    Note,
    Image
}

public sealed class FocusCard
{
    public Guid Id { get; init; }
    public CardType Type { get; init; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? ImagePath { get; set; }
    public bool ImageFill { get; set; }
    public bool Completed { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Opacity { get; set; } // legacy SQLite compatibility; UI keeps it at 1.0

    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
```

Глобальный режим хранится как app setting, а не дублируется в каждой карточке.

## SQLite

```sql
CREATE TABLE IF NOT EXISTS cards (
    id TEXT PRIMARY KEY,
    type INTEGER NOT NULL,
    title TEXT NOT NULL DEFAULT '',
    content TEXT NOT NULL DEFAULT '',
    image TEXT NULL,
    done INTEGER NOT NULL DEFAULT 0,
    image_fill INTEGER NOT NULL DEFAULT 0,
    x REAL NOT NULL,
    y REAL NOT NULL,
    w REAL NOT NULL,
    h REAL NOT NULL,
    op REAL NOT NULL DEFAULT 1,
    created TEXT NOT NULL,
    updated TEXT NOT NULL
);
```

## Invariants

- `Id` уникален и не меняется.
- `Width >= 180`, `Height >= 100`.
- `Opacity` сохранён для совместимости схемы v1, но приложение нормализует его в `1.0` и не показывает control.
- `Title` не должен приводить к crash при пустом значении.
- `ImagePath` и `ImageFill` используются только для Image; `image_fill` добавляется к старой базе идемпотентной migration.
- Timestamps записываются в UTC ISO 8601 round-trip format.
- Неизвестный `type` пропускается с записью в log, остальные карточки продолжают загружаться.

## Repository contract

Минимальные операции:

- `GetAllAsync()`
- `SaveAsync(FocusCard card)`
- `DeleteAsync(Guid id)`

Repository должен сериализовать writes либо использовать отдельные connections безопасным способом. UI thread не должен блокироваться длительным SQL вызовом.
