# Agent VSDX Engine

**在未安装 Microsoft Visio 的环境中，创建、读取、增量修改并渲染真实的 VSDX 图形文件。**

一套独立图形工具（CLI + C# API）加一份通用 Agent Skill。输出是结构正确的 OPC/VSDX 包，
不是改了扩展名的图片；预览图由**保存后的 VSDX 本身**渲染，不是另外画一张 SVG 充数。

```
JSON 模型  ──create──▶  VSDX  ──render──▶  PNG / SVG
   ▲                      │
   └────export-model──────┘
                          │
               update ────┘   在已有 VSDX 上增量修改，未改对象原样保留
```

- 后端：OfficeIMO.Visio，锁定版本见 [`Directory.Packages.props`](Directory.Packages.props)
- SDK：.NET 8，精确版本见 [`global.json`](global.json)
- 运行时不访问网络
- 许可证：MIT

---

## 为什么需要它

AI Agent 能画出漂亮的图，但产出通常是 PNG 或 SVG——一次性快照，无法回到 Visio 里继续编辑。
VSDX 本身是 ZIP/OPC + XML 的开放包，程序完全可以读写，难点在于：

- 常规库没有"读 VSDX → 改一个节点 → 只保存差异"这条路
- 重命名一个节点，很容易变成"删除旧形状 + 新建形状"，原有 ID 与连接关系全丢
- 中文渲染需要额外处理字体，否则全是缺字方框
- 文件"能打开"和"结构正确"是两件事

这个项目把这些坑填了，并把结论写成可执行的测试。

---

## 当前状态：0.9.0（prerelease）

**核心承诺尚未验证。** 本项目要交付的是「原生、可编辑的 VSDX」，但文件**从未在 Microsoft Visio
中打开过**——本机无 Visio，WPS 不可替代。全部证据止于 L1（包结构）、L2（读回）、L3（渲染）。

因此版本号是 `0.9.0` 而非 `1.0.0`：能力已建立，验证不完整。详见
[`docs/limitations.md`](docs/limitations.md)。

已建立的能力见 [`docs/capability-boundaries.md`](docs/capability-boundaries.md)，
其中同时写明**不能画什么**（时序图、状态机语义）和**即使好图也不保证什么**
（列对齐 ±0.3″、正交路由、零标签重叠）。


## 快速开始

```bash
git clone <this-repo> agent-vsdx && cd agent-vsdx
dotnet build -c Release

# 创建
dotnet run --project src/Vsdx.Cli -c Release -- create \
  --input examples/architecture.json --output out/architecture.vsdx

# 渲染预览（PNG 会自动挑选 CJK 字体）
dotnet run --project src/Vsdx.Cli -c Release -- render \
  --input out/architecture.vsdx --format png --output out/architecture.png --dpi 120

# 结构校验
dotnet run --project src/Vsdx.Cli -c Release -- validate --input out/architecture.vsdx

# 布局质量
dotnet run --project src/Vsdx.Cli -c Release -- check-layout --input out/architecture.vsdx
```

构建后可直接调用 `src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool`，或安装为全局工具：

```bash
dotnet pack src/Vsdx.Cli/Vsdx.Cli.csproj -c Release -o artifacts
dotnet tool install --global --add-source ./artifacts AgentVsdx.Engine.Tool
```

安装细节、字体配置、离线部署与容器镜像见 [docs/installation.md](docs/installation.md)。

---

## 交付示例

### 第一轮：分层企业架构图

三个水平逻辑分层（接入 / 服务 / 数据），层内与跨层连线，每层是一个真实的 Visio 容器。

![第一轮架构图](demo/architecture.png)

想知道确切的节点数、连线数、分组与图层，直接问文件：

```bash
vsdx-tool inspect --input demo/architecture.vsdx --json
vsdx-tool check-layout --input demo/architecture.vsdx --json
```

### 第二轮：增量修改

以下修改全部通过**在原 VSDX 上增量更新**完成，没有重建整图：

1. `Query Service` → `Analytics Service`（原地改名，Visio 形状 ID 保持不变）
2. 新增 `Audit Service`
3. `Audit Service` → `PostgreSQL` 新增连接
4. 移动 `Report Engine`
5. 分组 `数据层 Data Layer` → `数据层  Data Platform Layer`

![第二轮架构图](demo/architecture_v2.png)

两版的差异可以直接对比出来：

```bash
vsdx-tool diff --before demo/architecture.vsdx --after demo/architecture_v2.vsdx
```

```console
  node.added       audit-service
  edge.modified    e-ingest-query.endpoints: ingest-service->query-service
                                            -> ingest-service->analytics-service
  edge.added       e-audit-pg
  group.modified   grp_data.name: 数据层  Data Layer -> 数据层  Data Platform Layer
```

产物：

| 文件 | 说明 |
| --- | --- |
| [demo/architecture.vsdx](demo/architecture.vsdx) | 第一轮 VSDX |
| [demo/architecture.png](demo/architecture.png) · [.svg](demo/architecture.svg) | 由上面那个 VSDX 渲染 |
| [demo/architecture.json](demo/architecture.json) | 导出的 JSON 模型 |
| [demo/architecture_v2.vsdx](demo/architecture_v2.vsdx) | 第二轮：增量修改后的 VSDX |
| [demo/architecture_v2.png](demo/architecture_v2.png) · [.svg](demo/architecture_v2.svg) | 第二轮预览 |
| [demo/architecture_v2.json](demo/architecture_v2.json) | 第二轮模型 |

### 不同图形类型，不同表达方式

架构图不该千篇一律。示例覆盖三种图形语义，各自采用不同的布局策略：

| 示例 | 类型 | 布局策略 |
| --- | --- | --- |
| [examples/architecture.json](examples/architecture.json) | 分层架构 | 水平色带 + 列对齐 |
| [examples/workflow.json](examples/workflow.json) | 业务流程图 | 阶段容器 + 判断分支 |
| [examples/network.json](examples/network.json) | 网络拓扑 | 安全域分区 + 设备层级 |

![网络拓扑](examples/network.png)

---

## CLI

| 命令 | 作用 |
| --- | --- |
| `create --input model.json --output out.vsdx` | 由 JSON 模型创建 VSDX |
| `--layout layered\|flow\|tree\|grid\|swimlane\|manual\|auto` | 自动布局（`auto` 按图形类型选择策略） |
| `--fit-text --fit-page --recenter` | 按文字推导尺寸、页面贴合内容、内容居中 |
| `inspect --input out.vsdx [--json]` | 读取并报告结构（节点/连线/分组/图层/Shape Data） |
| `export-model --input out.vsdx --output model.json` | 导出 JSON 模型 |
| `update --input old.vsdx --model new.json --output new.vsdx` | **增量**应用修改 |
| `render --input out.vsdx --format png\|svg --output preview.png` | 渲染预览 |
| `validate --input out.vsdx [--json]` | ZIP / XML / OPC / Visio 语义校验 |
| `check-layout --input out.vsdx [--json]` | 硬/软约束分离：重叠、容器相压、穿线、绕行比、行对齐、尺寸突变 |
| `batch --op <create\|render\|validate\|export-model> --input-dir <dir> --out-dir <dir>` | 批量处理，单项失败不中断 |
| `repair --input out.vsdx [--max-rounds n]` | 有界改进循环：计划 → 尝试 → 接受或回滚 → 升级 → 止损 |
| `compare --input out.vsdx [--emit-dir d]` | 生成多个候选布局，硬约束筛选后按软指标排序 |
| `execute --ir model.json [--spec design.json] --output out.vsdx` | 三层协议入口：IR + DesignSpec → VSDX + ExecutionResult |
| `diff --before a.vsdx --after b.vsdx [--json]` | 两版本结构对比 |
| `version --json` | 工具 / 后端 / 模型版本 |

所有命令支持 `--json`，输出稳定可解析。退出码：`0` 成功，`1` 有错误，`2` 用法错误。

### 内容与外观分离：`execute`

`create` 的 JSON 同时承载内容与外观。若要在**不改内容**的前提下调整配色、字号或纸张，
用 `execute`：内容放 IR 文件，外观放 DesignSpec 文件。

```bash
vsdx-tool execute --ir diagram.ir.json --spec design.json --output diagram.vsdx --json
```

```json
{
  "specVersion": "1.0",
  "layout": "auto",
  "canvas": { "mode": "paper" },
  "theme": { "palette": "enterprise" },
  "typography": { "baseSize": 12, "minReadableSize": 9 }
}
```

| 字段 | 取值 | 作用 |
| --- | --- | --- |
| `layout` | `auto` `layered` `flow` `tree` `grid` `swimlane` `manual` | 布局策略 |
| `direction` | `leftToRight` `topToBottom` | 不填则按结构推断（有容器→纵向） |
| `canvas.mode` | `auto` `fixed` `paper` | `paper` 吸附 A5/A4/A3/A2/Tabloid |
| `theme.palette` | `enterprise` `muted` `technical` `midnight` | 角色配色 |
| `typography.minReadableSize` | 磅值 | 字号下限，**可单独使用** |

`--json` 的输出里，`appliedDesignNotes` 说明实际执行了什么，`unmetConstraints` 列出
**每一条未能满足的请求及原因**——引擎不静默丢弃请求。交付前应检查
`quality.hardConstraintsPassed`。

完整字段见 [`skill/SKILL.md`](skill/SKILL.md) 与 [`docs/api.md`](docs/api.md)。
完整参数见 [docs/api.md](docs/api.md)，或 `vsdx-tool --help`。

### 批量处理

```bash
# 一次创建目录下所有 JSON 模型
vsdx-tool batch --op create --input-dir examples --input-glob "*.json" --out-dir out/

# 批量渲染与校验
vsdx-tool batch --op render   --input out/ --out-dir out/ --format png --dpi 120
vsdx-tool batch --op validate --input out/ --json
```

单个文件失败不会中断整批，每项结果独立报告。

---

## 能力边界

**能做**

- 真实 VSDX 创建：矩形 / 圆形 / 菱形 / 圆角 / 文本框 / 流程图 / 六边形 / 平行四边形等，
  连接线、箭头、容器、图层、多页面、Shape Data
- 读取**外部** VSDX（非本工具生成），无法映射的属性显式报告而非静默丢弃
- 增量修改：只写模型与包之间有差异的对象；模型未提及的对象默认保留
- 由 VSDX 渲染 SVG / PNG，含中文（自动挑选 CJK 字体），支持多页面、自定义 DPI、背景、
  透明背景、字体替代
- 自动布局：分层 / 流程 / 树形 / 网格四种策略，`auto` 按图形类型选择
- 文字驱动几何：字号层级（含可读性下限）、按标签推导节点/容器/页面尺寸
- 有界自动修复：快照、影响分析、三级修复决策、失败回滚、策略去重与止损
- 候选方案比较：多策略/多间距并行生成，隔离互不影响，硬约束过滤 + 软分数排序
- 三层结构化协议（Diagram IR / DesignSpec / ExecutionResult），供外部 Agent 稳定调用；
  未满足的约束必报告，未知约束码不静默跳过
- 容器级布局与局部重排：只重排指定容器，容器外节点逐字节不变
- 独立文本（标题/副标题/图例/注释）与自定义几何路径，经协议与模型双层支持
- 保真守卫：修改前后结构对比，发现致命损坏时拒绝输出而非静默返回
- 布局质量检查，**硬约束与软约束分离**：
  - 硬（不过即不可交付，分数强制为 0）：节点重叠、连线穿节点、容器相压、节点逃出容器
  - 软（影响专业度）：连线绕行比、行错位、同行尺寸突变、色带间距、间距过密、文字溢出、连线交叉
  - 每条问题带 `rootCause` / `recommendedFix` / `level`（Local / RegionRelayout / FullRedraw）
- 正交自动布线：为被遮挡的连线寻找无碰撞路径

**不做（第一阶段）**

- Word OLE 嵌入 / 提取（接口已预留，见 [docs/architecture.md](docs/architecture.md)）
- Microsoft Visio COM 自动化
- GUI 编辑器 / 在线渲染服务

---

## 验证状态

严格区分四个层级，**不能互相替代**：

| 层级 | 手段 | 状态 |
| --- | --- | --- |
| 文件结构 | ZIP / OPC / XML / Visio 语义逐项检查 | ✅ |
| 后端重载 | OfficeIMO.Visio 重新加载并比对结构 | ✅ |
| 第三方渲染 | 由 VSDX 渲染 SVG/PNG，人工目视 | ✅ |
| **Microsoft Visio 原生打开** | 真实 Visio 应用打开并编辑 | ⬜ **未执行** |

本环境没有安装 Visio，因此**不声称**已通过 Visio 原生验证。
可在 Windows + Visio 环境逐项执行的验收清单见 [docs/limitations.md](docs/limitations.md)。

### 实测发现的上游缺陷

逐项验证后端库时发现以下问题，均已补偿并有回归测试锁定：

| 问题 | 补偿 |
| --- | --- |
| `VsdxPackageValidator` 对合法包误报；`FixFile` 会写坏 `[Content_Types].xml` | 不使用它，改用自研验证器，并专测重复声明 |
| PNG 缺省字体无 CJK 覆盖，中文渲染成 `□` | `FontResolver` 自动检测并注入字体文件 |
| `AddToContainer` 无视显式 bounds，色带压住节点 | `AutoResize=false` + 自行推导容器几何 |
| 重命名会变成"删除 + 新增"，丢失 ID 与连接关系 | `User.VsdxModelId` 单元格绑定稳定 ID |

复现步骤与源码定位见 [docs/technical-validation.md](docs/technical-validation.md)。

---

## 项目结构

```
agent-vsdx/
├── src/
│   ├── Vsdx.Model/         JSON 模型、适配器接口、兼容性报告（不依赖 OfficeIMO）
│   ├── Vsdx.Adapters/      OfficeIMO 读写、增量编辑、样式器、元数据桥
│   ├── Vsdx.Engine/        门面、布局推导、正交布线、质量分析、批量处理
│   ├── Vsdx.Renderer/      PNG / SVG 渲染 + CJK 字体解析
│   ├── Vsdx.Validator/     结构与语义校验
│   └── Vsdx.Cli/           vsdx-tool
├── tests/Vsdx.Tests/       按类别组织的测试
├── skill/SKILL.md          Agent Skill：图形设计、布局、迭代流程
├── schemas/                JSON Schema
├── examples/               示例模型（架构 / 流程 / 网络）
├── benchmarks/             基准语料（6 族 × 3 档 + 8 个故障注入）
├── demo/                   两轮验收产物
├── tools/                  示例生成脚本
└── docs/                   安装、架构、验证、API、依赖、限制、测试报告
```

---

## 文档

| 文档 | 内容 |
| --- | --- |
| [docs/installation.md](docs/installation.md) | 安装、字体配置、离线部署、容器镜像 |
| [docs/architecture.md](docs/architecture.md) | 分层设计、适配器接口、增量算法、第二阶段扩展点 |
| [docs/technical-validation.md](docs/technical-validation.md) | P0–P3 实测记录与已知限制 |
| [docs/phase0-report.md](docs/phase0-report.md) | Phase 0–6：诊断、技术选型、目标架构、分阶段实施结果 |
| [docs/phase7/](docs/phase7/) | Phase 7：独立审计、能力基线、语义语料、差距分析、协议契约、VSDX 兼容性 |
| [docs/api.md](docs/api.md) | C# API 与 CLI 完整参考 |
| [docs/dependencies.md](docs/dependencies.md) | 依赖清单、版本锁定、许可证 |
| [docs/limitations.md](docs/limitations.md) | 限制说明、Visio 验收清单、兼容性报告代码 |
| [docs/test-report.md](docs/test-report.md) | 测试报告与覆盖矩阵 |
| [skill/SKILL.md](skill/SKILL.md) | Agent Skill |
| [schemas/diagram.schema.json](schemas/diagram.schema.json) | JSON Schema |

### 项目管理文档（2026-11-18 复盘产出）

| 文档 | 内容 |
| --- | --- |
| [PROJECT-CHARTER.md](PROJECT-CHARTER.md) | 项目目标、职责边界、架构约束、验收层级定义 |
| [PROJECT-STATUS.md](PROJECT-STATUS.md) | 当前真实能力（实测）、未验证项、25 项缺陷状态、与目标的差距 |
| [ENGINEERING-HEALTH.md](ENGINEERING-HEALTH.md) | 构建/测试/依赖/分层健康度、测试分布不均、架构债、风险登记 |
| [ROADMAP.md](ROADMAP.md) | 按产品目标排序的路线、四阶段与可证伪验收标准 |
| [TASK-REGISTER.md](TASK-REGISTER.md) | 统一任务台账（25 项缺陷 + 2 项新发现 + 路线任务） |

---

## 开发

```bash
dotnet build -c Release
dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release

# 按类别运行
dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release \
  --filter "FullyQualifiedName~ExternalVsdxTests"
```

测试按类别拆分，文件名即类别名，见 [`tests/Vsdx.Tests/`](tests/Vsdx.Tests/)。
各类别的覆盖点与用例说明见 [docs/test-report.md](docs/test-report.md)。

外部包测试的输入由 `ExternalFixtures` 用 `System.IO.Compression` 手写 OPC 结构生成，
**完全不经过本项目的写入器**——只用自己的产物验证自己的读取能力没有意义。

---

## 贡献

欢迎提交 issue 与 PR。改动增量编辑逻辑时，务必跑 `RoundTripTests` 与
`ExternalVsdxTests`；`RoundTrip_NoOpUpdateIsLossless` 会在"空操作却报告修改"时失败，
是判断增量逻辑是否被破坏的最快信号。

---

## 许可证

MIT，见 [LICENSE](LICENSE)。第三方依赖许可证见 [docs/dependencies.md](docs/dependencies.md)。
