# 独立审查报告 — vsdx-engine

> **归档说明**：本报告由独立 subagent 于 2026-11-18 对 HEAD `f7eabb6` 产出，报告 15 项问题
> （2 个 P0、5 个 P1、8 个 P2）。逐项处置结果见 `phase8.3-conclusion.md` §五。
> 原文保留，未改动；其引用的临时产物目录已清理。

审查对象：`/home/kity/oclab/vsdx-engine`
HEAD：`f7eabb6`（2026-11-18 18:13:47）
二进制度量（所有结论均基于此二进制复现）：
```
1ba0cc6d62bcb28f40e94f7925f52144  vsdx-tool
3c4e14bbeada3fcce6009f08fef6f17e  Vsdx.Protocol.dll
e9b3ca65c127e63adb9699ed6a338bd2  Vsdx.Adapters.dll
```
所有复现命令均 `source .envrc.sh` 后执行，工作目录 `.review-final/`，产物保留以便复核。

> **时序说明（影响可信度，须先读）**：审查期间项目被持续提交，
> 二进制在 18:12:18 被重建、18:13:47 提交了 `fix(update): an edit no longer resizes
> containers the caller did not touch`。我早期观察到的"no-op update 把容器 1.23″→1.61″"
> 在重建后**不再复现**（25 次重跑全部保持 1.23″），已作为"已修复"从结论中剔除，
> 仅在下文"查过但未发现问题"里留记录。下面每一条都在最终二进制上重新执行过。

---

## P0

### P0-1 `update` 无操作往返静默改变文本块对齐（数据丢失，且 `diff` 报告"完全相同"）

**现象**：把 `export-model` 的产物原样喂回 `update`，标题文本块的水平对齐从 `center`
变成 `left`；`changes` 为空、`diff --json` 报 `identical: true`。

**复现**：
```bash
cd /home/kity/oclab/vsdx-engine && source .envrc.sh
T=./src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool; W=.review-final
$T update --input $W/al0.vsdx --model $W/al0.json --output $W/P0-1.vsdx --json \
  | python3 -c 'import json,sys;d=json.load(sys.stdin);print("update ok=",d["ok"]," changes=",d["changes"])'
$T export-model --input $W/P0-1.vsdx --output $W/P0-1.json >/dev/null
python3 -c 'import json;b=json.load(open("'$W'/al0.json"))["pages"][0]["textBlocks"][0];a=json.load(open("'$W'/P0-1.json"))["pages"][0]["textBlocks"][0];print("hAlign:",b["style"]["hAlign"],"->",a["style"]["hAlign"])'
```
实际输出：
```
update ok= True  changes= []
hAlign: center -> left
```
XML 层证据（`HorzAlign`：0=left 1=center 2=right）：
```bash
echo -n "before: "; grep -o 'N="HorzAlign" V="[0-9]"' $W/zA/visio/pages/page1.xml | head -1
echo -n "after : "; grep -o 'N="HorzAlign" V="[0-9]"' $W/zB/visio/pages/page1.xml | head -1
```
```
before: N="HorzAlign" V="1"
after : N="HorzAlign" V="0"
```
而 `diff` 同时声称无差异：
```bash
$T diff --before $W/al0.vsdx --after $W/P0-1.vsdx --json \
  | python3 -c 'import json,sys;print("diff identical =",json.load(sys.stdin)["identical"])'
# diff identical = True
```

**判断**：这同时违反两条项目铁律。
1. **数据丢失**：作者的居中标题变成左对齐，`changes: []` 却什么都没说——属于"执行时改变、结果又没有报告"。
2. **`diff` 的结论是错的**：它只比 `Bounds`/`Text`/`Style.Fill`/`Style.Stroke`/`ShapeData`，
   **完全不比较 `TextBlock`**（`src/Vsdx.Engine/VsdxEngine.cs:250-360` 只遍历 nodes/edges/groups/pages）。
   任何"用 diff 验证增量修改保真"的流程都会在这里得到假阳性。

**根因线索**：`TextBlock` 模型有独立的 `Align` 字段，但读者把对齐写进
`Style.HAlign`（`OfficeImoDocumentReader.cs:199-235`），写者读的是 `block.Align`
（`VisioTextBlockBridge.cs:180-190`）。两者永不交汇，所以对齐在**每一次**写回时
都回落到默认 `left`。同一个 bug 也解释了为什么"请求 align=right"不生效：
```bash
# 把模型改成 right，update 后仍是 left
$T update --input $W/al0.vsdx --model $W/al-right.json --output $W/al2.vsdx --json  # changes: []
# after align request=right -> left
```

---

### P0-2 `canvas.unit` 为 cm/mm 且 `shrinkToFit=true` 时页面缩小 2.54×/25.4×，几何全部出界，而报告说"收缩为 4.85x5.63 英寸"

**现象**：请求 10×10 cm 的固定画布，得到 **1.909×2.215 英寸**的页面，4 个节点全部
`node.out-of-page`；`unmetConstraints` 只写"收缩为 4.85x5.63 英寸"，与实际产物不符。

**复现**：
```bash
cat $W/c-cm.json
# {"canvas":{"mode":"fixed","width":10,"height":10,"unit":"cm","shrinkToFit":true}}
$T execute --ir .review-rv/ir4.json --spec $W/c-cm.json --output $W/P0-2.vsdx --json \
  | python3 -c 'import json,sys;d=json.load(sys.stdin);print("status:",d["status"]);print("applied:",d["appliedDesignNotes"]);print("unmet:",[(u["code"],u["reason"]) for u in d["unmetConstraints"]])'
```
```
status: partial
applied: ['sizing', 'layout=Layered, direction=topToBottom (auto)', 'container.bands', 'canvas.fixed+shrunk']
unmet: [('canvas.shrunk', '请求 10x10 英寸，为容纳内容收缩为 4.85x5.63 英寸')]
```
实际页面与合规性：
```bash
$T inspect --input $W/P0-2.vsdx --json | python3 -c 'import json,sys;print("actual page (inches):",json.load(sys.stdin)["pages"][0]["size"])'
$T check-layout --input $W/P0-2.vsdx --json | python3 -c 'import json,sys;d=json.load(sys.stdin);print("hardConstraintsPassed:",d["hardConstraintsPassed"],"| node.out-of-page:",d["byCode"].get("node.out-of-page"))'
```
```
actual page (inches): {'width': 1.9094488188976377, 'height': 2.2145669291338583, 'unit': 'inches'}
hardConstraintsPassed: False | node.out-of-page: 4
```
对照组（同一物理尺寸改用 inches 表达）行为正确：
```bash
echo '{"canvas":{"mode":"fixed","width":3.937,"height":3.937,"unit":"inches","shrinkToFit":true}}' > $W/c-in.json
$T execute --ir .review-rv/ir4.json --spec $W/c-in.json --output $W/P0-2b.vsdx --json | python3 -c '...'
# status: succeeded | unmet: [('canvas.exceeded', '请求 3.94x3.94 英寸，内容超出，实际为 4.85x5.63 英寸')]
# actual page: 4.85 x 5.625
```

**判断**：`DesignSpec.cs:534-556` 把 `w/h` 以**调用方单位**写进 `page.Size`，
`FitPage` 按英寸语义把 `page.Size` 改成**英寸值**（4.85×5.63），但**没有把 `page.Size.Unit`
改回 `inches`**。写者 `OfficeImoDocumentWriter.cs:52-60` 再用 `UnitFactor(pageSize.Unit)`
乘一次——于是 4.85 in 被当成 4.85 cm 写出，得到 1.909 in。这是**双重单位换算**，
只在 `unit≠inches` 且 `FitPage` 实际改过尺寸时触发（`shrinkToFit=true` 且内容与请求尺寸不符）。

三重违反：
1. 产物坏（全部内容出界）；`quality.hardConstraintsPassed=false` 却没进 `unmetConstraints`。
2. `unmetConstraints` 的 reason 描述的是**中间状态**而非最终产物（说 4.85×5.63，实得 1.909×2.215）。
3. `applied` 写 `canvas.fixed+shrunk`（"收缩过"），实际是**多缩了 2.54 倍**。

`mm` 同样触发，且更夸张：
```bash
echo '{"canvas":{"mode":"fixed","width":254,"height":254,"unit":"mm","shrinkToFit":true}}' > $W/mmgrow.json
# page(in) = {'width': 0.19094..., 'height': 0.22145...}   ← 应为 4.85 x 5.63
```

---

## P1

### P1-1 容器几何修改被完全忽略，且 `changes` 为空、无 `unmet`、无 warning

**现象**：把两个容器的 bounds 都改成 `{x:1,y:1,w:7,h:3}`，`update` 返回
`ok: true`、`changes: []`，产物中容器 bounds 一字未变。

**复现**：
```bash
$T update --input $W/G0.vsdx --model $W/G-big.json --output $W/G1.vsdx --json \
  | python3 -c 'import json,sys;d=json.load(sys.stdin);print("ok:",d["ok"],"| changes:",d["changes"],"| nodes:",d["nodes"])'
$T export-model --input $W/G1.vsdx --output $W/G1.json >/dev/null
python3 -c 'import json;print("after:",[(g["id"],g["bounds"]) for g in json.load(open("'$W'/G1.json"))["pages"][0]["groups"]])'
```
```
ok: True | changes: [] | nodes: {'added': 0, 'modified': 0, 'deleted': 0, 'preserved': 5}
before: [('c2', {'x': 6.225, 'y': 2.095, 'w': 1.9, 'h': 3.08}), ('c1', {'x': 4.15, 'y': 2.095, 'w': 1.9, 'h': 3.08})]
after : [('c2', {'x': 6.225, 'y': 2.095, 'w': 1.9, 'h': 3.08}), ('c1', {'x': 4.15, 'y': 2.095, 'w': 1.9, 'h': 3.08})]
```

**判断**：`ApplyGroups`（`OfficeImoIncrementalEditor.cs:610-650`）只做三件事：
改容器标题文字、重设成员关系、`AddToContainer(...)`。它**从不读写 `group.Bounds`**，
所以容器的位置/尺寸在增量路径上既不可改、也不报警。
`docs/limitations.md:67-70` 明确声称"仅支持修改：…分组…"，`docs/api.md` 同样承诺
分组可改——这是**声称支持但实际忽略**，且属于铁律禁止的"接受 → 忽略 → 不报告"。
调用方唯一能看到的信号是 `changes: []`，但那与"确实无需修改"无法区分。

### P1-2 `--delete-missing` 删除带连线的节点/删除连线时整体失败，且两条删除路径报告不一致

**现象**：删除"节点 b + 它的一条连线"或"只删一条连线"，`update --delete-missing`
直接以 `ok:false` + `code:"unhandled"` 失败；而删除一个孤立节点却成功，
并且 `changes` 里多报了一条 `node.deleted target:"11"`（该形状实际未被删，只是重排）。

**复现**：
```bash
$T update --input $W/r13.vsdx --model $W/del-ne.json --output $W/r13a.vsdx --delete-missing --json; echo "exit=$?"
```
```
{
  "ok": false,
  "error": {
    "code": "unhandled",
    "message": "InvalidOperationException: 拒绝输出：本次修改无法保证保真，可能损坏原有图形。形状 '8' 的几何行数 2 → 0。请改用只读方式检查，或报告该限制。"
  }
}
exit=1
```
删孤立节点的成功路径：
```bash
$T update --input $W/r14.vsdx --model $W/wo-iso.json --output $W/r14b.vsdx --delete-missing --json
# ok: True, nodes: {'deleted': 2, ...}
# changes: [{"kind":"node.deleted","target":"iso",...},
#           {"kind":"node.deleted","target":"11","before":"text='Incremental Source' ..."}]
```
产物核对：`r14` 里有 `id=10 Rectangle Iso` + `id=11 TextBlock Incremental Source`；
`r14b` 里两者都消失，但文本块以 `id=9` 重新出现（仍存在！）。
```bash
# r14  : 1,2,3,4,5,6,7,8,10(Rectangle Iso),11(TextBlock 'Incremental Source')
# r14b : 9(TextBlock 'Incremental Source'),1,...,8
```

**判断**：三处问题叠加。
1. **可删除性不一致**：同样"删一个节点"，孤立节点能删、带连线的节点删不掉，
   错误分类是 `unhandled`（顶层兜底）而不是 `update.*` 语义码——调用方无法据此做决策。
   而 `docs/limitations.md:70` 只说"`--delete-missing` 会删除模型中不存在的对象"，
   没有说明"删除会连带失败"。
2. **误报删除**：文本块被报成 `node.deleted`（类型也错：它不是 node），
   但它在输出里以新 ID 存活。报告的 `target:"11"` 与产物对不上。
3. **ID 漂移**：文本块每次写回都会旧 ID 移除、新 ID（`max+1` 或回收）重建
   （`VisioTextBlockBridge.cs:85-100` 先删所有 `NameU="TextBlock"` 再重加）。
   任何按 `visioShapeId` 建立的引用都会在下一次 update 后失效。

### P1-3 `typography.minReadableSize` 单独出现时被完全忽略，且不报告

**现象**：只给 `{"typography":{"minReadableSize":20}}`（不给 `baseSize`），
`status: succeeded`、`unmet: []`，字号仍是 12pt。

**复现**：
```bash
$T execute --ir .review-rv/ir4.json --spec $W/t-min.json --output $W/F3.vsdx --json \
  | python3 -c 'import json,sys;d=json.load(sys.stdin);print(" status:",d["status"],"| applied:",d["appliedDesignNotes"],"| unmet:",d["unmetConstraints"])'
# status: succeeded | applied: ['sizing','layout=...','container.bands','canvas.auto'] | unmet: []
rm -rf $W/zF3; mkdir $W/zF3; (cd $W/zF3 && unzip -q ../F3.vsdx)
grep -o 'N="Size" V="[^"]*"' $W/zF3/visio/pages/page1.xml | sort -u
# N="Size" V="0.166666666666667"      ← 12pt，不是请求的 20pt
```

**判断**：`DesignSpec.cs:294` 的整个排版分支以
`if (spec.Typography.BaseSize is not null || spec.Typography.FontFamily is not null)` 为门。
只设 `MinReadableSize` 时分支不进入，字号保持未缩放默认值 12pt——**低于请求的 20pt**，
而 `applied` 里连 `typography` 都没有、`unmet` 也没有条目。这正是"协议接受、Schema 允许、
执行时忽略、结果又没有报告"。
（`minReadableSize` 的语义写得很硬："Never go below this size; density must be solved by
splitting, not shrinking"——所以这不是"可选优化未触发"，而是下限被违反。）

**补充证据（同一条上的第二个洞）**：即使给了 `baseSize`，`minReadableSize` 也**管不到显式 `fontSize``**：
```bash
# textBlock 显式 fontSize:5，spec minReadableSize:20
$T execute --ir $W/tf.json --spec $W/tf-spec.json --output $W/tf.vsdx --json   # status: succeeded, unmet: []
python3 -c '...'   # fontSize: 7.999992   ← 被夹到 8pt（硬编码 MinReadablePt），不是 20pt
```
`VisioTextBlockBridge.cs:37,146-147` 用的是**硬编码常量** `MinReadablePt = 8.0`，
完全不读 spec 的 `minReadableSize`。即"可读下限"存在两套互不相干的值。

### P1-4 `theme.palette` 的合法名不在文档/Schema 里，文档说它"无读取方"

**现象**：`{"theme":{"palette":"office"}}` 报"未知调色板"，可用列表是
`enterprise / midnight / muted / technical`；而源码注释与能力矩阵写的都是 `office`。

**复现**：
```bash
$T execute --ir .review-rv/ir-role.json --spec $W/c-pal.json --output $W/r3.vsdx --json | python3 -c '...'
# unmet: [{"code":"theme.palette-unknown","target":"theme.palette",
#          "reason":"未知调色板 'office'，可用：enterprise / midnight / muted / technical"}]
```
三处文档互相打架：
```
src/Vsdx.Protocol/DesignSpec.cs:129  /// <summary>Named palette: enterprise | office | technical | muted.</summary>
schemas/phase7/design-spec.schema.json  "palette": { "type": "string" }      ← 无 enum
docs/phase8/capability-matrix.md:68   | theme.palette | 📄 PO | 仅声明，无读取方 | 协议接受但静默忽略 |
```
但实际是**已实现且工作**：
```bash
$T execute --ir .review-rv/ir-role.json --spec $W/pal.json --output $W/pal.vsdx --json | grep palette
# applied: ['theme.palette=enterprise']
# fills: [('a','#70AD47'), ('b','#C55A11'), ('c','#2E75B6')]   ← 三个角色各自着色
```

**判断**：`docs/phase8/capability-matrix.md` 把 `theme.palette` 列为"协议接受但静默忽略"，
这是**错的**——它有完整读取方（`Palettes.TryGet` + 逐角色填充），并且对未知名字会显式报
`theme.palette-unknown`（正是不该"静默"的证据）。文档把自己的能力说少了，同时源码注释里
的 `office` 是个不存在的名字。按 Phase 8 自己的分类口径，这是把 `✅ IV` 误标为 `📄 PO`，
方向上与它当初批评的"过度乐观"相反，但同样会误导工作量估算。

### P1-5 `require_title` 可以被"IR 的 id 兜底"满足，页面上却没有任何标题文字

**现象**：IR 没有 `title`，硬约束 `require_title` 仍通过，因为 `Diagram.Name` 兜底成了 `ir.Id`。

**复现**：
```bash
$T execute --ir $W/ir-notitle.json --spec $W/c-title.json --output $W/r2.vsdx --json \
  | python3 -c 'import json,sys;d=json.load(sys.stdin);print(" status:",d["status"],"unmet:",d["unmetConstraints"])'
# status: partial | unmet: []
$T inspect --input $W/r2.vsdx --json | python3 -c '...'   # pageName: Page-1 ,无任何标题文本
```
只有把 `title` 显式设为空串才会失败：
```bash
$T execute --ir $W/ir-emptytitle.json --spec $W/c-title.json --json
# unmet: [{'code':'require_title','reason':'缺少图表标题','isHard':True}]
```

**判断**：`DiagramIrConverter.ToDiagram` 的 `Name = ir.Title ?? ir.Id`（`DiagramIr.cs:290`）
使 `require_title`（`DesignSpec.cs:707`：`!string.IsNullOrWhiteSpace(diagram.Name)`）
几乎恒真——只要 IR 有 `id`（Schema 里 `id` 是可选的但不能为空串）就通过。
调用方读到的信号是"标题约束满足"，而交付物里没有标题。**约束的可证伪性被消解了**，
这与该项目在其它地方反复强调的"约束必须能被违反"（如把 `text.overflow-*` 提为 error）
是同一类问题的反面。

---

## P2

### P2-1 `spacing` 在 `layout=manual` 下被静默忽略（而 `direction` 会被报告）

**复现**：
```bash
$T execute --ir .review-rv/ir4.json --spec $W/t-gap.json --output $W/t2.vsdx --json \
  | python3 -c 'import json,sys;d=json.load(sys.stdin);print(" applied:",d["appliedDesignNotes"]);print(" unmet:",json.dumps(d["unmetConstraints"],ensure_ascii=False))'
```
```
applied: ['sizing','layout=manual','container.bands','canvas.auto (manual: coordinates kept)']
unmet: [{"code":"direction.manual-ignored","target":"layout","reason":"manual 保留作者坐标，direction=topToBottom 未应用","isHard":false}]
```
`nodeGapX=5, bandGapY=5` 没有被应用，也没有任何对应条目。

**判断**：同一个分支里 `direction` 被显式报告为 `direction.manual-ignored`，
`containerLayouts` 也被报告为 `container_layout.manual`——**唯独 `spacing` 漏了**。
这不是"设计如此"，而是同一份"manual 保留坐标"的判断只覆盖了两个字段中的两个，
第三个被忘掉。属于一致性问题：调用方根据 `unmet` 判断"哪些设计意图没生效"时会漏掉间距。

### P2-2 `theme.roleColors` 在没有带 `role` 的节点时既不 applied 也不 unmet

**复现**：
```bash
$T execute --ir $W/ir-meta.json --spec $W/rc-norole.json --output $W/rcn2.vsdx --json \
  | python3 -c '...print([a for a in d["appliedDesignNotes"] if "theme" in a]); print([u["code"] for u in d["unmetConstraints"] if "theme" in u["code"]])'
# theme.* in applied: []
# theme.* in unmet: []
```
同一条约束在"有 role 但未覆盖"时是**会**报的，说明这不是有意设计的静默：
```bash
$T execute --ir .review-rv/ir-role.json --spec $W/rc2.json --json
# unmet: [{'code':'theme.role-uncovered','target':'client'}, {'code':'theme.role-uncovered','target':'database'}]
```

**判断**：`DesignSpec.cs:488-540` 的 `if (coloured > 0) applied.Add("theme.roleColors")`
只在至少着色一个节点时记录；一个都没着色时既不进 `applied` 也不进 `unmet`。
对"IR 根本没有 role 字段"这种情形，正确做法是报一条"roleColors 无处可用"。
现在调用方无法区分"着色成功"和"颜色声明被丢弃"。

### P2-3 `container.bands` 恒被记为 applied，即使图里零个容器

**复现**：
```bash
$T execute --ir $W/ir-meta.json --spec $W/rc-norole.json --output $W/cbz.vsdx --json \
  | python3 -c '...print("container.bands" in d["appliedDesignNotes"])'   # True
python3 -c '...'   # containers actually in file: 0
```

**判断**：`DesignSpec.cs:600` 的 `applied.Add("container.bands")` 在循环**外面**无条件执行。
这是"报告的内容与实际产物不符"的最小样本：`applied` 里有一条不存在的动作。
（同类但可接受：`sizing`/`canvas.auto` 总是有实际效果。）

### P2-4 `IrContainer.Order` 被接受但从不影响泳道行序

**复现**（强制 swimlane 后交换两个容器的 `order`）：
```bash
for f in o1 o2; do $T execute --ir $W/$f.json --spec $W/swim.json --output $W/$f.vsdx --json >/dev/null
  $T export-model --input $W/$f.vsdx --output $W/$f-out.json >/dev/null
  python3 -c '...print([(g["id"],round(g["bounds"]["y"],2)) for g in p["groups"]])'; done
# o1 container Y: [('c2', 1.17), ('c1', 3.25)]
# o2 container Y: [('c2', 1.17), ('c1', 3.25)]     ← 与 o1 完全相同
```

**判断**：`IrContainer.Order` 的文档注释是"Optional ordering hint for stacked bands or lanes
(lower = higher on the page)"，`ToDiagram` 也忠实把它转进 `Group.Order`，但
`SwimlaneLayoutEngine` 不用它（只有 `FlowAndTreeLayoutEngine.cs:110` 用 `g.Order`）。
Schema 允许、协议接受、执行忽略、不报告。

### P2-5 `IrMetadata` 整体被丢弃，不报告

**复现**：
```bash
$T execute --ir $W/ir-meta.json --output $W/F8.vsdx --json | python3 -c '...'
# status: partial | unmet: [] | warnings: ['layout.page.underused']
rm -rf $W/zF8; mkdir $W/zF8; (cd $W/zF8 && unzip -q ../F8.vsdx)
grep -rl "TEAM-ZZZ\|XYZZY" $W/zF8/ | wc -l
# 0
```
IR 里的 `metadata.description / tags.owner` 在包内**零出现**，且 `unmet`/`warnings` 都不提。

**判断**：`DiagramIr.Metadata` 是 Schema 里的合法字段（`description/source/created/tags`），
`DiagramIrConverter.ToDiagram` 根本没读它。按铁律应至少报一条
`input.not-executed`（Phase 8 计划里自己写的码名），实际一条也没有。

### P2-6 Schema 与实现漂移：`canvas.mode=paper`、`layout=swimlane` 能用但不在 enum 里

**复现**：
```bash
python3 -c 'import json;s=json.load(open("schemas/phase7/design-spec.schema.json"));print(s["properties"]["canvas"]["properties"]["mode"]["enum"])'
# ['auto', 'fixed']
python3 -c '...print(s["properties"]["layout"]["enum"])'
# ['auto', 'layered', 'flow', 'tree', 'grid', 'manual']
$T execute --ir $W/ir-tbal.json --spec $W/p2.json --output $W/F6.vsdx --json | grep paper
# applied: ['canvas.paper=A5']       ← 成功
$T execute --ir $W/ir-swim.json --spec $W/swim.json --output $W/F7.vsdx --json
# applied: ['layout=Swimlane, direction=topToBottom (auto)']   ← 成功
```
而 `docs/limitations.md:183-188` 明确要求用户写 `{"canvas": {"mode": "paper"}}`。

**判断**：两个已实现、已文档化的取值会被 Schema 校验器判为非法（`additionalProperties:false`
+ enum 不含）。任何"先按 Schema 校验再调用"的 Agent 都会在合法用法上被拒。
方向与 Feishu 的惯例相反：Schema 应滞后于实现，但这里是实现跑在 Schema 前面且没人更新。

### P2-7 `specVersion` 完全不参与版本协商（IR 的 `irVersion` 参与）

**复现**：
```bash
$T execute --ir .review-rv/ir4.json --spec .review-rv/spec-v9.json --output $W/F5.vsdx --json
# specVersion 9.9 → status: succeeded | errors: [] | unmet: []
$T execute --ir .review-rv/ir2.json --output $W/ir2.vsdx --json
# irVersion 2.0 → status: failed | errors: ['ir.version']
```
`ExecutionResult.DesignSpecVersion` 会把这个 9.9 原样回显。

**判断**：`ProtocolVersion.IsCompatible` 存在且对 IR 用得很好，但 DesignSpec 路径从不调用它。
主版本不匹配本应拒绝（Schema 的 `pattern: ^1\.[0-9]+$` 也禁止 9.9）。
结果是一个 9.9 的 spec 被当作 1.0 执行，并在结果里回显成"已使用 9.9"。

### P2-8 畸形 JSON 的种类区分很粗：所有结构性错误都归 `unhandled`

**复现**：
```bash
echo '[1,2,3]' > $W/spec-arr.json
$T execute --ir $W/ir-meta.json --spec $W/spec-arr.json --output $W/sa.vsdx --json
# {"ok": false, "error": {"code":"unhandled","message":"JsonException: The JSON value could not be
#  converted to Vsdx.Protocol.DesignSpec. Path: $ | LineNumber: 0 | BytePositionInLine: 1."}}
```
`--ir` 传数组同理。

**判断**：错误确实被拦住（不会静默产坏文件），但 `code` 是顶层兜底的 `"unhandled"`，
与真正的引擎内部异常同码，调用方无法区分"输入格式错"（可修正后重试）与"引擎 bug"（应上报）。
另外 `--spec` 不存在时抛的是 `FileNotFoundException`（码 `file.not-found`），
而 JSON 语法错是 `unhandled`——同类问题两套码。

---

## 查过但**未**发现问题的方面（附查法）

1. **悬空引用 / 空 ID / 重复 ID / 容器自环 / 父子环 / 文本块缺 box**：全部被拦。
   ```bash
   # 悬空边：{"edges":[{"id":"e1","from":"a","to":"zzz"}]}
   # → status failed, errors ['ir.edge-to-missing'] "连接 'e1' 的终点 'zzz' 不存在"
   # 空节点 id → errors ['ir.node-id']
   # 文本块无 box → errors ['ir.text-box'] "文本块 't1' 缺少位置（box）"
   ```
   `IrValidator.ValidatePage`（`ProtocolRunner.cs:222-441`）覆盖了 node/container/edge/
   text/path 的 id、重复、悬空、自环、父子环、id 跨类型碰撞——这一块做得扎实。

2. **失败时不会破坏已有产物**：`--output` 指向已存在的合法 vsdx 且本次执行失败时，
   原文件 md5 不变、仍可 `validate`：
   ```bash
   cp inc.vsdx protected.vsdx; md5sum protected.vsdx   # f1e5b7bc...
   # execute 以非法 spec 失败后
   md5sum protected.vsdx                                # f1e5b7bc...  (未变)
   # validate → valid: true
   ```
   写入是"全成功或不留文件"，`x1.vsdx` 在 `update` 失败后确实不存在。**这是正确行为**。

3. **`--render` 的报告与产物一致**：`renders[].path/bytes/width/height` 与磁盘文件、
   实际 PNG 尺寸吻合（`735x432` 对 `rr1.png`），`outputFiles` 同时含 vsdx 与 png。

4. **错误级别的 7 项确实会让 `hardConstraintsPassed=false`**：用 2×2 英寸的
   `canvas.fixed` 强制出界，`byCode` 出现 `node.out-of-page`（error），
   `quality.hardConstraintsPassed=false`、`score=0`、exit code=1。
   `no_hard_violations` 硬约束也随之进 `unmet`——这条约束是**可被违反**的，与 P1-5 的
   `require_title` 形成对比。

5. **`layout=manual` 保留作者坐标**（C23 那条声称的修复）：`anchor:{mode:"fixed",x:2,y:9}` 在
   `layout=manual` 下确实原样落在 (2,9)/(6,9)；只在无 spec 时被布局覆盖（见 P1-6 类问题）。

6. **`nodeGapX` 真的生效**（不是空转）：`0.6` → 页宽 4.9″、`b.x=3.4`；
   `4.0` → 页宽 8.3″、`b.x=6.8`。间距参数不是装饰。

7. **`unknown_constraint` / `theme.palette-unknown` / `container_layout.unknown` /
   `layout.unknown` / `direction.unknown` 全都真的会报**（用 Schema 非法的值探测）：
   ```bash
   # {"containerLayouts":{"c1":"chartreuse"}}
   # → unmet [{'code':'container_layout.unknown','target':'c1','reason':"未知容器布局 'chartreuse'，已跳过该容器"}]
   ```
   这一组是"协议接受→忽略→报告"的正面样本。

8. **5000 节点会超时，但 400 节点线性可扩展**：n=50/100/200/400 分别 559/723/1239/2288 ms，
   `status=succeeded`；n=5000 在 180 s 超时内未完成（`exit=124`，无产物）。
   这是**性能上界**而非正确性缺陷，记录备查；项目文档未对规模作承诺，故不计为缺陷。

9. **no-op update 会不会整体重排/漂移**（早期怀疑）：重建后的二进制上连跑 25 次，
   容器 bounds 全部保持 `h=1.23`，节点的 `PinX/PinY/Width/Height/FillForegnd/LineColor`
   与原件逐一相同（仅新增 `TxtPinX/TxtWidth` 等后端派生单元格，属实现细节）。
   **`VisioZOrderBridge` 仍会把文本块从文档序末尾挪到最前**
   （`['a','b','c','d','9','c2','c1','e1','e2']` → `['9','a','b','c','d',...]`），
   但它保留在 `shapeOrder` 里对应的相对位置，且渲染像素差仅 25 行（文本块与边线重叠区域），
   未造成可见的遮挡变化——记为**观察项**而非缺陷。

---

## 附：判据说明（为什么这些算"问题"而非"取舍"）

- 项目铁律是"不允许：协议接受、Schema 允许、执行时忽略、结果又没有报告"。
  P0-1/P1-1/P1-3/P1-4/P2-1/P2-2/P2-4/P2-5 全部逐字命中该模式，其中 P0-1/P1-1 还附带
  **静默的产物变化**（不是"没做"，而是"做了别的"）。
- P0-2 是唯一一条**产物坏到不可交付**（全部节点出界）却仍以 `partial` 收场、
  且 `unmet` 描述与实际不符的问题——它同时踩中"报告与产物不符"。
- P1-2 的 `unhandled` 码是"承诺支撑但路径不通"：文档承诺可删，命令却有两条路径必失败。
- P2-6 的方向与常规相反（实现领先 Schema），对"Agent 先校验后调用"的用例是硬阻塞。
