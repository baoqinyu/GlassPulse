# GlassPulse 1.1.1

Fix the remaining rectangular backdrop in real desktop rendering. Native window-region checks alone did not catch the compositor painting outside the card in 1.1.0.

The glass material now uses a dedicated, rounded composition visual behind the controls. Its visual clip and native boundary are synchronized during expansion, resizing, movement and show/hide. The previous unbounded accent blur is disabled on the foreground. Unsupported material composition falls back to transparency.

Validation includes 40 accounting/configuration cases, 18 native-window checks, synthetic screen-pixel comparisons across themes/providers/views/movement, and checks that real material composition is active. No personal desktop content, paths or usage is included in diagnostics intended for publication.

---

修复实屏中仍存在的矩形底板。1.1.0 的窗口区域测试未能发现合成器在圆角外绘制背景的问题。

玻璃材质改为界面后方的独立圆角合成层，并同步其视觉裁剪与原生边界。展开、缩放、移动、隐藏和恢复时同步更新；前景不再启用整窗矩形模糊。不支持材质合成时回退为透明显示。

验证包括 40 个计数 / 配置场景、18 个窗口检查，以及主题、工具、视图和移动状态的合成背景屏幕像素对比，确认真实玻璃材质已启用。公开诊断不包含个人桌面、本机路径或实际用量。
