# Phase 8.2 回归基线（含命令、退出码与结果）

**任务书 §5.5 明确要求："报告实际执行的命令、退出码和结果。"**
本文件记录基线状态，供实施后逐项对比。

**记录日期：** 2026-11-18
**基线提交：** `3288d98`

---

## 1. §5.5 六条要求逐条对照

| # | 要求 | 基线状态 | 证据 |
| --- | --- | --- | --- |
| 1 | 所有原有测试通过 | ✅ 373 / 373 | §2.2 |
| 2 | 原有 18 个基准硬约束全部通过 | ✅ 18 / 18 | §2.3 |
| 3 | 不允许通过降低测试要求制造通过 | ✅ 基线已固定，实施后须逐项对比 | 本文件 |
| 4 | 新增测试必须覆盖新能力和错误输入 | ✅ 现有 118 处负向断言 | §2.4 |
| 5 | 构建无新增 warning | ✅ 0 warning | §2.1 |
| 6 | **报告实际执行的命令、退出码和结果** | ✅ **本文件按此格式记录** | §2 |

> **第 6 条此前被遗漏。** 我在多轮报告中贴了命令与输出，但**从未记录退出码**。
> 命令跑成功与退出码为 0 不是同一件事——尤其 `check-layout` 在硬约束失败时
> 仍返回 0，只看输出会误判。

---

## 2. 基线执行记录

### 2.1 构建

```console
$ dotnet build VsdxEngine.sln -c Release --no-incremental
exit: 0
warnings: 0
```

### 2.2 测试

```console
$ dotnet test VsdxEngine.sln -c Release
exit: 0
失败: 0    通过: 373
```

### 2.3 基准（18 例）

```console
$ for each <family>/<case>:
    vsdx-tool create --input benchmarks/... --output b.vsdx \
      --layout auto --fit-text --fit-page --recenter
    vsdx-tool check-layout --input b.vsdx --json
passed: 18 / 18
all hard constraints pass
```

**注：** `create` 与 `check-layout` 的退出码均需为 0，且 `hardConstraintsPassed` 为 true，
两者同时满足才计入通过。

### 2.4 负向测试覆盖

```console
$ grep -rhcE "Assert\.(Throws|Contains)" tests/Vsdx.Tests/*.cs | paste -sd+ | bc
118
```

覆盖类型包括：IR 校验失败（重复 ID、悬空引用、容器环）、
VSDX 保真守卫拒绝、未知枚举、非法 palette、路径几何非法等。

---

## 3. 实施后的对比方式

每个子任务完成后，重跑 §2 三项并记录：

```console
$ dotnet build VsdxEngine.sln -c Release --no-incremental
exit: ?   warnings: ?
$ dotnet test VsdxEngine.sln -c Release
exit: ?   失败: ?  通过: ?
$ 基准 18 例
passed: ? / 18
```

**任何一项退化即回退该子任务**，不得以"其它项通过"为由放行。

---

## 4. 为什么要把退出码单独记下来

这一条不是形式主义。**先实测再写**（我最初凭印象写了错误的值）：

```console
$ execute  --spec '{"canvas":{"mode":"fixed","width":30,"height":20}}'   # 硬约束失败
exit: 1

$ execute  --ir arch.ir.json                                            # 成功
exit: 0

$ check-layout --input <硬约束失败的图>
exit: 1

$ check-layout --input <通过的图>
exit: 0
```

| 命令 | 结果 | 退出码 |
| --- | --- | --- |
| `execute` 成功 | `status=succeeded` | **0** |
| `execute` 部分失败 | `status=partial`，**仍有产物** | **1** |
| `check-layout` 通过 | 硬约束全过 | **0** |
| `check-layout` 失败 | 有 error 级问题 | **1** |

**关键区别：`execute` 在 `partial` 时退出码为 1，但产物已经写盘。**
所以"退出码非零"不等于"没有文件"，"文件存在"也不等于"可交付"。
这三件事混用，正是此前多轮误判的来源之一。

> **本节勘误：** 初稿我写「`check-layout` 硬约束失败时返回 0」——**是错的**，实测为 1。
> 凭印象写退出码，恰好违反了本节自己主张的"实测后再写"。

---

## 5. 测试数量不作验收指标（任务书 §5.5 末段）

任务书明确：**"373 是当前报告中的测试基线，不应把测试数量本身当作验收指标。"**

因此本文件只记录数量作为**回归基线**（用于发现意外减少），
不作为"能力提升"的证据。新增测试的数量由覆盖需要决定，不由指标倒推。
