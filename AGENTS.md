# 工作约定

本目录下有两个独立 git 仓库：`Match3/`（Unity 工程，权威源码）与 `PolyMatch3.Web/`（WASM 游乐场，框架层与 Match3 逐文件同步）。

## 验证流程（owner 定稿）

**每次改动完成后，由用户在 PowerShell 手动验证**（agent 的 Git Bash 可能因 cygwin 故障不可用）：

```powershell
# Match3：测试必须全绿
cd C:\Users\yeqingxin\code\MatchThree\Match3\Tests; dotnet test

# Web：编译必须零错误（Game 层会连带编译 Core/Tools/Samples，覆盖大部分改动）
cd C:\Users\yeqingxin\code\MatchThree\PolyMatch3.Web\src; dotnet build PolyMatch3.Game

# 改了 Bridge 或要出包时（需要 wasm-tools 工作负载）
cd C:\Users\yeqingxin\code\MatchThree\PolyMatch3.Web; .\publish-web.ps1
```

agent 改完代码后应主动给出对应的验证命令，用户跑完贴回输出。

## 同步纪律

- `PolyMatch3.Web/src/PolyMatch3`、`PolyMatch3.Tools`、`PolyMatch3.Samples` 与 Match3 的 `Assets/Scripts/` 下对应目录**逐文件一致**；Match3 侧改动必须同步到 Web 侧（Unity 侧多出的 `.meta` 文件不复制）。
- **同步脚本（机器化执行纪律）**：`PolyMatch3.Web\sync-web.ps1`（版本化在 Web 仓库）。改完 Match3 侧跑一次 `powershell -ExecutionPolicy Bypass -File PolyMatch3.Web\sync-web.ps1`（`-WhatIf` 预演、`-DeleteExtras` 清 Web 侧多出文件）。行尾归一比较（LF/CRLF 不误报），自动跳过 obj/bin。
- **不同步清单**（脚本内置，同名不同归属）：`PlayerSwapStep.cs`/`RevertSwapStep.cs`（Web 侧在 PolyMatch3.Game 项目）、`SampleCatalogRegistration.cs`（依赖未引入 Web 的 Defs 层）。
- **路径映射（易错）**：Match3 侧 Tools 是框架**子目录** `Assets/Scripts/PolyMatch3/Tools/`，Web 侧是**独立项目** `src/PolyMatch3.Tools/`；Samples 则两边都是平级目录（`PolyMatch3.Samples`）。新建文件先核对既有同类文件的位置。
- Match3 侧新增 `.cs` 文件必须同时创建同名 `.meta`（fileFormatVersion: 2 + 唯一 guid）。

## 设计文档

- `Match3/docs/game-design.md`：**游戏宪法（锚定版，动工前必读）**——结算牌序/双键排序/三类钩子/两层状态/IPieceDeck/WinCheck/表现栅栏/配置分层/技术债红线/实施顺序
- `Match3/docs/architecture.md`：架构总览（权威）
- `Match3/docs/toolbox-design.md`：工具箱四原语与扩展路线（仲裁/选择器/生成物裁决/平行层等）
