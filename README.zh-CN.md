# GlassPulse

简体中文 · [English](README.md)

一个轻巧的玻璃风桌面用量条，统计 **Codex + Claude Code 的 token 和标准 API 等值费用**。平时安静悬浮，双击展开 dashboard。

![GlassPulse 收缩视图、展开面板和极简横条；图中全部为演示数据。](assets/preview.png)

## 日常简洁，需要时展开

- **默认收缩。** Codex 薄荷绿、Claude 暖杏色，两条细进度条；还可切换极简横条。
- **双击展开面板。** 按小时／按日的折线图、每日并列柱状图、模型占比环图，以及缓存复用比例和 token 分类。时间图可切换 token／美元，悬停查看数值。
- **按自然周统计。** 使用电脑本地时区，周一或周日起算，跨周自动切换；源日志保留期间可浏览最近 52 周。
- **实时本地更新。** 默认每 2 秒检查一次，首次扫描后仅读取新增日志；编码工具写入用量记录后才会显示。
- **Windows 原生。** C# / WPF，使用系统的 .NET Framework 4.8，运行时不需要 Electron、WebView、Node.js 或后台网页服务。EXE 约 150 KB，运行库由 Windows 提供。
- **桌面交互。** 拖动、贴边、置顶、玻璃浓度、托盘、可选鼠标穿透和登录启动；快捷键可用时，`Ctrl+Alt+G` 隐藏／唤回。
- **离线使用。** 软件不发起网络请求，不需要 API Key，不另存对话副本；可导出所选周的每日汇总 CSV。

当前界面为简体中文，仓库提供中英文文档。

## 使用

从本仓库 Releases 下载 `GlassPulse.exe`，或按下文自行编译，双击即可运行。免安装，无需管理员权限。

运行环境：Windows 10 / 11、.NET Framework 4.8，以及本机 Codex 或 Claude Code 的用量日志。已验证 Windows x64。原生模糊效果取决于 Windows 合成器；模糊不可用时仍可显示半透明界面。

| 操作 | 功能 |
| --- | --- |
| 双击空白处 | 展开／收起 dashboard |
| 双击极简横条 | 打开 dashboard |
| 拖动空白处 | 移动，靠近屏幕边缘时贴边 |
| 齿轮／右键／托盘 | 设置与视图选项 |
| `‹` / `›` | 浏览历史周 |
| `−` 或 `Esc` | 收起到托盘，继续统计 |
| `Ctrl+Alt+G` | 隐藏／唤回，同时恢复鼠标交互 |
| 托盘 → 恢复鼠标交互 | 鼠标穿透或快捷键冲突时恢复操作 |
| `F5` | 立即刷新 |
| 托盘 → 退出 | 完全退出 |

### 进度条与金额的含义

进度条计算方式是：**已定价的 API 等值用量 ÷ 自己设置的周预算**。Codex、Claude 默认分别为 $100，可在设置中修改。超过预算时进度条保持满格，文字显示实际百分比。

这里显示的是自定预算进度。套餐官方剩余额度需要查看官方用量页。金额使用**标准、全球 API token 单价**折算，与订阅实付费用的计算口径不同，不包含极速／优先模式溢价、地区加价、工具调用费、税费、折扣或服务方其他账单调整。已支持的长上下文价格按单次请求判断。

未知模型的 token 会保留，缺少的费用会明确提示，已知部分金额以 `≥` 开头。旧版 Claude 日志缺少缓存时长时，按短期缓存写入单价估算并提示。已包含在输出中的推理 token 只计算一次。

### 数据来源

| 工具 | 默认目录 |
| --- | --- |
| Codex CLI／桌面会话 | `%USERPROFILE%\.codex\sessions\**\*.jsonl` |
| Codex 已归档会话 | `%USERPROFILE%\.codex\archived_sessions\**\*.jsonl` |
| Claude Code，含子代理日志 | `%USERPROFILE%\.claude\projects\**\*.jsonl` |

默认支持 `CODEX_HOME`、`CLAUDE_CONFIG_DIR` 环境变量，也可在设置中填写工具的**主目录**。WSL 日志可填写 Windows 能访问的路径。普通 Claude 网页／桌面聊天、其他设备或云端的活动，只有兼容的用量日志存在于所选目录时才会计入。

统计从现有日志重建：保留日志即可查看历史，删除日志会移除对应贡献。日志损坏、不可读或目录缺失会显示提示。读取器支持未写完的 UTF-8 行、正在追加的日志、文件截断和新会话发现；通过 Codex response ID、Claude message/request ID 去重，并协调新旧 Codex 记录，避免重复计算。

### 配置与价格

配置默认保存在程序旁的 `data` 文件夹。例如 `GlassPulse.exe` 使用 `data`，搬动整个程序文件夹即可带走配置，无需在系统盘安装应用数据：

```text
settings.json   # 预算、外观、目录和窗口位置
pricing.json    # 可编辑价格快照，单位为美元／百万 token
```

在设置中点击「编辑价格表」，保存后会在下次刷新时重算已加载的历史。价格表无效时继续使用上次有效值并提示。初始单价核验日期为 **2026-10-27**，包含 29 个模型条目。软件不会自动联网更新单价；自然周会自动切换，不依赖价格表更新。升级 EXE 时会保留已经编辑过的用户价格表。

官方来源：[OpenAI 定价](https://developers.openai.com/api/docs/pricing)、[GPT-6.1 Sol 上下文计价](https://developers.openai.com/api/docs/models/gpt-6.1-sol)、[Claude 定价](https://platform.claude.com/docs/en/about-claude/pricing)。价格表也保留了来源链接。

如需便携配置目录：

```powershell
.\GlassPulse.exe --data-dir "data"
```

## 编译与验证

在 Windows PowerShell 5.1 或 PowerShell 7 中运行：

```powershell
.\build.ps1 -Test
```

脚本使用 Windows 自带的 .NET Framework C# 编译器，无需下载 SDK、还原 NuGet 或调用付费服务。输出为 `dist\GlassPulse.exe`。测试覆盖去重、增量读取、缓存计价、模型别名、未知单价、长上下文、夏令时和周边界。

生成演示截图与检查原生交互：

```powershell
$exe = (Resolve-Path .\dist\GlassPulse.exe).Path
Start-Process $exe -ArgumentList '--preview build\preview --data-dir build\preview-data' -Wait
Start-Process $exe -ArgumentList '--smoke --data-dir build\smoke-data' -Wait
```

从其他工作目录运行时，请使用绝对输出路径。预览仅使用演示数据。`--report <路径>` 会扫描两次，并将汇总诊断写入本地 JSON；诊断文件应保留在本机。

## 仓库结构

```text
src/                 原生界面、图表、日志读取和计价
tests/CoreTests.cs   独立计数与读取场景测试
pricing.json         带官方来源的价格快照
assets/              图标和演示数据截图
docs/                需求说明和发布说明
build.ps1            Windows 编译与测试
package.ps1          便携 ZIP 与 SHA-256 清单
publish.ps1          通过已登录的 gh 发布全新仓库
.github/workflows/   Windows CI 与标签发布
```

## 调研过的相关项目

| 项目 | 适合的用途 |
| --- | --- |
| [ccusage](https://github.com/ccusage/ccusage) | 从本地日志生成丰富的 CLI token／费用报告 |
| [codex-bar](https://github.com/jspann21/codex-bar) | 轻量 Windows 悬浮条，重点显示 Codex 官方周容量 |
| [CodexWinBar](https://github.com/ItayCohen-Prog/CodexWinBar) | Windows 任务栏集成与多个提供商的额度查询 |

GlassPulse 采用自己的原生实现，侧重玻璃悬浮条和本地 token 分析，未打包上述项目的源代码。

## 许可证

[MIT](LICENSE)。
