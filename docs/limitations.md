# 已知限制与验收边界

> 本文档明确区分**已验证**与**未验证**的能力，并列出不能依赖的行为。
> 与 `docs/technical-validation.md` 的实测记录配套阅读。

---

## 1. 验证状态

| 层级 | 手段 | 状态 |
| --- | --- | --- |
| 文件结构 | ZIP / OPC / XML / Visio 语义逐项检查 | ✅ 通过 |
| 后端重载 | OfficeIMO.Visio 重新加载并比对结构 | ✅ 通过 |
| 第三方渲染 | 由 VSDX 渲染 SVG / PNG，人工目视 | ✅ 通过 |
| **Microsoft Visio 原生打开** | 真实 Visio 应用打开并编辑 | ⬜ **未执行** |

**前三项不能替代第四项。** 本环境未安装 Visio，因此不对"Visio 中完全正确"作任何断言。

---

## 2. 后端限制（OfficeIMO.Visio 3.6.1）

> 编号用 **R**(Restriction)，与验证层级的 **L1–L4**（L1 包结构 / L2 读回 / L3 渲染 / L4 Visio 原生）
> 区分开。两者曾共用 `L` 前缀，同一份文档里 `L4` 既是"`VisioConnector.From/To` 只读"又是
> "Visio 原生验证未执行"。

| 编号 | 限制 | 表现 | 本项目的处置 |
| --- | --- | --- | --- |
| R1 | `VsdxPackageValidator` 对 `pages.xml` 误报；`FixFile` 会写入重复 content type | `ValidateFile` 对合法包返回 False | 不使用该验证器；`VsdxPackageValidatorService` 替代，并显式检测重复声明 |
| R2 | PNG 缺省字体无 CJK 覆盖 | 中文渲染成 `□` | `FontResolver` 自动注入字体文件；找不到时给 `render.cjk-no-font` 警告 |
| R3 | `AddToContainer` 默认按成员重算容器包围盒 | 显式 bounds 被覆盖，色带错位 | 传 `AutoResize=false`；`DiagramLayout.Apply` 在模型层先算一致 bounds |
| R4 | `VisioConnector.From/To` 为 `internal set` | 直接赋值编译失败 | 统一用 `VisioPage.ReconnectConnector` |
| R5 | 无稳定 ID 概念 | 重命名变成删除+新增 | 写入 `User.VsdxModelId` 单元格，读取时恢复 |
| R6 | 无自定义文档属性 API | 模型历史无处存放 | `VisioMetadataBridge` 后置 patch `docProps/custom.xml` |
| R7 | 嵌套组按兄弟容器写入 | Visio 内非真正层级嵌套 | 保留 `ParentId` 并发出 `write.nested-group` 警告；渲染与结构正确 |
| R8 | 连接线路由为直线/折线近似 | 密集区域可能穿过节点 | `OrthogonalRouter` 自动避障；`check-layout` 检出残留问题 |

---

## 3. 模型表达能力限制

JSON 模型**不是** VSDX 的等价表示。以下内容无法完整映射：

| Visio 特性 | 状态 |
| --- | --- |
| 自定义 Geometry 段（手绘轮廓） | 保留在 `FreePath.Geometry`，未知行类型原样保留并记入兼容性报告 |
| 主题 / StyleSheet 引用 | 包级主题部分保留；模型只用显式样式 |
| Data Graphics、注释、签名 | 保留在包内，模型不建模 |
| 动态连接线的高级路由参数 | 仅建模 `kind` / `waypoints` |
| 保护的 ShapeSheet 单元格 | 保留，模型不暴露 |
| 宏（.vsdm） | 不在范围内 |

**原则**：无法映射的内容在包中**保留原样**，并在兼容性报告中说明；不会静默丢弃。
但"保留"不等于"可通过 JSON 编辑"。

`Page.Unmapped` 用于记录结构上识别到、但无法归一化的对象。

---

## 4. 渲染限制

- 渲染基于 OfficeIMO 的几何投影，**不是** Visio 渲染引擎，复杂效果（渐变、阴影、
  主题色、Data Graphics）可能与 Visio 有差异
- 文字度量使用估算模型，极端字距/字体的换行位置可能与 Visio 不同
- 连线标签避让是启发式的，`--no-text` 可用于只关心几何的场景
- PNG 需要 CJK 字体文件；SVG 不需要

---

## 5. 增量更新限制

- 仅支持修改：节点增删改、属性、样式、Shape Data、连接线增删改、分组、图层、页面尺寸
- **不支持**在保留原文件其它内容的前提下修改：母版定义、主题、页面模板
- `--delete-missing` 会删除模型中不存在的对象；不加此参数时保留并报告
- 嵌套组层级会扁平化（R7）

---

## 6. 未实现（第一阶段范围外）

| 功能 | 状态 |
| --- | --- |
| Word OLE 嵌入 / 提取 / 更新 | 未实现，接口已预留（`docs/architecture.md` 第 10 节） |
| Microsoft Visio COM 自动化 | 未实现，且不作为运行时依赖 |
| GUI 图形编辑器 | 未实现 |
| 在线渲染服务 | 未实现，且不作为核心依赖 |
| 从图片/自然语言直接生成图形 | 由 Agent + Skill 完成，不在工具内 |

---

## 7. Microsoft Visio 验收清单（待外部环境执行）

在装有 Visio 的 Windows 机器上，按序执行并记录结果：

### 7.1 打开与结构

- [ ] 双击 `demo/architecture.vsdx`，Visio 无修复提示地打开
- [ ] 页面显示三个分层容器，标题在容器左上角，**未遮挡**成员形状
- [ ] 15 个节点全部可见，形状类型与设计一致（矩形/六边形/圆形/平行四边形…）
- [ ] 打开"形状数据"窗口，能看到 `owner` / `tier` / `sla` 等自定义属性

### 7.2 交互编辑

- [ ] 拖动一个节点，容器不破裂、连线跟随
- [ ] 双击节点可编辑文字，中文正常显示与输入
- [ ] 选中容器可整体移动，成员随之移动
- [ ] 图层面板可见 `Edge Layer` / `Application Layer` / `Data Layer`

### 7.3 连接线

- [ ] 每条连线的两端粘附在形状连接点上（拖动形状时线保持连接）
- [ ] 箭头方向与设计一致
- [ ] 虚线样式（`linePattern: 2`）在 Visio 中显示为虚线
- [ ] 连线标签可见且未压在形状上

### 7.4 保存回环

- [ ] 在 Visio 中另存为后，`vsdx-tool export-model` 仍能读取
- [ ] 在 Visio 中修改一个节点的文字并保存，`vsdx-tool diff` 能检出该修改
- [ ] 用 `vsdx-tool update` 在 Visio 保存过的文件上继续增量修改，Visio 再次打开无异常

### 7.5 中文

- [ ] 中文节点文字无缺字、无乱码
- [ ] 字体显示为 `Microsoft YaHei`（或系统等效字体）

### 7.6 v2 版本

- [ ] `demo/architecture_v2.vsdx` 打开正常，"Analytics Service" 已改名
- [ ] "Audit Service" 存在且连接到 PostgreSQL
- [ ] 未修改节点的位置、颜色与 v1 一致

### 7.7 结果记录

| 检查项 | 结果 | 备注 |
| --- | --- | --- |
| 7.1 打开与结构 | ⬜ | |
| 7.2 交互编辑 | ⬜ | |
| 7.3 连接线 | ⬜ | |
| 7.4 保存回环 | ⬜ | |
| 7.5 中文 | ⬜ | |
| 7.6 v2 | ⬜ | |

若任一项失败，请把失败的 `.vsdx` 与操作步骤记录到 issue，并在
`docs/technical-validation.md` 中更新结论。

---

## 8. 兼容性报告代码

引擎在 `--json` 输出的 `compatibility.issues[].code` 中给出如下代码：

| Code | 含义 |
| --- | --- |
| `read.shape-no-master` | 形状无母版，编辑后几何可能不可靠 |
| `read.master-external` | 包级母版无法通过标准模型编辑，但已保留 |
| `read.edge-dangling` | 连接线端点指向未知形状 |
| `read.container-empty` | 容器无可枚举成员 |
| `read.no-nodes` | 页面有形状但无一能映射为节点 |
| `write.master-unknown` | 未知 shape 类型，回退为 Rectangle |
| `write.group-bounds` | 组既无 bounds 也无成员可推导 |
| `write.nested-group` | 嵌套组按兄弟容器写入（R7） |
| `write.container-attach` | 成员未能加入容器 |
| `write.edge-*` | 连线端缺失或写入失败 |
| `update.retained-unmodelled` | 包内对象不在模型中，已保留 |
| `update.matched-by-text` | 通过文本匹配到形状 |
| `render.cjk-no-font` | 含中文但未找到 CJK 字体 |
| `render.page-not-found` | 指定页面不存在 |
| `post-save.*` | 保存后校验发现的问题 |

**Agent 必须把这些代码透传给用户**，不得吞掉。

---

## 9. 纸张尺寸

默认 `canvas.mode=auto` 让页面贴合内容，产出的是**自定义尺寸**，不是标准纸张。

实测三个真实场景：

| 场景 | auto 产出 | A4 横 11.69 x 8.27 |
| --- | --- | --- |
| 跨境电商架构 | 8.55 x 9.68 | 高度超 17% |
| 部署拓扑 | 11.00 x 11.85 | 高度超 43% |
| 用户数据流 | 15.57 x 10.45 | 宽度超 33% |

**`canvas.mode=paper` 可让产出吸附到标准纸张**（A5/A4/A3/A2/Tabloid，按需旋转）。
三个场景在 paper 模式下全部落到 A3。

A4 容不下它们——内容本就需要 A3 的面积，这不是可以靠调参解决的。

需要打印交付物时请显式指定 `"canvas": {"mode": "paper"}`。

---

## 10. L4 脚本状态

`tools/l4/verify-visio.ps1` 已写好但**从未执行**——本机无 Visio，`/mnt/c` 只读且无 PowerShell。

**2026-11-18 做了一次静态审查**，发现并修正三处问题：

| 问题 | 原状 | 修正 |
| --- | --- | --- |
| 自由路径检查恒真 | `-ge 0`，任何图都 PASS | 改为查找 `NameU="FreePath"` |
| 几何行阈值过严 | 要求 ≥6 行 | 实测两节点路径只有 `MoveTo`+`LineTo` 共 2 行，改为 ≥2 |
| 无自由路径时误判 | `-ge 1` 会 FAIL 正常图 | 降为 INFO 输出，几何检查仅在存在时进行 |

**这三处若不修，脚本在能跑的环境里会给出错误结论**——比没有脚本更糟。

**仍未验证的部分**：脚本本身是否能在真实 Visio 中运行、COM API 调用是否全部正确、
`$results` 有序字典的 `+=` 追加行为是否符合预期。这些只能靠实际执行确认。

**解除条件**：一台装有 Microsoft Visio 的 Windows 机器，或可写挂载的 `/mnt/c` 加 PowerShell。

---

## 11. 校验器覆盖缺口

`validate` 执行 17 项检查，其中 13 项是 OPC 容器结构，4 项是 Visio 语义。
`ValidatorDetectionTests` 证明它能发现五类真实损坏（缺 `[Content_Types].xml`、XML 截断、
悬空连线引用、缺 `_rels/.rels`、重复 override）。

**Master 引用的有效性已检查**（`visio.master-references`）。

把 `page1.xml` 里的 `Master="17"` 改成 `Master="9999"`，`validate` 仍返回 `valid: true`，
而 Visio 打开这样的文件会显示为损坏形状（`shapes-not-valid`）。

**影响**：`valid: true` 不能推出“文件在 Visio 中完整可用”。外部编辑过的文件可能带有
悬空的 Master 引用会令 `valid: false`。

**若要补齐**：在 `visio.shape-ids-unique` 旁增加一项，解析 `visio/masters/masters.xml`
的 `<Master ID>` 集合并比对页面中的 `Master=` 属性。`ValidatorCoverageTests` 里那条
断言当前行为是 `IsValid == true` 的测试会在实现后失败，届时需要一并更新。
