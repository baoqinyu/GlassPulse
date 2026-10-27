# GlassPulse

简体中文 · [English](README.md)

玻璃风桌面用量条，统计 **Codex / Claude Code 的 token、每周 API 等值费用**，双击展开可视化面板。

![桌面预览，全部为演示数据](assets/preview.png)

## 外观与显示内容都可选择

- **浅色 / 深色玻璃**：点击太阳按钮，或在设置中切换。悬浮条、面板和设置页同步配色。
- **仅 Codex、仅 Claude、同时显示**：通过设置或右键选择。数据行、图表、汇总和 CSV 均随选择变化。
- **默认收缩**：双击展开 dashboard。迷你模式选择单个工具后会缩短为单条。
- **自由设置周预算**：点击预算文字直接修改。默认不设上限，0 表示不设置预算；此时仍显示 token 和金额，预算进度条留空。
- **直接选择历史周**：日期下拉框列出本地日志覆盖的全部周，面板注明各工具最早可用日期。
- **原生圆角边界**：窗口本身和模糊层使用相同圆角轮廓，展开、缩放和 DPI 改变后同步更新，防止透明边缘变成矩形底板。
- **默认 2 秒刷新**：首次扫描后仅读取新增部分；编码工具写入用量记录后更新显示。

![浅色面板，全部为演示数据](assets/light-dashboard.png)

## 运行与操作

从本仓库 Releases 下载 `GlassPulse.exe`，或下载便携 ZIP 并解压后运行。免安装，不需要管理员权限或 API Key。

需要 Windows 10/11 和 .NET Framework 4.8，使用已有运行库，不捆绑浏览器内核。已验证 Windows x64。原生模糊取决于 Windows 合成器，模糊不可用时仍保留透明显示。

| 操作 | 功能 |
| --- | --- |
| 双击空白处 | 展开 / 收起 dashboard |
| 双击迷你条 | 展开 dashboard |
| 点击预算文字 | 修改对应工具的周预算 |
| 太阳按钮 | 切换浅色 / 深色 |
| 日期下拉框 | 选择任一可用历史周 |
| 右键 / 设置 | 选择显示的工具、调整外观 |
| 拖动空白处 | 移动并贴近屏幕边缘 |
| `−` / `Esc` | 隐藏到托盘，继续统计 |
| `Ctrl+Alt+G` | 快捷键可用时隐藏 / 唤回，并恢复鼠标交互 |
| 托盘 → 恢复鼠标交互 | 退出鼠标穿透，或应对快捷键冲突 |
| `F5` | 立即刷新 |
| 托盘 → 退出 | 完全退出 |

## 图表与计价

面板包括按小时 / 按日折线、每日并列柱状图、模型 token 环图、缓存复用与 token 分类。时间图可切换 token / 美元，悬停查看数值，也可导出所选工具的每日汇总 CSV。

预算条计算方式为：**已定价的 API 等值用量 ÷ 自己设置的周预算**。套餐官方剩余额度需查看提供商的用量页。

金额按标准、全球 API token 单价折算，支持已列出的缓存和长上下文档位。当前价格表会用于重算所选历史周，不包含极速 / 优先模式、地区加价、工具费、税费或账户折扣。

未知价格仍保留 token。全部未定价时显示「待定价」，部分已定价时金额带 `≥`。旧 Claude 日志未记录缓存时长时按短期缓存写入估算并提示。输出已经包含的推理 token 只计一次，并协调 Claude 重复块与 Codex 新旧重复记录。

## 本地数据与历史范围

自动发现工具主目录，支持 `CODEX_HOME`、`CLAUDE_CONFIG_DIR`，也可在设置中指定主目录。读取 Codex 会话与归档 JSONL，以及 Claude Code 项目和子代理 JSONL。

历史范围取决于本机仍保留的日志。普通网页聊天、其他设备或云端活动，需要相应兼容日志才会计入。已删除的源日志无法凭空恢复。缺失数据源或异常记录会提示，面板中的最早可用日期可帮助区分「没有记录」与「用量为零」。

配置默认保存在**程序旁的 `data` 文件夹**，包括 `settings.json` 与 `pricing.json`。移动整个程序文件夹即可带走配置，无需在系统盘安装应用数据。也可使用其他相对目录：

```powershell
.\GlassPulse.exe --data-dir ".\my-data"
```

价格表有 30 个条目，核验日期为 2026-10-27，采用本地快照。升级会补充缺失的内置模型，同时保留已编辑的单价；可通过「编辑价格表」修改现有条目。自然周会自动切换，单价不会自动联网下载。

官方来源：[OpenAI 定价](https://developers.openai.com/api/docs/pricing)、[旧 Sol 模型定价](https://developers.openai.com/api/docs/models/gpt-5.6-sol)、[Claude 定价](https://platform.claude.com/docs/en/about-claude/pricing)。价格表也保留来源链接。

## 隐私、编译与验证

软件不发起网络请求，不另存对话副本。运行配置、本地日志、数据库、诊断和凭据均排除在版本控制及发布包之外。公开截图只使用演示数据，不展示本机绝对路径。

```powershell
.\build.ps1 -Test
.\package.ps1 -SkipBuild
```

使用 Windows 自带的 .NET Framework 编译器，无需下载 SDK 或还原 NuGet。验证包括 40 个计数 / 配置场景，以及两种主题、三个工具选择和三种视图组合的 18 个原生窗口检查。

开发参数：`--preview <目录>` 生成演示预览，`--smoke` 检查图表与设置，`--report <文件>` 输出本地汇总诊断。可用 `--data-dir` 指定它们的便携配置位置。诊断报告请保留在本机。

## 相关工具与许可证

[ccusage](https://github.com/ccusage/ccusage) 适合 CLI 报告，[codex-bar](https://github.com/jspann21/codex-bar) 侧重 Codex 周容量，[CodexWinBar](https://github.com/ItayCohen-Prog/CodexWinBar) 提供任务栏额度集成。GlassPulse 使用自己的原生实现。

[MIT 许可证](LICENSE)。