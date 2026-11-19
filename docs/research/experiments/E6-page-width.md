# 实验 E6：`canvas.auto` 不收缩页面，`canvas.fixed` 会

**日期：** 2026-11-20
**状态：** 完成（现象已确定，成因未定位）
**依据 Goal：** §6 输出适配、第三轮视觉评审的「右侧空白 4.41 英寸」

---

## 一、现象

`cbo` 渲染后页宽 **11.00 英寸**，但所有元素最右只到 **6.59**，右侧空出 **4.41**。
其余三个场景（`deploy-topology` / `user-data-flow` / `checkout-sequence`）只有 0.55–0.80 的右空白。

## 二、已排除的三个假设

| 假设 | 验证方式 | 结论 |
| --- | --- | --- |
| 标题框太宽撑大了页面 | 已实现 `FitTextBlocks`，标题框 6.00 → 3.60 | ❌ 页宽仍是 11.00 |
| `note` 文本框撑大 | 删掉 `note` 后重跑 | ⚠️ 页宽变 7.35，但 `note` 右边界只到 5.00，解释不通 |
| `FitPage` 本身有问题 | 手工对同样几何调用 | ❌ **正常**，11.00 → 7.35，`Shrank=True` |

## 三、确定的结论

| 路径 | 请求 | 实得 |
| --- | --- | --- |
| `canvas.mode=auto`（默认） | — | **11.00**（内容只到 6.59）|
| `canvas.mode=fixed, width=7.5` | 7.5 | **7.50** ✅ |

**`canvas.fixed` 能收缩，`canvas.auto` 不能。** 问题精确定位在 auto 分支。

## 四、仍未解释的矛盾

- `canvas.auto` 确实执行了（`appliedDesignNotes` 有 `canvas.auto`）
- 它内部调用 `DiagramSizer.FitPage(page, shrink: true)`
- 同一个函数、同样的几何，**手工调用能收缩到 7.35**
- 但 `execute` 里没有产生 `canvas.shrunk` 记录，说明 `Shrank == false`

**可能方向**（未验证）：
- `Recenter` 在 `FitPage` 之前执行，可能改变了内容坐标使 `neededW >= currentW`
- 布局引擎的 `Math.Max(page.Size.Width, width)` 在 auto 分支之前锁定了 11.00

## 五、临时绕开

需要窄页面时用 `canvas.mode=fixed` 显式指定尺寸。这是已验证可用的路径。

## 六、如何复现

```bash
source .envrc.sh
CLI=src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool

# 现象
echo '{"specVersion":"1.0"}' > /tmp/s.json
$CLI execute --ir examples/realworld/cbo.ir.json --spec /tmp/s.json --output /tmp/a.vsdx
$CLI export-model --input /tmp/a.vsdx --output /tmp/a.json   # 页宽 11.00

# 对照：显式尺寸可收缩
echo '{"specVersion":"1.0","canvas":{"mode":"fixed","width":7.5,"height":9}}' > /tmp/f.json
$CLI execute --ir examples/realworld/cbo.ir.json --spec /tmp/f.json --output /tmp/b.vsdx
$CLI export-model --input /tmp/b.vsdx --output /tmp/b.json   # 页宽 7.50
```
