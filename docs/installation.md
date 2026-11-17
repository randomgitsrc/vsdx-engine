# 安装与部署

## 1. 系统要求

| 项 | 要求 |
| --- | --- |
| .NET SDK | **8.0.428**（`global.json` 锁定；`rollForward: latestFeature` 允许 8.0.x 更高补丁） |
| 操作系统 | Linux / Windows / macOS（.NET 8 支持范围） |
| Microsoft Visio | **不需要** |
| 磁盘 | 约 500 MB（SDK + NuGet 缓存） |
| 字体 | 含中文时需要一个 CJK 字体文件（见第 4 节） |

验证：

```bash
dotnet --version      # 期望 8.0.x
```

---

## 2. 从源码构建

```bash
git clone <repo> agent-vsdx
cd agent-vsdx

dotnet restore
dotnet build -c Release
dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release
```

产物位置：

| 产物 | 路径 |
| --- | --- |
| CLI | `src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool` |
| 引擎库 | `src/Vsdx.Engine/bin/Release/net8.0/Vsdx.Engine.dll` |

---

## 3. 安装 CLI

### 方式 A：直接使用构建产物

```bash
./src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool version --json
```

加入 PATH：

```bash
export PATH="$PWD/src/Vsdx.Cli/bin/Release/net8.0:$PATH"
```

### 方式 B：安装为 .NET 全局工具

```bash
dotnet pack src/Vsdx.Cli/Vsdx.Cli.csproj -c Release -o artifacts
dotnet tool install --global --add-source ./artifacts AgentVsdx.Engine.Tool
vsdx-tool version --json
```

卸载：`dotnet tool uninstall --global AgentVsdx.Engine.Tool`

### 方式 C：在自有项目中引用

```xml
<ItemGroup>
  <ProjectReference Include="path/to/src/Vsdx.Engine/Vsdx.Engine.csproj" />
</ItemGroup>
```

```csharp
using Vsdx.Engine;
using Vsdx.Model;

var engine = new VsdxEngine();
engine.Create("model.json", "out.vsdx");
```

---

## 4. 中文字体

VSDX 内部只记录字体**名称**，因此中文文件名与内容本身不受字体影响。只有 **PNG 渲染**
需要真实字体文件——OfficeIMO 内置回退字体不含 CJK 覆盖，缺失时中文会变成 `□`。

### 检查

```bash
fc-list :lang=zh | head -5
```

### 常见系统的可用路径（引擎按顺序自动探测）

| 系统 | 路径 |
| --- | --- |
| Ubuntu/Debian | `/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc`、`/usr/local/share/fonts/NotoSansSC-VF.ttf` |
| WSL（复用 Windows 字体） | `/mnt/c/Windows/Fonts/msyh.ttc`、`simsun.ttc` |
| Windows | `C:\Windows\Fonts\msyh.ttc`、`simsun.ttc` |
| macOS | `/System/Library/Fonts/PingFang.ttc` |

### 安装（Ubuntu）

```bash
sudo apt-get install -y fonts-wqy-zenhei fonts-noto-cjk
```

### 显式指定

```bash
vsdx-tool render --input d.vsdx --format png --output d.png \
  --font-file /usr/share/fonts/truetype/wqy/wqy-zenhei.ttc
```

SVG 不需要字体文件：`font-family` 交给消费端解析。

---

## 5. 离线部署

所有依赖都是 NuGet 公开包，可完全离线运行：

```bash
# 联网机器
dotnet restore
dotnet nuget locals global-packages --list          # 得到缓存目录
# 用 --packages 指定本地缓存目录重新还原，或使用离线 feed
dotnet restore --source /path/to/local-feed

# 目标机器
dotnet restore --source /path/to/local-feed
dotnet build -c Release --no-restore
```

运行时**无任何网络调用**：不访问在线渲染服务、不下载字体、不校验许可证。

---

## 6. 容器镜像（参考）

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Vsdx.Cli/Vsdx.Cli.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
# 渲染中文所需
RUN apt-get update && apt-get install -y --no-install-recommends fonts-wqy-zenhei \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["/app/vsdx-tool"]
```

```bash
docker build -t vsdx-tool .
docker run --rm -v "$PWD:/work" vsdx-tool create \
  --input /work/examples/architecture.json --output /work/architecture.vsdx
```

---

## 7. 验证安装

```bash
vsdx-tool version --json
```

期望包含：

```json
{
  "tool": "vsdx-tool",
  "engine": "Agent VSDX Engine",
  "backend": "OfficeIMO.Visio",
  "backendVersion": "3.6.1.0",
  "modelVersion": "1.0"
}
```

端到端冒烟：

```bash
vsdx-tool create  --input examples/architecture.json --output /tmp/a.vsdx
vsdx-tool validate --input /tmp/a.vsdx
vsdx-tool render  --input /tmp/a.vsdx --format png --output /tmp/a.png
vsdx-tool check-layout --input /tmp/a.vsdx
```

四步全部返回退出码 0 即安装成功。

---

## 8. 常见安装问题

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| `dotnet: command not found` | SDK 未安装或不在 PATH | 安装 .NET 8 SDK，检查 `dotnet --info` |
| `NU1101: Unable to find package OfficeIMO.Visio` | 源不可达 / 离线未配置 feed | 配置 NuGet 源或本地 feed |
| `error NU1008: 使用中央包版本管理的项目不应定义 PackageReference 项上的版本` | 在 `Directory.Packages.props` 管理的项目里写了 `Version=` | 版本只写在 `Directory.Packages.props` |
| 构建报 `CS0234: 命名空间 OfficeIMO 中不存在 OfficeImageExportFormat` | 该类型在 `OfficeIMO.Drawing` | 使用 `OfficeIMO.Drawing.OfficeImageExportFormat` |
| 渲染中文变 `□` | 无 CJK 字体 | 见第 4 节 |
| `error NETSDK1045: 当前 .NET SDK 不支持 .NET 8.0` | SDK 太旧 | 升级到 8.0.x |

> 若使用工作区本地 SDK（本项目开发时的做法），设置
> `DOTNET_ROOT`、`PATH`、`DOTNET_CLI_HOME`、`NUGET_PACKAGES` 指向工作区内目录即可，
> 不必污染系统环境。
