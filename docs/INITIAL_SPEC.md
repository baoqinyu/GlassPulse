# GlassPulse 1.0

## Problem and behavior

Codex and Claude Code users need a desktop view of weekly token use and its API-price equivalent. Terminal reports are useful for analysis but do not provide a compact glass floating meter.

The default view is collapsed. Double-clicking its background opens a dashboard containing daily/hourly line charts, grouped daily bars, model-share donut and usage statistics. A second double-click collapses it. Mini mode is selectable from settings or the context menu.

## Acceptance criteria

- Windows-native, portable executable using the installed .NET Framework runtime; no browser engine or service required.
- Codex and Claude Code local logs, including archived Codex and Claude subagent logs.
- Input, output, cache reads and cache writes; avoid double-counting reasoning and duplicate streamed/modern records.
- Local calendar weeks, automatic rollover, Monday/Sunday start, history navigation and DST-safe boundaries.
- Default two-second incremental refresh after initial discovery. UI remains responsive during scanning.
- Standard global API-equivalent prices from an editable, dated catalog; unknown prices remain visibly incomplete.
- Progress represents user-defined weekly budgets and is labelled accordingly.
- Drag, edge snap, topmost, glass opacity, tray, optional passthrough and optional login startup.
- Charts have value tooltips, token/USD switches, and CSV daily export.
- Application performs no network requests and does not persist conversation copies.
- English and Simplified Chinese README, MIT license, synthetic screenshots, Windows CI and tagged release workflow.

## Scope

Only available local usage logs contribute. Subscription capacity percentages, cloud account reconciliation, unlogged plain chats and a live exchange-rate or price service are outside this version. Prices are a versioned local snapshot. Removing logs removes their reconstructed history.

## Verification

`build.ps1 -Test` compiles with warnings treated as errors and runs 57 accounting/configuration scenarios and 18 native-window checks. `--preview` renders real UI controls with synthetic data. `--smoke` exercises native composition, expand/mini/hide/restore, chart mode switches and CSV export. Runtime scanning is also checked against local logs; no personal logs or diagnostics belong in the repository.
