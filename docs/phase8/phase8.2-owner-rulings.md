# Phase 8.2 负责人裁决记录

**日期：** 2026-11-17
**性质：** 记录本阶段负责人已经给出的裁决，避免把已决事项重复提为待决问题。

---

## D1 — `Direction`

**负责人原文（Stage 2 审查回复）：**

> 接受修复，不增加兼容开关。Direction 是已有协议承诺的功能，topToBottom 必须真正生效。
> 修复后旧 spec 的输出可能改变，必须在报告和发布说明中明确披露。
>
> 但 Stage 3 必须重点审查完整空间变换的正确性。不能只交换 Node 的 X/Y。
> 必须检查 Group、Edge、TextBlock、FreePath 及其内部 Geometry、独立端点、Waypoints、
> 页面尺寸等所有相关空间数据。
> **Manual 模式必须保留用户坐标，不得擅自转置。**

**裁决：** 接受修复，无兼容开关。

**其中已包含对 D-8 的答案：**

> Manual 模式必须保留用户坐标，不得擅自转置。

**即 §11.2 的方案 A：`manual` 下 `direction` 不生效。**

---

## D2 — `ObjectMapping`

**负责人原文：**

> 不批准"单页使用裸 ID、多页改用 pageId/objectId"的混合键格式。
> 优先保持现有裸 ID 格式，并明确保证 IR 对象 ID 在整个 IR 范围内全局唯一，
> 包括跨页面、跨对象类型。**请通过实际检查 Validator 和测试确认；如果目前只保证
> 页面内唯一，则补充全局唯一性校验。**
>
> 如果全局唯一 ID 无法成为协议保证，则不要使用动态混合键格式，
> 应设计结构化 ObjectMappings 列表（pageId、objectId、objectKind、modelId），
> 并保留现有 ObjectMapping 作为兼容投影。

**裁决：** 保持裸 ID + 补全局唯一性校验（T7a）。已实测当前缺失。

---

## D3 — `BaseSize`

**负责人原文：**

> 接受缩放基准方案，但修正语义定义。
> 当前源码注释明确说明 BaseSize 是 Base node-title size in points。
> 因此 BaseSize 应代表 NodeTitle 的目标绝对字号，不是所有节点统一字号。
> scale = BaseSize / TypeScale.NodeTitle
> 将比例应用于所有文本角色，保留 DiagramTitle、RegionTitle、NodeTitle、
> NodeCaption、EdgeLabel、Annotation 的层级关系。
> 显式对象字号仍然优先。MinReadableSize 夹紧时必须报告。
> 字体确定必须发生在文字测量和布局之前，确保几何尺寸与最终字号一致。

**裁决：** 缩放基准，`scale = BaseSize / TypeScale.NodeTitle`。

---

## D-4 — TextBlock 字体回读

**状态：** ⬜ **待裁决**（本轮唯一真正未决项）

详见 `phase8.2-open-decisions.md`。

---

## D-8 — `manual` 下 `direction`

**状态：** ✅ **已由 D1 裁定为方案 A**

> Manual 模式必须保留用户坐标，不得擅自转置。

**我的失误：** 上一轮把它作为"待裁决"上报，但它早已包含在 D1 里。
原因是我把 D1 的这句要求只当作"注意点"抄进了 plan §11.4 的一句话，
**没有把它当作正式裁决登记**，于是在写 §11.2 时又把两种解释并列，
制造出一个并不存在的待决问题。

**教训：** 负责人的裁决必须以**独立条目**记录并标明适用范围，
否则会被自己的后续文档稀释成"背景信息"。

---

## 汇总

| 编号 | 事项 | 状态 |
| --- | --- | --- |
| D1 | `Direction` 修复 | ✅ 已裁决，已落实 T0 |
| D2 | `ObjectMapping` 键格式 | ✅ 已裁决，已落实 T7a/T7b |
| D3 | `BaseSize` 语义 | ✅ 已裁决，已落实 T1 |
| D-4 | TextBlock 字体回读 | ⬜ 待裁决 |
| D-8 | `manual` 下 direction | ✅ 由 D1 裁定（方案 A） |
