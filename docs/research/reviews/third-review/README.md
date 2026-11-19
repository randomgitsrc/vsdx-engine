# 第三轮独立视觉评审：证据图

**评审日期：** 2026-11-20
**对象：** `cbo` / `deploy-topology` / `netflix-active-active` 三张渲染图
**评审方式：** 独立 subagent，逐像素测量 + 与 IR 对账
**结论：** 5/10、6/10、5/10；「没有真正在往专业方向走」

---

## 为什么保留这些截图

Goal §15.7 要求阶段切换满足「结果可以复现」。评审的结论若只有文字，
后来者无法判断它当时看到的是什么。这些是评审自己裁的图，不是本项目的产物。

---

## 证据图与问题的对应

| 文件 | 证明了什么 | 后续处理 |
| --- | --- | --- |
| `cbo-datalabel.png` | `SQL` 线切过「数据层 Data」标题 | 未修（E7：布局问题）|
| `cbo-gap455.png` | `HTTPS` 标签白底把层边框切出 67px 缺口 | 未修（E8：渲染库限制）|
| `cbo-gw.png` | 网关六边形顶点汇多条线打结 | 部分修（连接点从 1 个改为 4 个）|
| `nf-sqs.png` | SQS 被连线上下贯穿并压住文字 | 未修 |
| `dep-legend2.png` | 图例被连线穿过 | 图例已改为自动生成 |
| `dep-9000.png` | 三条 `9000` 标签挂同一 trunk | 未修 |
| `cbo-tangle.png` | 两处交叉 | 未修 |

---

## 这份证据的边界

- 只覆盖**渲染预览**，不涉及 Visio 原生（L4 Blocked）
- 截图是 2026-11-20 的状态；此后 `cbo` 页宽、图例、字号均有改动，
  **重新截图会与这些不同**
- 每张图对应的检测项，可用 `check-layout` 复现

---

## 如何复现当前状态

```bash
source .envrc.sh
CLI=src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool
echo '{"specVersion":"1.0"}' > /tmp/s.json
$CLI execute --ir examples/realworld/cbo.ir.json --spec /tmp/s.json --output /tmp/a.vsdx
$CLI render --input /tmp/a.vsdx --format png --output /tmp/a.png
$CLI check-layout --input /tmp/a.vsdx
```
