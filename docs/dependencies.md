# 依赖清单与许可证

所有依赖版本在 `Directory.Packages.props` 中集中锁定，`Directory.Build.props` 打开
`ManagePackageVersionsCentrally`，因此任何项目文件都不允许内联 `Version=`。

---

## 1. 运行时依赖

| 依赖 | 版本 | 用途 | 许可证 |
| --- | --- | --- | --- |
| .NET SDK | **8.0.428** | 构建与运行（`global.json` 锁定） | MIT |
| .NET Runtime | 8.0.x | 运行 | MIT |
| **OfficeIMO.Visio** | **3.6.1** | VSDX 读写、Shape/Connector/Container、SVG/PNG 渲染 | **MIT** |
| OfficeIMO.Core | 3.6.1 | `OfficeLength` / `OfficeColor` / `OfficePoint` 等基础类型 | MIT |
| System.IO.Packaging | 10.0.8 | OPC 包操作（OfficeIMO 传递依赖） | MIT |

OfficeIMO.Visio 3.6.1 对应的源码 commit：
`923a2f612e7f24f6893d97b0798c377165603e18`（来自 nuspec `repository` 字段）。

### 为什么锁定 3.6.1

- `net8.0` 目标框架可用（同时提供 `netstandard2.0` / `net472` / `net10.0`）
- 包含 `Inspect()` 稳定快照、`VsdxPackageValidator`、CJK 字体注入所需 API
- 该版本有 3 处需适配层补偿的缺陷（见 `docs/technical-validation.md`），已修复并有测试
- 新版本可能改变这些行为，升级前必须重跑 `tests/Vsdx.Tests`

---

## 2. 测试依赖

| 依赖 | 版本 | 用途 | 许可证 |
| --- | --- | --- | --- |
| Microsoft.NET.Test.Sdk | 17.11.1 | 测试宿主 | MIT |
| xunit | 2.9.2 | 测试框架 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | VS / `dotnet test` 适配 | Apache-2.0 |

---

## 3. 系统依赖

| 项 | 是否必需 | 说明 |
| --- | --- | --- |
| Microsoft Visio | **否** | 项目核心目标即消除此依赖 |
| Microsoft Office | **否** | 未使用 |
| 中文字体文件 | 渲染中文 PNG 时需要 | 见 `docs/installation.md` 第 4 节 |
| 系统字体目录 | 可选 | `FontResolver` 会探测常见路径 |
| 图形库 / 浏览器 | **否** | 渲染完全托管实现，无外部进程 |

---

## 4. 字体依赖

VSDX 内部只存字体**名称**，所以写文件不需要字体。渲染 PNG 需要真实轮廓：

| 字体 | 常见来源 | 许可证 |
| --- | --- | --- |
| Noto Sans SC / Noto Sans CJK | `fonts-noto-cjk` 包 | SIL Open Font License 1.1 |
| WenQuanYi Zen Hei | `fonts-wqy-zenhei` 包 | GPL v2 + 字体嵌入例外 |
| Microsoft YaHei / SimSun | Windows 系统字体（WSL 经 `/mnt/c` 访问） | Microsoft 专有，仅本机使用 |
| PingFang SC | macOS 系统字体 | Apple 专有，仅本机使用 |
| DejaVu Sans | `fonts-dejavu` 包 | Bitstream Vera + Arev（宽松） |

> 引擎**不打包也不分发**任何字体文件，只在运行时按路径引用已安装在系统中的字体。
> 在容器或服务器部署中请自行安装符合许可的字体（推荐 Noto 系）。

---

## 5. 许可证汇总

| 许可证 | 依赖 |
| --- | --- |
| MIT | .NET、OfficeIMO.Visio、OfficeIMO.Core、System.IO.Packaging、本项目 |
| Apache-2.0 | xunit |
| SIL OFL 1.1 | Noto 字体（若安装） |
| GPL v2 + 例外 | WenQuanYi（若安装） |

本项目自身：**MIT**。

本项目不依赖任何付费 API 或在线服务。所有功能在离线环境可完整运行。

---

## 6. 升级检查清单

升级 OfficeIMO.Visio 时必须复核以下补偿措施是否仍然必要：

```bash
# 1. 验证器误报是否修复（若已修复，可移除自研验证器的重复检查）
vsdx-tool create --input examples/architecture.json --output /tmp/a.vsdx
# 检查 VsdxPackageValidator.ValidateFile 行为

# 2. PNG CJK 缺省字体是否已含中文覆盖

# 3. AddToContainer 是否仍覆盖显式 bounds

# 4. VisioConnector.From/To 是否已可写

# 5. 全量回归
dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release
```

在 `docs/technical-validation.md` 中同步更新结论。
