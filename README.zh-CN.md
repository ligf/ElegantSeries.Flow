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
- **导航操作与保留策略**——`Push` / `Replace` / `Reset`，与 `Destroy` / `KeepAlive` 保留策略正交组合
- **KeepAlive 缓存**——按需复用 ViewModel，跨导航保持状态
- **多实例认人**——同一页面类型的多个实例可以共存；前进导航按参数、或由实例本身
  （`INavigationTargetAware`）认出正确的那一个
- **强类型参数**——`NavigateToAsync<TViewModel, TParam>(param)`，编译期类型安全
- **生命周期回调**——`INavigationAware`（`OnNavigatedTo` / `OnNavigatedFrom`），
  需要 `await` 时用异步版 `INavigationAwareAsync`
- **导航守卫**——`INavigationGuard.CanNavigateFromAsync(cancellationToken)` 可取消导航，
  需要上下文时用 `INavigationGuardWithContext`
- **取消支持**——导航调用接受 `CancellationToken`，请求执行前可取消撤回
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
using ElegantSeries.Flow.Extensions;

services.AddElegantSeriesFlow();   // 单例——单窗口应用
// 或
services.AddElegantSeriesFlow(ServiceLifetime.Scoped);      // 作用域——每个窗口独立导航栈
```

**单例还是作用域？** *region* 是一个命名的导航槽——通常对应 UI 里的一个
`NavigationHost` 控件——每个 region 拥有独立的页面栈（见[Region](#region)）。
这个选择只决定这些栈的*作用域*：

- `AddElegantSeriesFlow()` 注册一个应用级 `INavigationService`。所有窗口
  共享同一套 region 栈：两个窗口各放一个 `"MainRegion"` 会互相干扰。
- `AddElegantSeriesFlow(ServiceLifetime.Scoped)` 让每个 DI 作用域（通常一个窗口一个）拥有独立的
  `INavigationService`，region 栈完全隔离。

`AddElegantSeriesFlow` 内部用 `TryAdd`，所以调两次也只会注册第一次——一个容器里恰好只有
一个 `INavigationService`。ViewModel 的生命周期与此选择无关（见
[页面级服务作用域](#页面级服务作用域)）。传入 `ServiceLifetime.Transient`
会抛 `ArgumentOutOfRangeException`：导航服务是有状态的，transient 注册会让每个
消费者拿到相互隔离的空栈。

**混合配置**——一个全局栈加独立窗口：为共享栈注册单例，每个窗口手动从自己的
作用域构造服务（不注册进 DI）。作用域的存活期必须覆盖窗口——窗口关闭时再释放，
不能提前：

```csharp
services.AddElegantSeriesFlow(); // 全局栈

// 每个窗口的独立栈：
var windowScope = rootProvider.CreateScope(); // 窗口关闭时释放
var windowNavigation = new NavigationService(windowScope.ServiceProvider);
```

### 2. 写 ViewModel

```csharp
using ElegantSeries.Flow.Mvvm;
using ElegantSeries.Flow.Navigation;

public partial class HomeViewModel : BaseViewModel, INavigationAware
{
    public void OnNavigatedTo(object? parameter) { /* 页面出现 */ }
    public void OnNavigatedFrom() { /* 页面离开 */ }

    public Task GoToDetailAsync()
        => NavigateToAsync<DetailViewModel, string>("hello");
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
await navigation.NavigateToAsync<DetailViewModel>("Sidebar", operation: NavigationOperation.Replace);
await navigation.GoBackAsync();                                                // 弹出

bool canGoBack = navigation.CanGoBack();
var current = navigation.GetCurrentViewModel();
```

### 4. 渲染活动 ViewModel

ElegantSeries.Flow 与 UI 无关：它管理 ViewModel，平台层负责把活动 ViewModel
映射成 View。典型做法是一个 `ContentControl` 风格的 host 绑定
`GetCurrentViewModel()`，并在 `RegionNavigated` 事件里刷新。AOT 安全的
ViewModel→View 映射方式：在 `services.AddElegantSeriesFlowViews(...)` 里用 `views.Register` /
`views.RegisterTransient` / `views.RegisterSingleton` 显式注册——无运行时反射，
无命名约定。也可以在 View 类上放 `[ViewFor]` attribute
（`ElegantSeries.Flow.Routing`）声明它的 ViewModel，让 ElegantSeries.Flow
source generator 在编译期生成等价的注册代码，生成一个 `RegisterAttributedViews()`
扩展方法（在 `AddElegantSeriesFlowViews` 里调用）；attribute 的 `Lifetime` 可选 `Transient`
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
services.AddElegantSeriesFlowViews(locator =>
{
    locator.RegisterTransient<HomeView, HomeViewModel>();   // view 映射 + AddTransient
    locator.RegisterTransient<DetailView, DetailViewModel>();
    // 或者只做映射，ViewModel 的 DI 注册自己管：
    // locator.Register<HomeView, HomeViewModel>();
});

// 2. 给 host 提供依赖——用 XAML 绑定（MVVM）：
//    <flow:NavigationHost RegionName="MainRegion"
//                          NavigationService="{Binding NavigationService}"
//                          ViewLocator="{Binding ViewLocator}" />
//    或在 code-behind 里：
//    navigationHost.NavigationService = navigationService;
//    navigationHost.ViewLocator = viewLocator;
//    Host 会立即显示该 region 的当前页面，初始化顺序无所谓。
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

### 视图工厂

当视图需要构造参数、或构造方式取决于页面显示的位置时，可以注册工厂而不是视图类型：

```csharp
services.AddElegantSeriesFlowViews(locator =>
{
    // 简单工厂：只接收 ViewModel。
    locator.Register<ChartViewModel>(vm => new ChartView(vm));

    // 上下文工厂：还接收 region 与页面最近一次激活时的参数。
    locator.RegisterFactory<DetailViewModel>(context =>
        context.RegionName == "Sidebar"
            ? new DetailSidebarView()
            : new DetailView());
});
```

上下文工厂接收一个 `ViewCreationContext`（`ElegantSeries.Flow.Locating`）：
ViewModel 实例、region 名（仅经旧式 `CreateView(viewModel)` 路径创建视图时为 `null`）、
以及页面最近一次激活时所带的参数。该上下文刻意不携带 `IServiceProvider` 与页面
作用域——页面作用域在页面离开时即被释放；视图若需要服务，应在工厂闭包中捕获
应用的根 provider，或经其 ViewModel 获取。

工厂在 UI 线程运行、每个 ViewModel 实例只运行一次：宿主按实例缓存已创建的
视图，因此工厂决定的是页面首次创建时得到哪个视图。KeepAlive 页面重新激活时
不会再咨询工厂，也无法在此时更换视图。

## 导航操作与保留策略

每次导航由两个正交的选择组合而成：`NavigationOperation`（对栈做什么）
与 `NavigationRetention`（新页面日后离开时如何处置）。

| 操作 | 行为 |
|------|----------|
| `Push` | 把新页面推入 region 栈。上一页被停用。 |
| `Replace` | 替换当前页面。旧页面按它自己的保留策略离开。 |
| `Reset` | 清空整个栈，用新页面重新开始。 |

| 保留策略 | 行为 |
|------|----------|
| `Destroy` | 页面离开时销毁（释放其页面作用域）。 |
| `KeepAlive` | 页面离开时进缓存；之后以 `KeepAlive` 导航到同类型、且该导航被认出是发给缓存实例的（见「认出已有的页面」）时复用缓存的 ViewModel，而不是新建。 |

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
    Task<bool> CanNavigateFromAsync(CancellationToken cancellationToken);  // 返回 false 取消导航
}

public interface INavigationGuardWithContext  // 决策需要上下文时用这个，代替 INavigationGuard
{
    Task<bool> CanNavigateFromAsync(NavigationGuardContext context, CancellationToken cancellationToken);
    // context: RegionName, TargetViewModelType, Operation（回退时为 null）, Retention（回退时为 null）, Parameter, IsBack
}

public interface INavigationTargetAware  // 可选：认领或拒绝一次发给本页面类型的导航
{
    bool IsNavigationTarget(object? parameter);
}
```

生命周期回调和页面作用域的释放永远在**内部锁之外**执行，所以可以安全地
回调 `INavigationService` 而不会死锁。固定的过渡顺序是：
`OnNavigatedFrom` → 页面作用域释放 → `OnNavigatedTo` → 事件（`RegionNavigated`）。
UI 层应在 `RegionNavigated` 里换 view；`OnNavigatedTo` 执行时 view 还不保证
已经挂载。

回调里抛异常不会中断过渡：异常会被收集起来在事后重抛（单个原样抛出，
多个包成 `AggregateException`）。传给 `INavigationAwareAsync` 回调的 token
会在页面离开活动位置（被覆盖、弹出、替换或清空）时取消——即使回调仍在
运行。应响应该 token：一旦它触发，就不要再触碰页面的依赖。因该生命周期
取消引发的 `OperationCanceledException` 视为正常控制流，不是错误。

### 认出已有的页面

同一页面类型的多个实例可以共存——两个订单详情、两份文档。当一次前进
导航的目标类型已经有 `KeepAlive` 实例（被盖在栈里，或存在 KeepAlive
缓存中）时，这次导航只会发给其中一个。候选按离栈顶最近优先逐个判定，
认人方式有两种：

- **按参数认**。候选条目上次激活时存的参数与本次参数做 `object.Equals`
  比较。参数是值类型、`string` 或 `record` 时零代码自动成立——直接传 id
  本身，或传一个小 record：

  ```csharp
  await navigation.NavigateToAsync<OrderDetailViewModel, int>(42, retention: NavigationRetention.KeepAlive);
  await navigation.NavigateToAsync<OrderDetailViewModel, int>(43, retention: NavigationRetention.KeepAlive); // 第二个实例
  await navigation.NavigateToAsync<OrderDetailViewModel, int>(42, retention: NavigationRetention.KeepAlive); // 弹栈回到第一个
  ```

- **由实例自己认**。ViewModel 实现
  `INavigationTargetAware.IsNavigationTarget(parameter)` 即可认领——或
  拒绝——一次导航，它的回答对该候选是最终裁决。适用于身份在实例的当前
  状态里、而不在存着的参数里的页面，例如打开后被「另存为」改了名的文档。

没有任何候选被认出时就新建实例。已有的实例绝不会只因为类型相同就被
复用——正是这一点保证了订单 43 不会被递到订单 42 的页面上。参数类型
没有值相等（普通 class 且未重写 `Equals`）时按参数永远认不出；第一次
因此失去复用时，服务会经 `SubscriberError` 按参数类型报告一次，不会
静默退化。补救由便宜到贵：直接传 id、把参数改成 `record`、或在
ViewModel 上实现 `INavigationTargetAware`（最后一种连参数类型本身都不
用改）。

### 从回调里发起导航

一个页面的*窗口*从其 `OnNavigatedTo` 开始、到 `OnNavigatedFrom` 结束。
在窗口打开期间发起的导航调用——无论来自生命周期回调、离开守卫，还是过渡
运行期间执行的代码——都不会立即执行。每次调用只是为该 region 登记一个
待定请求，后登记的覆盖先登记的：窗口关闭时，只有**最后一个**登记的请求
会被执行。

- 从回调内部发起的调用返回一个已完成的 Task（`true` = 已登记；若服务
  已被释放、请求被丢弃则为 `false`），不会与它所属的过渡互相等待。
- 被覆盖的请求不产生任何效果：其 ViewModel 从不构造、生命周期回调从不
  运行；外部调用方 await 它会得到 `false`。
- 被执行的请求在当前页面的 `OnNavigatedFrom` 完成**之后**才构造目标
  ViewModel，随后成为当前页并开启自己的窗口。因此重定向链（页面在
  `OnNavigatedTo` 里继续向外跳）是逐跳推进的，每个中间页都走完整的
  `OnNavigatedTo` → `OnNavigatedFrom` 周期。
- `RegionNavigated` 只在一个页面落定为 region 的当前页时触发；重定向链
  的中间页不触发。
- 离开守卫每笔离场只运行一次，上下文是发起该笔离场的请求；之后登记的
  请求不会重跑守卫。
- 由回调登记的请求驱动的连续跳数受
  `NavigationOptions.MaxNavigationChainLength`（默认 10）限制。达到上限
  时，链会被停下，并经 `SubscriberError` 以 `InvalidOperationException`
  上报——这类请求没有等待方，因此上限永远不会让某个调用方的 Task 失败——
  当前页面保持活动且完好，服务可继续使用。

### 取消

`NavigateToAsync` / `GoBackAsync` 接受 `CancellationToken`，在请求仍处于
待定状态时有效：取消会撤回该请求——它不会被执行——等待的调用方抛
`OperationCanceledException`。一旦执行该请求的离场已经开始运行当前页面的
`OnNavigatedFrom`，离场本身不再被打断：回调会运行到底。此后到达的取消，
在执行步骤仍检查令牌的位置（目标解析期间、提交前一刻）仍会被受理；
在那里撤回会让离开页面被重新激活——其 `OnNavigatedTo` 再运行一次——
而不是把离场执行到底。（这些检查点只存在于 `NavigateToAsync` 的跳；
`GoBackAsync` 没有，因此其 `OnNavigatedFrom` 开始后令牌不再撤回请求。）

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

`NavigationRetention.KeepAlive` 把页面（ViewModel + 它的页面作用域）按
`(region, ViewModel 类型)` 缓存在每个 region 里。导航到同类型、且缓存页被
本次导航认出（参数相等或实例认领，见上文「认出已有的页面」）时复用缓存页
及其作用域——不建新作用域；认不出则新建，缓存页留待之后被逐出回收。

```csharp
await navigation.NavigateToAsync<SettingsViewModel>(retention: NavigationRetention.KeepAlive);

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

所有公开成员都可以在任意线程调用。同一 region 内的导航请求由该 region 的
导航泵逐个执行，共享状态由专用锁保护。

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
| `NavigateToAsync<T>(region?, operation?, retention?, refreshIfActive?, cancellationToken?)` | 导航到 ViewModel 类型 |
| `NavigateToAsync<T, TParam>(param, region?, operation?, retention?, refreshIfActive?, cancellationToken?)` | 带强类型参数导航 |
| `GoBackAsync(region?, cancellationToken?)` | 弹出当前页面 |
| `GoBackToAsync<T>(region?, cancellationToken?)` / `GoBackToAsync<T, TParam>(param, region?, cancellationToken?)` | 回退到栈上的某个页面，弹出其上方全部页面 |
| `SetRootAsync<T>(region?, retention?, cancellationToken?)` / `SetRootAsync<T, TParam>(param, region?, retention?, cancellationToken?)` | 以新页面为根替换整个栈 |
| `Remove<T>(region?)` / `Remove<T, TParam>(param, region?)` | 同步移除栈中被覆盖的页面 |
| `CanGoBack(region?)` / `GetCurrentViewModel(region?)` / `IsActive<T>(region?)` / `GetCurrentRetention(region?)` / `GetCurrentParameter(viewModel, region?)` / `GetNavigationStack(region?)` | 查询 |
| `ClearCache(region?)` / `ClearCacheAsync(region?)` | 清空单个 region 的 KeepAlive 缓存 |
| `ClearAllCache()` / `ClearAllCacheAsync()` | 清空所有 KeepAlive 缓存 |
| `RegionNavigated` / `ViewModelReleased` / `RegionCacheCleared` / `SubscriberError` | 事件 |
| `SubscribeRegionNavigated` / `SubscribeViewModelReleased` / `SubscribeRegionCacheCleared` / `SubscribeSubscriberError` | 可释放的事件订阅 |
| `AddElegantSeriesFlow()` / `AddElegantSeriesFlow(ServiceLifetime.Scoped)` | DI 注册 |
| `AddElegantSeriesFlow(configure, lifetime?)` | 带 `NavigationOptions` 的 DI 注册 |
| `NavigationOptions.DisposalGateTimeout` | 异步生命周期等待超时（默认 5 秒） |
| `NavigationOptions.SyncLockTimeout` | 同步 API 锁获取超时（默认 2 秒） |
| `NavigationOptions.MaxNavigationChainLength` | 回调驱动的连续跳数上限（默认 10） |

## 参与贡献

欢迎提 Issue 和 PR。新代码请保持 AOT/裁剪兼容（测试套件和 `IsAotCompatible`
标记会守护这一点），行为变更请加测试。

## 许可证

MIT — 见 [LICENSE.txt](LICENSE.txt)。
