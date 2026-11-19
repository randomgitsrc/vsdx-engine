# 文档索引

## 先读这三份

| 文档 | 回答什么问题 |
| --- | --- |
| [`capability-boundaries.md`](capability-boundaries.md) | **能画什么、不能画什么**。四种可用结构，两种不适用，以及"即使好图也不保证"的三件事 |
| [`limitations.md`](limitations.md) | **验证到哪一步**。L1 包结构 / L2 读回 / L3 渲染已验证，**L4 Visio 原生从未执行** |
| [`RELEASE-PROPOSAL.md`](RELEASE-PROPOSAL.md) | **为什么是 0.9.0**，以及发布剩余动作 |

## 使用

| 文档 | 内容 |
| --- | --- |
| [`../README.md`](../README.md) | **项目总览与快速开始**——第一次接触本项目从这里进 |
| [`../skill/SKILL.md`](../skill/SKILL.md) | Agent 使用指南：选图型、写模型、`create` 与 `execute` 两条路径、读结果 |
| [`api.md`](api.md) | CLI 与 C# API 完整参数 |
| [`installation.md`](installation.md) | 安装与环境要求 |
| [`../CHANGELOG.md`](../CHANGELOG.md) | 对外可见的能力变化 |

## 专业制图知识

**这是 Goal 2.1 §4 要求的知识基线**——让未参与此前讨论的 Agent 也能理解产品要求。

| 文档 | 内容 |
| --- | --- |
| [`diagram-knowledge/architecture-diagrams.md`](diagram-knowledge/architecture-diagrams.md) | 专业架构图的七种构成要素；可判定的评审清单 |
| [`diagram-knowledge/diagram-complexity-levels.md`](diagram-knowledge/diagram-complexity-levels.md) | **Level 1–3 的校准**：E（表达）/ D（布局）双维度模型 |
| [`diagram-knowledge/output-profiles.md`](diagram-knowledge/output-profiles.md) | A4/A3/Web 适配与物理可读性（成品字号公式） |
| [`research/index.md`](research/index.md) | **外部来源登记、实验记录、证据缺口** |
| [`plans/active/phase0-to-phase1-plan.md`](plans/active/phase0-to-phase1-plan.md) | 阶段 0 → 1 的实施计划 |
| [`product/management-consistency-report.md`](product/management-consistency-report.md) | 管理文件冲突/遗漏/重复清单 |

## 设计与架构

| 文档 | 内容 |
| --- | --- |
| [`architecture.md`](architecture.md) | 七个项目的分层与依赖方向 |
| [`dependencies.md`](dependencies.md) | 外部依赖清单与选择理由 |
| [`phase8/capability-matrix.md`](phase8/capability-matrix.md) | 能力逐条状态（完整/部分/缺失/格式可行），文末有汇总表 |
| [`phase8/palette-contrast.md`](phase8/palette-contrast.md) | 40 个角色色的 WCAG 实测值 |
| [`phase8/scale-benchmark.md`](phase8/scale-benchmark.md) | 规模实测曲线（200–5000 节点） |
| [`phase8/score-weights.md`](phase8/score-weights.md) | 评分权重表，以及「为何是这个权重」的缺口 |
| [`phase8/known-issues.md`](phase8/known-issues.md) | **当前仍未解决的问题**，每项写明为何未修 |
| [`phase8/status-2026-11.md`](phase8/status-2026-11.md) | **项目现在处在什么位置**：验证到哪一步、能画什么、未解什么 |
| [`phase8/phase8.3-checkpoint.md`](phase8/phase8.3-checkpoint.md) | 阶段检查点：做了什么、未解什么 |

## 阶段记录

`phase8/` 下有几十份文件（`ls docs/phase8/*.md | wc -l`），大部分是**过程记录**——审查轮次、评审包、
阶段性报告。
它们是决策依据，不是入门材料。

| 文件 | 性质 |
| --- | --- |
| `phase8.3-conclusion.md` | **当前结论**：已建立与未建立什么 |
| `phase8.3-candidates.md` | 22 个条目（C1–C20、C22、C23）的发现与处置；C22 已撤回 |
| `phase8.3-*.md` | 真实场景、时序图、人工编辑三项验证 |
| `phase8.2-review-r*.md`、`*-packet.md` | 审查过程记录，仅作追溯 |
| `phase8.0-report.md`、`phase8.1-report.md` | 历史阶段报告 |

## 文件职责

```
docs/            人读文档
  phase*/        阶段记录，按时间累积，不追求最新
schemas/         JSON Schema，协议契约
skill/           Agent 指令
src/             产品代码（七个项目，依赖单向）
tests/           测试
benchmarks/      18 个计分基准 + 8 个故障注入用例
examples/        可交付示例（IR + PNG）
tools/           运维脚本（L4 验证等）
```
