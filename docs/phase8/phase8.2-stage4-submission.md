# Phase 8.2 Stage 4 最终材料

**日期：** 2026-11-18
**状态：** Stage 1–3 完成；**负责人已原则批准进入 Stage 5**（附执行条件）
**产品代码：** 零改动（`git diff 3288d98..5c760ce -- src/ tests/` 为空）

---

## 一、版本与提交身份（三者不同，勿混用）

| 用途 | 提交 | 说明 |
| --- | --- | --- |
| **产品代码回归基线** | `3288d98` | 最后一次触及 `src/`、`tests/` 的提交。回归数字（373 测试、18 基准、0 warning）以此为基准 |
| **r5 审查锚点** | `5e576dd` | 第五轮独立审查固化 md5 并据此裁决 PASS WITH CONDITIONS 的提交 |
| **当前文档 HEAD** | `5c760ce` | 本材料所基于的 HEAD（`3288d98` 之后均为文档改动） |

**三者关系：** `3288d98`（代码）→ 后续提交仅动 `docs/`，故产品代码自 `3288d98` 起未变；
`5e576dd` 是 r5 裁决时的文档状态，其后再有文档修订（4 处残留 + gitignore），
不影响 r5 的结论范围——r5 的 PASS 针对的是"4 个阻断项 + C3/C4/C5 是否关闭"，
而这些状态在 `5c760ce` 上只会更完整。

---

## 二、五轮审查的状态（统一口径）

| 轮次 | 文件 | 裁决 | 范围与要点 |
| --- | --- | --- | --- |
| r1 | `phase8.2-independent-review.md` | **FAIL** | 全量审查。否定计划两处核心主张（"Writer 已支持只缺接线"、"字号提前即解决溢出"），给出 M1–M12 |
| r2 | `phase8.2-rereview.md` | **FAIL** | 针对 M1–M12 的定向复审。3 项通过 / 9 项部分。指出 U=IN 事实错误、排序风险不存在、漏放大方向 |
| r3 | `phase8.2-review-r3.md` | **FAIL** | 全量审查。17 项缺陷**全部成立**，新发现 2 个 P0（渲染器不消费 FontFamily、TextBlock 层级错），列 4 阻断项 + C3/C4/C5 |
| r4 | `phase8.2-review-r4.md` | **FAIL** | 针对 4 阻断项的窄范围复审。3 项干净关闭，C3/C5 未落定。纠正 T2e 修法（只移动会让结果更差） |
| r5 | `phase8.2-review-r5.md` | **PASS WITH CONDITIONS** | **窄范围**：只验证 C3/C5 是否关闭 + C4/E-4/E-9/T2e 附带项。**不声称 25 项缺陷已修复** |

### r5 的 PASS 到底覆盖什么

**覆盖：** C3（破坏性变更计数一致）、C5（"同一画布"前置条件三处齐备）、
C4（文件表无幻影文件、路径正确、行数相符）、E-4（闸门挂 T2h）、
E-9（矛盾表已删）、T2e（改为"移动+注册同提交"）。

**不覆盖：** 任何缺陷的**实际修复**。25 项缺陷全部仍是计划状态。
r5 的条件正是"在 Stage 5 实施并按各任务的验收断言验证"。

**r5 的范围局限（其自述）：** 它明确声明只做"是否关闭"的判定，
未重新审计项目；且指出基线在审查期间移动过（`4df0c28`→`5e576dd`），
其 PASS 锚定 `5e576dd`，若以 `4df0c28` 为准则为 FAIL。

---

## 三、缺陷与任务

| 项 | 实测值 |
| --- | --- |
| 缺陷总数 | **25**（P0 8 / P1 15 / P2 2） |
| 缺陷正文 | 25 节，`### 缺陷 N 【Px】`，按 1–25 升序 |
| §4 缺陷行 | 25（每行有明确任务） |
| §4 任务引用 | 25 |
| §4 不同任务 | 24（缺陷 2 与缺陷 10 共用 `T1`） |
| §9 任务行 | **27**（24 + `T2h` 闸门实现 + `T8`/`T9` 两个跨任务门禁） |

**编号体系：** 审计正文统一用 `缺陷 N`（不再用 `§2.x`），与 §10 表和 plan §4 一一对应。
此前正文编号错乱（2.1→2.7→2.10→2.21→…→2.8），已按缺陷编号重排。

---

## 四、关键实施说明（易错处）

### T2e：必须同时完成两件事，且同一提交

```
Character/Row 写入  +  FaceNames 注册
```

**只做前者会让结果比现状更差**（实测三变体）：

| 变体 | font | textColor | fontSize |
| --- | --- | --- | --- |
| 现状 | None | None | 18 ✅ |
| 只移入 `Character/Row` | None | None | **None** ❌ |
| 移入 + 注册 | Verdana | #FF0000 | 18 ✅ |

原因：`Character/Row` 的 `Font` 单元格存**索引**（指向 `FaceNames`），
写字符串会使整个 `Character` 段失效，连原本正确的 `fontSize` 一并丢失。

### 缺陷 22 的映射

缺陷 22（`LocalRelayout` 不检查越界碰撞）→ **任务 T5b**。
验收：`containerLayouts` 施加后 `node.overlap` 必须为 0，或该次重排被回退并进 `unmet`。

---

## 五、回归基线（产品代码 `3288d98`）

```console
$ dotnet build VsdxEngine.sln -c Release --no-incremental
exit: 0    warnings: 0

$ dotnet test VsdxEngine.sln -c Release
exit: 0    失败: 0    通过: 373

$ 18 个基准
passed: 18 / 18
```

详见 `phase8.2-regression-baseline.md`（含命令、退出码、结果）。

---

## 六、材料清单（与当前 HEAD `5c760ce` 一致）

| # | 文件 | 内容 |
| --- | --- | --- |
| 1 | `phase8.2-audit.md` | 现状审计：25 项缺陷，正文按编号 1–25 排序 |
| 2 | `phase8.2-plan.md` | 实施计划：19 节，27 个任务行，11 张任务书逐条映射表 |
| 3 | `phase8.2-independent-review.md` | r1 审查（FAIL） |
| 4 | `phase8.2-rereview.md` | r2 复审（FAIL） |
| 5 | `phase8.2-review-r3.md` | r3 审查（FAIL） |
| 6 | `phase8.2-review-r4.md` | r4 复审（FAIL） |
| 7 | `phase8.2-review-r5.md` | **r5 审查（PASS WITH CONDITIONS，窄范围）** |
| 8 | `phase8.2-owner-rulings.md` | 负责人裁决登记（D1/D2/D3/D-4/D-8/D-18） |
| 9 | `phase8.2-open-decisions.md` | 决策事项最终状态（全部已裁定） |
| 10 | `phase8.2-review-packet.md` | 提交摘要 |
| 11 | `phase8.2-regression-baseline.md` | 回归基线（命令/退出码/结果） |
| 12 | `phase8.2-visual-review.md` | 四类图人工视觉检查基线 |
| 13 | `phase8.2-implementation-readiness.md` | 实施就绪度（已验证 vs 待确认） |
| 14 | 本文件 | Stage 4 最终提交说明 |

---

## 七、Stage 5 执行纪律（负责人指定）

每个子任务独立提交、独立验收，完成后必须执行：

1. Release build，0 warning
2. 全部测试
3. 18 个基准
4. 对应缺陷的专项验收断言

**不得**以测试数量增加代替能力验收；**不得**降低原有验收要求制造通过。
任一退化即回退该子任务。

顺序：`T0 → T0.5 → T1`，之后按计划依赖（见 `phase8.2-implementation-readiness.md` §4）。

---

## 八、边界

- 25 项缺陷在**全部验收通过前仍是待修复项**，不得提前宣称 Phase 8.2 完成
- L4 Visio 原生验证不作为当前 Stage 5 阻塞条件；L1/L2/L3 须持续通过
- 本阶段不做：外部 Stencil、图片/SVG 嵌入、复合图形、多区域协调、Word OLE、GUI
