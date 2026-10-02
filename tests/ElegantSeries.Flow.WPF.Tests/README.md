# ElegantSeries.Flow.WPF.Tests

## 双 TFM 测试策略

WPF 程序集在 Linux 上**连启动都做不到**（缺少 `Microsoft.WindowsDesktop.App`
运行时，进程在框架解析阶段直接退出——已实测验证）。因此测试项目采用双目标
框架：

| TFM | 内容 | Linux | Windows CI |
|---|---|---|---|
| `net10.0` | 纯逻辑测试（`Logic/`）：`ViewRegistry<TView>`、`NavigationHostController<TView>`、`IDispatcher` 的源码被直接编译进测试程序集（`Link`），用 `object` 代替视图类型，**真实运行** | ✅ 运行 | ✅ 运行 |
| `net10.0-windows` | 完整 WPF 测试（`Windows/`）：`ViewLocator`、`BaseView`、`NavigationHost`、`WpfDispatcher`、DI 扩展 | 🔨 仅编译验证（`EnableWindowsTargeting`） | ✅ 运行 |

关键设计点：

- `NavigationHostController<TView>` 与视图类型解耦（泛型），region 过滤、
  dispatcher 封送、CWT 视图缓存、失败处理等核心逻辑在 Linux 上就能跑测试。
- `ViewRegistry<TView>` 同理：注册/查表的全部逻辑与 WPF 无关。
- Windows 测试里凡是实例化 WPF 控件的一律走 `StaHelper`（xUnit 跑在 MTA 线程池线程上）。

## 本地运行

```bash
# Linux：只跑 net10.0 逻辑测试
~/.dotnet/dotnet test ElegantSeries.Flow.WPF.Tests.csproj -f net10.0

# Windows：跑全部
dotnet test ElegantSeries.Flow.WPF.Tests.csproj
```

注意：本机 `dotnet test`（Microsoft.Testing.Platform）在该 Linux 环境曾出现
"Zero tests ran"（core 项目同样），可改用直接运行测试 dll：

```bash
~/.dotnet/dotnet bin/Debug/net10.0/ElegantSeries.Flow.WPF.Tests.dll
```
