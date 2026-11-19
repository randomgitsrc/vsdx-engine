# 实验：独立 Agent 自主制图（第 1 次）

**日期：** 2026-11-19
**状态：** 完成（Verified）
**依据 Goal：** §1.3 第 1 条「Agent 能否通过明确接口完成真实制图任务」、§15.2 阶段 1

---

## 一、这是要验证什么

Goal §1.3 的第一条成功标准。此前**从未被测试过**——`ROADMAP.md` 声称阶段 A 已完成，
但 `docs/phase8/phase8.3-realworld-validation.md` 自述第 1 步是「手工转为 JSON（未测 Agent 语义理解）」。

**本次是真的测**：一个未参与项目的 Agent，只读 `skill/SKILL.md`，从自然语言需求开始。

---

## 二、方法

**给 Agent 的唯一指令**（原文见对话记录）：

- 读 `skill/SKILL.md`（当时 521 行）
- **不得读** `docs/diagram-knowledge/`、`docs/research/`、`docs/product/`、`goal-2.1.md`、`ROADMAP.md`
- 可以读 `docs/api.md`、`docs/capability-boundaries.md`、`examples/`
- 最多迭代 3 轮（实际用了更多，见 §四）

**需求**（模拟真实用户，未泄露实现细节）：

> 订单系统分三块：接入层（网页+App→API网关）、业务层（订单/支付/库存，互相调用）、
> 数据层（PostgreSQL+Redis）。另有统一 Kafka，业务服务发事件，下游分析系统消费。
> **这张图要打印在 A4 纸上给领导看。**

---

## 三、结果

| 项 | 值 |
| --- | --- |
| 最终分数 | **96** |
| `hardConstraintsPassed` | **true** |
| 利用率 | 84% |
| `validate` | 全项 PASS |
| 迭代轮数 | 5 个有效版本 + 2 个回退 |
| **读源码时间占比** | **约 40%** |

**产物**：`final.vsdx` / `final.png` / `final.model.json`（本目录）

---

## 四、图中存在的问题（Agent 自评，我核实）

| # | 问题 | 我的核实 |
| --- | --- | --- |
| 1 | `支付事件` 那条线是斜的，因为 `rightAngle` 被降级成 `curved` | ✅ 成立，但**根因不是几何**——XML 里是 5 行正交折线。是读回时标签错了 |
| 2 | 两个标签挤在业务带下沿（约 0.2"），`check-layout` 没报 | ✅ 成立。`label.overlaps-label` 阈值 0.02" 未触发，属检测盲区 |
| 3 | 2 处连线交叉，拓扑性 | ✅ 成立，与 `capability-boundaries.md` §8 一致 |
| 4 | 带高 1.75" 装 0.95" 节点，留白偏多 | ✅ 成立，视觉判断 |

---

## 五、发现的引擎缺陷（2 项，均已修）

### 5.1 `SKILL.md` 教了错误的字段名，且失败是静默的

§6c 的 `textBlocks` 示例用 `box: {x, y, width, height}`——那是 **IR 语法**（`execute`）。
`create` 读的是 `bounds: {x, y, w, h}`。

**我的核实**：请求 `box: (7,2,3,0.6)`，实得 `(0,0) 4x0.5`（默认值），`ok: true`，0 error。

Agent 照抄后 v1 得 **0 分**，标题被裁到页面左下角，`check-layout` 只报 `page.margins 左-2.00`——
**错误信息完全指错方向**。

**已修**：`skill/SKILL.md` 加了两种路径的字段对照表 + 症状到原因的提示。

### 5.2 `rightAngle` 读回成 `curved`

**几何是对的**（5 行折线、2 个 waypoints），但读回时 `kind` 变成 `curved`。
`OfficeIMO` 把所有路由过的连接线报为 "curved"，Reader 直接透传。

**已修**：Reader 检查每段是否轴对齐，是则恢复 `rightAngle`。

### 5.3 我核实后**否定**的一条

Agent 说 `check-layout --json` 没有 `level` 字段。**实际有**：
`{code, level, message, page, recommendedFix, rootCause, severity, targets}`。

---

## 六、`SKILL.md` 的缺口（Agent 报告，按我核实的结论分类）

| # | 缺口 | 我是否认可 |
| --- | --- | --- |
| 1 | §6c 的 `box` 语法错误 | ✅ **已修** |
| 2 | IR 无字段表，`IrNode` 无 `shape` 字段 | ✅ 成立。IR 路径无法精确指定形状 |
| 3 | `--layout auto` 被过度推荐，无「何时该手写」判据 | ✅ **有实测反例**（见下）|
| 4 | `create` 路径无法指定纸张，只能手算 A4 英寸值 | ✅ 成立 |
| 5 | 几何三常量（0.46/0.12/0.22）未文档化 | ✅ 成立，值我核实过完全相同 |
| 6 | `Group.Order` 在 layered 布局下**不生效** | ✅ 成立。`LayeredLayoutEngine` 零引用 |
| 7 | `rightAngle` 被降级为 `curved` | ✅ 成立（已修）|
| 8 | `check-layout` 的 `level` 字段不存在 | ❌ **不成立** |

### 6.1 auto 布局的反例（可复现）

| 路径 | 分数 | 利用率 | 问题 |
| --- | --- | --- | --- |
| 手写坐标（Agent 采用） | **96** | 84% | 2 处拓扑交叉 |
| `execute --layout auto` | **84** | 54% | `text.overlaps-container` + `text.overlaps-node` ×2 |

证据：`auto.ir.json` + `auto.design.json` 在本目录，可复跑。

**而 `SKILL.md` 把 auto 标为 "preferred"。**

---

## 七、结论

**Goal §1.3 第 1 条成立，但有条件。**

| 判断 | 依据 |
| --- | --- |
| Agent 能独立完成真实制图任务 | ✅ 96 分，硬约束通过，未人工干预坐标 |
| 但需读源码补知识 | ⚠️ 40% 时间花在读 `DiagramLayout.cs`、`DiagramIr.cs` 等 |
| 指南只覆盖「怎么点按钮」 | ⚠️ 未覆盖「写什么字段」和「几何为什么会撞」 |

**关键判断**：一个**不读源码**的 Agent 会卡死在 §6c 的 `box` 语法上——v1 已经证明。

---

## 八、后续行动

| 优先 | 动作 |
| --- | --- |
| P0 | 修 `SKILL.md` §6c（**已完成**） |
| P0 | 修 `rightAngle` 读回（**已完成**） |
| P1 | 补 IR 字段表，或生成 IR schema |
| P1 | 补几何三常量，或给出「节点在带内必须下沉」的规则 |
| P1 | 给 `--layout auto` 加「何时该手写」判据 |
| P2 | 修 `Group.Order` 在 layered 下无效（或从文档删除该说法） |
| P2 | `create` 路径支持指定纸张 |

---

## 九、如何复现

```bash
source .envrc.sh
CLI=src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool

# 最终产物
$CLI check-layout --input docs/research/experiments/agent-trial-01/final.vsdx   # 96

# auto 反例
$CLI execute --ir docs/research/experiments/agent-trial-01/auto.ir.json \
             --spec docs/research/experiments/agent-trial-01/auto.design.json \
             --output /tmp/auto.vsdx --json                                 # 84
```
