# Memory Bank Index

Memory bank хранит контекст, который следующий агент обязан прочитать до реализации.

## Порядок чтения

1. [`01-project-context.md`](01-project-context.md) — проблема, пользователь и границы MVP.
2. [`02-architecture.md`](02-architecture.md) — компоненты, данные и Windows behavior.
3. [`03-decisions.md`](03-decisions.md) — принятые решения и отвергнутые альтернативы.
4. [`04-data-model.md`](04-data-model.md) — модель domain и SQLite.
5. [`../docs/prd/focus-overlay.md`](../docs/prd/focus-overlay.md) — источник истины по поведению и задачам.
6. [`05-active-context.md`](05-active-context.md) — текущая точка работы.
7. [`06-one-shot-implementation-plan.md`](06-one-shot-implementation-plan.md) — последовательность реализации и проверки.
8. [`07-progress.md`](07-progress.md) — журнал фактически выполненного.

## Правило обновления

После каждого законченного прохода агент обновляет `05-active-context.md`, `07-progress.md` и PRD. Решения из `03-decisions.md` меняются только с явным объяснением причины и последствий.
