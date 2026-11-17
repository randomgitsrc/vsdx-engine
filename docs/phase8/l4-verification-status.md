# L4 Visio 原生验证状态

**检查日期：** 2026-11-17
**结论：** ⬜ **无法执行** — 环境缺少 Microsoft Visio

---

## 1. 本次实际做的检查

负责人要求 L4 提前安排。我先确认环境是否具备条件，而不是直接假设不具备。

### 1.1 WSL 互操作可用

```console
$ cd /mnt/c && /mnt/c/Windows/System32/cmd.exe /c "echo interop-ok"
interop-ok
```

**这是重要发现**：本机是 WSL，且 **Windows 二进制可以执行**。
如果装了 Visio，理论上可以调用 COM 自动化完成 L4 验证。

### 1.2 但 Visio 未安装

```console
$ ls "/mnt/c/Program Files (x86)/Microsoft Office/Office/"
（空目录）

$ powershell -Command "Get-ItemProperty 'HKLM:\...\Uninstall\*' |
    Where-Object {$_.DisplayName -match 'Visio|Office'} | Select DisplayName"
WPS Office 2023 增强版
```

**只有 WPS Office，没有 Microsoft Visio，也没有 Microsoft Office。**

---

## 2. 为什么 WPS 不能替代

| 维度 | Microsoft Visio | WPS Office |
| --- | --- | --- |
| 原生 VSDX 渲染 | ✅ 参考实现 | ⚠️ 有限支持，非参考实现 |
| ShapeSheet / Cells 语义 | ✅ 权威 | ❌ 不可验证 |
| 容器 / 嵌套行为 | ✅ 权威 | ❌ 不可验证 |
| COM 自动化接口 | ✅ | ❌ 无等价接口 |

**用 WPS 验证等于用另一个第三方渲染器验证**——那已经是 L3，不能冒充 L4。
任务与负责人决策都明确：**不得用其它层级替代 L4**。

---

## 3. 因此 G 任务的状态

| 项 | 状态 |
| --- | --- |
| 环境探测 | ✅ 已完成（WSL interop 可用，Visio 缺失） |
| 验证执行 | ⬜ **阻塞：需要安装 Microsoft Visio** |
| 替代方案 | ❌ 不接受（WPS 属 L3） |

---

## 4. 一旦环境就绪，应立即验证的内容

按负责人决策 4，8.1 完成后需验证：**自由路径、文本、Z-order、外部文件编辑**。

### 4.1 具体清单

| # | 验证项 | 通过标准 |
| --- | --- | --- |
| 1 | 打开 `examples/phase8/slice.vsdx` | 无修复提示，正常显示 |
| 2 | 标题/副标题/图例/注释可见且字号分层 | 标题明显大于注释 |
| 3 | 分隔线（自定义 Geometry）形状正确 | 是直线而非矩形 |
| 4 | 三个容器可整体拖动 | 成员跟随 |
| 5 | 保存后重新打开 | 无内容丢失 |
| 6 | 编辑标题文字并保存 | 保存成功，文字更新 |
| 7 | 打开含 5 个重叠 shape 的文件 | 前后遮挡关系正确 |
| 8 | 外部无几何文件编辑后打开 | 原形状轮廓不变（P0 验证） |

### 4.2 可自动化的部分

WSL interop 可用，因此可用 PowerShell + COM：

```powershell
$app = New-Object -ComObject Visio.Application
$doc = $app.Documents.Open("C:\path\to\slice.vsdx")
$page = $doc.Pages.Item(1)
Write-Output $page.Shapes.Count
$doc.Close()
$app.Quit()
```

**注意：** 用 COM 自动化验证"能否打开、形状数、文字内容"是可行的，
但**不能替代人工视觉确认**（字号层级、视觉专业度仍需人看）。

---

## 5. 需要负责人决策

**是否可在本机安装 Microsoft Visio？**

| 选项 | 影响 |
| --- | --- |
| 可以安装 | 立即执行上述 8 项，闭合 L4 |
| 不可安装 | L4 保持未验证，Phase 8 交付时继续如实标注 |

**在此之前，所有"可在 Visio 中编辑"的结论仍属推断，不得表述为已验证。**
