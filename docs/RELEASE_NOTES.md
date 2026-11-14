# GlassPulse 1.2.1

- Add Claude Fable 5.2 and Mythos 5.2 standard API-equivalent pricing: $8 input, $40 output, $0.20 cache reads, $10 five-minute cache writes and $16 one-hour cache writes per 1M tokens.
- Refresh the bundled catalog to 32 models on 2026-11-13 and record the official model and pricing sources. Existing installations add these missing models on startup while preserving edited rates.
- Keep separate prices for Fable / Mythos 5 and 5.1; version 5.2 uses standard rates through its full 1M context window.
- Add seven regression scenarios covering all token categories, aliases, unknown versions, long context, historical prices and Claude log-to-weekly-cost accounting.

Validation: 64 accounting/configuration scenarios and 18 native-window cases.

---

- 补充 Claude Fable 5.2 与 Mythos 5.2 标准 API 等值价格，每百万 token：输入 $8、输出 $40、缓存读取 $0.20、5 分钟缓存写入 $10、1 小时缓存写入 $16。
- 内置价格表于 2026-11-13 更新至 32 个模型，保留官方来源；现有安装启动时会补充缺失模型并保留已编辑的单价。
- 5 与 5.1 版本分别计价，5.2 在完整 1M 上下文内使用相同单价。
- 新增七个回归场景，覆盖各 token 类别、模型别名、未知版本、长上下文、历史价格与 Claude 日志周费用汇总。

验证：64 个计数 / 配置场景和 18 个原生窗口检查。

---

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
