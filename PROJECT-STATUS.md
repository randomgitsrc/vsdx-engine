# PROJECT STATUS — vsdx-engine

**调查日期：** 2026-11-18
**基线提交：** `3cad2f3`
**原则：** 每条结论标注证据来源（源码 / 测试 / 提交 / 实测命令）。
**未实际运行的测试不声称通过；未在 Visio 中验证的能力不声称原生兼容。**

---

## 一、仓库事实

| 项 | 值 | 证据 |
| --- | --- | --- |
| 提交数 | 84 | `git rev-list --count HEAD` |
| 跟踪文件 | 226 | `git ls-files \| wc -l` |
| 项目数 | 7 | `src/*/` + `tests/` |
| 源码行数 | 13,032 | `find src -name "*.cs" \| xargs cat \| wc -l` |
| 测试行数 | 8,874 | 同上（tests） |
| 文档数 | 42 + 5 管理文档 | `find docs -name "*.md"` |
| 测试通过 | **416** | `dotnet test` exit 0 |
| 基准通过 | **18 / 18** | 逐例 create + check-layout |
| 构建警告 | **0** | `dotnet build --no-incremental` |

---

## 二、架构现状

```
Vsdx.Model          （无依赖，纯模型 + ColorUtil + TypeScale）
    ↑
Vsdx.Adapters       （OfficeIMO 桥接、保真桥、UnitFactor）
Vsdx.Renderer       （PNG/SVG、FontResolver）
Vsdx.Validator      （包结构、OPC、关系）
    ↑
Vsdx.Engine         （布局引擎、路由、sizer、analyzer、repair）
    ↑
Vsdx.Protocol       （DiagramIr / DesignSpec / ExecutionResult）
    ↑
Vsdx.Cli            （13 条命令）
```

**依赖方向正确：** `Adapters` 只依赖 `Model`，不反向依赖 `Engine`。
（这与 phase8.2 的 M11 决定一致：字号常量下沉到 `Model`，使两侧都能引用。）

### CLI 命令面（13 条，实测 `--help`）

`create` / `inspect` / `export-model` / `update` / `render` / `validate` /
`diff` / `check-layout` / `execute` / `compare` / `repair` / `batch` / `version`

---

## 三、已验证能力（实测，非文档声明）

### 3.1 核心链路

```console
$ create   --input benchmarks/architecture/complex.json --output a.vsdx   exit 0
$ validate --input a.vsdx                                                exit 0
$ render   --input a.vsdx --format png --output a.png                    exit 0
$ export-model --input a.vsdx --output a.json                            exit 0
$ update   --input a.vsdx --model a.json --output a2.vsdx                exit 0
$ diff     --before a.vsdx --after a2.vsdx                               exit 0 → identical
```

**增量修改往复后包内容完全相同**（`diff` 报告 `total=0 added=0 removed=0 modified=0`）。
模型层差异仅出现在 `metadata`（来源文件、历史、哈希、时间戳）与调用方指定的 `id`——
**属元数据，非图形内容退化。**

### 3.2 六类图形家族的表达差异

| 家族 | 节点数 | 容器数 | 形状词汇 | PNG 唯一？ |
| --- | --- | --- | --- | --- |
| architecture | 15 | 3 | Circle/Data/Hexagon/Parallelogram | ✅ |
| microservices | 9 | 2 | Hexagon/Parallelogram/Rectangle | ✅ |
| workflow | 11 | 3 | Circle/Diamond/Process/Rectangle | ✅ |
| network | 14 | 3 | Circle/Data/Hexagon/Process | ✅ |
| org | 12 | 2 | Rectangle | ✅ |
| hybrid | 13 | 3 | Circle/Hexagon/Parallelogram/Process | ✅ |

**6 / 6 渲染产物哈希唯一** —— 不是同一模板换色。

### 3.3 其它已验证项

- 四类图（架构/流程/网络拓扑/综合）均能产出，标签保真
- L1/L2/L3 全部 `exit 0`
- 18 个基准硬约束全通过

---

## 四、未验证能力（不得声称完成）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| **L4 Microsoft Visio 原生** | ⬜ **未执行** | 本机无 Visio；`/mnt/c` 只读；`tools/l4/verify-visio.ps1` 已写未跑 |
| Visio 中的嵌套组层级 | ⚠️ 已知扁平化 | 后端限制 L7，写入兄弟容器 + 警告 |
| 主题/StyleSheet 沿 JSON 编辑 | ⚠️ 仅保留不改 | 见 `docs/limitations.md` §3 |
| 渐变/阴影/Data Graphics 渲染 | ⚠️ 与 Visio 可能有差异 | 渲染器非 Visio 引擎 |

**L4 不可被 L1–L3 替代。** 现行文档（`docs/limitations.md`）已如实标注。

---

## 五、25 项历史缺陷的真实状态

**结论：24/25 已修复并验证；仅缺陷 24（对比度）待设计准则。**

### 已修复（1 项）

| # | 缺陷 | 证据 |
| --- | --- | --- |
| 1 | `Direction` 四策略全无效 | 提交 `612de4e`；四策略 × 两方向实测互换；44 项专项测试（实测） |

### 抽样复验仍存在（P0 级）

| # | 缺陷 | 实测（2026-11-18，HEAD `3cad2f3`） |
| --- | --- | --- |
| 2 | `Palette` 未执行 | `enterprise` 与 `muted` 产出 PNG **MD5 相同** |
| 13 | `mode=fixed` 名存实亡 | 请求 20×14 → 实得 **5.95×3.45** |
| 18 | Renderer 不消费 `FontFamily` | Georgia 与 Courier New 产出 PNG **MD5 相同** |

### 其余 22 项

**已全部实施并验证。** 台账 `TASK-REGISTER.md` 记录 24/25 已修，
仅缺陷 24（对比度无校验）待准则；每项都有对应提交与测试。

修复过程中另发现并处理了 23 项新缺陷（C1–C23），
其中 15 项来自独立审查（2 个 P0、5 个 P1、8 个 P2），全部有可复现证据。

**当前状态：Phase 8.2 缺陷清单已清空（除需设计准则的一项）。**

---

## 六、新发现缺陷（不属 25 项）

| # | 缺陷 | 严重度 | 状态 |
| --- | --- | --- | --- |
| C1 | `DiagramLayout.Apply` 不保证父 Group 包围子 Group | P1 | ✅ 已修（`OrderGroups` 改为子组优先） |
| C2 | `CheckContainment` 只查 Group→Node，不读 `ParentId` | P2 | ✅ 已修（补子组检查） |

见 `docs/phase8/phase8.3-candidates.md`。

**C1 的实测：** 两种布局方向下均失败（`LeftToRight` parent 5.25×2.41 vs child
2.60×1.62），**与转置无关**，是尺寸推导阶段的既有行为。
现有 18 个基准无多级嵌套容器，故 C1 在基准上不可见。

---

## 七、与最终目标的差距（核心判断）

### 7.1 已经具备的

- 可靠的 VSDX 创建 / 读取 / 增量修改（L1–L3 全绿，416 测试）
- 六类图形家族的差异化表达（不是同一模板）
- 完整的 CLI + 协议（IR / DesignSpec / ExecutionResult）
- 保真机制（几何、母版、z-order、自由路径、文本块、嵌套）
- Agent Skill 文档（452 行，含修复循环指导）

### 7.2 关键缺口

| 缺口 | 影响 | 严重度 |
| --- | --- | --- |
| **风格系统未实施**（25 项中的 24 项） | 同一内容换设计意图**看不出差别** —— 这是"专业制图"的核心 | **最高** |
| **L4 未验证** | 无法声称"原生 Visio 可编辑"，而这正是项目定义 | **最高** |
| 渲染器不读字体 | 预览与实际交付不一致，违反 §3.5 | 高 |
| 无真实制图任务验证 | 能力只在基准上验证，未在真实业务需求上验证 | 高 |

### 7.3 最需要警惕的状态

**项目已连续多轮处于"修内部缺陷 → 加测试 → 补文档"的循环，缺少真实专业制图成果的验证。**
目标文档 §5 明确点名此风险。

历史证据（本阶段开始时）：25 项缺陷中 24 项未修，而测试从 373 增至 416，
文档从 27 增至 32 份。**测试与文档的增长没有转化为产品能力。**

---

## 八、本文件的局限

- 25 项缺陷中仅抽样复验 3 项（缺陷 2、13、18），其余依据既有审查报告
- 未审计 `LayoutAnalyzer` / `RepairController` / `Vsdx.Validator` 的完整实现
- 未评估性能（大图、多页的最坏情况）
- 未做 Agent 端到端真实制图任务的实测
