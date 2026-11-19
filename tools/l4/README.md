# L4 原生验收工具

**目的：** 用 Microsoft Visio 打开 Engine 产出的 VSDX，验证 L1–L3 无法覆盖的部分。

---

## 为什么需要单独的工具

L1（包结构）、L2（后端重载）、L3（第三方渲染）都不能证明"Visio 能正确打开并保留编辑能力"。
这三层全部通过而 Visio 打开失败是完全可能的，因为只有 Visio 自己知道它的完整规则。

**不得用其它层级替代本工具。**

---

## 前置条件

| 项 | 要求 |
| --- | --- |
| 操作系统 | Windows（或 WSL + 可用的 Windows 侧 Visio） |
| 软件 | Microsoft Visio（桌面版，非查看器） |
| PowerShell | 5.1+，允许执行脚本 |
| 待测文件 | 能被 Windows 进程读到，**且包含下表列出的对象**（见「用例要求」）|

---

## 用法

```powershell
# 直接把待测文件拷到 Windows 可写目录后运行
powershell -ExecutionPolicy Bypass -File verify-visio.ps1 `
    -Vsdx C:\work\slice.vsdx `
    -Output C:\work\slice.l4.json
```

退出码：`0` = 全部检查通过，`1` = 有失败项或异常。

---

## 用例要求

脚本的检查针对**具体对象**，用例缺哪一类，对应检查就会失败或跳过。合格的用例应包含：

| 对象 | 为什么需要 | 怎么产出 |
| --- | --- | --- |
| 至少 2 个**字号不同**的文本块 | 验证字号层级 | `textBlocks` 里放一个 `role=title`、一个 `role=note` |
| 至少 1 个**容器** | 验证 `msvStructureType` 写入 | 节点设 `groupId` |
| 至少 1 条**自由路径** | 验证自定义几何 | 模型里加 `paths[]` |
| 至少 2 个节点 + 1 条连线 | 验证 Z-order 与连线 | 常规内容 |

引擎自身的 `examples/realworld/*.vsdx` 缺自由路径，需要另外构造。用 `create` 时加
`--layout auto --fit-text --fit-page` 可让四个场景的分数与 `check-layout` 一致。

## 检查内容

| # | 检查 | 判据 |
| --- | --- | --- |
| 1 | 能否打开 | 无修复提示、无异常 |
| 2 | 页面数 | ≥ 1 |
| 3 | 形状数 | > 0 |
| 4 | 文本块存在 | 存在 `NameU = TextBlock` 的形状 |
| 5 | **字号层级** | ≥ 2 种不同字号（标题应明显大于注释） |
| 6 | 文本非空 | 每个文本块都有内容 |
| 7 | 自定义几何 | 自由路径至少 2 行几何（`RowCount(10) -ge 2`；两点的路径就是 MoveTo + LineTo）|
| 8 | Z-order | 能按文档顺序枚举形状 |
| 9 | 容器存在 | 存在 `msvStructureType = Container`；**用例无容器时此项会失败** |
| 10 | 保存后重开 | 形状数不变 |

---

## COM 能验证什么、不能验证什么

**能：** 文件能否打开、对象数量、属性数值、文字内容、保存往返。

**不能：** 字号层级在视觉上是否明显、标题位置是否合理、配色是否符合设计意图、
连线是否可读。这些必须由人看。

脚本在 JSON 里输出 `humanReviewRequired` 字段列出这四项，
避免"自动化全绿"被误读成"视觉合格"。

---

## 当前环境状态（2026-11-17 实测）

本机为 WSL，**互操作可用**（`cmd.exe` 可执行），但有两个硬限制：

```console
$ mount | grep ' /mnt/c '
C:\ on /mnt/c type 9p (ro,nosuid,nodev,noatime,...)
```

1. **`/mnt/c` 是只读挂载（`ro`）** —— 不能往 Windows 侧写文件
2. **未安装 Microsoft Visio** —— 注册表仅有 WPS Office 2023

因此本工具**尚未执行过**。WPS 不能替代（那是另一个第三方渲染器，属 L3）。

**Windows 侧能看到本仓库**（`\\wsl.localhost\...` 路径有效），
所以一旦装上 Visio 且找到一个可写位置，脚本可直接使用。
