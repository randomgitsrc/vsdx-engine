# 实验 E4：组件内分区文本（标题 + 说明不同字号）

**日期：** 2026-11-19
**状态：** 完成（结论为 **Blocked**）
**依据 Goal：** §4.2 C（组件不应只被限制为一个矩形加一段文本）、§4.3 Level 2

---

## 一、问题

Goal §4.2 C 要求组件有「标题区域、说明区域、多文本区域、状态区域」。
E2 实验已证明当前 `Node` 只有一个 `Text` 字段，多行共用一套样式。

**问题**：能否用 Visio 的字符级样式（`Character` 段多 `Row`）实现「第一行 14pt、第二行 10pt」？

---

## 二、实验

**已知**：`VisioTextBlockBridge` 已经在写 `Character` 段（单 Row），说明机制存在。

**步骤**：

1. 生成一个双行节点（`订单服务\nJava / Spring`）
2. 手工在该节点的 `Character` 段插入第二个 `Row IX="5"`
   （第 5 个字符起，即第二行开头），字号设为 10pt
3. 校验 + 渲染

```xml
<Section N="Character">
  <Row IX="0"><Cell N="Font" V="Microsoft YaHei"/><Cell N="Size" V="0.194444" U="PT"/></Row>
  <Row IX="5"><Cell N="Font" V="Microsoft YaHei"/><Cell N="Size" V="0.138889" U="PT"/></Row>
</Section>
```

---

## 三、结果

| 项 | 结果 |
| --- | --- |
| 写入两个 Row | ✅ 成功 |
| `validate` | ✅ VALID |
| `render` | ✅ 成功生成 PNG |
| **渲染效果** | ❌ **两行字号相同** |

**根因（已定位到依赖层）**：

渲染走 `page.SaveAsImage()` / `page.ToSvg()`，完全委托 OfficeIMO。
而 OfficeIMO **加载时就把分段信息丢掉了**：

```csharp
var doc = VisioDocument.Load(path);
var shape = doc.Pages[0].Shapes[0];
shape.TextStyle.Size        // 空
shape.TextStyle.FontFamily  // "Microsoft YaHei"
```

`VisioTextStyle` 的 19 个属性**全部是形状级**（`FontFamily`/`Size`/`Bold`/…），
`VisioShape` 也没有 `Runs`/`Spans` 成员。**OfficeIMO 3.6.1 的文本模型不支持分段。**

所以不是渲染器「没读」，是**依赖库没有这个概念**。

---

## 四、结论

**结论（Verified）：能力可以在 VSDX 中写入，但无法在本项目的渲染预览里验证。**

这是一个**验证阻断**，不是实现阻断：

| 环节 | 状态 |
| --- | --- |
| 模型 | 需新增字段（如 `Node.TextRuns`）|
| Writer | **可行**——`Character` 段多 Row 是 Visio 标准做法，已验证可写入 |
| Reader | 需新增——当前不读 `Character` 段 |
| **Renderer** | **不读字符级样式**，无法预览 |
| Visio 原生 | **未知**（L4 Blocked）|

---

### 四附、3.6.2 的验证（2026-11-19）

查 NuGet 发现 **OfficeIMO.Visio 3.6.2 已发布**，其 README 声称支持
"mixed character runs … run fonts, **sizes**, colors, bold and italic"。

**实测 3.6.2，结论不变**：

| 检查 | 3.6.1 | 3.6.2 |
| --- | --- | --- |
| `VisioShape` 有 `Runs` 成员 | ❌ | ❌ 仍只有 `Text` + `TextStyle` |
| `VisioTextStyle` 属性数 | 19 | 27（新增全是 `TextPinX`/`TextWidth`/`TextAngle` 等**形状级**布局）|
| 渲染两个不同 `Size` 的 Row | 两行同字号 | **仍同字号**（SVG 拆成两个 `<text>`，但 `font-size` 都是 24）|

README 描述的"mixed character runs"是**读取已存在文件时的渲染能力**，
不是**新建时的 API**。3.6.2 没有暴露构造分段文本的入口。

**所以升级依赖不能解决 E4。** 这个结论现在对两个版本都成立。

## 五、对产品的影响

按 Goal §9.2「L1–L3 不能替代 L4」与「不得伪造验证证据」：

**如果现在实现分区文本，将无法证明它是对的。**

- 渲染预览显示不出来（Renderer 不支持）
- Visio 里是否正确，无 Visio 可验

**因此本轮不实现。** 建议顺序：

1. 先让 **Renderer 支持字符级样式**——这是纯渲染工作，可用 PNG 哈希与目视验证
2. 再实现模型字段 + Writer + Reader
3. L4 环境具备后验证 Visio 侧

---

## 六、这份结论的边界

- 只验证了「写入两个 Row 不破坏文件」，**没有验证 Visio 是否按预期渲染**
- 插入位置 `IX="5"` 是按字符数算的，实际 Visio 的索引规则可能不同
- 若 Renderer 将来支持，本实验应重跑

---

## 七、如何复现

见 §二步骤。关键检查点：`render` 出的 PNG 中两行字号是否不同——**当前相同**。
