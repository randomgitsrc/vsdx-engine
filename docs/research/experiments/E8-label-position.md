# 实验 E8：`LabelPosition` 不影响渲染结果

**日期：** 2026-11-20
**状态：** 完成（结论为 **Blocked**）
**依据 Goal：** 第三轮视觉评审第 3 项「标签白底切掉层边框」

---

## 一、现象

`cbo` 渲染后，接入层下边框在 `x[462,529]` 处有一个 **67px 的缺口**——
`HTTPS` 标签的白色背板把浅蓝色边框切成两段。

裁剪该区域可见：边框在文字左右各一截，中间被白底覆盖。

## 二、几何成因

```
HTTPS 连线: web(1.50,6.88) -> (2.40,6.17) -> (4.53,6.17) -> gw(5.42,6.88)
标签按连线中点放置: (3.46, 6.05)
接入层下边界: 6.29
相差 0.24 英寸 —— 小于标签半高 + 渲染 padding
```

## 三、尝试的修复

在 `DecorationLayout` 增加 `LabelClearOfBorders`：检测标签中点是否接近容器边框，
是则把 `edge.LabelPosition` 从 `0.5` 改为 `0.28` 或 `0.72`。

**验证链**：

| 步骤 | 结果 |
| --- | --- |
| 1. 判定命中 | ✅ 追踪显示 `e1 HTTPS mid=(3.46,6.05) clashes=True` |
| 2. 写入模型 | ✅ `edge.LabelPosition = 0.28` |
| 3. 写入产物 | ✅ `TxtPinX = 1.291`（非默认值）|
| 4. **渲染缺口消失** | ❌ **仍是 67px** |

## 四、结论

**OfficeIMO 的渲染器不用 `TxtPin` 定位连线标签**，它自己按连线中点计算。

所以：

- 引擎能改 `LabelPosition` → 影响 VSDX 文件里的文字位置单元格
- 但**本项目的 PNG 预览不受影响**
- Visio 原生打开时是否生效：**未知**（L4 Blocked）

**这与 E4（多行文本）属同一类限制**：数据能写进去，但渲染器不读。

## 五、对产品的影响

评审说的「标签白底切边框」**无法在本项目内修复**，除非：

1. 换渲染库
2. 自建渲染
3. 让标签位置由布局阶段决定（把标签当成独立图形对象，而不是连接线的一部分）

方案 3 最彻底，但那是**结构变更**：标签从「连接线的属性」变成「独立元素」。

## 六、如何复现

```bash
source .envrc.sh
CLI=src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool
echo '{"specVersion":"1.0"}' > /tmp/s.json
$CLI execute --ir examples/realworld/cbo.ir.json --spec /tmp/s.json --output /tmp/a.vsdx
$CLI render --input /tmp/a.vsdx --format png --output /tmp/a.png
# 检查 y=454 那一行的浅蓝色连续性
```
