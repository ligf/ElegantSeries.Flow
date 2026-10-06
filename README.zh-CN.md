# ElegantSeries.Flow

[![NuGet](https://img.shields.io/nuget/v/ElegantSeries.Flow.svg)](https://www.nuget.org/packages/ElegantSeries.Flow)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)

[English](README.md) | 中文

轻量、**AOT 友好**的 .NET MVVM 导航库。

ElegantSeries.Flow 提供基于 region 的导航栈、可选的 KeepAlive ViewModel 缓存、
强类型导航参数、导航守卫和 ViewModel 生命周期回调——不依赖任何 UI 框架。
目标框架为 `net10.0`，兼容裁剪/AOT，线程安全。

## 功能特性

- **基于 Region 的导航**——每个 region 拥有独立的导航栈（默认 `"MainRegion"`）
- **导航模式**——`New`、`Replace`、`KeepAlive`、`ClearStack`
- **KeepAlive 缓存**——按需复用 ViewModel，跨导航保持状态
- **强类型参数**——`NavigateToAsync<TViewModel, TParam>(param)`，编译期类型安全
- **生命周期回调**——`INavigationAware`（`OnNavigatedTo` / `OnNavigatedFrom`），
  需要 `await` 时用异步版 `INavigationAwareAsync`
- **导航守卫**——`INavigationGuard.CanNavigateFromAsync()` 可取消导航，
  需要上下文时用 `INavigationGuardWithContext`
- **取消支持**——导航调用接受 `CancellationToken`，栈更新前均可取消
- **刷新**——用新参数重新触发当前页面的回调（`refreshIfActive`）
- **AOT / 裁剪安全**——`DynamicallyAccessedMembers` 注解，无运行时反射
- **线程安全**——过渡串行化，所有共享状态都有锁保护
- **尽力释放**——单个 `Dispose` 抛异常也不会泄漏其余 ViewModel
  （异常会被收集后重抛：单个原样抛出，多个包成 `AggregateException`）

## 安装

```bash
dotnet add package ElegantSeries.Flow
```

需要 **.NET 10** 或更高版本。

## 快速上手

### 1. 注册服务

```csharp
using ElegantSeries.Flow.Core.Extensions;

services.AddSingletonFlowNavigation();   // 单例——单窗口应用
// 或
services.AddScopedFlowNavigation();      // 作用域——每个窗口独立导航栈
```

**单例还是作用域？** *region* 是一个命名的导航槽——通常对应 UI 里的一个
`NavigationHost` 控件——每个 region 拥有独立的页面栈（见[Region](#region)）。
这个选择只决定这些栈的*作用域*：

- `AddSingletonFlowNavigation()` 注册一个应用级 `INavigationService`。所有窗口
  共享同一套 region 栈：两个窗口各放一个 `"MainRegion"` 会互相干扰。
- `AddScopedFlowNavigation()` 让每个 DI 作用域（通常一个窗口一个）拥有独立的
  `INavigationService`，region 栈完全隔离。

两个方法都用 `TryAdd`，所以同时调用也只会注册第一个——一个容器里恰好只有
一个 `INavigationService`。ViewModel 的生命周期与此选择无关（见
[页面级服务作用域](#页面级服务作用域)）。

**混合配置**——一个全局栈加独立窗口：为共享栈注册单例，每个窗口手动从自己的
作用域构造服务（不注册进 DI）。作用域的存活期必须覆盖窗口——窗口关闭时再释放，
不能提前：

```csharp
services.AddSingletonFlowNavigation(); // 全局栈

// 每个窗口的独立栈：
var windowScope = rootProvider.CreateScope(); // 窗口关闭时释放
var windowNavigation = new NavigationService(windowScope.ServiceProvider);
```

### 2. 写 ViewModel

```csharp
using ElegantSeries.Flow.Mvvm;
using ElegantSeries.Flow.Core.Navigation;

public partial class HomeViewModel : BaseViewModel, INavigationAware
{
    public void OnNavigatedTo(object? parameter) { /* 页面出现 */ }
    public void OnNavigatedFrom() { /* 页面离开 */ }

    public Task GoToDetailAsync()
        => NavigateToAsync<DetailViewModel, string>("hello", mode: NavigationMode.New);
}
```

`BaseViewModel.Navigation` 会在 ViewModel 成为活动页面时自动挂载、离开时清空，
所以 `protected NavigateToAsync` / `GoBackAsync` 辅助方法可以直接在 ViewModel 里调用。

`BaseViewModel` 在可选的 `ElegantSeries.Flow.Mvvm` 包里，基于 CommunityToolkit.Mvvm。
如果不想依赖 toolkit，改从核心包的 `NavigationViewModelBase` 派生即可——同样的
导航 plumbing，只是换成手写的 `INotifyPropertyChanged` 实现。

ViewModel 按常规注册进 DI（通常用 transient）：

```csharp
services.AddTransient<HomeViewModel>();
services.AddTransient<DetailViewModel>();
```

### 3. 导航

```csharp
var navigation = serviceProvider.GetRequiredService<INavigationService>();

await navigation.NavigateToAsync<HomeViewModel>();                              // 推入新页面
await navigation.NavigateToAsync<DetailViewModel, string>("hello");             // 带强类型参数
await navigation.NavigateToAsync<DetailViewModel>("Sidebar", NavigationMode.Replace);
await navigation.GoBackAsync();                                                // 弹出

bool canGoBack = navigation.CanGoBack();
var current = navigation.GetCurrentViewModel();
```

### 4. 渲染活动 ViewModel

ElegantSeries.Flow 与 UI 无关：它管理 ViewModel，平台层负责把活动 ViewModel
映射成 View。典型做法是一个 `ContentControl` 风格的 host 绑定
`GetCurrentViewModel()`，并在 `RegionNavigated` 事件里刷新。AOT 安全的
ViewModel→View 映射方式：在 `services.AddFlowViews(...)` 里用 `views.Register` /
`views.RegisterTransient` / `views.RegisterSingleton` 显式注册——无运行时反射，
无命名约定。也可以在 View 类上放 `[ViewFor]` attribute
（`ElegantSeries.Flow.Core.Routing`）声明它的 ViewModel，让 ElegantSeries.Flow
source generator 在编译期生成等价的注册代码，生成一个 `RegisterAttributedViews()`
扩展方法（在 `AddFlowViews` 里调用）；attribute 的 `Lifetime` 可选 `Transient`
（默认）、`Singleton` 或 `ViewOnly`（只生成映射，ViewModel 的 DI 注册自己管）。
运行时从不扫描这个 attribute。每个 ViewModel 二选一：手动注册或 attribute 生成，
不要两边都做——重复注册会在启动时抛 `InvalidOperationException`。

## 包

| 包 | 内容 |
|---------|----------|
| `ElegantSeries.Flow` | 核心：导航服务、region、页面 DI 作用域、KeepAlive 缓存、守卫、生命周期——无 MVVM toolkit 依赖 |
| `ElegantSeries.Flow.Mvvm` | 可选的 CommunityToolkit.Mvvm 集成：`BaseViewModel`（`ElegantSeries.Flow.Mvvm`） |
| `ElegantSeries.Flow.WPF` | WPF host：`NavigationHost`、`BaseView<TViewModel>`、`ViewLocator` |
| `ElegantSeries.Flow.Avalonia` | Avalonia host：`NavigationHost`、`BaseView<TViewModel>`、`ViewLocator` |
| `ElegantSeries.Flow.Generator` | Roslyn source generator：从 `[ViewFor]` attribute 生成 `IViewLocator` 注册代码（纯 Analyzer，无运行时依赖） |

## UI 集成

为桌面 UI 框架准备的现成 host——API 表面一致，按平台分命名空间：

| 包 | 目标框架 | Host 控件 | View 基类 |
|---------|---------|--------------|-----------------|
| `ElegantSeries.Flow.WPF` | `net10.0-windows` | `NavigationHost`（`ElegantSeries.Flow.WPF.Hosting`） | `BaseView<TViewModel>`（`ElegantSeries.Flow.WPF.Views`） |
| `ElegantSeries.Flow.Avalonia` | `net10.0` | `NavigationHost`（`ElegantSeries.Flow.Avalonia.Hosting`） | `BaseView<TViewModel>`（`ElegantSeries.Flow.Avalonia.Views`） |

```bash
dotnet add package ElegantSeries.Flow.WPF        # WPF 应用
dotnet add package ElegantSeries.Flow.Avalonia   # Avalonia 应用
```

```csharp
// 1. 注册 view（AOT 安全：无运行时反射）
services.AddFlowViews(locator =>
{
    locator.RegisterTransient<HomeView, HomeViewModel>();   // view 映射 + AddTransient
    locator.RegisterTransient<DetailView, DetailViewModel>();
    // 或者只做映射，ViewModel 的 DI 注册自己管：
    // locator.Register<HomeView, HomeViewModel>();
});

// 2. 挂载 host（通常在窗口的 code-behind）
navigationHost.Attach(navigationService);
```

```xml
<!-- 3. 在 XAML 里放 host（以 WPF 为例；Avalonia 用 ...Avalonia.Hosting 命名空间） -->
<Window xmlns:flow="clr-namespace:ElegantSeries.Flow.WPF.Hosting;assembly=ElegantSeries.Flow.WPF">
    <flow:NavigationHost x:Name="navigationHost" RegionName="MainRegion" />
</Window>
```

两个 host 都监听 `RegionNavigated`，通过注册好的 `IViewLocator`
（`ElegantSeries.Flow.WPF.Locating` / `ElegantSeries.Flow.Avalonia.Locating`）
解析 View，经 `IDispatcher`（`...Threading`）封送到 UI 线程，再设为内容。
ViewModel 必须实现 `INavigationViewModel`（或从 `BaseViewModel` /
`NavigationViewModelBase` 派生）。

完整用法见 [ElegantSeries.Flow.WPF](src/ElegantSeries.Flow.WPF/README.md) 和
[ElegantSeries.Flow.Avalonia](src/ElegantSeries.Flow.Avalonia/README.md)，
可运行的 WPF/Avalonia 演示见 [samples](samples/)（多 region 布局、强类型参数、
KeepAlive、单例 ViewModel）。

## 导航模式

| 模式 | 行为 |
|------|----------|
| `New` | 把新页面推入 region 栈。上一页被停用。 |
| `Replace` | 替换当前页面。除非是 `KeepAlive`，旧页面会被释放。 |
| `KeepAlive` | 复用该 region 里同类型的缓存 ViewModel，而不是新建。 |
| `ClearStack` | 清空整个栈，用新页面重新开始。 |

## Region

*region* 是一个命名的导航槽——通常就是一个 `NavigationHost` 控件——
每个 region 拥有独立的页面栈。一个 region 里导航永远不影响另一个，
所以窗口的不同部分可以各自独立导航：

```xml
<!-- Sidebar region：菜单页 -->
<flow:NavigationHost RegionName="Sidebar" />
<!-- Main region：详情页 -->
<flow:NavigationHost RegionName="MainRegion" />
```

```csharp
await navigation.NavigateToAsync<MenuViewModel>("Sidebar");
await navigation.NavigateToAsync<DetailViewModel>("MainRegion");
await navigation.GoBackAsync("Sidebar");   // 只弹出 sidebar
```

默认 region 名是 `"MainRegion"`，单 region 应用可以到处省略参数。
一个 ViewModel *实例*同一时间只能在一个 region 里活动——导航一个已在
*另一个* region 栈里的实例会抛 `InvalidOperationException`。

## 生命周期

```csharp
public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);  // 用 INavigationAware<T> 实现强类型
    void OnNavigatedFrom();
}

public interface INavigationAwareAsync   // 需要 await 时用这个，代替 INavigationAware
{
    Task OnNavigatedToAsync(object? parameter, CancellationToken cancellationToken = default);
    Task OnNavigatedFromAsync(CancellationToken cancellationToken = default);
}

public interface INavigationGuard
{
    Task<bool> CanNavigateFromAsync();      // 返回 false 取消导航
}

public interface INavigationGuardWithContext  // 决策需要上下文时用这个，代替 INavigationGuard
{
    Task<bool> CanNavigateFromAsync(NavigationGuardContext context);
    // context: RegionName, TargetViewModelType, Mode（返回时为 null）, Parameter, IsBack
}
```

生命周期回调和页面作用域的释放永远在**内部锁之外**执行，所以可以安全地
回调 `INavigationService` 而不会死锁。固定的过渡顺序是：
`OnNavigatedFrom` → 页面作用域释放 → `OnNavigatedTo` → 事件（`RegionNavigated`）。
UI 层应在 `RegionNavigated` 里换 view；`OnNavigatedTo` 执行时 view 还不保证
已经挂载。

回调里抛异常不会中断过渡：异常会被收集起来在事后重抛（单个原样抛出，
多个包成 `AggregateException`）。传给 `INavigationAwareAsync` 回调的 token
永远是 `CancellationToken.None`——栈一旦更新，过渡就会跑完。

### 取消

`NavigateToAsync` / `GoBackAsync` 接受 `CancellationToken`，在 **region 栈更新
之前**都有效：取消会释放已创建的页面作用域（如果有）并抛
`OperationCanceledException`。栈更新之后过渡会跑完，token 被忽略。

### 刷新活动页面

导航到已活动的类型是静默 no-op，返回 `true`。传 `refreshIfActive: true`
可把它变成*刷新*：不推新页面、不建新作用域——只是用新参数重新跑一遍
活动实例的激活回调，并触发 `RegionNavigated`。当前页面的 from-guard 照常
执行，仍可否决刷新。

### 页面级服务作用域

每个页面拥有独立的 `IServiceScope`。ViewModel 从这个作用域解析，页面离开
时作用域被释放——DI 容器随即释放 ViewModel 及其整个 Transient/Scoped 依赖图。
导航服务从不直接释放 ViewModel。

ViewModel 自身的 DI 生命周期——即你的 `services.AddXxx<MyViewModel>()` 调用，
不是导航服务的注册方式——与页面作用域的交互：

| ViewModel 生命周期 | 导航行为 | 页面离开时 |
|---|---|---|
| `Transient` | 每个页面作用域一个新实例 | 作用域释放 ViewModel 及其依赖图 |
| `Scoped` | 每个页面作用域一个实例 | 作用域释放它 |
| `Singleton` | 从根容器共享 | 页面作用域永远不释放它 |

一个 ViewModel *实例*永远不会在同一个 region 栈里出现两次：导航到已活动的
类型是返回 `true` 的 no-op（或加 `refreshIfActive` 变成*刷新*，用新参数重新
触发活动实例的回调）；导航到实例在栈深处的类型会*弹回它*——上面的页面被丢弃，
已有的实例被重新激活。单例 ViewModel 和缓存的 KeepAlive 页面因此能在离开后
存活、返回时复用，和主流框架一致（参考 Prism 的 `IsNavigationTarget`）。只有
实例在*另一个* region 栈里时才抛 `InvalidOperationException`（一个 ViewModel
不能同时在两个 region 里活动）。

`ViewModelReleased` 表示服务**释放了所有权**（页面离开导航状态、作用域正在释放），
不是"每个 disposable 都释放了"：DI 容器在遇到第一个抛异常的 disposable 后就会
停下。单例 ViewModel 在页面拆除时也会触发这个事件——页面作用域被释放了，但共享
实例本身还在，因为它归根容器所有。

## KeepAlive 缓存

`NavigationMode.KeepAlive` 把页面（ViewModel + 它的页面作用域）按
`(region, ViewModel 类型)` 缓存在每个 region 里。导航到同类型时复用缓存页
及其作用域——不建新作用域。

```csharp
await navigation.NavigateToAsync<SettingsViewModel>(mode: NavigationMode.KeepAlive);

navigation.ClearCache("MainRegion");   // 或 await navigation.ClearCacheAsync("MainRegion");
navigation.ClearAllCache();            // 或 await navigation.ClearAllCacheAsync();
```

一个 `(region, 类型)` 键最多缓存一个页面。仍被导航栈引用的页面作用域**不会**
立即释放；等最后一个栈/缓存引用消失才释放。释放优先用异步路径的
`IAsyncDisposable`。同步路径用 `IDisposable.Dispose` 释放作用域——如果页面作用域
里有只实现 `IAsyncDisposable` 的服务，DI 容器会抛 `InvalidOperationException`
（页面需要异步清理时请用异步方法）。

### 释放的错误语义

拆卸是**跨页面尽力而为**的：即使其中一个抛异常，每个页面作用域都会尝试释放，
所以单个坏掉的 `Dispose` 永远泄漏不了其余页面。清理和事件都跑完后，收集到的
异常会被重抛——单个保留原始堆栈原样抛出，多个包成 `AggregateException`。

**注意：***单个*页面作用域内，DI 容器在第一个抛异常的 disposable 之后就会停下，
不再释放剩下的服务（平台行为，同步异步都一样）。导航服务只提供跨页面的隔离。

## 线程安全

所有公开成员都可以在任意线程调用。导航过渡和释放用异步锁串行化，共享状态由
专用锁保护。

## Native AOT / 裁剪

库设置 `IsAotCompatible=true`，泛型 ViewModel 参数都打了
`[DynamicallyAccessedMembers(PublicConstructors)]` 注解。ViewModel 经
`IServiceProvider` 解析——在 DI 里注册好，应用层避免用 `Activator.CreateInstance`。
`[ViewFor]` attribute（放在 View 类上，声明它的 ViewModel）由 ElegantSeries.Flow
source generator 在编译期消费，生成等价的 `IViewLocator` 注册代码；运行时从不
扫描它。注意：WPF 示例应用本身不能做 NativeAOT 发布（WPF 框架不支持 Native AOT）；
Avalonia 示例的 csproj 里开了 `PublishAot=true`，作为框架 AOT 能力的实证
（CI 里有 Native AOT 发布冒烟测试验证）。

## API 速览

| 成员 | 说明 |
|--------|-------------|
| `NavigateToAsync<T>(region?, mode?, refreshIfActive?, cancellationToken?)` | 导航到 ViewModel 类型 |
| `NavigateToAsync<T, TParam>(param, region?, mode?, refreshIfActive?, cancellationToken?)` | 带强类型参数导航 |
| `GoBackAsync(region?, cancellationToken?)` | 弹出当前页面 |
| `CanGoBack(region?)` / `GetCurrentViewModel(region?)` / `IsActive<T>(region?)` / `GetCurrentMode(region?)` | 查询 |
| `ClearCache(region?)` / `ClearCacheAsync(region?)` | 清空单个 region 的 KeepAlive 缓存 |
| `ClearAllCache()` / `ClearAllCacheAsync()` | 清空所有 KeepAlive 缓存 |
| `RegionNavigated` / `ViewModelReleased` / `RegionCacheCleared` | 事件 |
| `AddSingletonFlowNavigation()` / `AddScopedFlowNavigation()` | DI 注册 |

## 参与贡献

欢迎提 Issue 和 PR。新代码请保持 AOT/裁剪兼容（测试套件和 `IsAotCompatible`
标记会守护这一点），行为变更请加测试。

## 许可证

MIT — 见 [LICENSE.txt](LICENSE.txt)。
