# GlassPulse 1.1.0

- Fix the native rectangular blur outside the card by clipping the HWND to the same rounded region in every view.
- Add light/dark glass and Codex-only / Claude-only / combined display.
- Make weekly budgets directly editable and unset by default; preserve explicitly saved values.
- Add direct selection of every available historical week, source availability dates, and missing historical Sol pricing. Fully unpriced usage now displays a clear label.
- Keep source, screenshots and release packaging free of workstation-specific paths; screenshots use synthetic, path-free fixtures.

Validation: 40 accounting/configuration scenarios and 18 native window combinations, plus rendered UI and interaction checks. Native corner regression improved from 0/3 before the fix to 3/3, with both themes and provider modes also checked.

---

- 修复圆角外的矩形模糊底板，所有视图同步裁剪实际窗口。
- 新增浅色 / 深色，以及仅 Codex、仅 Claude、同时显示。
- 预算可点击直接修改，默认不设上限，保留明确保存的自定义值。
- 直接选择全部可用历史周，显示数据起始日期，补充旧 Sol 单价；全部未定价时明确提示。
- 公开内容移除本机绝对路径示例；截图使用无路径的演示数据。

验证：40 个计数 / 配置场景、18 个原生窗口组合，以及界面渲染和交互检查。原生圆角回归检查由修复前 0/3 提升为 3/3。