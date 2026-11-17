# Phase 8.1 实施报告

**日期：** 2026-11-17
**范围：** P0 保真修复 → ShapeOrder → FreePath → TextBlock → 多页协议 → 纵向切片
**状态：** A–F 完成；G（L4 原生验证）待环境

---

## 1. 完成情况

| 任务 | 状态 | 证据 |
| --- | --- | --- |
| A. P0 Geometry 注入修复 | ✅ | `p0-geometry-injection.md` + `FidelityTests`（7） |
| A2. 最小保真结构对比测试 | ✅ | 同上，已提前到 8.1 |
| B. ShapeOrder 读取与写入 | ✅ | `ZOrderTests`（7） |
| C. FreePath 读取/创建/编辑/保留 | ✅ | `FreePathTests`（11） |
| D. TextBlock | ✅ | `TextBlockTests`（14） |
| E. IR 多页协议 | ✅ | `MultiPageTests`（10） |
| F. 纵向切片 + L1–L3 | ✅ | `examples/phase8/slice.*` |
| G. L4 Visio 原生验证 | ⬜ **阻塞** | 环境探测完成：WSL interop 可用，但**未安装 Visio**（仅 WPS）。见 `l4-verification-status.md` |

**测试：** 297 → **333**（+36）
**基准：** 18/18 硬约束通过
**构建：** 0 warning

---

## 2. 四个真实缺陷（全部由测试发现，非推测）

### 2.1 编辑 FreePath 产生重复形状

接入 `update` 后，编辑的路径**与旧副本同时存在**。
根因：增量编辑器看不见几何形状，其保存会把旧 shape 重新写出。

**修复：** 写入前删除同名 `FreePath` 形状并重算空闲 id。
**关键克制：** 删除时跳过 master-backed / Container / Connector——
删掉圆形的 25 行几何就是破坏真实节点。

### 2.2 ShapeOrder 单条记录被忽略

守卫写的是 `Count > 1`，导致"只把某一个形状置顶"这种合法用法被静默跳过。

### 2.3 TextBlock 字号单位错误

首次渲染四个文本全是极小的统一字号，尽管 XML 写着 `Size="20"`。
Visio 的 `Character` 单元格 `Size` 是**英寸长度**，`U="PT"` 记录创作单位——
裸写 `20` 意味着 20 英寸。对照库自建形状的 `V="0.333333" U="PT"`（24pt）才定位到。

**这是"格式正确但语义错误"的典型**：XML 完全合法，只有渲染才暴露。

### 2.4 TextBlock 被页面裁剪

`FitPage` 与 `Recenter` 只遍历 `page.Nodes`，
于是标题、图例、注释全部落在收缩后的页面之外——**交付物上直接消失**。

**修复：** 抽出 `ContentBounds(page)` 统一包含 nodes / groups / textBlocks / paths，
`Recenter` 同步移动文本与路径。

**连带修正：** 页面利用率原本只按节点计算，加入文本后又把"宽横幅"当成填满。
改为按**实际绘制范围**（含容器与文本）计算。

---

## 3. 纵向切片

`examples/phase8/slice.json` → `slice.vsdx` / `slice.png` / `slice.svg`

**包含全部要求的对象类型：**

| 要求（任务 §6） | 实现 |
| --- | --- |
| 传统 Node / Edge | 4 节点 + 3 连线 |
| 独立文本 | 4 个 TextBlock（标题/副标题/图例/注释） |
| 自由路径 | 1 条分隔线（自定义 Geometry） |
| 语义符号 | Hexagon 网关、Process 数据库 |
| 页面区域 | 3 个带标题容器 |
| 不同样式 | 各角色不同配色与字号 |

**L1：「包结构」** `validate` 全 PASS
**L2：「读回」** 4 节点 / 3 边 / 3 容器 / 4 文本 / 1 路径，全部还原
**L3：「渲染」** PNG 1680×1260，视觉可见全部元素

**链路完整：** IR → Model → Layout → Writer → 保存 → 回读 → Render → Check

---

## 4. 与负责人决策的对应

| 决策 | 落实 |
| --- | --- |
| 1. 先定位根因 | ✅ 定位到库在 Save 时合成，非本项目写入器 |
| 1. 上层修复优先 | ✅ post-save 精准剥离，未改库 |
| 1. 不得静默损坏 | ✅ 有 `write.geometry-preserved` 记录；当前修复已足够，无需拒绝 |
| 2. FreePath 五环节验证 | ✅ 创建/读取/编辑/保存/重读全测 |
| 2. 未知指令保留原文 | ✅ `ArcTo` 往返保留，且渲染为真实曲线 |
| 4. L4 提前 | ⬜ 环境未就绪 |

---

## 5. 明确未做

- **L4 Visio 原生验证**：已探测环境——WSL 互操作可用，Windows 二进制可执行，
  但 `HKLM` 注册表仅有 WPS Office，**无 Microsoft Visio**。
  已写好 8 项验证清单与可用的 PowerShell COM 方案，待环境就绪即可执行。
  **未用 WPS 替代**（那是另一个第三方渲染器，属 L3）。
- **图片/SVG 工程化**：属 8.3；本轮只完成格式实验（8.0）
- **Stencil 接入**：属 8.3
- **`check-fidelity` 完整版**：8.1 只做了最小结构对比（A2），完整工具在 8.5

---

## 6. 遗留风险

| 风险 | 说明 |
| --- | --- |
| 几何剥离依赖"5 行矩形"签名 | 库若改默认形状，测试会报警 |
| 路径删除按 `NameU="FreePath"` 判定 | 若外部文件恰有同名形状可能误删（概率极低，且已排除 master/容器） |
| 文本块强制尺寸 | 长文本不自动换行扩展，超出会溢出框体 |
| 切片右侧留白 | 由作者给文本块的宽度决定，非布局缺陷 |
