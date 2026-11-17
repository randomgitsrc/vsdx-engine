# Phase 8.0 技术实验报告

**目的：** 任务 §16 要求"对于不确定的技术方案，先做小型验证实验，再决定是否正式引入"。
本文件记录三个实验的**实际执行结果**，用于修正 `capability-matrix.md` 中的判断。

**执行环境：** 本项目 workspace，`9f62178` + 文档分支；实验脚本不进入产品代码。

---

## 实验 1：自由路径（自定义 Geometry）能否写入并保留

**背景：** 任务 §4.1 把"任意路径"列为需要覆盖的表达能力。
模型侧 `Node.Shape` 是字符串，只能选内置 master，无路径表达。

**做法：** 直接在 `visio/pages/page1.xml` 注入一个含 4 段折线的 `Shape`：

```xml
<Section N="Geometry" IX="0">
  <Cell N="NoFill" V="1"/>
  <Row T="MoveTo" IX="1"><Cell N="X" V="0"/><Cell N="Y" V="0"/></Row>
  <Row T="LineTo" IX="2"><Cell N="X" V="1.33"/><Cell N="Y" V="1"/></Row>
  <Row T="LineTo" IX="3"><Cell N="X" V="2.66"/><Cell N="Y" V="0"/></Row>
  <Row T="LineTo" IX="4"><Cell N="X" V="4"/><Cell N="Y" V="1"/></Row>
</Section>
```

**结果：**

| 检查 | 结果 |
| --- | --- |
| 包校验 | ✅ VALID |
| 渲染 | ✅ 12887 bytes，折线正确绘出（红色锯齿线可见） |
| 经 `update` 往返后几何是否保留 | ✅ 保留（`geom=1, rows=4`，锯齿坐标 `1.33` 仍在） |

**结论：** **自由路径技术上可行，无兼容性风险。**
渲染图见 `assets/exp-image.png` 底部红线。

**但发现一个模型层缺陷：** 该自由路径被读回模型时变成了
`Node { shape: "Rectangle", text: null }`——**自定义几何被错误标记为矩形节点**。
这意味着"读取外部自定义几何"目前会丢失语义。

---

## 实验 2：图片嵌入（ForeignData）是否可行

**背景：** 反射确认 OfficeIMO.Visio 3.6.1 **无** image/picture API。
`capability-matrix.md` 原本标记为 🚫 Unsupported by Dependency。

**做法：** 手写 OPC：

1. 增加 `visio/media/image1.png`
2. 在 page1.xml 增加 `Type="Foreign"` 的 Shape，内含 `<ForeignData>`
3. 在 `page1.xml.rels` 增加 image 关系
4. `[Content_Types].xml` 已有 `<Default Extension="png"/>`（库自动写入，无需改动）

**结果：**

| 检查 | 结果 |
| --- | --- |
| 包校验 | ✅ VALID（含 `backend.reload`：4 shapes） |
| 渲染 | ✅ 28693 bytes，占位图形被绘出 |
| Content-Types 是否需要手工补 | ❌ 不需要，库已写 `png/jpg/emf` |

**结论：** **图片技术上可行，且不需要修改 Content-Types。**
原先"🚫 Unsupported by Dependency"的判断**过于悲观，应予修正**：
依赖库不支持图片的**高层 API**，但包结构完全支持，自写 XML 可行。

**渲染图见** `assets/exp-image.png` 右上角（1×1 PNG 被放大，显示占位框）。

**注意：** 渲染器画的是占位框而非真实位图缩略。这说明
**渲染器对图片的支持弱于包格式支持**——图片能存进 VSDX，但 SVG/PNG 预览里可能只是占位。
这是一条需要如实记录的差异。

---

## 实验 3：SVG 嵌入是否可行

**做法：** 与实验 2 相同，媒体改为 `image1.svg`，并在 `[Content_Types].xml`
补 `<Default Extension="svg" ContentType="image/svg+xml"/>`。

**结果：**

| 检查 | 结果 |
| --- | --- |
| 包校验 | ✅ VALID |
| 渲染 | ✅ 34840 bytes |

**结论：** **SVG 技术上可行**，但**必须自行补 Content-Type**（库默认只写 png/jpg/emf）。
这比 PNG 多一个步骤，是真实的实施成本。

---

## 实验 4（失败记录）：首次图片实验因脚本 bug 失败

第一次尝试产生 `Format error in package`。原因**不是**图片能力不可行，
而是我的 Python 脚本用 `zipfile` 追加写入，产生了**重复的 `page1.xml` 条目**。

```
UserWarning: Duplicate name: 'visio/pages/page1.xml'
```

改为"读全部部件 → 内存修改 → 一次性重写"后通过。

**记录此条的理由：** 它说明"包校验失败"不等于"能力不可行"。
Phase 8.3 若遇到类似报错，应先排除构造方式错误，再下"不支持"的结论。

---

## 对能力矩阵的修正

| 项 | 原判定 | 修正为 | 依据 |
| --- | --- | --- | --- |
| 自由路径 | ❌ Missing | ⚠️ 可写入（模型未支持） | 实验 1 |
| 图片 | 🚫 Unsupported by Dependency | ⚠️ 可写入，但渲染器仅画占位 | 实验 2 |
| SVG 嵌入 | 🚫 Unsupported by Dependency | ⚠️ 可写入，需补 Content-Type | 实验 3 |

**共同结论：三项都能进 VSDX，缺的是模型层与写入器的支持，不是底层格式的限制。**

这显著降低了 Phase 8.1 / 8.3 的风险等级：
从"可能根本做不到"变为"需要写 XML 与模型字段"。

---

## 仍未验证的部分

| 项 | 原因 |
| --- | --- |
| Visio 原生打开含图片的 VSDX | 无 Windows + Visio（L4） |
| 图片在 Visio 中的缩放/裁剪行为 | 同上 |
| 真实 PNG（非 1×1）的渲染保真 | 渲染器可能只画占位框 |
| 大图（>1MB）对包大小与性能的影响 | 未测 |

**L4 依然未验证，不得由 L1–L3 结果推断。**
