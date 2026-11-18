# vsdx-engine — 项目约定

在未安装 Visio 的环境中创建、读取、增量修改和渲染真实 VSDX 文件。
C#/.NET 8，七个项目，**无循环依赖**（`Cli` 与 `Protocol` 都直接引用 `Engine`，不是严格链式）。

## 读什么

| 需要 | 文档 |
| --- | --- |
| 全貌 | [`README.md`](README.md) |
| 文档地图 | [`docs/README.md`](docs/README.md) |
| 能画什么、不能画什么 | [`docs/capability-boundaries.md`](docs/capability-boundaries.md) |
| 验证到哪一步 | [`docs/limitations.md`](docs/limitations.md) |
| 当前状态 | [`PROJECT-STATUS.md`](PROJECT-STATUS.md) |
| Agent 用法 | [`skill/SKILL.md`](skill/SKILL.md) |

数字（提交数、测试数、行数）**不写进文档**，用命令指针。

## 命令

```bash
source .envrc.sh                                   # 必需，否则没有 dotnet
dotnet build VsdxEngine.sln -c Release --no-incremental
dotnet test VsdxEngine.sln -c Release
```

## 本项目的硬约束

**一、报告而不静默。** 协议接受、Schema 允许、执行时忽略、结果又不报告——这四种情况同时出现是不可接受的。
做不到的请求必须进 `unmetConstraints`；做了的必须进 `appliedDesignNotes`。

**二、验证分级不可混淆。** L1 包结构 / L2 读回 / L3 渲染 / **L4 Visio 原生**。
L4 **从未执行**，L1–L3 不能替代。任何"原生可编辑"的断言都需要 L4 证据。

**三、用 CLI 验证效果前必须全量重建。** `dotnet build` 不加 `--no-incremental` 会留下旧二进制，
CLI 仍跑旧代码。"改完测了但没变"几乎总是这个原因。

**四、改动前先用 CLI 或测试复现。** 不要凭源码推断行为——夹具写错会伪装成产品缺陷。
怀疑产品有缺陷时，先做这三件事：

- **打印实际值。** `export-model` 导出产物，看 JSON 里的真实数字，不要看断言消息。
- **确认字段位置。** IR 的 `metadata` 是嵌套对象（`metadata.description`），不是顶层字段；
  放在顶层会被 JSON 绑定静默丢弃，看起来像引擎丢了数据。
- **先确认测试真的在失败。** 失败信息可能来自旧二进制或已被回退的改动。全量重建后单独重跑
  那个测试，再决定是否动手；改完回退后也要回头确认，不要沿用旧的失败结论。
- **确认方向。** 无容器的扁平图默认 `direction=leftToRight`：`layered` 会把节点排成横向一排，
  纵坐标全是常数。要测纵向，显式写 `"direction":"topToBottom"`。
  有容器时按结构自动推断为纵向，两种写法结果相同。

**五、文档与代码由测试绑定。** 改能力/数字/命令时，对应测试会失败——那是设计，不是障碍。

## 临时文件

CLI 夹具写在 `.x/`、`.review*/` 等点目录。**`git add -A` 会把它们扫进仓库**——
提交前先 `git status`，`.gitignore` 用模式覆盖，不要逐条列名字。

## 当前阻塞

**L4 需要一台装有 Microsoft Visio 的 Windows 机器。** 本机无 Visio，`/mnt/c` 只读，WPS 不可替代。
脚本 `tools/l4/verify-visio.ps1` 已写并做过静态审查，从未运行。
