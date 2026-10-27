# GlassPulse

[简体中文](README.zh-CN.md) · English

A glass desktop meter for **Codex and Claude Code tokens, weekly API-equivalent cost, and expandable analytics**.

![Synthetic-data desktop preview](assets/preview.png)

## Choose how it looks and what it shows

- **Dark and light glass.** Switch with the sun button or in Settings. The meter, dashboard and settings share the selected palette.
- **Codex, Claude, or both.** Choose the tools in Settings or the right-click menu. Visible rows, charts, totals and CSV exports follow the selection.
- **Compact by default.** Double-click to open the dashboard. Mini mode becomes a narrow single-provider bar when only one tool is selected.
- **Editable weekly budgets.** Click a budget label to edit it. Budgets start unset; zero means no budget cap. Without a cap the meter shows tokens and dollars while the budget bar stays empty.
- **All available weeks.** Pick a week directly from the date dropdown. The local log timestamps determine the history range; each provider's earliest available date is shown in the dashboard.
- **Native rounded outline.** The controls and a separate composition-only glass surface share the same rounded outline. Both visual and native clipping are synchronized while resizing, expanding, moving and hiding. The foreground does not use whole-window accent blur.
- **Live local updates.** Two-second refresh by default, incremental after the initial scan. Values update when the coding tool writes a completed usage record.

![Light dashboard with demonstration data](assets/light-dashboard.png)

## Run

Download the single `GlassPulse.exe` or portable ZIP from this repository's Releases section. Extract the ZIP before running. No installer, administrator rights or API key is required.

Windows 10 version 1903 or later / Windows 11 and .NET Framework 4.8 are required. The executable uses the installed runtime and does not bundle a browser engine. Windows x64 is tested. Native blur depends on Windows composition; transparent rendering remains available when native blur is unavailable.

| Action | Result |
| --- | --- |
| Double-click the background | Expand/collapse the dashboard |
| Double-click the mini bar | Open the dashboard |
| Click a budget label | Edit that provider's budget |
| Sun button | Switch light/dark glass |
| Date dropdown | Select any available historical week |
| Right-click / Settings | Choose Codex, Claude, or both; change appearance |
| Drag the background | Move; snap near screen edges |
| `−` / `Esc` | Hide to tray; collection continues |
| `Ctrl+Alt+G` | Show/hide and restore mouse interaction when the shortcut is available |
| Tray → Restore mouse interaction | Recover from passthrough or a shortcut conflict |
| `F5` | Refresh now |
| Tray → Exit | Quit completely |

## Analytics and accounting

The dashboard has hourly/daily lines, daily grouped bars, a model-token donut, cache reuse and token breakdowns. Switch the time charts between tokens and USD. Hover for values, and export the selected providers' daily aggregates as CSV.

Each budget bar represents **priced API-equivalent usage / your chosen weekly budget**. It is a personal budget indicator. Official subscription capacity must be checked with the provider.

Dollars use standard global API token prices, including supported cache and long-context categories. The active catalog revalues all selected weeks at its configured prices. Fast/priority premiums, regional premiums, tool calls, taxes and account discounts are excluded.

Unknown prices retain their tokens. Fully unpriced usage displays `待定价`; a partially priced total uses `≥`. Old Claude records without cache TTL use the short-write rate and show an estimate notice. Reasoning already included in output is counted once. Duplicate streamed Claude blocks are reconciled. Codex follows canonical token-count request deltas, skips unchanged cumulative snapshots and copied fork prefixes, and adds only explicit compaction requests not already covered by cumulative usage. Request metadata is not another request. Routing aliases use the ccusage reference-model timeline and remain labelled estimates.

## Local data and history

The app discovers the tools' normal home directories. `CODEX_HOME` and `CLAUDE_CONFIG_DIR` are honored. Settings can override either home directory. It reads plain and Zstandard-compressed Codex session/archived rollouts, and Claude Code project/subagent JSONL logs. Plain siblings and active copies take precedence. Historical rollouts are decoded as streams; unchanged compressed files are not reread.

History covers the logs still available locally. Plain web chats, other devices and cloud activity require compatible logs in the selected source. Deleted source logs cannot be reconstructed by the app. Missing sources and malformed records are marked; the earliest available date distinguishes missing history from zero usage.

The portable `data` folder is created **beside the executable**, containing `settings.json` and `pricing.json`. Moving the application folder moves its configuration. No application-data installation on the system drive is required. An alternate relative data directory can be selected with:

```powershell
.\GlassPulse.exe --data-dir ".\my-data"
```

The price catalog has 30 entries, checked on 2026-10-27. It is a local snapshot. Upgrades add missing bundled models while preserving existing edited rates; edit existing rates through Settings → Edit price catalog. Calendar weeks roll over automatically, and prices do not download automatically.

Official sources: [OpenAI pricing](https://developers.openai.com/api/docs/pricing), [historical Sol pricing](https://developers.openai.com/api/docs/models/gpt-5.6-sol), [Claude pricing](https://platform.claude.com/docs/en/about-claude/pricing). Sources are also recorded in the catalog.

## Privacy and building

The application makes no network requests and does not persist conversation copies. Runtime configuration, local logs, databases, diagnostics and credentials are excluded from version control and release packaging. Public screenshots use synthetic data and path-free placeholders.

```powershell
.\build.ps1 -Test
.\package.ps1 -SkipBuild
```

The Windows .NET Framework compiler is used without an SDK download or NuGet restore. The test command runs 57 accounting/configuration scenarios and 18 native-window combinations covering both themes, all provider selections and all three view modes.

Developer switches: `--preview <directory>` renders synthetic previews; `--smoke` exercises the charts/settings; `--report <file>` writes local aggregate diagnostics. Use `--data-dir` to keep their output in a chosen portable folder. Diagnostic reports should stay private.

## Related tools and license

[ccusage](https://github.com/ccusage/ccusage) provides CLI reports; [codex-bar](https://github.com/jspann21/codex-bar) focuses on Codex capacity; [CodexWinBar](https://github.com/ItayCohen-Prog/CodexWinBar) integrates provider quotas with the Windows taskbar. GlassPulse uses its own native implementation.

[MIT license](LICENSE).\nActual desktop-pixel regression: `./scripts/test-pixel-edges.ps1` compares all exterior pixels against a generated checkerboard in 24 theme/provider/view/movement cases. It checks that glass composition is active and does not read or save personal desktop content. A runner without a visible synthetic desktop is reported unavailable, not passed. Material composition uses Windows components already installed; unsupported composition falls back to transparency.\n
Compressed-history support uses an embedded, pinned Zstandard decoder (BSD-3-Clause); it is hash-verified and extracted only into the portable `data/native` directory. The running app needs neither Python nor ccusage. The first full archive scan can take longer than later incremental refreshes. The current distribution is Windows x64. See [third-party notices](THIRD_PARTY_NOTICES.md).

Accounting compatibility was checked against ccusage 20.0.40 using matching dates, timezone, week start and Standard API pricing. Four complete September weeks matched every input/cache/output/total token and rounded dollar total. Custom price overrides, different timezones or Fast/Flex tiers can produce intentional differences. Real usage reports stay local; only synthetic fixtures and aggregate equality results are published.
