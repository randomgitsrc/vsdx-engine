# 故障注入用例

每个文件是一个**故意有问题**的模型，用来验证检测器在真实输入上会报出对应问题。

用途：调用方遇到某个错误时，可以在这里找到"它长什么样"；修改检测器时，这些是回归基线。

```bash
vsdx-tool create --input benchmarks/faults/fault-overlap.json \
                 --output /tmp/x.vsdx --layout manual
vsdx-tool check-layout --input /tmp/x.vsdx
```

**必须加 `--layout manual`**——自动布局会修正掉大部分故障，用例就失去意义了。

## 用例与预期检测

| 文件 | 预期码 | 级别 |
| --- | --- | --- |
| `fault-overlap` | `node.overlap` | error |
| `fault-out-of-page` | `node.out-of-page` | error |
| `fault-container-too-small` | `group.member-escapes` | error |
| `fault-long-text` | `text.overflow-horizontal` | error |
| `fault-text-vertical` | `text.overflow-vertical` | error |
| `fault-group-overlap` | `group.overlap` | error |
| `fault-through-node` | `edge.through-node` | error |
| `fault-cross-region` | `edge.diagonal-across-band` | warning |
| `fault-crossing` | `edge.crossing` | warning |
| `fault-misaligned` | `row.misaligned` | warning |
| `fault-size-mix` | `row.size-jitter` | warning |

**7 个 error 级检测全部有对应用例**（`ErrorCoverageTests` 会检查这一点）。

## 构造这些故障时的陷阱

有几类故障**默认不会出现**，需要绕开引擎的自动修正：

| 故障 | 陷阱 | 做法 |
| --- | --- | --- |
| `group.overlap` | 容器默认按成员收缩，两个重叠的框会被拉开 | 两个容器都设 `"autoSize": false` |
| `edge.through-node` | 路由器会绕开障碍，引擎自己永远不会产生 | 显式给 `waypoints`，把一个点放进另一个节点里 |
| `text.overflow-*` | 框会随文字撑大 | 显式给 `bounds`，让框小于文字所需 |
| `node.out-of-page` | 自动布局会把节点拉回页面内 | `--layout manual` + 显式坐标 |

`fault-cross-region` 需要三个容器：起点、终点，以及**中间被斜穿的那个**。

## 与基准用例的区别

`benchmarks/<family>/` 下的 18 个用例是**正常图**，衡量布局质量；
本目录的用例是**坏图**，衡量检测能力。
