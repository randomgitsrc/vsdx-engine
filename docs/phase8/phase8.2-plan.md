# Phase 8.2 实施计划

**依据：** `phase8.2-audit.md`（Stage 1 交付物）
**状态：** Stage 2 交付物，**尚未编码**
**日期：** 2026-11-17

---

## 0. 本计划的修订索引（按独立审查 M1–M12）

> Stage 3 独立审查裁决 **FAIL**，提出 M1–M12。本计划已逐项修订，
> 下表是"哪条修订落在哪一节"的对照，供复核。

| # | 审查要求 | 本计划落点 |
| --- | --- | --- |
| **M1** | spec 路径补 `DiagramSizer.Apply`；修正数据流图；重写缺陷 11 归因 | §1.2 图、§12（整节重写） |
| **M2** | 重定义 `mode=fixed` 与 `shrinkToFit`；删除"功能正确"判语 | §4 缺陷 13、§2.9（audit）、T4b |
| **M3** | `DiagramIr.cs:282` 保留 `lane` | §4 缺陷 14、§2.10（audit）、T0b |
| **M4** | 补 Reader 侧字体回读；T2 改"中" | §5 文件表、T2、§6.2 |
| **M5** | §4 表补齐 12 行→现 14 行；清混合键；统一 Writer 范围 | §4（整节重写）、§4.1 |
| **M6** | 验收换真 IR 输入；断言标签一致 | §6.4（整节重写） |
| **M7** | 机制性闸门而非逐项枚举 | §15（新增） |
| **M8** | 转置补 ports/padding/PageSize/Orientation；明确 waypoint 时序 | §11.1 表、§11.2（重写）、§11.6 |
| **M9** | `Canvas.Unit` 规范化契约，避免双重换算 | §17（新增） |
| **M10** | `Theme.Background` 实现路径与重写顺序 | §18（新增） |
| **M11** | 统一字号表，指定唯一权威 | §16（新增） |
| **M12** | 修正评审材料包 | `phase8.2-review-packet.md`（整份重写） |

**M1–M6 是阻断项**（审查：补齐后可转 PASS WITH CONDITIONS）；
**M7–M12 为条件项**，可在实施中闭环。

---

## 1. 目标架构

### 1.1 一句话

引入**一个**样式解析入口 `StyleResolver`，把四处互不相识的 if 合并成一条优先级明确的管线，
让 Node / Group / Edge / TextBlock / Background 共用同一条路径。

### 1.2 数据流

```
DesignSpec
  ├─ Theme.Palette ─────┐
  ├─ Theme.RoleColors ──┤
  ├─ Theme.Background ──┼──▶ StyleResolver ──▶ ResolvedStyle ──▶ 模型对象（含字号）
  └─ Typography ────────┘         ▲                                   │
                                  │                                   ▼
                          TypeScale（角色字号）          **DiagramSizer.Apply** ← spec 路径当前缺失
                                                                      │  （字号→文字占位→框体尺寸）
                                                                      ▼
                                                         DiagramLayoutEngine.Apply（布局）
                                                                      │
                                                                      ▼
                                                         [Direction 转置]（T0）
                                                                      │
                                                                      ▼
                                                         VSDX Writer → Reader → Renderer
```

> **本节原图缺了 `DiagramSizer.Apply`，是错的。** 独立审查（B1）实测证明：
> 带 spec 时该阶段**根本不在流水线上**（`ProtocolRunner.cs:77` 只在 `spec == null` 分支调用），
> 于是所有节点保留 `DiagramIr.cs:297` 写死的 `Bounds(0,0,2.0,0.9)`。
>
> 同一字号下的对照：带 spec 节点宽 `[2.00, 2.00]`，不带 spec `[1.30, 2.33]`。
> **带 spec 反而更差**，这是本阶段最该修的缺陷。
>
> 我最初把缺陷 11 归因为"字号应用顺序错"，真正的原因是**阶段缺失**——
> "把字号提前到一个不存在的阶段之前"不会生效。

**关键：** Writer 的样式通路**大部分**够用（`SetFontSize`、容器 `TextStyle`、TextBlock 字号都在），
缺的只是上游赋值。

**唯一例外：`Theme.Background` 必须改 Writer/Reader** —— 该字段当前设置后从不落盘，
修复需要真正写入 `PageSheet/PageBackground` 并读回。

### 1.3 为什么不是一个"万能样式系统"

任务 §1.3 禁止"为抽象而抽象"。`StyleResolver` 的立项依据是审计 §3 的四个**实际缺陷**：

1. 新增 palette 只能是第 5 处 if
2. 优先级取决于代码顺序，无法表达
3. Typography 只处理 Node（第一处 if 写的）
4. `typography.min_size` 报告只在 Node 分支里，且判断写反成死代码

**若没有这四条，不应新增此类。**

---

## 2. 样式优先级（本阶段固定契约）

任务 §3.1 建议：`Palette Defaults → Role Overrides → Explicit Object Styles`

### 2.1 精确规则

```
最终样式 = Palette[role]                    ← 第 1 层：调色板按角色给色
          → RoleColors[role]                ← 第 2 层：spec 的角色级覆盖
          → 对象显式 Style 字段（非 null）   ← 第 3 层：调用方显式指定
```

**字号独立于颜色，走 TypeScale：**

```
最终字号 = ObjectStyle.FontSize            ← 显式优先
          ?? TypeScale[textRole]           ← 否则按文本角色
          → max(result, MinReadableSize)   ← 最后夹紧，且夹紧时报告
```

### 2.2 三层各自"是否覆盖"的判据

| 层 | 覆盖条件 |
| --- | --- |
| Palette | 总是提供基础值；角色未定义时用 `default` |
| RoleColors | 仅当 `spec.Theme.RoleColors` 含该 role |
| Object Style | 仅当字段**非 null**（不是空字符串） |

**这一条要写进测试**：显式 `fill=""` 与 `fill=null` 必须区分，
否则"未设置"与"设为空"会被混为一谈。

### 2.3 文本角色到 TypeScale 档位

| 对象 | 文本角色 | TypeScale 档位 |
| --- | --- | --- |
| TextBlock role=title | DiagramTitle | 20 |
| TextBlock role=subtitle | RegionTitle | 12 |
| Group 标题 | RegionTitle | 12 |
| Node 主标题 | NodeTitle | 11 |
| Node 第二行 | NodeCaption | 10 |
| Edge 标签 | EdgeLabel | 9 |
| TextBlock role=caption | EdgeLabel | 9（当前桥接层写 9，`NodeCaption` 是 10 —— 需统一） |
| TextBlock role=legend | NodeCaption | 10 |
| TextBlock role=note | Annotation | 9 |

**不新增 TypeScale 字段**——现有 6 档 + role 映射已覆盖任务 §3.3 的 8 个层级。

---

## 3. Palette 设计

### 3.1 四套命名调色板

每套提供**色彩 token**，不是完整模板：

```csharp
record Palette(
    string Background,
    IReadOnlyDictionary<string, string> RoleFills,   // service/database/gateway/client/queue/...
    // 容器不是单色：同层多个容器需要彼此可区分，否则改完比改前更难读
    IReadOnlyList<string> ContainerFills, IReadOnlyList<string> ContainerStrokes,
    IReadOnlyList<string> ContainerTexts,
    string EdgeStroke, string EdgeLabelText,
    string TextPrimary, string TextSecondary)
```

| Palette | 定位 |
| --- | --- |
| `enterprise` | 沉稳蓝灰，高对比，适合正式文档 |
| `office` | 明亮饱和，接近 Office 默认色板 |
| `technical` | 深色低饱和，适合工程图 |
| `muted` | 低对比柔和，适合印刷 |

**容器色必须是序列，不能是单值**（审查 E-11）：当前 `bandFills` 是 5 个颜色的数组，
按容器序号轮转，因此同层容器彼此不同色。若 Palette 只提供单个
`ContainerFill`，T3 移除数组后**同层容器会全部同色**——
那正好是 §6.4 基线里要修的"三个容器全同一种浅蓝"。

**因此 Palette 的容器色必须是 `IReadOnlyList<string>`，按容器序号轮转**，
保持"同层可区分"这一现有优点。

### 3.2 硬约束（来自任务 §3.2）

1. 不同 palette 必须产生**真实可观察差异** —— 验收用 PNG 目视，不用 JSON 比对
2. **不只是背景色** —— 必须影响角色色、容器色、边框、文本色
3. 未知 palette **必须**进 `UnmetConstraints`
4. **不得写成固定模板** —— palette 只给颜色，不决定布局、形状或字号

### 3.3 未知 palette 的行为

```csharp
if (!Palettes.TryGet(spec.Theme.Palette, out var palette))
{
    unmet.Add(new UnmetConstraint {
        Code = "theme.palette-unknown",
        Reason = $"未知调色板 '{spec.Theme.Palette}'，可用：{known}"
    });
    // 回退到默认调色板继续执行，但结果标记为未满足
}
```

**回退而不中止**，因为样式缺失不应阻断图形交付；但**必须报告**。

---

## 4. 二十四项缺陷的修法（与 audit 一一对应）

> **本节已按 M5 补齐，并按三轮审查与复核增至 24 项。**
> 其中缺陷 18/19 由第三轮审查发现，缺陷 20 由该轮 E-3 指出。 原表只有 9 行；
> 独立审查又发现两项（`mode=fixed` 名存实亡、`swimlane` 协议不可达），
> 均已并入 audit 与本节。
>
> **编号规则：** 与 `phase8.2-audit.md` §10 缺陷清单**逐号对应**，不加不减。

| # | 缺陷（对应 audit §10） | 修法 | 任务 |
| --- | --- | --- | --- |
| 1 | `Direction` 四策略全无效 | 各引擎读 `options.Direction`；Transpose 覆盖全部空间数据 | T0 |
| 2 | `Palette` 未执行 + 未知值不报告 + Schema 不约束 | `StyleResolver` 第 1 层；未知值进 `unmet`；Schema 加 `enum` | T1 |
| 3 | `Theme.Background` 设置后丢失 | Writer 写 `PageSheet/PageBackground`；Reader 还原 | T10 |
| 4 | Typography 仅覆盖 Node | 所有对象经同一解析器；**含 Reader 侧字体回读**（M4） | T2 |
| 5 | 容器色硬编码 | 移除 `bandFills` 数组，改由 palette 提供 | T3 |
| 6 | `Canvas.Unit` 不换算（**注意：Writer 硬传 Inches，标签未写入文件**，不存在"标签与数值不一致"） | 规范化单位契约；**与 `OfficeImoIncrementalEditor.cs:62` 协调，避免双重换算**（M9） | T4 |
| 7 | `containerLayouts` manual 下静默跳过 | 进 `unmetConstraints`，说明 manual 不重排 | T5 |
| 8 | `ObjectMapping` 仅首页 + 仅 Node/Container | 补全局唯一性校验（T7a）；裸 ID 覆盖全页全类型（T7b） | T7 |
| 9 | `typography.min_size` 死代码 | 判断改为 `clamped > size`；全对象走此路径 | T6 |
| 10 | `BaseSize` 抹平字体层级 | 改为缩放基准 `scale = BaseSize / TypeScale.NodeTitle` | T1 |
| 11 | 字号在布局后应用 → 文字溢出 | **根因是阶段缺失，不是顺序**：spec 路径补 `DiagramSizer.Apply`（M1） | T0.5 |
| 12 | `roleColors` 无 `default` 回退 | 支持 `default` 键；未覆盖角色进 `unmet`（按角色去重） | **T2b** |
| 13 | **`mode=fixed` 名存实亡**（P0） | `fixed` 时忽略 `shrinkToFit`；若确需收缩**或放大**，必须进 `unmet` 说明实际尺寸（M2） | **T4b** |
| 14 | **`swimlane` 协议不可达**（P1） | `DiagramIr.cs:282` 保留 `lane`，不改写为 `container`（M3） | **T0b** |
| 15 | **装饰元素独立夹紧致间距塌陷** | 先整组统一偏移，仅整组仍越界时才逐个夹 | **T8a** |
| 16 | **IR `order` 被忽略致流程倒置**（P0） | 转换器消费 `Order`；布局优先用显式 order，其次 `MeanY`，最后声明序 | **T1b** |
| 17 | **样式静默丢失 + `lossless` 谎报**（P0） | `Opacity`/`Shadow`/`Radius`/`LineJoin` 四字段无消费者；要么执行、要么进 `unmet`；`lossless` 不得在丢失时报告 | **T2c** |
| 18 | **Renderer 不消费 `FontFamily`**（P0） | `FontResolver` 从 VSDX 的 `FaceNames` 读字体名；找不到时降级并进 `unmet`（**已由任务书 §3.5 第 4 条裁定，无替代方案**） | **T2d** |
| 19 | **TextBlock `Color`/`Font` 层级错**（P0） | 移入 `Character/Row`，与 Node 一致；验证回读 | **T2e** |
| 20 | **文字裁切不计入硬约束** | `text.overflow-*` 升级为 error（score 归零） | **T6b** |
| 21 | **非法枚举静默回退并谎报** | 统一 `TryParse` 语义：失败记录原值进 `unmet`，再回退 | **T2f** |
| 22 | **`LocalRelayout` 不检查越界碰撞** | 重排后对全部页面节点做碰撞检查；重叠则回退并报告 | **T5b** |
| 23 | **默认路径不写字体，混排无保障**（任务书 §5.2 要求） | 默认字族来自 TypeScale 同源值；混排可分离 latin/cjk；无法解析进 `unmet` | **T1c** |
| 24 | **对比度无任何校验**（任务书 §5.3 要求） | `LayoutAnalyzer` 新增低/高对比度检查；显式 textColor 同样校验 | **T1d** |

**映射关系（已按审查 M5 更正，不再声称"一一对应"）：**

**映射关系（脚本核验，非人工估算）：**

```
§4 缺陷行数        : 24
§4 任务引用数      : 24
§4 不同任务数      : 23   ← 缺陷 2(Palette) 与缺陷 10(BaseSize) 共用 T1
§9 任务行数        : 25   ← 23 + T8 + T9（两个跨任务门禁）
```

- **24 项缺陷 → 24 个任务引用**：每行都有明确任务
- **但有 1 处合并**：缺陷 2 与 10 共用 `T1`（`StyleResolver`），故不同任务 23 个
- **§9 多出 2 个**：`T8`（三套产物 + 目视）与 `T9`（全量回归）不对应单一缺陷，
  是**跨任务的验收门禁**

> **三轮反复纠正的记录：** 我先写"17⇄17⇄17 一一对应"（假），
> 再写"17:17:18，只解释 T9"（漏 T8），现在才用脚本数清。
> **三个数字从不相等，之前的整齐是猜的。**
> 审查还指出我上一版佐证写"§4 refs=17"，实际当时是 16——连佐证本身也是估的。

--- | --- |
| T7a | 补全局唯一性校验 `ir.id-duplicate-global`（当前缺失，见 §14.3） |
| T7b | 裸 ID 覆盖全部页面与 Node/Container/Edge/TextBlock/Path |

**若 T7a 无法成为协议保证**，退回结构化方案：
`ObjectMappings` 列表（`pageId`/`objectId`/`objectKind`/`modelId`），
保留现有 `ObjectMapping` 作为兼容投影。

**不采用动态混合键。** 理由（负责人原话）：避免"增加页面后既有键格式改变"。

---

## 5. 文件级改动计划

| 文件 | 改动 | 风险 |
| --- | --- | --- |
| `src/Vsdx.Protocol/StyleResolver.cs` | **新增** —— 解析器 + Palette 定义 | 低 |
| `src/Vsdx.Protocol/DesignSpec.cs` | `Apply()` 改为调用解析器；移除硬编码容器色；修 unit 换算；修 min_size 判断；containerLayouts 报告 | **中**（核心改动） |
| `src/Vsdx.Protocol/ExecutionResult.cs` | `BuildMapping` 覆盖全对象 + 多页 | 低 |
| `src/Vsdx.Engine/LayeredLayoutEngine.cs` 等四个引擎 | 读 `options.Direction`，实现转置 | **中**（触碰已验证布局） |
| `src/Vsdx.Adapters/OfficeImoDocumentWriter.cs` | **写页面背景**（`PageSheet`，位于 `pages.xml`） | 中（触碰保真层） |
| `src/Vsdx.Adapters/VisioTextBlockBridge.cs` | **修字体单元格**：当前写 shape 级裸 `<Cell N="Font">`，该字体未注册 FaceNames，Reader 读不到（M4） | 中 |
| `src/Vsdx.Protocol/DiagramIr.cs` | **需改动**：`lane` 保留（T0b）、消费 `IrContainer.Order`（T1b）。**`IrEdge.Waypoints` 不需要**（D-8 已裁定方案 A） | 中 |
| `src/Vsdx.Protocol/ProtocolRunner.cs` | **需改动**：spec 路径补 `DiagramSizer.Apply`（T0.5，M1 核心） | 中 |
| `src/Vsdx.Engine/LayoutStrategies.cs` | **需改动**：`Transpose(Page)` 公共化 + 各引擎消费 `Direction`（T0）；移除逐个 clamp 改整组偏移（T8a） | 中 |
| `src/Vsdx.Engine/LayoutAnalyzer.cs` | **需改动**：`text.overflow-*` 升级为 error（T6b） | 小 |
| `src/Vsdx.Engine/TypeScaleDefaults.cs` | **新增**（放 `Vsdx.Model`）：字号档位唯一权威（M11） | 小 |
| `src/Vsdx.Adapters/VisioBackgroundBridge.cs` | **新增**：写 `pages.xml` 的 `PageSheet`（T10） | 小 |
| `src/Vsdx.Protocol/DiagramIrConverter.cs` | **需改动**：消费 `IrContainer.Order`（T1b） | 小 |
| `src/Vsdx.Renderer/FontResolver.cs` | **需改动**：读 VSDX 的 `FontFamily` 而非仅推断 CJK（T2d，缺陷 18） | **中** |
| `src/Vsdx.Protocol/JsonGate.cs` | **新增**：闸门 0/1/2/3 的统一实现（Schema 校验 + 未知成员 + 枚举白名单） | 中 |
| `src/Vsdx.Adapters/OfficeImoDocumentReader.cs` | **读回页面背景** + **读回 Edge 标签字体 / TextBlock 字体**（M4） | **中** |
| `schemas/phase7/design-spec.schema.json` | 枚举 4 个 palette 名 | 低 |
| `skill/SKILL.md` | 新增 palette 与样式优先级说明 | 低 |
| `docs/phase8/capability-matrix.md` | 更新样式相关行 | 低 |

**不触碰：** `FidelityGuard` / 协议版本号。

**`Visio*Bridge` 的范围已修正（审查 M4 + 任务书 §3.5）：**
原文写"不触碰 Visio*Bridge"，但 TextBlock 字体必须修，理由是任务书硬要求，非偏好：

> §3.5：「**Reader 能正确读回已支持的样式**」「无法表达或渲染的样式**必须报告**」
> §4.4：「不为了视觉系统**重写** Writer / Reader」——补一处注册不属于重写

**结论：修 `VisioTextBlockBridge`（方案 A），而非声明"不回读"**——
后者会让每次遇到自定义字体都要进 `unmet`，比修好更啰嗦。

原问题：

```csharp
// VisioTextBlockBridge.cs:141 当前写的是 shape 级裸 Cell
AddCell(shape, "Font", style.Font);   // ← 该字体未注册进 FaceNames
```

实测：`font=Verdana` 时 Verdana 在 `document.xml` 的 FaceNames 中 **0 命中**，
Reader 无法通过 `shape.TextStyle.FontFamily` 取回。

**因此 Bridge 中至少 `VisioTextBlockBridge` 必须改，另加背景桥。**
`VisioNestingBridge` / `VisioFreePathBridge` / `VisioZOrderBridge` / `VisioMasterBridge`
确实不改。

**需触碰（初稿遗漏）：** Writer/Reader 各一处（背景色）、四个布局引擎（Direction）。

---

## 6. 测试计划

### 6.1 优先级（任务 §5.2 要求"每项独立测试"）

| 测试 | 断言 |
| --- | --- |
| `PaletteProvidesDefaults` | 无 roleColors 时节点取 palette 色 |
| `RoleColorsOverridePalette` | roleColors 覆盖 palette 同角色 |
| `ObjectStyleOverridesEverything` | 显式 fill 覆盖 roleColors |
| `NullVsEmptyStringDistinguished` | `fill=null` 走继承，`fill=""` 视为显式空 |
| `ExplicitFontSizeBeatsTypeScale` | 显式字号优先 |
| `MinReadableClampsAndReports` | 低于下限被提升**且**进 unmet（修死代码） |

### 6.2 覆盖范围（逐项对应任务书 §5.2 的 10 项要求）

> 任务书 §5.2 列出 10 项必须覆盖的样式解析场景。此前本计划只覆盖了 9 项，
> **遗漏"中英文混排"**（缺陷 23）。下表逐项对应。

| §5.2 要求 | 本计划对应 | 任务 |
| --- | --- | --- |
| Palette 默认样式 | ✅ `StyleResolver` 第 1 层 | T1 |
| RoleColors 覆盖 | ✅ 第 2 层 | T1 / T2b |
| 对象显式样式覆盖 | ✅ 第 3 层 | T1 |
| 文本角色字号 | ✅ TypeScale + BaseSize 缩放 | T1 |
| 容器标题 | ✅ `RegionTitle` 档 + 移除硬编码色 | T3 |
| Edge 标签 | ✅ `EdgeLabel` 档 + Writer | T2 |
| TextBlock | ✅ 含字色/字体层级修正 | T2e |
| 背景色 | ✅ `VisioBackgroundBridge` | T10 |
| 多页面 | ✅ 逐页应用 | T7 |
| **中英文混排** | ⚠️ **此前遗漏，现补** | **T1c** |

**10 项全部有对应任务。** 此前"中英文混排"完全未提及——
这不是表述问题：默认路径根本不写字体（缺陷 23 实测），混排从未被处理过。

**§5.3 的视觉验收项同样逐条对应：**

| §5.3 要求 | 对应 | 任务 |
| --- | --- | --- |
| 产生明确的视觉差异 | 三套 spec 对比 PNG | T8 |
| 不只是页面背景颜色变化 | palette 影响角色/容器/边框/文字 | T1 |
| 字号层级清晰 | TypeScale + BaseSize 缩放 | T1 |
| 不同对象角色可辨认 | roleColors + 形状语义 | T1/T2 |
| 文字可读 | `MinReadableSize` 夹紧 + 报告 | T6 |
| **颜色与文字对比合理** | ⚠️ **此前遗漏，现补** | **T1d** |
| 不发生文本裁切 | `text.overflow-*` 升级为硬约束 | T6b |
| 不破坏原有硬约束 | 18 基准回归 | T9 |

**8 项全部有对应任务。**

| 测试 | 断言 |
| --- | --- |
| `TypographyReachesAllObjectKinds` | Node/Group/Edge/TextBlock 四种都有 font+size |
| `EdgeLabelUsesTypeScale` | Edge 标签字号来自 EdgeLabel 档 |
| `ContainerTitleUsesRegionTitle` | 容器标题来自 RegionTitle 档 |
| `ContainerColorsComeFromPalette` | 换 palette 后容器色改变 |
| `BackgroundFromPalette` | palette 决定背景 |

### 6.3 协议契约

| 测试 | 断言 |
| --- | --- |
| `UnknownPaletteIsReported` | 进 `unmetConstraints`，`isHard=false` |
| `ManualLayoutReportsContainerLayouts` | manual 下进 unmet |
| `ObjectMappingCoversAllKinds` | Edge/TextBlock/Path 都在 |
| `ObjectMappingCoversAllPages` | 两页都有条目 |
| `UnitConversionIsCorrect` | cm→英寸数值正确，标签与数值一致 |

### 6.4 真实产物（M6：必须用真正的 IR 输入）

> **本节已按 M6 重写。** 原版说用 `benchmarks/architecture/complex.json` 等作三套样本，
> **那是错的**：那些文件是 `Diagram` 模型格式（顶层有 `modelVersion`，无 `irVersion`），
> 不是 `DiagramIr`。

**实测后果：** 把模型文件当 IR 传给 `execute`，IR 校验层解析出空节点集，
15 个节点的中文标签全部丢失（`'Web 门户\nReact SPA'` → `'web'`）。

**验收会对着 15 个 id 方块判"通过"。**

#### 正确做法

**三个 IR 文件已创建并实测**（Stage 3 复审前补齐，见下）：

| 产物 | 输入 | 实测结果 |
| --- | --- | --- |
| 分层架构图 | `examples/phase8/arch.ir.json` | 10 节点 / 9 边 / 4 文本块；`succeeded`，hard=True，score 92 |
| 流程图 | `examples/phase8/flow.ir.json` | 11 节点 / 12 边 / 2 文本块；`succeeded`，hard=True，score 100 |
| 综合图 | `examples/phase8/overview.ir.json` | 10 节点 / 9 边 / 4 文本块 / 1 路径；`succeeded`，hard=True，score 92 |

**标签保真已验证**（对照旧输入格式的失败）：

```
node: 'Web 门户\nReact SPA'  ✅   （旧 benchmarks 输入会退化成 'web'）
edge: 'HTTPS'                 ✅
text: '企业分析平台'            ✅
```

每份 IR 分别配 **3 套 DesignSpec**（enterprise / technical / muted），
产出 9 组 VSDX + PNG。

#### 实测暴露的现状缺陷（作为「修前基线」记录）

用这三个 IR 跑当前代码，PNG 目视可见：

| 观察 | 对应缺陷 |
| --- | --- |
| 三个容器**同一种浅蓝**，看不出层次 | 缺陷 5（容器色硬编码） |
| 节点无角色配色，service/database/queue 视觉无差别 | 缺陷 2（Typography/roleColors 仅部分） |
| **标题与副标题重叠** | 缺陷 15（装饰元素独立夹紧） |
| 分隔线穿过副标题文字 | 缺陷 15（同上） |
| **流程图三个容器上下颠倒**（履约在最上、受理在最下） | 缺陷 16（IR `order` 被忽略） |
| 连线大量交叉 | **不属本阶段**：连线交叉是路由/布局问题（`OrthogonalRouter` + 布局策略），样式系统不改变连线几何。缺陷 16 是 **P0** 因为它是**语义倒置**（可通过数据修复），不是视觉偏好 |

**这五条是 Phase 8.2 要改善的视觉基线。** 修复后再用同样三个 IR 产出对比 PNG。

#### 验收断言（可证伪）

| # | 断言 |
| --- | --- |
| 1 | **PNG 中标签文本与 IR 源一致**（如 `Web 门户` 而非 `web`），抽查比例 > 90% |
| 2 | 三套 spec 产出的 PNG 在**非背景区域**有可见差异 |
| 3 | 字号层级在 PNG 中可辨（标题 > 区域 > 节点 > 标签） |
| 4 | `text.overflow-*` 计数为零 |
| 5 | 硬约束全部通过 |

第 1 条是 M6 新增的**输入正确性闸门**——没有它，验收可能在错误输入上"通过"。

### 6.5 回归

- 373 项原测试全过
- 18 基准硬约束全过
- 0 新增 warning

---

## 7. 兼容性

| 项 | 处理 |
| --- | --- |
| 未声明 `theme` 的旧 spec | 行为**不变**（解析器在无 palette 时返回当前默认值） |
| 已声明 `roleColors` 的旧 spec | 效果不变（新路径给出相同结果），需回归验证 |
| `Canvas.Unit` | **行为改变**：以前写错标签，现在换算正确。属修 bug，需在报告说明 |
| `ObjectMapping` | 单页保持裸 id，兼容 |
| 协议版本 | 不变（1.0）——只加执行，不加字段 |

**两处破坏性变更：`Canvas.Unit` 与 `Direction`。**

`Direction` 之前完全不生效，修好后**旧 spec 若声明了 `topToBottom`，产出会真的转置**——
这是修 bug，但会改变既有产物外观，必须明说并在报告中列出。

**`Canvas.Unit`**： 现状是"标签写 cm、数值仍按英寸"，
修正后数值会变。这是修 bug，但必须明说。

---

## 8. 风险

| # | 风险 | 缓解 |
| --- | --- | --- |
| R1 | 统一解析改变既有产物外观 | 逐项对比改动前后 PNG；若变化属"修复"则记录，若属"回归"则回退 |
| R2 | Palette 被做成固定模板 | palette 结构里**没有**布局、形状、字号字段——只有颜色 |
| R3 | 三层优先级实现成四层 | 测试固定每层，尤其 `null` vs `""` |
| R4 | 多页 ObjectMapping 破坏兼容 | 单页保持裸 id |
| R5 | 眼神验收流于形式 | 必须提交 PNG 且逐张写出观察，不以 `check-layout` 分数替代 |

---

## 9. 子任务与验收标准

| # | 子任务 | 验收 |
| --- | --- | --- |
| T0 | 修 `Direction` | 四策略 × 两方向，`xspread`/`yspread` 必须互换；转置覆盖 §11.1 全部空间数据 |
| T0.5 | **spec 路径补 `DiagramSizer.Apply`** | 同字号下带 spec 与不带 spec 框体一致；`baseSize` 9→24 框体随之增大；`text.overflow-*` 为 0 |
| T0b | `DiagramIr` 保留 `lane` | 声明 lane 的 IR → `applied` 含 `layout=Swimlane` **且几何与 layered 不同** |
| T4b | `mode=fixed` 语义（**双向**） | 请求 20×14（内容少，会缩）→ 实得 20×14 或 `unmet`；请求 8×6（内容多，会涨）→ 实得 8×6 或 `unmet` |
| T1 | `StyleResolver` + 4 套 palette | 单元测试证明三层优先级；palette 只有颜色字段 |
| T1b | 容器顺序：消费 IR `order`（**T8 的前置**） | `flow.ir.json` 中 `intake` 的 Y 最大（最上）；缺失 order 时退回 `MeanY`，再缺失用声明序 |
| T2 | 接入 Node / Group / Edge / TextBlock | 四种对象都拿到 font+size+color；**且能回读**（M4） |
| T2b | `roleColors` default 回退 | 未覆盖角色进 `unmet`，按角色去重（缺陷 12） |
| T2c | 样式可渲染性报告（任务书 §3.5） | **四个死字段**（`opacity`/`shadow`/`radius`/`lineJoin`）要么执行、要么进 `unmet`；**有丢失时 `compatibility` 不得为 `lossless`** |
| T2d | Renderer 消费 FontFamily | 换字体 → PNG **必须不同**（当前 MD5 相同）；找不到字体时降级并进 `unmet` |

**T2d 的实现路径已查明：**

```csharp
// OfficeImoVsdxRenderer.cs:37 —— 关键行
var fontFile = resolver.Resolve(requiresCjk, request.FontFile);
//                                        ^^^^^^^^^^^^^^^^ 只来自请求，从不来自 VSDX
```

`FontResolver.Resolve(bool requiresCjk, string? preferredFontFile)` **已经支持**
指定字体文件；缺的是**上游把 VSDX 里的 FontFamily 传进来**。

因此 T2d 分三步：

| 步 | 改动 |
| --- | --- |
| 1 | 打开 VSDX 后收集实际用到的 `FaceName`（`document.xml` 已有） |
| 2 | 把 `FaceName` 映射到系统字体文件（新 `FontResolver.ResolveByFamilyName`） |
| 3 | 传进 `Resolve` 的 `preferredFontFile`；找不到时**降级并进 `unmet`** |

**这条链是通的**，不存在"渲染器架构不支持"的障碍——纯属参数没接上。
这与缺陷 18 的性质一致（同缺陷 15/19：管线都在，最后一跳断了）。
| T2e | TextBlock 字色/字体层级 | `color:#FF0000` 与 `font:Verdana` 回读非 null；三色三字体产出 PNG 互不相同 |

**T2e 的修法只有两行移动**（已核实代码位置）：

```csharp
// VisioTextBlockBridge.BuildShape 现状
if (style?.TextColor is not null) AddCell(shape, "Color", style.TextColor);  // ❌ shape 级
if (style?.Font      is not null) AddCell(shape, "Font",  style.Font);       // ❌ shape 级

var character = new XElement(VisioNs + "Section", new XAttribute("N", "Character"));
character.Add(new XElement(VisioNs + "Row", new XAttribute("IX", "0"), ...));  // ← 段已建好
```

**把前两行改为加入 `character` 的 `Row` 内部即可**，与 Node 的写法一致
（Node 的 `<Row><Cell N="Font" .../></Row>` 已验证可回读）。

**不是架构问题，是位置问题**——这也解释了为什么三轮审查才找到：
代码"看起来"在设置字体，字段名也对，只有对照 Node 的 XML 才看出层级不同。
| T6b | 文字裁切计入硬约束 | `baseSize=24` 时 `hardConstraintsPassed=false`、score=0 |
| T1d | 对比度校验（§5.3） | 浅底浅字（1.05:1）→ `text.low-contrast`；深底近黑（1.1:1）→ 同样报告；显式 textColor 不得绕过 |
| T1c | 默认字体 + 中英混排（§5.2） | 不传 spec 时 `FaceNames` 非空；中文/混排/英文三类节点渲染无方框、无字重跳变 |
| T5b | `LocalRelayout` 越界碰撞检查 | `containerLayouts` 施加后 `node.overlap` 必须为 0，或该次重排被回退并进 `unmet` |
| T2f | 枚举 `TryParse` + 未识别值报告 | `containerLayouts:"kaboom"` → `unmet` 含 `input.unknown-enum`；`applied` 不得声称执行了 `kaboom` |
| T3 | 移除容器硬编码色 | 换 palette → 容器色变（PNG 可见） |
| T4 | 修 `Canvas.Unit`（**两处**：Writer + DesignSpecApplier） | `create` 与 `execute` 的 cm/mm 换算一致；标签与数值一致；`update` 不再二次换算 |
| T5 | 修 `containerLayouts` 报告 | manual 下进 unmet |
| T6 | 修 `min_size` 死代码 | 低于下限时进 unmet |
| T7 | `ObjectMapping` 全对象 + 多页 | 两页 × 四类对象全覆盖 |
| T8 | 三套产物 + PNG + 目视（**依赖 T1b**） | 提交 VSDX/PNG/spec，附观察记录；容器顺序必须与 IR 声明一致 |
| T8a | 装饰元素相对间距 | title/subtitle 间距 0.6" → 页面收缩后仍 ≥ 0.5" |
| T9 | 全量回归 | 373 测试 + 18 基准 + 0 warning |
| T10 | `Theme.Background` 落盘 + 回读 | 写 `#FAFAFA` → 读回同值 |

**每个 T 独立提交、独立测试，不合并未验证的大改动**（任务 §7 Stage 5 要求）。

---

## 10. 明确不做（任务 §6）

外部 Stencil、MasterCatalog/AssetResolver、图片/SVG 嵌入、复合图形、
多区域协调、通道预留、完整 check-fidelity、六族终验、Word OLE、GUI。

**L4 不作为阻塞条件**（任务 §5.7）；L1/L2/L3 必须持续通过。

---

## 11. T0 的实现细节：Transpose 必须覆盖全部空间数据

### 11.1 现有实现只覆盖一半

`CandidateComparison.cs:141` 已有 `Transpose(Diagram)`，处理了：

```csharp
node.Bounds  X/Y 与 W/H 互换        ✅
group.Bounds X/Y 与 W/H 互换        ✅
edge.Waypoints = null（清空）       ✅
page.Size W/H 互换                  ✅
```

**但它遗漏了以下空间数据，直接复用会产生错误几何：**

| 遗漏项 | 位置 | 后果 |
| --- | --- | --- |
| `Page.TextBlocks` | `Page.TextBlocks[].Bounds` | 标题位置不转，跑到页面外 |
| `Page.Paths` | `Page.Paths[].Bounds` | 分隔线位置与宽高不转 |
| **`FreePath.Geometry` 内部坐标** | `PathRow.Cells["X"/"Y"]` | **路径形状本身不转**——框转了、线没转 |
| `Node.Angle` / `FreePath.Angle` | 旋转角 | 角度未镜像，图形朝向错误 |
| `Edge.FromPoint` / `ToPoint` | 自由端点 | 端点留在原坐标 |
| `Edge.Waypoints` | 折点列表 | 当前是**清空**而非转置。**按 D1 裁定，`manual` 下不转置，故此项目前无需实现**（见 §11.2） |
| **`Edge.FromPort` / `ToPort`** | 端口语义 | `left` ↔ `bottom` 的语义随轴变，必须重映射 |
| **`ShapeStyle.PaddingX` / `HeadingHeight` / `PaddingBottom`** | 容器内边距 | X/Y 轴方向不同，转置后应对调 |
| **`Diagram.PageSize`** | 顶层页面尺寸 | 与 `Page.Size` 是两处，都要换 |
| **`Diagram.Orientation`** | landscape / portrait | 转置后应对调，否则语义与几何矛盾 |

### 11.2 【M8】Waypoints 的真实时序问题

> **本节已按独立审查 M8 重写。** 原版只说"应当转置而非清空"，
> 但审查指出更硬的时序障碍：转置发生在布局**之后**，
> 而 waypoints 在布局**期间**就被清空了，**转置时已无输入可转**。

```csharp
// LayoutStrategies.cs:138-142 —— 布局阶段，无条件清空
foreach (var edge in page.Edges)
    edge.Waypoints = null;
```

**实测：** 模型给 3 个 waypoint，走 `execute` 后读回 `waypoints: None`。

**因此：**

1. 转置逻辑里"转置 waypoints"的代码**永远拿不到数据**
2. §11.6 原验收断言 4（"Waypoints 逐点转置"）**不可达**，
   除非手工构造模型绕过布局——那是**假绿**

**修正后的处理：**

| 场景 | waypoints 行为 | 理由 |
**`Direction + manual` 已由负责人 D1 裁定：方案 A。**

> **本节已按审查 E-9 修正。** 原文此处保留了一张旧表格，写着
> 「`Layout == manual` → **保留并转置**」，与下面的裁决直接对立。
> 该表已删除——它是方案 B 的残留，三轮审查中被点了两次。
>
> **waypoints 在两种模式下的实际行为（裁决后）：**

| 模式 | 转置 | waypoints |
| --- | --- | --- |
| `layout != manual` | 生效 | 布局期已清空，无需处理 |
| `layout == manual` | **不生效**（进 `unmet`） | 保持作者原值，不转置 |

> 负责人原文：「**Manual 模式必须保留用户坐标，不得擅自转置。**」

| 已裁定的行为 | 说明 |
| --- | --- |
| `layout=manual` 时 `direction` **不生效** | manual 的契约是"作者坐标即最终结果" |
| **必须进 `unmetConstraints`** | 报告 `direction` 未被应用，不得静默忽略 |

因此：

- **不需要**给 `IrEdge` 加 `Waypoints` 字段（方案 B 的需求已作废）
- `DesignSpec.cs:163` 的 manual 分支保持不转置，只增加一条 `unmet` 记录
- §11.6 断言 4 与 7 统一为方案 A 的表述

**我上一轮把此项列为"待裁决"是错的**——D1 里已经写明，是我没有把它登记为正式裁决。
见 `phase8.2-owner-rulings.md` 的 D-8 条目。

### 11.3 几何内部坐标必须镜像

`FreePath.Geometry` 的 `PathRow.Cells` 存的是**局部坐标**（相对 box）。
当 box 的 W/H 互换时，局部 X 坐标范围也变了，必须相应交换 X/Y 单元格：

```
前面: MoveTo(0,0) → LineTo(8,0)        // 水平线
转置: MoveTo(0,0) → LineTo(0,8)        // 竖直线
```

**只换 Bounds 不换 Cells，会得到一条"框转竖了、线还是横的"错误图形。**

### 11.4 Manual 模式不得转置

任务要求"Manual 模式必须保留用户坐标"。

**已裁定（D1）：`manual` 下 `Direction` 不生效，且必须进 `unmetConstraints`。**
见 §11.2 与 `phase8.2-owner-rulings.md`。

### 11.5 提取位置

移到 `DiagramLayout` 作为 `Transpose(Page)`，`CandidateComparison` 改为调用，
避免两份实现漂移。签名：

```csharp
/// <summary>Mirrors every spatial value on the page across the X=Y diagonal.</summary>
public static void Transpose(Page page);
```

### 11.6 验收

| # | 断言 |
| --- | --- |
| 1 | 四策略 × 两方向：`xspread`/`yspread` 互换 |
| 2 | TextBlock 位置跟随转置 |
| 3 | Path Bounds 与**内部 Geometry 坐标**同时转置 |
| 4 | `layout=manual` + `direction=topToBottom` → 坐标**不变**，且 `unmetConstraints` 含 `direction.manual-ignored` |
| 5 | 自由端点 `FromPoint`/`ToPoint` 转置 |
| 6 | `Angle` 镜像 |
| 6b | `Edge.FromPort`/`ToPort` 语义重映射（left↔bottom） |
| 6c | `Diagram.Orientation` 与 `Diagram.PageSize` 同步转置 |
| 7 | Manual 模式不转置，且 `unmetConstraints` 有记录（与断言 4 同一条） |
| 8 | 转置两次回到原状态（幂等性反向验证） |

---

## 12. 【M1 重写】spec 路径缺失尺寸推导阶段（T0.5）

> **本节已按独立审查 M1 重写。** 原版把问题归因为"字号应用顺序错"，
> 结论是"把字号提前到 `DiagramSizer.Apply` 之前"。
> **但在 spec 路径上该阶段根本不存在**，提前到一个不存在的步骤不会生效。

### 12.1 真正的根因：阶段缺失，不是顺序

```csharp
// ProtocolRunner.cs:66-88
if (spec is not null)
{
    DesignSpecApplier.Apply(diagram, spec);   // ← 只调 Recenter/FitPage（页面级）
}                                              //   没有 DiagramSizer.Apply（框体级）
else
{
    DiagramSizer.Apply(diagram);               // ← 只在这里
    DiagramLayoutEngine.Apply(...);
    ...
}
```

`DiagramSizer.Apply` 的全部调用点（`grep -rn` 实测）：

| 位置 | 条件 |
| --- | --- |
| `ProtocolRunner.cs:77` | **仅 `spec == null` 分支** |
| `VsdxEngine.cs:58` | `FitToText`，默认 false |
| `CandidateComparison.cs:103` | `recipe.FitToText` |
| `Program.cs:550` | repair 命令 |

**spec 路径上一次都不调用。**

### 12.2 实测后果

同一 IR、**同一字号**（`baseSize=11`），唯一变量是 spec 是否存在：

```
带 spec（baseSize=11）: 节点宽 [2.00, 2.00]     ← DiagramIr.cs:297 写死的 Bounds(0,0,2.0,0.9)
不带 spec             : 节点宽 [1.30, 2.33]     ← 按文字推导
```

**带 spec 比不带 spec 更差**——因为恰好跳过了尺寸推导。

进一步：`baseSize` 9→24 时框体**纹丝不动**（仍 2.00），
`check-layout` 报 `text.overflow-horizontal: 最长行约 5.09"，实际 2.00"`，
而 `execute` 返回 `status=succeeded`。

### 12.3 修法

**在 spec 路径补上该阶段**，位置在样式解析之后、布局之前：

```csharp
if (spec is not null)
{
    var application = DesignSpecApplier.ApplyStyleOnly(diagram, spec);  // 仅样式+字号
    DiagramSizer.Apply(diagram);                                        // ← 新增：按字号推导框体
    DiagramLayoutEngine.Apply(diagram, options, strategy);              // 再布局
}
```

需要把 `DesignSpecApplier.Apply()` 拆成两段：

| 阶段 | 职责 | 时机 |
| --- | --- | --- |
| `ApplyStyleOnly` | palette / roleColors / typography / 字号 → 模型 | **尺寸推导之前** |
| `ApplyLayoutAndCanvas` | 布局、Recenter、FitPage、约束求值 | 尺寸推导之后 |

**这不是"调整顺序"，是"把缺的环节装回去"。**

### 12.4 验收（独立项，可证伪）

| # | 断言 |
| --- | --- |
| 1 | **同一字号、同一画布尺寸下**，带 spec 与不带 spec 的框体宽高必须一致。**必须同时固定画布**——审查 E-14 复算：`TypeScale.ForCanvas` 按页面尺寸推导，11×8.5 得 NodeTitle 8.919、20×14 得 14.000，画布不同则该断言必然失败 |
| 2 | `baseSize` 9→24，框体尺寸必须随之增大 |
| 3 | 增大后 `text.overflow-*` 计数为零 |
| 4 | spec 路径的 `stagesCompleted` 必须含尺寸推导阶段 |
| 5 | 修后带 spec 与不带 spec 的节点宽差 < 0.05" |

第 1 条与第 5 条是审查新增的**对照式断言**——只有它们能证明"阶段真的装回去了"，
单纯断言"框变大了"无法区分"装回阶段"与"把 2.0 改成 5.0"。

---

## 13. BaseSize 语义修正（依负责人 D3）

### 13.1 当前错误

源码注释写明 `Base node-title size in points`，但实现是：

```csharp
node.Style.FontSize = Math.Max(baseSize, MinReadableSize);   // 所有节点同值
```

**注释说"node-title 基准"，实现却抹平了所有层级。**

### 13.2 正确语义

`BaseSize` = **`NodeTitle` 的目标绝对字号**，作为整体缩放基准：

```csharp
double scale = baseSize / TypeScale.NodeTitle;      // 例如 14 / 11 ≈ 1.27

// 应用到全部档位，保留层级关系
double For(double tier) => tier * scale;
```

| 档位 | 默认 | baseSize=14 时（scale=1.2727） |
| --- | --- | --- |
| DiagramTitle | 20 | 25.45 |
| RegionTitle | 12 | 15.27 |
| NodeTitle | 11 | **14.00**（基准点，恒等于 baseSize） |
| NodeCaption | 10 | 12.73 |
| EdgeLabel | 9 | 11.45 |
| Annotation | 9 | 11.45 |

数值由 `python3` 逐档核算，非手算。**校验点：`NodeTitle * scale` 必须精确等于 `baseSize`。**

**层级比例保持不变，整体缩放。** 这正是任务 §3.3 要求的"不能只给一个全局 FontSize"。

### 13.3 显式字号与下限

```csharp
最终字号 = ObjectStyle.FontSize                        // 显式优先
        ?? For(TypeScale[textRole])                    // 否则按角色
        → max(result, MinReadableSize)                 // 最后夹紧
        → 若被夹紧，进 unmetConstraints（修死代码）
```

### 13.4 验收

| # | 断言 |
| --- | --- |
| 1 | `baseSize=14` 时 NodeTitle 恰为 14，且 title > region > node > label |
| 2 | 缩放后层级**比例**与原 TypeScale 一致 |
| 3 | 显式 `fontSize` 覆盖缩放结果 |
| 4 | 低于 `MinReadableSize` 时被提升**且**进 unmet |
| 5 | 字号在布局前确定（见 §12） |

---

## 14. ObjectMapping 契约（依负责人 D2）

### 14.1 决策

**保持裸 ID 格式**，前提是 IR 对象 ID 在**整个 IR 范围内全局唯一**（跨页面、跨类型）。

### 14.2 必须做的事

| # | 事项 |
| --- | --- |
| 1 | ~~核查~~ **已核查完毕（结论见 §14.3）：当前仅页内唯一，跨页不检查** |
| 2 | **补全局唯一性校验**（`ir.id-duplicate-global`）—— T7a 的实施项 |
| 3 | 覆盖全部页面与 Node / Container / Edge / TextBlock / Path |
| 4 | 加测试断言：两页各有同名 id 时校验拒绝 |

**若全局唯一成为协议保证**，则裸 ID 可安全用作键，无需改格式，
也避免"加页面后既有键格式改变"。

**若无法保证**，则退回结构化方案：`ObjectMappings` 列表含
`pageId / objectId / objectKind / modelId`，并保留现有 `ObjectMapping`
作为兼容投影。**不采用动态混合键。**

### 14.3 实测：跨页面 ID 唯一性**当前不成立**

已实测，结论明确：

```console
$ execute dup.ir.json     # 两页各有 id="same"
  status: partial
  errors: []

$ export-model → 
  page1 nodes: [('same', 'same')]
  page2 nodes: [('same', 'same')]
```

**跨页面重复 id 未被拒绝，且两页都保留了 `same`。**

代码原因：`IrValidator.ValidatePage()` 内的 `nodeIds` 是**局部变量**
（`ProtocolRunner.cs:238`），每页新建一个，页间不共享。

**后果：** 裸 ID 作 `ObjectMapping` 键会互相覆盖——第二页的 `same`
会盖掉第一页的 `same`。

### 14.4 因此 T7 必须分两步

| 步 | 内容 |
| --- | --- |
| T7a | **补全局唯一性校验**：跨页面 + 跨对象类型的 id 重复 → `ir.id-duplicate-global` |
| T7b | 在 T7a 成立的前提下，`ObjectMapping` 用裸 ID 覆盖全部页面与五类对象 |

**T7a 是 T7b 的前置条件。** 若 T7a 因兼容性原因不能加（旧 IR 可能已有跨页重复），
则退回结构化 `ObjectMappings` 方案，**不采用动态混合键**（依负责人 D2）。

**T7a 的兼容性影响需评估：** 现有 IR 若存在跨页重复 id，加上校验后会从
`partial` 变成 `failed`。这属修 bug，但须在报告中披露。

---

## 15. 【M7】机制性闸门：一次性封闭"接受但不执行不报告"

### 15.1 问题

审查指出：本计划的模式是**逐项枚举缺陷**（12→14 项），
而非建立机制性闸门。因此下一轮审计必然再发现同类残留。

审查实测的残留（修完 14 项后仍存在）：

| 残留 | 表现 |
| --- | --- |
| 拼错字段 `typografy` | 被 `System.Text.Json` 静默忽略 |
| `specVersion: "99.0"` | 不校验，照常执行 |
| `layout: "kaboom"` | 回退到 Layered，不报告 |
| `IrContainer.Order` | 协议接受，从不执行 |
| `anchor.mode: "relative"` | 接受，等同 fixed |
| `Edge.FromPort` / `ToPort` | 接受，未完整执行 |
| `IrMetadata` 各字段 | 接受，不落盘 |
| `Spacing` @ `layout=manual` | 接受，无效果，不报告 |
| `ConstraintSpec.Target` | 接受，未用于作用域 |

### 15.2 机制性修法（三条闸门）

**闸门 1：未知成员检测**

`JsonSerializerOptions` 增加 `UnmappedMemberHandling.Disallow`（或自定义 converter
收集未知键），反序列化后：

```csharp
public sealed class IrParseResult
{
    public DiagramIr? Value { get; init; }
    public List<string> UnknownMembers { get; init; } = new();
}
```

未知成员进 `ExecutionResult.UnrecognizedInputs`。

**闸门 2：枚举白名单**

所有字符串枚举字段（`layout` / `direction` / `palette` / `role` / `kind` /
`canvas.mode` / `unit` / `align` / `row.type`）统一校验：

```csharp
if (!Allowed.Contains(value))
    unmet.Add(new UnmetConstraint { Code = "input.unknown-enum", ... });
```

**禁止静默回退。** 当前 `Parse("kaboom")` 返回 Layered 且不报告——必须改。

**闸门 0：先加载 JSON Schema**（审查 E-4 指出，此前完全没有）

`execute` 当前**根本不校验 Schema**——闸门 1 在重造 Schema 已有的约束。
`schemas/phase7/*.schema.json` 已存在且写明 `enum`/`required`，但无人使用。

```csharp
// 新增：协议入口先做 Schema 校验
var schemaErrors = JsonSchemaValidator.Validate(json, "design-spec.schema.json");
// 校验失败 → status=failed，不进后续流程
```

**这比手写闸门更可靠**：Schema 是协议的公开契约，改一处即全局生效。

**闸门 2 补充：闸门覆盖不到的 4 类**（审查实测漏网）：

| 漏网项 | 现状 | 应报告 |
| --- | --- | --- |
| `containerLayouts` 非法值 | `"svc":"kaboom"` → `applied: 'container svc relaid as kaboom'`，unmet 空 | `input.unknown-enum` |
| `anchor.mode: "relative"` | 等同 `fixed`，不报告 | `input.not-executed` |
| `FromPort` / `ToPort` | 路由用了，回读 null | 要么执行、要么报告 |
| `ConstraintSpec.Target` | 只回显，不限作用域 | `input.not-executed` |

**闸门 3：未消费字段登记**

对"协议接受但**本阶段之后**仍不执行"的字段，显式登记并在 `unmetConstraints` 报告一次。

**注意（审查 M7 指出）：** `containers[].order` **不能**放进该清单——
缺陷 16 / T1b 正是要在本阶段实现它。清单中只应保留本阶段明确不做的项，
如 `metadata.*`、`IrContainer.Kind` 的部分取值。

```csharp
// 只列本阶段确定不执行的字段；随 T1b 实现，containers[].order 必须从清单移除
private static readonly string[] NotYetExecuted = { "metadata.*" };
```

**闸门 1 的实现方式（已编译验证三种方案）：**

```console
Q1: UnmappedMemberHandling.Disallow      -> THROWS JsonException
Q2: JsonDocument 预扫未知键              -> 可检出 ["bogus"]
Q3: UnmappedMemberHandling.Skip（默认）  -> 静默丢弃
```

- `Disallow` **会抛异常**，拿不到"值 + 未知键"两者，不可用
- 自定义 converter 可行，但要为每个类型重写读取逻辑，成本高
- **选定方案：先用 `JsonDocument` 预扫，再正常反序列化**

```csharp
var doc = JsonDocument.Parse(json);
var unknown = doc.RootElement.EnumerateObject()
    .Select(p => p.Name)
    .Where(n => !KnownRootMembers.Contains(n, StringComparer.OrdinalIgnoreCase))
    .ToList();
var value = doc.Deserialize<DiagramIr>(opts);   // 正常路径，不抛
```

实现简单、不侵入类型定义、能同时产出两者。
**嵌套对象**（如 `theme.typografy`）需要按已知 schema 递归比对同一份 `JsonDocument`，
工作量集中在一个辅助类里。

### 15.3 `ExecutionResult` 扩展

```csharp
public sealed class ExecutionResult
{
    // 已有
    public List<UnmetConstraint> UnmetConstraints { get; set; }
    // 新增：无法识别的输入
    public List<UnrecognizedInput> UnrecognizedInputs { get; set; } = new();
}

public sealed class UnrecognizedInput
{
    public string Path { get; set; } = "";       // 如 "theme.typografy"
    public string Reason { get; set; } = "";     // "unknown member" / "unknown enum value"
}
```

### 15.4 验收（三条都可证伪）

| # | 输入 | 期望 |
| --- | --- | --- |
| 1 | `"typografy": {...}` | `UnrecognizedInputs` 含 `theme.typografy` |
| 2 | `"specVersion": "99.0"` | 进 `unmet`（版本不支持） |
| 3 | `"layout": "kaboom"` | 进 `unmet`（未知枚举），**不回退** |

**三条全过才说明"这一类问题被机制挡住"，而不是又补了三个 if。**

---

## 16. 【M11】统一字号表，指定唯一权威

### 16.1 问题

当前存在**三处**字号定义，互不引用：

| 位置 | 内容 |
| --- | --- |
| `Vsdx.Engine/TextLayout.cs` `TypeScale` | 6 档默认值（20/12/11/10/9/9） |
| `Vsdx.Adapters/VisioTextBlockBridge.cs` `SizeForRole` | 硬编码 20/12/9/10/8 |
| `DesignSpec.cs` §2.3 映射 | 文档中的映射表 |

**三者数值不完全一致**（如 `caption` 在 Bridge 是 9、在 TypeScale 是 `NodeCaption=10`）。

### 16.2 依赖方向约束

`Vsdx.Adapters` **不能引用** `Vsdx.Engine`（依赖反转，Phase 8.1 已确立）。

**因此不能简单地把 TypeScale 移到 Engine 让 Adapters 引用。**

### 16.3 方案：权威常量下沉到 `Vsdx.Model`

把字号档位定义为 `Vsdx.Model` 中的纯数据常量（权威来源是 `Vsdx.Engine/TextLayout.cs` 的 `TypeScale`，不是 `TextScale`）：

```csharp
// src/Vsdx.Model/TypeScaleDefaults.cs
public static class TypeScaleDefaults
{
    public const double DiagramTitle = 20;
    public const double RegionTitle  = 12;
    public const double NodeTitle    = 11;
    public const double NodeCaption  = 10;
    public const double EdgeLabel    = 9;
    public const double Annotation   = 9;
    public const double MinReadable  = 8;
}
```

- `Vsdx.Engine.TypeScale` 引用它作为默认值
- `Vsdx.Adapters.VisioTextBlockBridge.SizeForRole` 引用它
- 两者**都不再各自定义数字**

**依赖方向正确：** Engine → Model ← Adapters。

### 16.4 验收

| # | 断言 |
| --- | --- |
| 1 | `caption` 在 Bridge 路径与 TypeScale 路径产出**同一字号** |
| 2 | `VisioTextBlockBridge.cs` 中不再出现裸字号字面量（字面量全部来自 `TypeScaleDefaults`） |
| 3 | Adapters 项目引用中**无** `Vsdx.Engine` |

---

## 17. 【M9】`Canvas.Unit` 规范化契约，避免双重换算

### 17.1 问题

审查实测：`OfficeImoIncrementalEditor.cs:62` **已经在做**单位换算：

```csharp
double factor = MasterCatalog.UnitFactor(pageModel.Size.Unit);
targetW *= factor; targetH *= factor;
```

因此若在 `DesignSpecApplier` 里再乘一次，**`update` 路径会双重换算**。

### 17.2 契约

**换算点不止一处——原写"唯一换算点是 `OfficeImoIncrementalEditor`"是错的**
（审查 M9 指出遗漏，我实测确认）：

| 路径 | 输入 `10×8 cm` | XML `PageWidth` | 是否换算 |
| --- | --- | --- | --- |
| `create`（模型→VSDX） | 10 × 8 cm | `10` / `8` | ❌ **不换算**，原样写成英寸（XML 中 `U="IN"` 恒真） |
| `update`（走 `IncrementalEditor`） | unit=cm | 3.937 / 3.150 | ✅ 换算 |

所以现状是**两条路径行为不一致**，而不是"已有一处换算、只需避免重复"。

危险在于：`create` 写入 `PageWidth=10` 却在模型里保留 `unit:"cm"`，
下游若信任该标签，就会把 10 英寸当成 10 厘米。

**根因（已定位到具体行）：**

```csharp
// OfficeImoDocumentWriter.cs:48-49
MasterCatalog.Inches(pageModel.Size?.Width ?? ... )   // ← 直接转单位，没乘 UnitFactor
```

`MasterCatalog.UnitFactor` 已经存在且正确（`"cm" => 1/2.54`），
**`create` 路径只是没调用它**；而 `IncrementalEditor:62` 调用了。

**修法（T4 拆为两处）：**

| 位置 | 改动 |
| --- | --- |
| `OfficeImoDocumentWriter.cs:48-49` | 页面尺寸乘 `UnitFactor(pageModel.Size.Unit)` |
| `DesignSpecApplier` | 把 spec 单位规范化为 `inches` 后写入（避免与上一行叠加） |

**两处必须同时改**，否则仍然一条路径换算、一条不换算。

`DesignSpecApplier` 的职责改为**把 spec 的单位规范化为英寸**后写入
`page.Size`（且 `Unit` 字段也写 `"inches"`），使下游 `UnitFactor("inches") == 1`。

```csharp
// DesignSpecApplier
double ToInches(double v) => spec.Canvas.Unit switch
{
    "cm" => v / 2.54,
    "mm" => v / 25.4,
    _    => v
};
page.Size.Width  = ToInches(w);
page.Size.Height = ToInches(h);
page.Size.Unit   = "inches";        // ← 关键：规范化后不再标注 cm
```

**这样 `update` 时 `UnitFactor("inches")` 返回 1，无双重换算。**

### 17.3 验收（双程）

| # | 步骤 | 期望 |
| --- | --- | --- |
| 1 | `10×8cm` → `execute` | 页面 `3.937 × 3.150` 英寸 |
| 2 | 紧接 `update` | **仍为 `3.937 × 3.150`**（不变） |
| 3 | 读回 `unit` 字段 | `"inches"`（已规范化） |

第 2 步是 M9 的核心——单程测试无法发现双重换算。

---

## 18. 【M10】`Theme.Background` 的实现路径与重写顺序

### 18.1 两条候选路径

| 路径 | 做法 | 风险 |
| --- | --- | --- |
| A. `PageSheet` 单元格 | 写 `<PageSheet><Cell N="PageBackground" V="#FAFAFA"/></PageSheet>` | 低：不动 shape 层 |
| B. 背景页 | 新增一个背景 shape / 独立 page | **高**：与 z-order、三处 `pageN.xml` 重写冲突 |

### 18.2 选定路径 A

理由：背景是**页面属性**，不是图形对象。路径 B 会让背景进入 `ShapeOrder`，
与 `VisioNestingBridge` / `VisioFreePathBridge` / `VisioTextBlockBridge`
的重写顺序产生竞争。

### 18.3 重写顺序（已按审查 M10 更正）

> **本节原写"背景桥必须最后，否则会被前面覆盖"——三处事实都错了。**

**更正后的事实：**

| 原说法 | 实际 |
| --- | --- |
| create 与 update 共用同一条桥链 | **不同**：`create` 有 Nesting/FreePath/TextBlock，**无** ZOrder/Master；`update` 有 ZOrder/Master，**无** Nesting |
| 前面的桥会覆盖背景 | **不会**：每个桥都是"读入 → 改内存 → 写回"整份 part，后续桥读到的是最新内容 |
| `PageSheet` 在 `pageN.xml` | **不在**：`PageSheet` 在 `visio/pages/pages.xml`（实测：`page1.xml` 无 `PageSheet`） |

**关键推论：没有任何现有桥触碰 `pages.xml`。**

```console
$ grep -l "pages.xml" src/Vsdx.Adapters/*.cs
（无结果）
```

因此**顺序风险不存在**。背景写入只需与 `pages.xml` 的其他写入者协调，
而当前没有竞争者。

**实现落点：** 新增 `VisioBackgroundBridge`，操作 `visio/pages/pages.xml`
的 `<PageSheet>`，与 `create` / `update` 两条链都无冲突。
**不需要**像原计划那样把它排在最后。

### 18.4 验收

| # | 断言 |
| --- | --- |
| 1 | 写 `#FAFAFA` → 回读同值 |
| 2 | `examples/phase8/mixed-fidelity.vsdx` 往返后逐字段不变（既有保真不回退） |
| 3 | 背景写入后 `ShapeOrder` 长度不变（证明未引入背景形状） |
