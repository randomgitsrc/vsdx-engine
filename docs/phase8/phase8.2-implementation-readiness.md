# Phase 8.2 实施就绪度

**用途：** 在等待批准期间，把"纸面方案"与"已验证可行"分开记录。
**日期：** 2026-11-18
**基线：** `5c472f6`

---

## 1. 已实测验证的可行性（可直接实施）

| 任务 | 验证内容 | 证据 |
| --- | --- | --- |
| **T0** | `Angle` 变换 = `90° − θ`，非镜像 | 推导 + 0/30/45/90 四点核算；Writer `:284` 确实写盘、Reader `:576` 回读 |
| **T2d** | 零依赖读 TTF `name` 表 | 可运行原型，三字体（DejaVu/Verdana/Georgia）全部正确解析 |
| **T2e** | 移动 + 注册必须同提交 | 三变体对照：只移动会让 `fontSize` 也丢失 |
| **T2g** | 语义角色往返 | `SetUserCell`/`GetUserCellValue` 通道已存在（`VsdxModelId` 在用） |
| **T6b** | 升级 error 的影响面 | 18 基准 0 处 overflow；`baseSize=24` 时 12 处，`Score` 自动归零 |

---

## 2. 已验证的机制存在性（不需新建概念）

| 任务 | 依赖的既有机制 | 位置 |
| --- | --- | --- |
| T0b | `Group.Kind == "lane"` 被 `SwimlaneLayoutEngine` 识别 | `DiagramIr.cs:282` 是唯一改写点 |
| T1b | `IrContainer.Order` 字段已定义 | `DiagramIr.cs` |
| T4 | `MasterCatalog.UnitFactor` 已实现且正确 | `MasterCatalog.cs` |
| T4b | `FitPage(shrink)` 分支已存在 | `DesignSpec.cs:258` |
| T5b | `LocalRelayout.IsolationBroken` 已实现（缺碰撞） | `LocalRelayout.cs` |
| T7 | `BuildMapping` 已存在（需扩展） | `ExecutionResult.cs:168` |
| T10 | `pages.xml` 的 `PageSheet` 无桥竞争 | `grep -l pages.xml src/Vsdx.Adapters/` 无结果 |

---

## 3. 仍需实施时确认的项（非阻塞，但需谨慎）

| 项 | 为什么现在无法确认 |
| --- | --- |
| T2c 白名单的具体字段集 | 需先枚举 `ShapeStyle` 全部字段并逐一判定"渲染器是否支持"；渲染器能力边界要读 `OfficeImoVsdxRenderer` 才能定 |
| T2h Schema 校验的落地方式 | `System.Text.Json` 不内置 Schema 校验；需决定是否引入库（任务书 §4 要求评估依赖） |
| T1 Palette 四套具体色值 | 属设计决策，应在实施时用 PNG 目视确认，而非纸面定值 |
| T8 三套 DesignSpec 的具体参数 | 同上 |

**这四项不是"没想清楚"，是"必须看到实物才能定"**——
纸面定值反而会制造需要推翻的假设。

---

## 4. 实施顺序与依赖

```
T0 (Direction)  ──→ T0.5 (尺寸阶段)  ──→ T1 (StyleResolver)
                          │                    │
                          └→ T2g (角色往返)     ├→ T1c/T1d (字体/对比度)
                                               ├→ T2/T2b (对象接入)
                                               └→ T3 (容器色)
T0b (lane) ──── 独立
T4/T4b/T5/T5b/T6/T6b ──── 各自独立，可并行
T2d (字体渲染) ──→ T2e 的"三字体 PNG 不同"断言
T7/T8/T9/T10 ──── 收尾
```

**关键依赖：**
- **T0 → T0.5**：转置必须在尺寸推导之前定义清楚，否则框体与朝向不一致
- **T2d → T2e**：T2e 只保证回读；"三字体 PNG 不同"依赖 T2d
- **T1 → 其余全部**：解析器是统一样式入口，其它任务向它注册

---

## 5. 回归门禁（每个子任务后必跑）

```console
$ dotnet build VsdxEngine.sln -c Release --no-incremental
exit: 0    warnings: 0

$ dotnet test VsdxEngine.sln -c Release
exit: 0    通过: 373    失败: 0

$ 18 个基准
passed: 18 / 18
```

**任一退化即回退该子任务**，不以"其它项通过"放行。

详见 `phase8.2-regression-baseline.md`。

---

## 6. 本文件的边界

**它不声称实施会成功。** 它只记录：

- 哪些任务的可行性已用代码/命令验证过（§1）
- 哪些依赖既有机制（§2）
- 哪些必须看到实物才能定（§3）

**25 项缺陷仍是"计划状态"**，真正确认修掉要等 Stage 5 的验收断言逐条跑过。
