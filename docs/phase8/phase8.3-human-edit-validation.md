# 人工编辑往返验证（2026-11-18）

章程 §1.2 E 要求：

> 人工可以在 Microsoft Visio 中继续编辑 Agent 生成的文件。
> Engine 可以重新读取人工修改后的文件。支持后续继续修改和维护。

**本机无 Visio**，因此用**直接修改 VSDX 的 XML**模拟人工编辑：
绕过引擎改写包内 `visio/pages/page1.xml`，等价于另一个程序（或 Visio 本身）动了文件。

## 一、过程

### 1. 基线

```console
$ execute --ir examples/realworld/cbo.ir.json --spec '{"palette":"enterprise"}'
  节点 9 / 容器 3 / 连线 8 / 文本 2
  web：文字='Web 商城'  位置=(1.77, 7.08)
```

### 2. 模拟人工修改

直接改 XML：

- `Shape ID="4"` 的 `<Text>` → `人工修改后的文字`
- 同一 Shape 的 `PinX` `1.775` → `4.5`

### 3. 引擎重新读取

```console
$ export-model --input human2.vsdx
  web：文字='人工修改后的文字'  位置=(4.50, 7.08)
```

**两项修改都被正确读回。**

### 4. 在此基础上继续增量修改

```console
$ update --input human2.vsdx --model edited.json --output final.vsdx
  web（人工改的）: '人工修改后的文字'      <- 保留
  order（Agent 改的）: '订单服务（Agent 再改）'  <- 生效
  节点总数: 9
```

**人工的改动没有被覆盖，Agent 的新改动也生效了。**

### 5. 检测器正确报告副作用

```console
$ check-layout --input final.vsdx
  score=0/100
  node.overlap (1)  [error] 节点 'web' 与 'app' 重叠
```

我把 `web` 移到 `x=4.5` 时撞上了 `app`。**这是人工修改造成的真实冲突，检测器如实报告了**——
不是引擎缺陷，而是引擎工作正常。

## 二、结论

| 章程要求 | 结果 |
| --- | --- |
| 人工修改后 Engine 能重新读取 | ✅ 文字与位置均正确读回 |
| 支持后续继续修改 | ✅ 人工改动保留，新改动生效 |
| 不因人工编辑破坏对象身份 | ✅ `web`/`order` 的模型 ID 稳定 |

**这一条从"未验证"变为"已验证（用 XML 级模拟）。"**

## 三、局限

**这不是 L4。** 直接改 XML 模拟的是"另一个程序改了文件"，但：

- 没有经过 Visio 的保存流程（Visio 会重排命名空间、可能重写 ID）
- 没有涉及 Visio 特有的专有结构
- 不能证明 Visio 能打开这个文件

**XML 级模拟能验证"引擎读得懂外部改动"，不能验证"Visio 与引擎互通"。**
后者仍需真实 Visio。
