# GlassPulse 1.2.0

- Restore compact start–end week labels while retaining historical selection.
- Read plain and `.jsonl.zst` Codex history with streaming, checksum-verified embedded Zstandard support; no Python or ccusage runtime dependency.
- Follow canonical Codex token-count deltas, unchanged-snapshot suppression, cross-file/fork replay deduplication, and explicitly uncovered compaction requests. Ordinary request metadata is not billed twice.
- Respect plain/active rollout precedence and label reference-model alias estimates.
- Keep existing visual pixel-clipping, themes, provider choices and budgets unchanged.

Validation: 57 accounting/configuration cases, 18 native-window cases, synthetic UI checks, and a private ccusage 20.0.40 comparison over four complete September weeks. Token categories match exactly; displayed dollar totals agree. No real usage, session identifiers or workstation paths are included in public fixtures.

---

- 周选择恢复为起始–结束日期范围。
- 支持普通及 `.jsonl.zst` 压缩历史，内嵌校验的流式解码器，运行不依赖 Python / ccusage。
- 修正请求元数据重复计数、累计快照、跨文件 / 父会话复制及上下文整理请求的处理。
- 普通 / 活动副本优先，参考模型别名计价明确标注为估算。
- 保留现有圆角像素裁剪、主题、工具选择和预算功能。

验证：57 个计数 / 配置场景、18 个原生窗口检查、演示界面检查，以及与 ccusage 20.0.40 的四个完整九月周私密对照。各类 token 精确一致，显示金额一致。公开样例不含真实用量、会话标识或本机路径。
