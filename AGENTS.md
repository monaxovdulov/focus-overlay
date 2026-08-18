# Instructions for implementation agents

## Source of truth

Before changing code, read these files in order:

1. `memory-bank/00-index.md`
2. `memory-bank/01-project-context.md`
3. `memory-bank/02-architecture.md`
4. `memory-bank/03-decisions.md`
5. `docs/prd/focus-overlay.md`
6. `memory-bank/05-active-context.md`
7. `memory-bank/06-one-shot-implementation-plan.md`

The PRD is both the product specification and the user manual. Do not implement features outside the MVP scope merely because they seem useful.

## Required workflow

1. Inspect the installed .NET environment and current repository state.
2. Implement all open MVP tasks in `docs/prd/focus-overlay.md` in task order.
3. Keep the application runnable after each vertical slice.
4. Add focused automated tests for domain behavior and persistence.
5. Run restore, build, tests, and a Release publish.
6. Perform a bounded startup smoke test and verify that the process does not immediately crash.
7. Update the PRD after implementation:
   - mark completed tasks `[x]`;
   - add `**Implemented:**` bullets to each completed task;
   - update the feature description so it describes actual behavior.
8. Update `memory-bank/05-active-context.md` and `memory-bank/07-progress.md` with facts, remaining limitations, and exact verification results.

## Engineering constraints

- Target Windows only with `net10.0-windows` and WPF.
- Do not replace WPF with Tauri, Electron, WinUI, Avalonia, or MAUI.
- Use one native WPF window per visible card.
- Keep SQLite as the source of truth. UI state must not be the only copy of card data.
- Use dependency injection and interfaces at persistence/OS boundaries, but avoid unnecessary framework layers.
- Make database writes serialized and parameterized.
- Store timestamps in UTC ISO 8601.
- Keep user-facing text in Russian for the prototype.
- Never require administrator privileges.
- Do not add telemetry, accounts, cloud sync, networking, autostart, updater, or an installer in the MVP.
- Do not copy GPL code from reference projects.
- If a third-party tray library is unavailable, fall back to `System.Windows.Forms.NotifyIcon` rather than blocking.
- Prefer a working bounded prototype over optional polish.

## Completion rule

Do not report completion if the solution does not build, tests fail, the Release publish fails, or the application immediately crashes on startup. Record any genuine environmental blocker with the exact command and error output.
