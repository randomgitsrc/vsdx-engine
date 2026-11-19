# 研究索引

**用途：** 登记所有外部研究、来源与实验，使后续 Agent 能独立复核。
**依据 Goal：** `goal-2.1.md` §8.3（资料来源）、§8.6（研究沉淀）

**状态分类**（Goal §2.4）：`Verified` / `Partially Verified` / `Hypothesis` / `Proposed` / `Superseded`

---

## 一、外部来源登记

| ID | 来源 | 类型 | 获取日期 | 用于 | 可靠度 |
| --- | --- | --- | --- | --- | --- |
| S1 | [C4 model 官方评审清单](https://c4model.com/diagrams/checklist) | 一手，作者 Simon Brown，CC BY 4.0 | 2026-11-19 | 专业图的**可判定评审问题** | 高 |
| S2 | [C4 model 首页](https://c4model.com/) | 一手 | 2026-11-19 | 确认 C4 是分层抽象而非复杂度分级 | 高 |
| S3 | [AWS 官方布局指南（awslabs）](https://raw.githubusercontent.com/awslabs/agent-plugins/main/plugins/deploy-on-aws/skills/aws-architecture-diagram/references/layout-guidelines.md) | 一手，AWS 官方仓库 | 2026-11-19 | 量化间距阈值、13+ 服务规则、边路由规则 | 高 |
| S4 | [Elsevier 制图规范](https://www.elsevier.com/en-gb/about/policies-and-standards/author/artwork-and-media-instructions/artwork-sizing) | 一手，学术出版方 | 2026-11-19 | 物理可读性下限（正文 7 pt，最小 6 pt） | 中高（原文自称 rule-of-thumb）|

### 1.1 来源的关键内容摘录

**S1（C4 评审清单）** —— 提供可判定的检查项：

- 图是否有标题、图类型、图范围、图例
- 每个元素是否有名称、类型、用途、技术选型
- 所有缩写、颜色、形状、图标、边框样式、元素尺寸的含义是否可理解
- **每条箭头是否有描述意图的标签**，方向是否与描述一致
- 所有箭头、线型的含义是否可理解

**S3（AWS 布局指南）** —— 提供量化阈值：

- 120px 服务容器之间：水平 180px / 垂直 120px
- **13+ 服务**：水平 220px / 垂直 160px
- 箭头端点与标签文字 30px 净距
- 一个服务多条出边**必须从不同侧面引出**
- 非相邻连接**必须加 waypoint 绕开中间容器**
- 每张图决策点最多 **1–2 个**
- 辅助服务仅 CloudWatch/CloudTrail/X-Ray/IAM，且无连线、放虚线框

**S4（Elsevier）** —— 印刷可读性：

> "lettering on the artwork should have a finished, printed size of **7 pt** for normal text and no smaller than **6 pt** for subscript and superscript characters. Smaller lettering will yield text that is hardly legible."

---

## 二、研究文档

| 文档 | 状态 | 研究的 Goal 条款 |
| --- | --- | --- |
| [`../diagram-knowledge/diagram-complexity-levels.md`](../diagram-knowledge/diagram-complexity-levels.md) | Proposed | §4.3 分级校准 |
| [`../diagram-knowledge/output-profiles.md`](../diagram-knowledge/output-profiles.md) | Proposed | §6.1–6.4 输出适配与可读性 |
| [`../product/management-consistency-report.md`](../product/management-consistency-report.md) | Confirmed（事实核对）| §12 文件治理、§15.1 阶段 0 |

---

## 三、实验记录

**已完成 6 个实验**（均在本机可复现）：

| 编号 | 文件 | 结论 |
| --- | --- | --- |
| E1 | [`experiments/E1-band-expression.md`](experiments/E1-band-expression.md) | 通条只能视觉近似，`groupId=null`，**无跨层语义** |
| E2 | [`experiments/E2-multi-text-component.md`](experiments/E2-multi-text-component.md) | 组件内**无分区样式**，三行文本共用一套 font/fill |
| E3 | [`experiments/E3-density-and-font.md`](experiments/E3-density-and-font.md) | 引擎**不检查成品字号**，24 组件放 A4 仍报 `succeeded` |
| E4 | [`experiments/E4-multi-run-text.md`](experiments/E4-multi-run-text.md) | 组件内分区文本**可写入但不可验证**——Renderer 不读字符级样式，L4 前无法证明 |
| E5 | [`experiments/E5-crossing-awareness.md`](experiments/E5-crossing-awareness.md) | 路由器对交叉**结构性无感知**；且现有交叉几乎全在 straight 边上，改正交搜索零收益 |
| E6 | [`experiments/E6-page-width.md`](experiments/E6-page-width.md) | `canvas.auto` 不收缩页面（cbo 右侧空 4.41"），`canvas.fixed` 可收缩；成因未定位 |
| E7 | [`experiments/E7-heading-column.md`](experiments/E7-heading-column.md) | 容器标题与首列节点同起点，垂直连线必然穿标题；**是布局问题不是路由问题** |
| E8 | [`experiments/E8-label-position.md`](experiments/E8-label-position.md) | `LabelPosition` 写入产物但**渲染器不读**，标签切边框无法修（同 E4 的依赖限制）|
| E9 | [`experiments/E9-fanout.md`](experiments/E9-fanout.md) | 容器并排已修，但扇出路由三条线走法不一，仍读不出「平行」；成因是 `Envoy` 偏右 3.07″ |
| E10 | [`experiments/E10-fanout-bus.md`](experiments/E10-fanout-bus.md) | 扇出改走总线，三条线形态一致；**代价是分数降 4 分**（竖线穿标题带，E7），判断为值得 |
| E11 | [`experiments/E11-style-boundary.md`](experiments/E11-style-boundary.md) | 四个「不支持」样式字段不可达的原因是 **IR 不暴露逐节点样式**（有意约束），非 Writer 缺陷 |
| E12 | [`experiments/E12-type-hierarchy.md`](experiments/E12-type-hierarchy.md) | 排版整段被 `if` 包住，默认 spec 下从不运行，节点与边标签同字号；已修为始终运行 |
| E13 | [`experiments/E13-junction-dots.md`](experiments/E13-junction-dots.md) | 总线分叉点标记；注释节点会被 `edge.through-node` 当成主体，靠前缀豁免 |
| E14 | [`experiments/E14-branch-labels.md`](experiments/E14-branch-labels.md) | 共享干线标签改贴自己分支，偏差 2.41/1.67/0.44 → 0.00/0.00/0.00 |
| **AT-2** | [`experiments/agent-trial-02/`](experiments/agent-trial-02/) | 第二轮 Agent 实验**未完成**：只做了探测未画图；倾向先验证而非先设计 |
| **AT-1** | [`experiments/agent-trial-01/`](experiments/agent-trial-01/) | **独立 Agent 自主制图**：96 分、3 轮内完成，但 40% 时间在读源码补指南缺口；发现 2 个引擎缺陷 |

### 3.1 建议的下一批实验（按 Goal §8.5 的要求设计）

| 编号 | 问题 | 输入 | 预期证据 | 优先级 |
| --- | --- | --- | --- | --- |
| ~~E1~~ | ~~通条表达~~ | — | ✅ 已完成 | — |
| ~~E2~~ | ~~多文本区~~ | — | ✅ 已完成 | — |
| ~~E3~~ | ~~密度与字号~~ | — | ✅ 已完成 | — |
| E4 | 嵌套容器在 Visio 中的真实层级 | 两层嵌套容器 | 本机无法验证（L4 Blocked）| — |
| E5 | 图标作为组件主体的可行性 | 一个 FreePath 作图标 + 文字 | 渲染与读回是否保持 | 中 |

**E4 标记为 Blocked**：需 Microsoft Visio，本机不具备（Goal §9.2 要求如实标记）。

---

## 四、证据缺口（明确记录，不掩盖）

| 缺口 | 影响 | 状态 |
| --- | --- | --- |
| **无任何外部真实 VSDX 样本** | Level 校准缺少一手案例；只能依赖文字描述的来源 | Blocked（需外部文件或 Visio）|
| **未覆盖企业架构框架**（TOGAF / ArchiMate） | 分级可能遗漏企业 IT 架构的特定表达 | 待研究 |
| **Web 输出无权威来源** | 该部分的密度阈值是推断 | Hypothesis |
| **7pt 来自单一来源** | 阈值可能偏保守或偏松 | Partially Verified |
| **无 Visio，L4 全部 Blocked** | 所有"原生可编辑"断言无证据 | Blocked |

---

## 五、维护规则

1. 新增外部来源 → 登记到 §一，标注获取日期与可靠度
2. 来源失效或被新结论替代 → 状态改 `Superseded`，**不删除**
3. 实验完成 → 在 `experiments/` 下建文件，并在 §三 登记
4. 本索引与 `docs/` 下的研究文档**双向链接**
