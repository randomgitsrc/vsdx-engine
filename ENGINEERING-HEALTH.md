# ENGINEERING HEALTH — vsdx-engine

**性质：快照，不是当前状态。** 记录时间用 `git log -1 --format=%cd -- ENGINEERING-HEALTH.md`。
当前规模用以下命令实测：

```bash
git ls-files | wc -l                              # 跟踪文件
find src -name '*.cs' | xargs cat | wc -l         # 源码行数
find src -name '*.cs' | xargs wc -l | sort -rn | head -12   # 最大文件
dotnet test VsdxEngine.sln -c Release             # 测试数
```

**仍然成立的判断**：`LayoutAnalyzer.cs` 与 `Program.cs` 是最大的两个文件，
承担职责过宽；`DesignSpec.cs` 是"单点 if 集合"。这三条是结构性观察，不随行数变化。

**调查日期：** 2026-11-18
**基线提交：** `3cad2f3`
**方法：** 命令实测 + 源码统计；无一项依据印象。

---

## 一、总览

| 维度 | 状态 | 说明 |
| --- | --- | --- |
| 构建 | ✅ 健康 | 0 warning，0 error，`--no-incremental` 全量 |
| 测试 | ✅ 健康 | 416 通过 / 0 失败 |
| 基准 | ✅ 健康 | 18 / 18 硬约束通过 |
| 依赖 | ✅ 健康 | 仅 2 个外部包，集中式版本管理 |
| 分层 | ✅ 健康 | 依赖方向无环，`Adapters` 不依赖 `Engine` |
| 代码卫生 | ✅ 健康 | 0 处 TODO/FIXME/HACK |
| **测试分布** | ⚠️ **不均衡** | Protocol/Renderer/Validator 薄弱，CLI 无单测 |
| **文档一致性** | ⚠️ **待核** | 42 份文档，部分数据可能过时 |
| **L4 验证** | ❌ **缺失** | 无 Visio，无法声称原生兼容 |

---

## 二、规模

```
提交           84
跟踪文件      226
源码          13,032 行 / 62 文件
测试           8,874 行 / 38 文件
测试/源码比     0.68
文档          42 份（docs/）+ 5 份管理文档
```

### 最大文件（前 10）

| 行数 | 文件 | 评价 |
| --- | --- | --- |
| 1,143 | `Vsdx.Engine/LayoutAnalyzer.cs` | ⚠️ 偏大，承担布局检测全部职责 |
| 1,025 | `Vsdx.Cli/Program.cs` | ⚠️ 偏大，13 条命令集中在单文件 |
| 740 | `Vsdx.Adapters/OfficeImoIncrementalEditor.cs` | 可接受（增量编辑逻辑复杂） |
| 723 | `Vsdx.Adapters/OfficeImoDocumentReader.cs` | 可接受（读 VSDX 本质复杂） |
| 522 | `Vsdx.Engine/OrthogonalRouter.cs` | 可接受 |
| 494 | `Vsdx.Adapters/OfficeImoDocumentWriter.cs` | 可接受 |
| 477 | `Vsdx.Validator/VsdxPackageValidatorService.cs` | 可接受 |
| 441 | `Vsdx.Protocol/ProtocolRunner.cs` | 可接受 |
| 434 | `Vsdx.Protocol/DesignSpec.cs` | ⚠️ 已知的"单点 if 集合"（见 §5） |
| 413 | `Vsdx.Protocol/DiagramIr.cs` | 可接受 |

**只有前两个构成真实维护风险**，其余在合理范围。

---

## 三、依赖

### 外部包（仅 2 个）

| 包 | 用途 | 风险 |
| --- | --- | --- |
| `OfficeIMO.Visio` 3.6.1 | VSDX 底层读写 | **单点依赖**，MIT，已知 8 项限制 |
| `System.IO.Packaging` | OPC 容器 | 低 |

集中式包管理：`Directory.Packages.props`（7 个 `PackageVersion`）。

**`OfficeIMO.Visio` 是最大结构性风险**：全部 VSDX 读写经它，
其 8 项限制（L1–L8，见 `docs/limitations.md`）已被逐个绕过或补偿。
若其升级或停止维护，本项目需重写适配层。

### 项目依赖图（无环）

```
Model ← Adapters, Renderer, Validator, Engine, Protocol
        Adapters ← Renderer, Validator, Engine
                    Engine ← Protocol, Cli
                             Protocol ← Cli
```

**分层正确**：`Adapters` 不依赖 `Engine`，字号常量下沉到 `Model` 使两侧共享。

---

## 四、测试分布（最大隐患）

| 层 | 引用该层的测试文件数 | 评价 |
| --- | --- | --- |
| `Vsdx.Model` | 34 | ✅ 充分 |
| `Vsdx.Engine` | 33 | ✅ 充分 |
| `Vsdx.Adapters` | 7 | ⚠️ 偏少（含保真、读写、增量） |
| `Vsdx.Protocol` | 3 | ⚠️ **偏少**（IR/DesignSpec/Result 是协议核心） |
| `Vsdx.Renderer` | 1 | ⚠️ **偏少**（正是缺陷 18 藏身处） |
| `Vsdx.Validator` | 1 | ⚠️ 偏少 |
| `Vsdx.Cli` | **0** | ❌ **无单元测试** |

### 与已知缺陷的对应关系

**测试薄弱处正是缺陷藏身处：**

- 缺陷 18（Renderer 不消费 FontFamily）——Renderer 仅 1 个测试文件
- 缺陷 2（Palette 未执行）——Protocol 仅 3 个测试文件
- 缺陷 13（`mode=fixed`）——同上

**这不是巧合。** 这些缺陷都不是"实现错误"，而是"实现了但没接上"，
而单元测试覆盖不足的地方，恰恰是"有没有接上"最容易被漏掉的地方。

### 38 个测试文件主题分布

```
Layout(2) Direction(5) Fidelity(3) RoundTrip(2) Repair(2) Protocol(2)
Create Connector Detector Sizing Swimlane Group TextBlock FreePath ZOrder
MultiPage Edit Read Render Validation Candidate Benchmark Integration
Phase7Regression LocalRelayout ExternalVsdx ExternalFixtures TestData
```

---

## 五、已知架构债

| # | 债 | 位置 | 影响 |
| --- | --- | --- | --- |
| D1 | `DesignSpecApplier` 是单点 if 集合 | `DesignSpec.cs`（434 行） | 4 个互不感知的 `if` 块；字段加进去容易被忽略（缺陷 4/12/21 均源于此） |
| D2 | `LayoutAnalyzer.cs` 1,143 行 | Engine | 检测规则全在一个类，新增规则需改大文件 |
| D3 | CLI 单文件 1,025 行 | `Program.cs` | 13 条命令共用文件，参数解析重复 |
| D4 | 字号表曾有三份 | 已部分解决 | `TypeScaleDefaults` 尚未实际创建（M11 未实施） |
| D5 | `DiagramLayout.Apply` 父组先于子组 | Engine | 导致 C1（父子包含不保证） |

**D4 特别说明：** phase8.2 的 M11 决定"字号常量下沉到 `Model`"，
但**该文件尚不存在**——决定已记录，代码未实施。

---

## 六、文档健康

| 项 | 状态 |
| --- | --- |
| `docs/limitations.md` | ✅ 详尽，区分已验证/未验证，含 L4 验收清单 |
| `docs/architecture.md` | ✅ 存在，含分层与依赖说明 |
| `docs/technical-validation.md` | ✅ 存在，实测记录 |
| `docs/phase8/` 27 份 | ⚠️ 部分为过程文档（5 轮审查、材料包），长期价值递减 |
| 一致性问题 | ⚠️ 见下 |

### 需核实的文档数据

- `docs/test-report.md` 的测试数可能已过时（当前 416）
- `docs/capability-matrix.md` 可能仍标"Palette 已实现"
- `phase8.2-audit.md` 的 25 项缺陷状态需与代码同步

**本次未逐份核对**，属遗留工作。

---

## 七、风险登记

| # | 风险 | 严重度 | 缓解 |
| --- | --- | --- | --- |
| R1 | `OfficeIMO.Visio` 单点依赖 | 高 | 适配层已隔离，但无替代方案 |
| R2 | L4 从未验证 | **高** | 需 Windows + Visio 环境 |
| R3 | Protocol/Renderer 测试薄弱 | 中 | 已在阶段 8.2 审计中暴露 |
| R4 | CLI 无单测 | 中 | 集成测试间接覆盖 |
| R5 | 文档与代码可能漂移 | 中 | 需定期核对 |
| R6 | 真实制图任务从未验证 | **高** | 见 `PROJECT-STATUS.md` §7.3 |

---

## 八、结论

**工程质量本身是健康的**：构建干净、测试全绿、依赖精简、分层正确、
零 TODO 债务。

**但健康不等于有用。** 三个结构性问题是：

1. **测试覆盖与缺陷分布负相关** —— 最容易出问题的地方测试最薄
2. **L4 从未验证** —— 项目定义的核心交付物（原生 Visio 可编辑）未经检验
3. **能力只在基准上验证** —— 从未用真实业务需求检验

前两项是工程债，第三项是**产品债**，且更严重。

---

## 九、局限

- 未做性能测试（大图、多页、复杂嵌套的最坏情况）
- 未做内存/资源泄漏检查
- 未逐份核对 42 份文档的数据准确性
- 未评估 `OfficeIMO.Visio` 升级到新版本的风险
