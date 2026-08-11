# ScrollMarkerItemsView Virtual Items 设计

> 文档状态：规范设计与首轮实现施工中，更新于 2026-08-02。本文固定
> `ScrollMarkerItemsView` 已经确认的数据来源、导航元数据投影、逻辑 Descriptor 实体化和
> 逻辑 End 增量追加契约，以及首版基于 Avalonia `VirtualizingStackPanel` 的内容虚拟化基线；
> 远距离定位采用有界两阶段协议，Automatic 活动项使用已实现布局快照判定；逻辑 End 追加和
> 尾部流式增长采用尊重用户历史浏览意图的内容 End 自动跟随协议。

本文描述 `AtomUI.Labs.Controls.ScrollMarker` 中 Virtual Items 内容 Host 的第一阶段设计。
双 Host 边界和共享 Navigator 见[双 Host 总体架构](host-architecture.md)，共享 Marker Track
与 Marker 容器虚拟化见[导航条 UI 与交互设计](navigator-design.md)。方案 A 的极限数据矩阵、
测试实现、Benchmark、发布门禁和方案 B 重审条件见
[Virtual Items 验证与极限性能门禁](virtual-items-verification.md)。

## 目标与边界

`ScrollMarkerItemsView` 面向数据驱动、当前数量已知、只向逻辑 End 持续追加的一维内容序列。
首版在任意时刻只处理一个有限集合：

```text
当前 ItemsSource.Count = N
N 有限且可知
运行期间可以继续向逻辑 End 追加
```

“不设置人为硬上限”不等于控件保存真正无限的数据。业务数据始终由应用的 `ItemsSource` 持有；
Virtual Items 只减少内容 Section 和 Navigator Marker 的视觉容器、布局与呈现成本，不把历史
数据写入文件、数据库或对象池。

首要对话模型中，每个 ItemsSource 数据项表示一个完整 `ConversationTurn`（Question + Answer），
不是一条独立消息。一个 Turn 投影一个 MarkerDescriptor，并在实现后由一个 Section Container
同时呈现 Question 和 Answer；Answer 流式变化更新同一 Item 的内容高度，不追加第二个 Marker。

本阶段已经确定：

- 当前 N 个数据项对应 N 个轻量 `MarkerDescriptor` 实体。
- Descriptor 在数据项进入逻辑序列时生成，不按 Marker Viewport 惰性投影。
- `ScrollMarkerItem` 和内容 Section 是两套独立的按 Viewport 实现与回收的视觉容器。
- Virtual Section 使用内部专用 `ScrollMarkerSectionContainer : ContentControl`，不复用公共
  `ScrollMarkerSection`。
- 内容排列使用内部密封 `ScrollMarkerItemsPanel : VirtualizingStackPanel`，不允许替换为普通
  非虚拟化 Panel。
- 导航元数据使用 `BindingBase` 投影，在数据项入列时求值一次。
- 首版只接受逻辑 End 追加；一次有效追加只新增对应 Descriptor，不重建已有 Descriptor。
- 内容 Panel 使用 Avalonia `VirtualizingStackPanel` 的原生实现范围、平均尺寸估算、容器回收池和
  `ScrollIntoView` 能力；首版不并行维护第二套逐 Item 精确尺寸数据或自研虚拟化引擎。
- 内容 Panel 的 `CacheLength` 固定为 `0.5`，沿用框架定义的前后各半个主轴 Viewport 缓存；该值
  是首版内部模板约束，不公开成 ScrollMarker API。
- 容器何时进入或离开实现范围由框架 Panel 决定。首版不为 Pointer Capture、局部
  ScrollViewer、焦点或 IME 增加 ScrollMarker 专用的离散保留集合和延迟回收状态机。
- Virtual Marker 导航先用框架 `ScrollIntoView(index)` 实现目标，再按真实布局坐标精确对齐；
  实现请求和精确 Offset 写入均有严格次数上限，不能形成无界布局反馈循环。
- Virtual Automatic 只使用当前正常实现范围的原子布局快照，在快照能够证明覆盖 AnchorOffset
  探针时才切换活动项；不使用平均尺寸猜测 SourceIndex，也不扫描全部 ItemsSource。
- `ContentEndFollowMode` 默认为 Automatic；追加前处于 FollowingEnd 时继续跟随 End，用户主动
  浏览历史后停止主 Offset 写入，直到用户主动回到 End；Disabled 永不主动跟随。
- 无效投影或不受支持的集合变更先使 Host 进入不可恢复的 Faulted，再抛出
  InvalidOperationException；首版不提供恢复、回滚或故障降级模式。

本阶段不确定：

- 框架平均尺寸估算和 Extent 修正在目标问答场景中的实际误差、抖动和性能边界。

## 四层数量模型

Virtual Items 必须区分业务数据、逻辑导航元数据和两种视觉容器：

```text
ItemsSource                         当前全部问答对数据
└─ TurnViewModel × N                每项 = Question + Answer
          │
          │ 入列时投影一次
          ▼
MarkerDescriptor 集合              当前全部轻量导航元数据
└─ MarkerDescriptor × N
          │
          ├─ Navigator Viewport + Cache
          │      └─ ScrollMarkerItem × Km
          │
          └─ Content Viewport + Framework Cache
                 └─ Section 内容容器 × Ks
```

数量关系固定为：

```text
ItemsSource.Count        = N
MarkerDescriptor.Count   = N

Km 通常远小于 N
Ks 通常远小于 N
```

`MarkerDescriptor` 不是 Control，不进入视觉树，不执行 Measure/Arrange，也不持有完整业务内容、
ItemTemplate 生成的控件、Section Bounds、焦点或 Pointer 状态。它只保存共享 Navigator 和
Host Adapter 所需的轻量逻辑信息，至少等价于：

```text
MarkerDescriptor
├─ AnchorKey
├─ Label
├─ MarkerTheme
├─ StableOrdinal / SourceIndex
└─ 当前逻辑状态
```

当前 N 个数据项立即对应 N 个 Descriptor，是逻辑模型的 O(N) 成本；虚拟化保证的是昂贵视觉
容器数量受各自 Viewport 约束，不承诺整个控件的所有内存都变成 O(Viewport)。首版不增加第二套
Descriptor 惰性缓存、命中规则和失效协议。

## Content Virtualizer 承载结构

Virtual Items 不复用公共 `ScrollMarkerSection` 作为回收容器。内容 Host 的固定结构为：

```text
ScrollMarkerItemsView : ItemsControl                       [public]
└─ ControlTemplate
   └─ PART_ContentScrollViewer
      └─ ItemsPresenter
         └─ ScrollMarkerItemsPanel                         [internal/sealed]
            : VirtualizingStackPanel
            ├─ ScrollMarkerSectionContainer                [internal/sealed]
            │  : ContentControl
            ├─ ScrollMarkerSectionContainer
            └─ ScrollMarkerSectionContainer
```

两个内部类型承担不同职责：

```text
ScrollMarkerSectionContainer
    -> 一个当前已实现 Virtual Item 的内容外壳
    -> 承载 ItemTemplate 结果和临时视觉状态

ScrollMarkerItemsPanel
    -> 单个内容 ItemsPanel
    -> 决定当前需要实现、排列和回收哪些 Container
```

### 专用内部 Section 容器

`ScrollMarkerSectionContainer` 直接继承 `ContentControl`，不能继承公共
`ScrollMarkerSection`。两者的生命周期契约相反：

| 维度 | Direct `ScrollMarkerSection` | Virtual `ScrollMarkerSectionContainer` |
|---|---|---|
| 可见性 | public | internal sealed |
| 所有者 | 开发者 | `ScrollMarkerItemsView` |
| 身份 | 注册期间 AnchorKey 不可修改 | 回收后绑定另一个 Descriptor 和 SourceIndex |
| 注册方式 | Attach/Detach Registry | ItemsControl 容器生成与回收 |
| 内容结构 | 任意内容树，可包含子 Section | 单个扁平 ItemTemplate 结果，不支持 Section 嵌套 |
| 回收 | 不由 ScrollMarker 回收 | 离开实现范围后由框架回收或保留 |

Virtual 容器不公开独立 `AnchorKey`、`Label` 或 `MarkerTheme` 配置入口。Prepare 只把当前
Descriptor 引用、SourceIndex 和内容呈现所需状态投影到容器；`MarkerTheme` 仍由共享 Navigator
应用到 `ScrollMarkerItem`，不能错误地成为 Section 内容容器的业务状态。容器实例不能成为稳定
身份，也不能加入 Direct Section Registry。

### 专用内容 ItemsPanel

`ScrollMarkerItemsPanel` 内部密封继承 Avalonia `VirtualizingStackPanel`。它仍然是一个 Panel，
但复用框架的 `VirtualizingPanel`、`ItemContainerGenerator`、EffectiveViewport、单轴排列和容器
回收机制；首版不直接继承普通 `Panel`，也不从抽象 `VirtualizingPanel` 重写整套虚拟化引擎。

固定约束为：

- Panel 的 Orientation 由 Host 的主轴投影，Vertical 与首版 Horizontal LTR 共用一个类型。
- 默认模板必须实际使用 `ScrollMarkerItemsPanel`，开发者 Theme 不能把它换成 StackPanel、
  WrapPanel、Canvas 或其它非虚拟化 Panel。
- 不公开 `ItemsPanel`、`EnableVirtualization`、`ObjectPoolSize` 或替换 Panel 的公共属性。
- 容器创建、准备和清理由 `ItemsControl` 与 ItemContainerGenerator 生命周期驱动；Panel 不创建
  业务 ViewModel，也不保存被回收 Item 的业务历史。
- 不建立独立手写对象池；实际保留数量由 Viewport、`CacheLength` 和框架为焦点或
  ScrollIntoView 保留的容器共同决定。

首版直接接受 `VirtualizingStackPanel` 的原生算法边界：Panel 根据已测量容器的平均主轴尺寸
估算未测量 Item，维护连续实现范围和内部回收池，并通过 `ScrollIntoView` 实现目标 Item。
ScrollMarker 不再并行维护逐 Item 精确尺寸数据，也不在该派生类中隐藏第二套实现范围或回收
算法。远距离定位后的布局收敛和 Offset 确认由 Adapter 与 Coordinator 协调，并必须通过本文的
验证门槛。

## 数据项与容器入口

Virtual Items 是数据驱动模式。`ItemsSource` 只能包含业务数据对象，开发者不能提前创建并放入
`Control`、`ScrollMarkerSection`、`UserControl` 或其它视觉对象：

```text
受支持：
ItemsSource
└─ TurnViewModel × N
       │
       └─ ItemTemplate 按实现范围创建视觉内容

拒绝：
ItemsSource
└─ Border / Grid / UserControl / ScrollMarkerSection × N
```

如果业务集合已经保存 N 个 Control，全部视觉对象在进入 Host 前就已创建，内容容器虚拟化无法
兑现按需创建的对象生命周期。首版固定：

- 非空 ItemsSource 必须配置 `ItemTemplate`。
- 任意数据项 `item is Control` 时抛出 `InvalidOperationException`。
- 异常至少包含 Host 类型、SourceIndex 和实际 Item 类型。
- 不允许原始 Item 自己充当 ItemsControl 容器；每一项都经过专用
  `ScrollMarkerSectionContainer` 包装。
- 初始批次或 End 追加批次中发现 Control Item 或缺失 ItemTemplate 时，遵循先验证后提交规则，
  不部分提交 Descriptor 或 Realized 映射。

`ItemTemplate` 的根视觉可以是任意普通 Avalonia Control，但它只能在对应 Container 进入内容
实现范围后生成。框架可以在 Content 或模板变化时重建模板视觉；首版只承诺 Section Container
受虚拟化范围约束，不承诺 ItemTemplate 内部完整视觉子树使用固定数量对象池。

## Section Container 生命周期

`ScrollMarkerItemsView` 统一通过 Avalonia ItemsControl 容器钩子驱动
`ScrollMarkerSectionContainer`：

```text
NeedsContainerOverride
        -> 所有业务数据项都需要专用 Container

CreateContainerForItemOverride
        -> 创建无身份空壳

PrepareContainerForItemOverride
        -> 将空壳绑定到当前 Item / Descriptor / SourceIndex

ClearContainerForItemOverride
        -> 解除全部旧投影，再允许框架复用
```

Host 始终使用同一种 Container 类型和稳定回收键。开发者不能向逻辑 Items 混入手工 Container，
也不能绕过 Prepare/Clear 直接把业务 Item 放进 Panel.Children。

### Create

Create 只生成没有业务身份的空壳：

```text
ScrollMarkerSectionContainer
├─ SourceIndex        = -1
├─ Descriptor         = null
├─ Item               = null
├─ Content            = null
├─ ContainerGeneration = 0
└─ State              = Unbound
```

Create 不读取 ItemsSource、不保存 AnchorKey，也不建立 Realized 映射或业务事件订阅。创建次数由
Viewport、`CacheLength` 和框架特殊保留项决定，不能被描述成开发者配置的固定对象池大小。

### Prepare

Prepare 每次都必须完整执行，不能假设 Container 是新实例。推荐顺序固定为：

```text
Prepare(Container, Item, Descriptor, SourceIndex)
        │
        ├─ 断言 Container 处于 Unbound
        ├─ 验证 Descriptor、SourceIndex 和当前集合版本仍匹配
        ├─ ContainerGeneration++
        ├─ 绑定 Item、Descriptor 和 SourceIndex
        ├─ 设置 Content = Item
        ├─ 设置 ContentTemplate = ItemTemplate
        ├─ 应用 Host 实际拥有的 ContainerTheme、自动化和内部状态
        ├─ 从 Coordinator 投影当前逻辑活动状态；不把状态所有权迁入 Container
        └─ 全部成功后注册 SourceIndex -> Container 的 Realized 映射
```

异步布局、测量或导航回调必须捕获 `ContainerGeneration + SourceIndex + Descriptor 身份`。回调
执行时任一值不匹配就停止，不能修改已经换绑到其它 Item 的 Container。

Prepare 是逻辑事务。ItemTemplate 创建、主题应用或其它准备步骤抛出异常时，不得发布半准备的
Realized 映射；Host 必须按 Clear 路径清理该 Container 后再传播异常。

### Clear

Clear 必须在同一个 Container 绑定另一项之前完整完成：

```text
Clear(Container)
        │
        ├─ ContainerGeneration++，使旧回调失效
        ├─ 注销 SourceIndex -> Container 的 Realized 映射
        ├─ 取消仅属于该实现实例的异步工作
        ├─ 解绑 Host 或模板建立的事件、Binding 和 IDisposable
        ├─ 清除 Host 写入的自动化、验证、Theme 与内部伪类状态
        ├─ 清除 Content、ContentTemplate 和局部 DataContext
        ├─ 清除 Descriptor、Item 和 SourceIndex
        └─ State = Unbound
```

只有 Clear 完成后，才能执行下一次 Prepare。实现不能依赖“用新值覆盖旧值”清理状态，因为可选
属性为 null 时最容易泄漏上一 Item 的 Theme、自动化名称、验证结果或事件关系。

Clear 不删除 ItemsSource 数据、不删除 MarkerDescriptor，也不改变 Coordinator 中的活动身份。
当同一 SourceIndex 未来重新进入实现范围时，新 Container 必须从 Descriptor 和 Coordinator
恢复逻辑状态，不能从旧 Control 实例恢复。

Clear 的调用时机属于 `VirtualizingStackPanel` 和 ItemsControl 生命周期。Host 必须服从框架
回调并完整清理容器，不能自行延迟 Clear 或在 Panel 外保留旧容器。框架可以因焦点或
`ScrollIntoView` 暂时保留特殊容器，但这属于 Avalonia 当前实现行为，不提升为 ScrollMarker
公共保证。Pointer Capture、局部 ScrollViewer、普通焦点和 IME 都不建立首版专用保留协议；
业务上要求跨回收恢复的状态必须存入数据项并通过 Binding 恢复。

## 公共元数据投影 API

数据项不需要实现 AtomUI.Labs 专用接口，也不需要包装成 UI 库定义的数据对象。
`ScrollMarkerItemsView` 使用与 AtomUI 数据型控件一致的 `BindingBase` 投影方式：

```csharp
[AssignBinding]
[InheritDataTypeFromItems(nameof(ItemsSource))]
public BindingBase? AnchorKeyBinding { get; set; }

[AssignBinding]
[InheritDataTypeFromItems(nameof(ItemsSource))]
public BindingBase? LabelBinding { get; set; }

[AssignBinding]
[InheritDataTypeFromItems(nameof(ItemsSource))]
public BindingBase? MarkerThemeBinding { get; set; }
```

公共契约为：

| 属性 | 投影结果 | 是否必填 | 缺失或 null 的行为 |
|---|---|---:|---|
| `AnchorKeyBinding` | `string` | 是 | 不能提交非空 ItemsSource 或追加项 |
| `LabelBinding` | `string?` | 否 | Descriptor Label 回退到 AnchorKey |
| `MarkerThemeBinding` | `ControlTheme?` | 否 | 使用 Host 的 ItemContainerTheme 或默认主题 |

### 内容 End 跟随 API

内容 End 跟随是独立的语义属性，不参与数据项元数据投影：

```csharp
public enum ScrollMarkerContentEndFollowMode
{
    Automatic,
    Disabled
}

public ScrollMarkerContentEndFollowMode ContentEndFollowMode { get; set; }
```

`ContentEndFollowMode` 默认为 `Automatic`；`Disabled` 时内容增长绝不主动写入主 Offset。

`ContentEndFollowMode` 是 Virtual Items 的内容行为属性，不是内部 ScrollViewer 的原始属性转发。
首版不提供 `Always`、像素距离阈值、强制粘底或平滑跟随模式；这些模式会在用户阅读历史内容时
产生视口争夺，不能以“方便聊天业务”为由绕过用户输入仲裁。

推荐使用 Avalonia 编译绑定；实现不得为了读取任意属性路径而增加运行时反射扫描：

```xml
<atom.labs:ScrollMarkerItemsView
    ItemsSource="{Binding Turns}"
    ItemTemplate="{StaticResource TurnTemplate}"
    AnchorKeyBinding="{CompiledBinding Id}"
    LabelBinding="{CompiledBinding Title}"
    MarkerThemeBinding="{CompiledBinding MarkerTheme}"
    ContentEndFollowMode="Automatic"
    Orientation="Vertical"
    NavigatorPlacement="End" />
```

例如业务模型保持普通应用类型：

```csharp
public sealed class TurnViewModel
{
    public required string Id { get; init; }

    public string? Title { get; init; }

    public ControlTheme? MarkerTheme { get; init; }

    public required string Question { get; init; }

    public required string Answer { get; init; }
}
```

标准 Virtual `TurnTemplate` 同时呈现一轮问答：

```xml
<DataTemplate x:Key="TurnTemplate" x:DataType="local:TurnViewModel">
    <StackPanel Spacing="12">
        <Border Classes="question">
            <TextBlock Text="{CompiledBinding Question}" />
        </Border>
        <Border Classes="answer">
            <TextBlock Text="{CompiledBinding Answer}" />
        </Border>
    </StackPanel>
</DataTemplate>
```

`ItemTemplate` 的数据上下文仍是原始 `TurnViewModel`，不是 `MarkerDescriptor` 或额外包装对象。
内容视觉与导航元数据使用同一业务数据项，但承担不同职责：

```text
TurnViewModel
├─ Id / Title / MarkerTheme
│       └─ BindingBase 投影 → MarkerDescriptor
│
└─ Question + Answer
        └─ ItemTemplate → 按需实现的一个 ConversationTurn Section 内容
```

标准 `TurnTemplate` 必须把 Question 与 Answer 放入同一个模板根内容。不得把业务消息集合直接
作为 `ItemsSource` 并让 Question、Answer 各占一项；那会把一轮问答投影为两个 Descriptor 和
两个 Marker。控件不检查属性名称或业务类型，开发者仍可在非对话场景中用其它完整语义内容单元
作为单个 Item。

## 入列快照语义

三个 `BindingBase` 是针对单个数据项的投影公式，不表示 Host 为全部 N 个数据项永久维持实时
UI Binding 订阅。数据项首次进入逻辑序列时，Host 对三个投影各求值一次并保存结果：

```text
TurnViewModel
├─ Id          = "turn-42"
├─ Title       = "为什么一个 Marker 对应一轮问答？"
└─ MarkerTheme = BlueTheme
        │
        │ 入列时求值一次
        ▼
MarkerDescriptor
├─ AnchorKey   = "turn-42"
├─ Label       = "为什么一个 Marker 对应一轮问答？"
└─ MarkerTheme = BlueTheme
```

入列完成后修改原对象的 `Id`、`Title` 或 `MarkerTheme`，不更新已经提交的 Descriptor。尤其是
`AnchorKey`，它是 Section、Marker、SelectionModel 和导航请求共同使用的稳定身份，不能因
`INotifyPropertyChanged` 悄然改变。

这一限制不影响已实现 Section 内的普通 `ItemTemplate` Binding。ItemTemplate 可以继续监听
业务属性并更新内容视觉；只是逻辑 Marker 的 Label 和单项 Theme 不随原对象属性实时变化：

```text
TurnViewModel.Title 变化
├─ ItemTemplate Binding              可以更新已实现的内容视觉
└─ MarkerDescriptor.Label            保持入列快照
```

首版不为 N 个 Descriptor 建立最多 3N 组长期元数据订阅。显式刷新、Replace 或 Reset 是否允许
重新生成快照，归入后续集合变更专题，不由实现者临时增加旁路 API。

## AnchorKey 验证

Virtual Items 复用 Direct Section 的 AnchorKey 字面与唯一性规则：

- 使用 `StringComparer.Ordinal`，大小写敏感。
- 拒绝 `null`、非字符串、空字符串和全空白字符串。
- 拒绝带有首尾空白的值。
- 不隐式 Trim、不转换大小写、不自动生成后缀。
- 同一个 Host 当前逻辑序列中不得出现重复 AnchorKey。
- 不回退为 SourceIndex 或对象哈希，也不跳过无效数据项。

`LabelBinding` 的非 null 结果必须是字符串；`MarkerThemeBinding` 的非 null 结果必须是
`ControlTheme`。`AnchorKeyBinding` 缺失、投影求值失败、类型错误、AnchorKey 无效或重复时，
Host 立即抛出
`InvalidOperationException`，Debug 与 Release 行为一致。异常至少包含 Host 类型、SourceIndex、
Binding 名称和可安全输出的 AnchorKey 诊断。

初始投影或追加批次使用“先投影并验证，后提交”的逻辑事务；批次中任意 Item 无效时，不得向
Descriptor 集合部分提交前半批，也不得由控件反向修改业务 `ItemsSource`。外部集合已经发出
无效变更后，Host 按[终止性集合故障协议](#终止性集合故障协议)先进入不可恢复的 Faulted 状态，
再抛出异常。

## 初始投影

Host 首次取得包含 5 个数据项的 ItemsSource 时：

```text
ItemsSource
├─ TurnViewModel A
├─ TurnViewModel B
├─ TurnViewModel C
├─ TurnViewModel D
└─ TurnViewModel E
        │
        │ 对完整初始批次投影并验证
        ▼
MarkerDescriptor 集合
├─ Descriptor A   SourceIndex=0
├─ Descriptor B   SourceIndex=1
├─ Descriptor C   SourceIndex=2
├─ Descriptor D   SourceIndex=3
└─ Descriptor E   SourceIndex=4
```

只有完整批次通过 Binding、类型、字面和唯一性验证后，才原子提交 Descriptor 序列和 Key 映射。
提交逻辑序列不要求创建全部视觉容器：

```text
ItemsSource.Count       = 5
Descriptor.Count        = 5
ScrollMarkerItem.Count  = Navigator 实现范围决定
SectionContainer.Count  = Content 实现范围决定
```

## 逻辑 End 增量追加

应用在运行期间创建第六个业务数据项并追加到集合：

```csharp
Turns.Add(new TurnViewModel
{
    Id = "turn-f",
    Title = "第六次回答",
    Content = "..."
});
```

当前已确定的正常链路为：

```text
ItemsSource CollectionChanged
Action = Add
NewStartingIndex = 5
NewItems = [TurnViewModel F]
        │
        ├─ 验证该批次从旧 Count 的逻辑 End 开始且连续
        ├─ 对 F 的三个 BindingBase 各求值一次
        ├─ 验证 AnchorKey 和元数据类型
        ├─ 验证与 A～E 及同批新增项均不重复
        └─ 构造 Descriptor F
        │
        ▼
一次提交
├─ Descriptor 集合追加 F
├─ Key → SourceIndex 映射追加 F
├─ MarkerCount：5 → 6
├─ Navigator 逻辑 Track 增长
└─ Content Virtualizer 的逻辑 ItemCount：5 → 6
```

有效追加不得清空或重新投影 A～E，不得重建全部 MarkerDescriptor，也不得无条件创建 F 的两个
视觉容器：

```text
Descriptor F
├─ 槽位进入 Navigator Viewport + Cache
│      └─ 创建或复用 ScrollMarkerItem
│
└─ 数据项进入 Content Viewport + Framework Cache
       └─ 创建或复用 Section 内容容器并应用 ItemTemplate
```

新 Item 是否立即进入内容实现范围只由后续 Content Virtualizer 算法和当前视口决定；不能把
“Descriptor 已追加”“Section 已实现”和“内容自动滚动到终点”视为同一个状态。

一次 `Add` 可以包含连续的多个新 Item，但 `NewStartingIndex` 必须等于变更前 Count，且新增项
按集合顺序获得连续 SourceIndex。首版不把中间插入、Start 插入或 Move 伪装成 End 追加。

### 内容 End 跟随与历史视口保持

逻辑 End 的内容变化必须区分两类来源：

```text
结构追加
    ItemsSource 在逻辑 End 新增一个或多个 Item
    -> CollectionChanged + Descriptor 增量提交 + Extent 重算

尾部原地增长
    最后一个已存在 Item 的 ItemTemplate 内容继续增长
    -> 没有 CollectionChanged，只有 Measure / Arrange / Extent 变化
```

典型一问一答模型可以把一轮用户提问和 AI 回答放在同一个 `TurnViewModel` 中：创建下一轮时走
结构追加，当前 AI 回答流式输出时走尾部原地增长。Host 必须同时处理两条链路，不能把
`CollectionChanged` 当作内容增长的唯一事实。

#### 两种 Follow 状态严格正交

Navigator 的 `Follow / Browse` 只决定 Marker Track 当前显示哪一段；主内容 End 跟随只决定
`PART_ContentScrollViewer` 是否继续停留在最新内容。两者由不同状态表示：

```text
NavigatorFollowState
    -> Navigator Follow / Browse
    -> 不写主内容 Offset

ContentEndFollowState
    -> FollowingEnd / BrowsingHistory
    -> 只约束 Virtual 主内容 Offset
```

合法组合包括 `Navigator=Follow + Content=BrowsingHistory`：用户阅读旧回答时，Navigator 仍跟随
当前活动 Marker，但新回答不能把主内容拉回 End。实现不得复用同一个 `IsFollowing` 字段、枚举
或状态机表达两种语义。

#### End 判定与初始状态

主轴终点统一按以下公式判断：

```text
MaxScrollOffset = max(0, MainExtentLength - MainViewportLength)

IsAtEnd =
    abs(MaxScrollOffset - MainOffset) <= LayoutTolerance

LayoutTolerance = 0.5 DIP
```

首版不增加“距 End 若干像素也算在 End”的模糊阈值。`LayoutTolerance` 只吸收浮点、缩放和布局
取整误差，不成为可公开配置，也不能与 Automatic 的 4 DIP 空间滞回混用。

首次稳定布局后按实际位置建立状态：

```text
ContentEndFollowMode = Disabled
    -> BrowsingHistory

ContentEndFollowMode = Automatic && IsAtEnd
    -> FollowingEnd

ContentEndFollowMode = Automatic && !IsAtEnd
    -> BrowsingHistory
```

因此内容未溢出的新对话自然进入 `FollowingEnd`；预加载的长历史记录若初始位于 Start，则保持
`BrowsingHistory`。首版不能借 End 追加策略擅自把任意预加载历史跳到 End；初始打开位置若将来
需要配置，必须作为独立语义设计。

#### 状态转换

内容 End 跟随使用锁存状态，而不是每次布局都按“当前是否接近 End”重新猜测：

```text
FollowingEnd
  │
  │ 用户直接操纵主内容并表达向逻辑 Start 浏览的意图
  │ 包括 Start 方向滚轮/手势、ScrollBar Thumb、PageUp、Home
  ▼
BrowsingHistory
  │
  │ 用户直接操纵主内容并实际到达 IsAtEnd
  ▼
FollowingEnd
```

- Start 方向输入必须在 ScrollViewer 消费输入前解除 `FollowingEnd`，避免同一轮布局把用户重新拉回
  End。主 ScrollBar Thumb 开始拖动即视为用户接管；局部 ScrollViewer 已完整消费的输入和
  Navigator Browse 不参与该转换。
- End 方向输入本身不解除 `FollowingEnd`；处于 `BrowsingHistory` 时，只有用户发起的主内容
  End 方向操作，或用户发起的 MarkerNavigation 最终实际使 `IsAtEnd=true`，才重新进入
  `FollowingEnd`。
- 框架锚定、范围钳制、普通布局校正、窗口尺寸变化和主题更新不能自行把
  `BrowsingHistory` 重新武装为 `FollowingEnd`。
- `ContentEndFollowMode=Disabled` 时始终按 `BrowsingHistory` 语义处理，新增或增长内容绝不主动
  修改主 Offset。

#### 追加前快照与原子提交

是否跟随必须根据追加前最后一份已提交状态决定，不能在追加后根据新 Extent 重新计算：

```text
追加前：Extent=5000, Viewport=800, Offset=4200 -> IsAtEnd=true
追加后：Extent=5600, Viewport=800, Offset=4200 -> 距新 End 还有 600
```

若在追加后才判断，原本正在跟随 End 的用户会被错误分类为历史浏览。集合通知到达时业务集合
已经改变，因此 Host 使用变更前持续保存的稳定布局快照和锁存状态，而不是从变更后的集合与
Extent 反推旧状态。一次追加批次至少捕获以下上下文：

```text
EndAppendContext
├─ PreviousDescriptorCount
├─ PreviousLayoutEpoch
├─ PreviousMainOffset
├─ PreviousMaxScrollOffset
├─ PreviousContentEndFollowState
└─ ShouldFollowEnd
```

`ShouldFollowEnd` 只在 `ContentEndFollowMode=Automatic`、追加前为 `FollowingEnd` 且当前没有用户
输入或 MarkerNavigation 接管主视口时为 true。随后先完整验证批次，再一次提交全部 Descriptor、
Key 映射与逻辑 Count；批次中间不得逐项滚动。

#### BrowsingHistory 的视口保持

`ShouldFollowEnd=false` 时规则是：

```text
提交新增 Descriptor
不调用 ScrollIntoView
不调用 ScrollToEnd
不主动写 Offset
不把 Offset 强制恢复为追加前的绝对值
```

VirtualizingStackPanel 的正常布局会将可见容器注册为框架滚动锚点候选。Host 复用 Avalonia 的
`IScrollAnchorProvider` 机制和已有 Offset/Extent 校正，不手工注册第二组锚点，也不实现第二套
Offset 补偿器。End 追加位于历史视口之后，正常情况下不会改变当前已实现历史内容的主轴起点；
若框架因估算修正产生 Offset 变化，按实际 `ScrollChanged` Facts 和 Automatic 规则重新观察，
不能用旧绝对 Offset 与框架锚定互相争夺。

- [IScrollAnchorProvider API](https://api-docs.avaloniaui.net/docs/T_Avalonia_Controls_IScrollAnchorProvider)
- [VirtualizingStackPanel 12.0.5 源码](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Controls/VirtualizingStackPanel.cs)

#### FollowingEnd 的结构追加事务

`ShouldFollowEnd=true` 时，Descriptor 提交与内容跟随仍是两个阶段：

```text
完整追加批次原子提交
        │
        ▼
等待新 Extent / LayoutEpoch
        │
        ├─ 最终 Item 尚未实现 -> 对最终 SourceIndex 调用一次 ScrollIntoView
        │
        ▼
最终 Item 完成 Measure / Arrange
        │
        ▼
TargetEndOffset = max(0, MainExtentLength - MainViewportLength)
        │
        ▼
ScrollIntent = ContentEndFollow
写入主轴 Offset
        │
        ▼
下一有效 LayoutEpoch 确认
```

一次批量追加无论包含多少 Item，都只跟随整批提交后的最终逻辑 End；不得逐项显示连续跳转。
`ScrollIntoView` 每个追加批次最多请求一次，只用于使最终 Item 进入实现流程，不改变选择模式，
也不代替布局后的精确 End Offset。若首份追加后布局已经实现最终 Item，则跳过该调用。

End 跟随使用独立的短期执行记录，至少保存 `Generation`、等待的 `LayoutEpoch`、最后处理的
`MainExtentLength` 和最后写入的 `TargetEndOffset`；不得长期保存可被回收换绑的 Container 引用。

#### 尾部流式增长

尾部流式增长没有集合通知。最后一个 Item 已实现且 `ContentEndFollowState=FollowingEnd` 时，Host
从有效布局和 `ScrollChanged.ExtentDelta` 观察新的主轴终点：

```text
NewMaxScrollOffset = max(0, MainExtentLength - MainViewportLength)

if NewMaxScrollOffset 与上一已处理终点不同
   && abs(NewMaxScrollOffset - MainOffset) > LayoutTolerance
   && 当前 LayoutEpoch 尚未写入：
       以 ContentEndFollow Intent 写入 NewMaxScrollOffset
```

同一个有效 `LayoutEpoch` 最多写一次，Extent 没有变化时不得重复写。流式回答可以长期增长，
因此不能套用一次 MarkerNavigation 最多两次精确写入的事务总上限；它的有界条件是“每个真实
Extent 变化最多一次 Offset 写入”。设置 Offset 引发但未改变 Extent 的后续布局只负责确认，
不能反馈为新的写入。

处于 `BrowsingHistory` 时，尾部增长与 End 追加一样不写 Offset。若最后一个 Item 尚未实现，
其业务数据仍可更新；Host 不为测量离屏流式内容强制创建 Container。用户以后滚动或导航到该项
时，框架实现并按真实尺寸修正布局。

#### Offset 写入仲裁

共享 `ScrollIntent` 增加 `ContentEndFollow`，用于识别主内容 End 跟随产生的 Offset；它不能冒充
`MarkerNavigation` 或 `UserInput`。同一 Host 的主内容 Offset 由 Coordinator 串行仲裁，首版优先
关系固定为：

```text
UserInput
    > MarkerNavigation
    > ContentEndFollow
    > FrameworkCorrection / None
```

- 合格用户输入立即取消待处理的 End 跟随写入并按上述状态转换处理。
- MarkerNavigation 开始时暂停 ContentEndFollow；End 追加仍可提交 Descriptor，但不能打断当前
  Marker 目标。导航到历史 Item 后进入 `BrowsingHistory`，导航结果实际位于 End 时才允许恢复
  `FollowingEnd`。
- ContentEndFollow 产生的 `ScrollChanged` 不解除 Explicit，也不被解释为用户滚动。尾部 Offset
  最终偏离旧 Explicit 的有效目标时，按既有布局偏离规则解除 Explicit，进入 Automatic；不得由
  End 跟随直接写 SelectedKey。
- End 跟随稳定后，Automatic 使用新的 `VirtualLayoutSnapshot` 自然选择终点附近的活动 Item，
  Navigator 再按自己独立的 Follow/Browse 状态显示对应 Marker。

本阶段不增加公共 `ScrollToEnd`、`ResumeEndFollow`、`IsFollowingEnd` 或未读数量 API。开发者可以
选择 Automatic 或 Disabled；Automatic 下用户主动回到 End 即恢复跟随。未来若需要“新内容”
提示或程序化恢复，必须作为语义 API 独立设计，不能通过公开内部 ScrollViewer 实例实现。

## 终止性集合故障协议

无效集合和投影错误属于开发者违反 Virtual Items 的硬契约，目标场景中应为低概率错误。首版
采用 fail-fast、fail-closed 策略，不为该路径实现业务集合回滚、内容副本、降级导航或复杂自愈。

### 故障入口

以下情况使当前 `ScrollMarkerItemsView` 实例进入终止性 `Faulted`：

- 初始非空 `ItemsSource` 缺失 `ItemTemplate`，或数据项本身是 `Control`。
- `AnchorKeyBinding` 缺失、求值失败，或者结果不是有效字符串。
- `LabelBinding`、`MarkerThemeBinding` 的非 null 结果类型错误。
- AnchorKey 为空、全空白、带首尾空白或与已提交/同批新增项重复。
- `CollectionChanged(Add)` 不是从变更前已提交 Count 的逻辑 End 开始，或者新增索引不连续。
- 运行期收到 `Remove`、`Move`、`Replace` 或 `Reset`。
- 已经完成一次有效初始提交后替换整个 `ItemsSource`；首版不把它定义为重载或恢复入口。
- 集合通知、SourceIndex、ItemsSource Count、Descriptor Count 或 Key/Index 映射之间出现无法由有效
  End 追加解释的不一致。

`ItemsSource=null` 到第一份有效集合属于初始建立，不是运行期替换。首版不能把不支持的集合操作
静默规整为 Reset、全量重建或 End 追加，也不能跳过单个坏 Item 继续运行。

### 终止状态与操作顺序

每个 Virtual Host 实例拥有独立且单向的运行状态：

```text
VirtualHostState.Running
        │
        │ 检测到无效集合、投影或映射事实
        ▼
VirtualHostState.Faulted
        │
        └─ 当前实例没有返回 Running 的转换
```

检测到错误时必须先终止内部运行，再向应用抛出异常。顺序固定为：

```text
1. 不提交当前批次的 Descriptor、Key/Index 映射和逻辑 Count
2. 原子保存 VirtualHostFault，并把 VirtualHostState 置为 Faulted
3. 递增 HostGeneration，使全部旧容器、布局和 Dispatcher 回调失效
4. 取消 CurrentNavigationRequest，清除短期 MarkerNavigation Intent
5. 取消 EndFollowExecution，停止 ContentEndFollow Offset 写入
6. 停止 Automatic 快照提交、selected 投影和 Navigator Follow 校正
7. 解除 Host 自行建立的集合投影、布局和模板推进订阅，并用 Faulted 门禁阻止后续回调提交状态
8. 在当前检测调用栈抛出 InvalidOperationException
```

必须先锁存 `Faulted`，再抛出异常。应用可能安装全局未处理异常处理器；即使异常被外部捕获，
当前 Host 也不能继续使用已经不一致的 ItemsSource、Descriptor 和容器映射。Faulted 后的 Marker、
Navigator 和主内容输入不再触发导航或状态提交，迟到回调发现 HostGeneration 或 State 不匹配时
立即退出。

进入 Faulted 不反向修改、清空或回滚业务 `ItemsSource`，也不尝试撤销框架在集合通知过程中
已经发生的视觉变化。没有可靠回滚事务时，控件不能声称“恢复最后有效画面”。

### 异常与诊断

初始投影和运行期集合故障统一抛出 `InvalidOperationException`，Debug 与 Release 行为一致。
异常消息必须包含足以定位开发者违规行为的安全上下文，至少等价于：

```text
VirtualHostFault
├─ HostType
├─ FaultReason
├─ CollectionAction
├─ CommittedDescriptorCount
├─ CurrentItemsSourceCount
├─ SourceIndex / NewStartingIndex
├─ ExpectedStartingIndex
├─ BindingName
├─ AnchorKey                         仅在可以安全输出时
└─ HostGeneration
```

与当前错误无关的字段可以省略，但不能用一条没有 SourceIndex、Binding 或集合动作上下文的通用
“invalid item”消息掩盖真正原因。`VirtualHostFault` 是内部不可变诊断记录，不成为公共异常恢复
对象，也不保存完整业务 Item 或可能泄露的内容文本。

示例消息：

```text
ScrollMarkerItemsView entered a terminal faulted state because a non-End Add
operation was detected. CollectionAction=Add, NewStartingIndex=42,
ExpectedStartingIndex=100, CommittedDescriptorCount=100,
CurrentItemsSourceCount=101, HostGeneration=8.
```

### 无恢复、回滚或故障 UI 保证

首版明确不提供：

- 自动等待下一次合法集合变更并恢复。
- `Recover()`、`ReloadDescriptors()`、`ResetFault()` 或等价旁路 API。
- 替换 `ItemsSource` 后复活同一个 Faulted 实例。
- 自动删除、修正、重排或跳过业务数据。
- 回滚已经由应用提交的集合变更。
- 为错误路径维护第二份完整业务集合、已提交内容投影或视觉树副本。

开发者必须修正数据和使用方式，并创建新的 `ScrollMarkerItemsView` 实例。重新应用模板、重新启用
控件或修改普通配置不能清除 Faulted；只有新实例拥有新的 Running 生命周期。

Faulted 后不承诺保留最后有效内容、清空全部内容、保持 Navigator 与 Section 视觉一致或继续
提供只读滚动。若应用强行捕获异常，屏幕上尚未卸载的视觉只属于失效残留，不是可交互或可依赖
的降级模式。首版不提供专用错误页面、恢复按钮或公共 Faulted 视觉契约。

## 状态所有权

```text
业务 ItemsSource
    -> 完整业务数据，以及回收后仍必须恢复的业务状态

MarkerDescriptor
    -> AnchorKey、Label、MarkerTheme、SourceIndex 和 StableOrdinal

Virtual Items Adapter
    -> Descriptor 序列、Key / Index 映射、集合版本和框架内容定位适配状态

ScrollMarkerCoordinator
    -> ActiveAnchorKey、Automatic / Explicit、Navigator Follow / Browse
    -> CurrentNavigationRequest、NavigationPhase 和 Generation
    -> Virtual ContentEndFollowState、EndFollowExecution 和主 Offset 写入仲裁

Virtual Host
    -> VirtualHostState、HostGeneration 和不可变 VirtualHostFault

ItemsControl / ScrollMarkerItemsPanel
    -> 容器 Create / Prepare / Clear、当前实现范围和布局状态

Content Section 容器
    -> 当前 Item / Descriptor / SourceIndex / ContainerGeneration
    -> 当前已实现数据项的内容呈现和 Host 投影的临时视觉状态

ScrollMarkerItem
    -> 当前已实现 Marker 的呈现、Pointer 和键盘焦点状态
```

`MarkerDescriptor` 只保存轻量导航元数据，不保存 `IsSelected`、当前活动状态、
Automatic/Explicit、Follow/Browse、焦点、Pointer、已实现 Container 引用或当前导航事务。
这些逻辑导航状态只能由 Coordinator 持有；已实现控件上的 `:selected`、`:active` 等状态只是
Coordinator 状态的视觉投影，不能反过来成为逻辑真相。

### Coordinator 与容器虚拟化的边界

一个 Host 控件实例只有一个 `ScrollMarkerCoordinator` 实例，不存在分别管理 Navigator 和
Section Container 的两个 Coordinator：

```text
ScrollMarkerItemsView 实例
│
├─ ScrollMarkerCoordinator
│      ├─ 当前活动身份与选择模式
│      ├─ Navigator Follow / Browse
│      ├─ Content FollowingEnd / BrowsingHistory
│      ├─ 当前导航请求与 Generation
│      └─ 主内容 Offset 写入仲裁
│
├─ Virtual Items Adapter
│      └─ ItemsSource、Descriptor、Index 和内容导航适配
│
├─ ScrollMarkerNavigator
│      └─ Marker 输入与视觉呈现
│
└─ ItemsControl / ScrollMarkerItemsPanel
       └─ Section Container 的创建、准备、排列和回收
```

`ScrollMarkerView` Direct Host 和 `ScrollMarkerItemsView` Virtual Host 复用同一个 Coordinator
类型和行为契约，但每个 Host 实例各自创建独立实例。两个 Host 的差异由 Direct Content Adapter
与 Virtual Items Adapter 消化，不通过复制 Coordinator 实现，也不建立应用级单例。

Coordinator 不创建、不排列也不回收 `ScrollMarkerSectionContainer`。容器 Prepare 时只从
Coordinator 投影当前活动状态；Clear 时只清除该投影，不得改变 Coordinator 中的活动
AnchorKey、选择模式或导航事务。容器重新实现时必须再次从 Descriptor 和 Coordinator 恢复当前
逻辑状态，不能依赖旧 Control 实例。

### 业务状态与视觉状态边界

回收后仍必须恢复的状态应由业务 Item 持有并通过 ItemTemplate Binding 恢复，例如业务编辑值、
业务展开状态或应用明确要求保存的局部阅读位置。Host 不把这些状态复制进 Descriptor、Coordinator
或容器池。

Section Container 的 Clear 只清理 Host 或模板承载链路建立的内容投影、Theme、自动化、验证、
内部伪类、事件、Binding、`IDisposable` 和容器局部异步回调。Host 不递归扫描 ItemTemplate
视觉子树，也不猜测并重置每个业务控件的属性；模板视觉通过 Content 和 ContentTemplate 解除而
离开当前容器。开发者要求跨回收保留的模板状态必须有明确的业务状态来源。

回收 Section 或 Marker 控件不能删除业务数据或 Descriptor。Section Container 按本章
Create/Prepare/Clear 契约清除旧实现状态后才能绑定新的 SourceIndex；Marker 容器继续遵循
Navigator 专题中独立的 Prepare/Clear 契约。

## 框架原生虚拟化边界

### CacheLength 与正常实现范围

首版内容侧不定义自己的 ExpandedViewport、离散保留集合或对象池容量。默认模板把
`ScrollMarkerItemsPanel.CacheLength` 固定为 `0.5`，实际含义遵循 Avalonia
`VirtualizingStackPanel`：在当前主轴 Viewport 的 Start 和 End 两侧各缓存半个 Viewport。

```text
Framework Cache      Viewport       Framework Cache
  0.5 Viewport     1.0 Viewport       0.5 Viewport
┌──────────────┬──────────────────┬──────────────┐
│              │                  │              │
└──────────────┴──────────────────┴──────────────┘
```

`0.5` 表示框架缓存长度比例，不是固定 Item 数量，也不是 ScrollMarker 自己计算的一段 DIP 长度。
同一视口内，Section 越矮，框架通常需要实现的 Container 越多；Section 越高，实现数量越少。
框架还可以为焦点或 `ScrollIntoView` 暂时保留特殊元素，因此首版不承诺精确 Container 数量，
只要求它不随 `ItemsSource.Count` 无界增长。

`CacheLength` 不注册为 ScrollMarker 的 StyledProperty，也不公开 `StartOverscan`、
`EndOverscan`、`EnableVirtualization` 或 `ObjectPoolSize`。实现阶段不得在派生 Panel 中复制一套
正常实现范围算法，再把它伪装成对 `VirtualizingStackPanel` 的简单复用。

### 可变尺寸与 Extent 估算

问答 Section 高度允许显著不同，也允许当前回答因流式文本继续增长。首版接受框架原生策略：

- 已实现 Container 的 `DesiredSize` 是当前布局事实。
- 尚未测量 Item 的主轴尺寸由 `VirtualizingStackPanel` 根据已测量实现项的平均尺寸估算。
- 随着更多 Item 被实现和重新布局，框架可以修正平均值、Extent 和 ScrollBar Thumb。
- SourceIndex 与 AnchorKey 仍是逻辑身份；框架布局产生的坐标与 Offset 是视觉定位事实。

首版不建立自定义前缀和树、逐 Item 精确尺寸表、离屏尺寸快照或另一套 Extent。这样会使同一
Content ScrollViewer 同时存在两个坐标真相，且必须重写连续实现、回收、锚定和布局修正，已经
不再是方案 A。框架估算造成的远距离初始定位误差只能通过目标实现后的布局收敛确认处理，不能
偷偷加入第二套自研虚拟化引擎。

### 回收与交互非保证

`VirtualizingStackPanel` 决定 Container 何时进入回收池，Host 只在 ItemsControl 生命周期回调中
执行 Create、Prepare 和 Clear。首版不增加专用保留原因集合、屏幕外离散 Container 集合或
等待布局完成的自定义回收状态，也不延迟框架已经请求的 Clear。

因此首版明确不保证以下交互在 Container 离开框架实现范围后继续存活：

- Pointer Capture 或尚未完成的 Pointer 手势。
- ItemTemplate 内局部 ScrollViewer 的拖动、惯性或 ScrollBar 操作。
- 普通键盘焦点、Tab 导航、TextBox 选区和未提交 IME 会话。
- 只保存在模板 Control 实例上的展开、编辑或局部阅读状态。

Avalonia 当前版本若因自身焦点或 `ScrollIntoView` 机制保留特殊元素，Host 接受框架结果，但不能
把该实现细节写成 ScrollMarker 的稳定公共契约。开发者要求跨回收恢复的状态必须保存到业务
Item，并由 ItemTemplate Binding 在下次 Prepare 时恢复。首版主要目标是长篇、以阅读和 Marker
导航为主的一问一答内容，不承诺复杂离屏编辑会话不中断。

### 有界两阶段导航协议

Virtual Marker 激活采用“框架粗定位 + 真实容器精确对齐”两阶段协议：

```text
AnchorKey -> SourceIndex
        │
        ▼
Framework ScrollIntoView(index)       第一阶段：实现目标并使其进入视口
        │
        ▼
读取目标 Container 的真实 Layout Bounds
        │
        ▼
计算并写入 EffectiveTargetOffset     第二阶段：对齐 AnchorOffset
        │
        ▼
下一份有效布局确认结果
```

`ScrollIntoView(index)` 只负责实现目标附近容器并把目标带入视口，不视为最终对齐完成。首版调用
`ItemsControl.ScrollIntoView(int)` 公开入口，不从 Host 直接调用 Panel 的受保护实现。Avalonia
12.0.5 的实现会为未实现目标估算位置、创建和测量容器、调用 `BringIntoView` 并在必要时执行额外
布局；这些属于框架粗定位能力，不承诺目标外框与 `AnchorOffset` 自动精确对齐：

- [ItemsControl.ScrollIntoView 源码](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Controls/ItemsControl.cs)
- [VirtualizingStackPanel.ScrollIntoView 源码](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Controls/VirtualizingStackPanel.cs)
- [ScrollViewer Offset 与范围钳制源码](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Controls/ScrollViewer.cs)

#### 共享状态机扩展

共享 Marker 导航状态机增加 `RealizingTarget`。Direct Host 不需要实现虚拟容器，正常跳过该
Phase；Virtual Host 必须显式进入，不能把目标尚未创建的状态隐藏在 `Requested` 或
`AwaitingLayout` 中：

```text
Idle
  -> Requested
  -> RealizingTarget                 Virtual 专用；Direct 跳过
  -> ApplyingOffset
  -> AwaitingLayout
  -> Terminated
       ├─ Completed
       └─ Cancelled(reason)
  -> Idle
```

`ScrollIntent=MarkerNavigation` 必须在调用 `ScrollIntoView` 前建立，并覆盖 RealizingTarget、
ApplyingOffset 和 AwaitingLayout 整个事务。框架粗定位本身产生的 `ScrollChanged` 也是当前内部
导航的一部分，不能被误判成用户输入或据此解除 Explicit。

#### Virtual 请求身份

Virtual 执行状态至少等价于：

```csharp
internal sealed class VirtualNavigationExecution
{
    public required string AnchorKey { get; init; }

    public required int SourceIndex { get; init; }

    public required MarkerDescriptor Descriptor { get; init; }

    public required long NavigationGeneration { get; init; }

    public required long TemplateGeneration { get; init; }

    public int RealizationAttempts { get; set; }

    public int AlignmentWrites { get; set; }

    public long LastOffsetWriteLayoutEpoch { get; set; }
}
```

请求不长期保存 `ScrollMarkerSectionContainer` 引用。每次继续状态机时都通过
`ContainerFromIndex(SourceIndex)` 重新取得容器，并验证当前 SourceIndex、Descriptor、
ContainerGeneration 和视觉祖先关系。旧回调即使仍持有已回收实例，也不能据此操作已经换绑的
新 Item。

逻辑 End 追加会增加集合版本，但只要当前 SourceIndex 仍映射到同一 Descriptor，就不能取消正在
执行的导航。首版不支持的 Remove、Move、Replace 或 Reset 由集合故障协议处理，不能伪装成普通
End 追加。

#### 目标实现阶段

内部常量固定为：

```text
MaxRealizationAttempts = 2
```

第一次进入 `RealizingTarget` 时调用 `ScrollIntoView(SourceIndex)`，随后通过
`ContainerFromIndex` 检查目标。如果调用正好发生在布局过程中，或者容器尚未形成有效 Arrange，
则等待下一份有效布局后最多重试一次。第二次有效布局后仍不能取得身份正确的目标容器，以
`Cancelled(TargetRealizationFailed)` 终止。

不能使用周期 Timer、固定毫秒延迟或每次 `LayoutUpdated` 都重新调用 `ScrollIntoView`。Host 未
附加视觉树、不可有效显示、模板部件缺失或正在替换模板时也不能进入无界等待；分别按模板和目标
失败语义终止。

目标已经实现、身份有效且完成 Measure/Arrange 时，可以直接从 `Requested` 进入
`ApplyingOffset`，不为形式强制再创建一次布局。

#### 真实坐标与目标 Offset

Virtual `TargetStart` 与 Direct `SectionStart` 使用同一布局语义：目标
`ScrollMarkerSectionContainer` 外框在主滚动 Content 布局坐标系中的主轴起始位置。Vertical 取
Top，首版 Horizontal LTR 取 Left；不使用 Margin 前沿、ItemTemplate 内部子元素或
RenderTransform 后的视觉坐标。

Container 是 `ScrollMarkerItemsPanel` 的直接 Item 外壳。实现应沿固定内部模板的布局祖先链把
Container Layout Bounds 换算到主 Scroll Content 坐标，不能只读取任意本地 `Bounds`，也不能把
包含 RenderTransform 的视觉点变换作为唯一坐标事实。

```text
EffectiveAnchorOffset =
    clamp(Sanitize(AnchorOffset), 0, MainViewportLength)

RequestedOffset =
    TargetStart - EffectiveAnchorOffset

MaxScrollOffset =
    max(0, MainExtentLength - MainViewportLength)

EffectiveTargetOffset =
    clamp(RequestedOffset, 0, MaxScrollOffset)
```

Vertical 只写 `Offset.Y`，Horizontal LTR 只写 `Offset.X`；首版已经禁止交叉轴滚动，因此交叉轴
保持 0。虽然 Avalonia 也会钳制 ScrollViewer Offset，Coordinator 仍必须显式计算并保存
`EffectiveTargetOffset`，以便正确判断内容首尾的可达对齐结果。

#### 布局 Epoch 与收敛判据

Host 对主内容的有效 `LayoutUpdated` 维护单调递增 `LayoutEpoch`。每次精确 Offset 写入保存当前
Epoch；只有在更新后的 Epoch 中观察到目标容器身份和布局仍有效，才能确认该次写入结果。稳定
确认不要求测量全部离屏 Section，也不要求框架 Extent 成为永久不变的精确总和。

```text
LayoutTolerance = 0.5 DIP

IsConverged =
    NavigationGeneration 仍是当前 Generation
    && TemplateGeneration 仍然有效
    && SourceIndex 仍映射到同一 Descriptor
    && 目标 Container 身份有效
    && Container.IsMeasureValid
    && Container.IsArrangeValid
    && TargetStart / Extent / Viewport / Offset 全部有限
    && CurrentLayoutEpoch > LastOffsetWriteLayoutEpoch
    && abs(ActualOffset - EffectiveTargetOffset) <= LayoutTolerance
```

`LayoutTolerance` 只处理浮点、缩放和布局取整误差，不能与 4 DIP 的 Automatic 活动项滞回混用，
也不公开为 StyledProperty。如果写入前 ActualOffset 已在容差内且当前布局有效，请求立即
Completed；赋相同 Offset 不产生 `ScrollChanged` 时不能永久停在 AwaitingLayout。

#### 有界精确校正

内部常量固定为：

```text
MaxAlignmentWrites = 2
```

第一次写入使用目标刚实现后的真实坐标。写入后的下一份有效布局如果使 TargetStart、Extent 或
有效目标再次变化，允许重新计算并执行最后一次精确写入。第二次写入后的确认仍超出
`LayoutTolerance`，以 `Cancelled(AlignmentDidNotConverge)` 终止，不执行第三次补偿：

```text
第一次精确写入
        │
        ▼
下一份有效布局
        ├─ 已收敛 -> Completed
        └─ 未收敛
              ├─ AlignmentWrites < 2 -> 最后一次精确写入
              └─ AlignmentWrites == 2
                        -> Cancelled(AlignmentDidNotConverge)
```

该上限防止流式内容、模板布局反馈或框架 Extent 修正与 Coordinator 形成无限 Offset 写入循环。
请求完成之后发生的新布局变化不重新启动补偿导航；若实际 Offset 已经偏离重新计算的有效目标，
沿共享规则解除 Explicit 并按当前位置进入 Automatic，控件不能与用户争夺视口。

#### 事件合并与线程边界

`ContainerPrepared`、`LayoutUpdated` 和 `ScrollChanged` 都只是“状态可能可以继续推进”的信号。
模板应用时统一订阅，模板替换时统一解除；不能为每个请求反复建立未集中释放的事件订阅。

事件处理器不递归写 Offset，而是在 Avalonia UI Dispatcher 中合并为当前 Generation 的一次
`TryAdvanceNavigation`。同一 Generation 已经安排检查时忽略重复信号。全部状态只在 UI 线程
访问，不加锁，也不使用后台线程读取或修改视觉树。

#### 完成、取消与失败出口

首版 Virtual 导航结果至少包括：

```text
Completed

Cancelled(Superseded)
Cancelled(UserInterrupted)
Cancelled(TemplateChanged)
Cancelled(TargetInvalidated)
Cancelled(TargetRealizationFailed)
Cancelled(InvalidLayout)
Cancelled(AlignmentDidNotConverge)
```

新 Marker 请求立即递增 Generation 并取代旧请求，不排队、不回滚旧请求已经产生的 Offset。
合格主内容用户输入在 RealizingTarget、ApplyingOffset 或 AwaitingLayout 任一阶段立即取消导航。
取消后清除短期 MarkerNavigation Intent；除 Superseded 直接建立新 Explicit 外，其余运行时失败
解除旧 Explicit，并按当前实际 Offset 进入 Automatic。

目标暂时不能实现或不能收敛属于运行时导航失败，不抛出应用级异常；Debug 记录 AnchorKey、
SourceIndex、Generation、阶段、尝试次数和最后布局事实。主轴被禁用、必需模板部件缺失等开发者
配置错误仍按既定规则立即抛出。

### Automatic 活动项与 selected 同步

Virtual Automatic 的唯一任务是回答：当前主内容 Viewport 的 AnchorOffset 探针属于哪个已经存在
的数据项。首版只信任当前正常实现范围中 Container 的真实布局，不使用框架平均尺寸反推活动
SourceIndex，也不为 Automatic 建立全部 N 项的坐标表。

```text
ItemsSource / Descriptor × N                    逻辑全集
        │
        └─ VirtualizingStackPanel 正常实现范围
                  │
                  ▼
             Container × K                      K 通常远小于 N
                  │
                  ▼
        VirtualLayoutSnapshot × 1
                  │
                  ▼
      Probe 覆盖证明 + PositionGroup + 滞回
                  │
                  ▼
        Coordinator.CommitActive(...)
                  │
                  ▼
       Navigator SelectionModel / selected
```

#### 原子已实现布局快照

每份有效主内容布局最多提交一份快照，内部结构至少等价于：

```text
VirtualLayoutSnapshot
├─ LayoutEpoch
├─ MainOffset
├─ MainExtent
├─ MainViewport
├─ EffectiveAnchorOffset
├─ FirstRealizedIndex
├─ LastRealizedIndex
└─ Entries，按 SourceIndex 稳定排序
   ├─ SourceIndex
   ├─ AnchorKey
   ├─ DescriptorIdentity
   ├─ ContainerGeneration
   ├─ Start
   └─ End
```

Entries 只来自 Panel 当前正常连续实现范围。框架可能为焦点或 ScrollIntoView 保留的远端特殊容器
不能填补正常范围中的缺口，也不能单独参与 Automatic 候选。每个 Entry 必须满足：

- SourceIndex 位于当前 Descriptor 序列范围内，且仍映射到同一 Descriptor 和 AnchorKey。
- ContainerGeneration 与当前 Prepare 身份一致。
- Container 已完成有效 Measure 和 Arrange，仍属于当前 TemplateGeneration 的主内容视觉树。
- Start、End、Offset、Extent 和 Viewport 均为有限值。
- Start 按 SourceIndex 非递减；不满足一维 Stack 布局顺序时整份快照无效并记录诊断。

快照构造和提交是同一个 UI 线程逻辑事务。不能先发布部分 Entries，再等待后半批容器；同一个
活动计算也不能混用两个 LayoutEpoch。流式回答使布局暂时失效时保留上一份稳定快照，等新快照
完整有效后原子替换；不能先清空 selected 或让 Navigator 闪回第一项。

快照规模为 O(K)。构造过程只能枚举当前已实现容器，不允许枚举全部 ItemsSource、重新投影全部
Descriptor，或测量离屏 ItemTemplate。

#### Probe 与覆盖证明

活动探针继续使用两个 Host 共享的 AnchorOffset 语义：

```text
EffectiveAnchorOffset =
    clamp(Sanitize(AnchorOffset), 0, MainViewportLength)

Probe =
    MainOffset + EffectiveAnchorOffset
```

Vertical 使用 Y，首版 Horizontal LTR 使用 X。完成同坐标分组后，未经滞回的基础候选是最后一个
`Group.Start <= Probe` 的 PositionGroup；其 Automatic 代表项是组内 SourceIndex 最大的最后
Item。

快照必须证明自身覆盖 Probe 后才能提交候选。行为至少等价于：

```text
SnapshotCoversProbe =
    已找到最后一个 Start <= Probe 的 CandidateGroup
    &&
    (
        CandidateGroup 中至少一个 Container.End > Probe
        || 已实现 NextGroup 且 NextGroup.Start > Probe
        || CandidateGroup 包含逻辑最后一个 Item
    )
```

第一项表示 Probe 位于候选 Section 内；第二项允许 Probe 位于候选 Section 与下一个 Section 的
布局间隙；第三项处理逻辑内容末尾。如果没有 `Start <= Probe`，只有 FirstGroup 包含 SourceIndex
0 时才能回退到第一组，否则视为快照尚未覆盖。

覆盖证明失败时固定执行：

```text
保留上一活动身份
不使用 MainOffset / AverageSize 猜测 Index
不清空 SelectionModel
不触发 Navigator Follow
等待下一份有效布局快照
```

初始阶段尚无上一活动身份时保持 null，直到第一份覆盖 Probe 的快照。持续可见 Host 多个布局
周期仍无法形成覆盖证明时记录 Debug 诊断，并由原型测试判为虚拟化或模板接线缺陷；不能以猜测
结果掩盖空白或错误实现范围。

#### 同坐标组与空间滞回

Virtual Item 不支持父子 Section 嵌套，但零尺寸或相邻布局仍可能形成相同 Start。Start 差值位于
`LayoutTolerance = 0.5 DIP` 内的连续 Item 形成一个 PositionGroup，组内按 SourceIndex 稳定
排序，Automatic 选择最后一项。Container 枚举顺序、创建顺序和对象哈希不能决定代表项。

Virtual 与 Direct 共享内部空间滞回：

```text
ActivationHysteresis = 4 DIP

RawGroup = BaseGroup(Probe)

RawGroup.Index > CurrentGroup.Index
    Candidate = BaseGroup(Probe - ActivationHysteresis)
    Candidate 位于 CurrentGroup 之后时才前进

RawGroup.Index < CurrentGroup.Index
    Candidate = BaseGroup(Probe + ActivationHysteresis)
    Candidate 位于 CurrentGroup 之前时才返回

RawGroup.Index == CurrentGroup.Index
    保持 CurrentGroup
```

滞回作用于 PositionGroup，不作用于组内 Item。首版不增加 debounce、Timer、滚动速度阈值或按
设备类型变化的滞回参数；一个 LayoutEpoch 最多提交一次最终活动变化。

以下情况没有可继承的稳定 Automatic 历史，直接使用 BaseGroup：

- 第一份覆盖 Probe 的有效快照。
- 当前活动项已经不在有效逻辑序列或新快照中。
- Explicit 解除后第一次进入 Automatic。
- 一次大跨度滚动后的新正常实现范围与旧活动组不重叠。

内容首尾绕过滞回：

```text
IsAtStart =
    MainOffset <= LayoutTolerance

IsAtEnd =
    MainOffset >= MaxScrollOffset - LayoutTolerance

AtStart -> 第一逻辑 PositionGroup
AtEnd   -> 最后一逻辑 PositionGroup
```

首尾提交仍要求当前稳定快照分别覆盖逻辑第一项或最后一项。大跨度滚动只产生最终活动身份变化，
不能依次选中路径上的中间 Marker。

#### Automatic、Explicit 与导航事务

Requested、RealizingTarget、ApplyingOffset 和 AwaitingLayout 期间可以继续生成最新布局快照，但
禁止 Automatic 提交活动变化。Marker 激活后目标立即成为 Explicit selected；框架粗定位经过的
中间 Section 不能依次抢占 selected。

```text
导航成功
    -> Idle + Explicit(target)
    -> selected 保持 target

合格用户输入打断
    -> Cancelled(UserInterrupted)
    -> 解除 Explicit
    -> 使用当前最新覆盖快照进入 Automatic

导航完成后的稳定布局明显偏离有效目标
    -> 不补写 Offset
    -> 解除 Explicit
    -> 使用当前实际位置进入 Automatic
```

用户拖动主 ScrollBar 或框架远距离切换实现范围时，布局事件通过 Dispatcher 合并；快照稳定后
只提交最终 SourceIndex，不能发布每个中间估算范围。Navigator Browse/Follow 与
Automatic/Explicit 正交：活动项改变时，Follow 可以把 Marker 带回安全区；Browse 只更新逻辑
selected，不抢夺用户当前 Navigator Offset。

#### Coordinator 单一写入与防回环

活动身份和 Navigator SelectionModel 只能由 `ScrollMarkerCoordinator` 原子提交。
ScrollMarkerItem、ScrollMarkerNavigator、Virtual Items Adapter 和 Container 生命周期回调都
不能各自直接写 selected。提交输入至少包括：

```text
CommitActive
├─ AnchorKey
├─ SourceIndex
├─ Origin
│  ├─ AutomaticContent
│  ├─ ExplicitMarkerActivation
│  └─ ContainerProjection
├─ LayoutEpoch
└─ NavigationGeneration
```

原子提交顺序固定为：

```text
1. 验证 LayoutEpoch、NavigationGeneration 和 Descriptor 身份
2. 更新 Coordinator.ActiveAnchorKey / ActiveSourceIndex
3. 更新 Automatic 或 Explicit 模式
4. 更新 Navigator SelectionModel 的逻辑 selected Index
5. 向当前已实现 Marker 投影 :selected
6. 根据 Follow/Browse 决定 Navigator 是否移动
7. 活动身份真实变化时产生一次自动化选择通知
```

旧活动身份与新活动身份相同是无操作，不重复产生 SelectionChanged、Follow 或自动化通知。
SelectionModel 的程序化更新必须携带内部 `SelectionCommitOrigin`；AutomaticContent 和
ContainerProjection 引起的 SelectionChanged 只表示状态投影，不能反向触发 Marker 激活或新建
Explicit 导航请求。只有真实的 Marker 用户输入可以使用 ExplicitMarkerActivation 进入导航。

selected 属于 Coordinator、SelectionModel 和 Descriptor SourceIndex，不属于
ScrollMarkerItem 或 Section Container 实例。Marker 容器 Clear 只移除旧视觉投影；重新 Prepare
时从 SelectionModel 恢复。Section Container 的 Prepare/Clear 不改变活动身份。

#### 空集合、单 Item 与可导航资格

首版逻辑行为固定为：

```text
0 个 Descriptor
    ActiveAnchorKey = null
    SelectionModel 为空
    Navigator Collapsed

1 个 Descriptor
    第一份有效布局后 ActiveAnchorKey = 唯一 Item
    SelectionModel 选择唯一 Index
    Navigator 仍然 Collapsed
```

Navigator 是否显示与是否存在逻辑活动项是两件事。单 Item 隐藏 Navigator，不等于清空活动身份。

每个成功提交的 MarkerDescriptor 都是首版 Virtual 模式的逻辑可导航项。不能依据只存在于已实现
ItemTemplate 中的 `IsVisible=false`、Opacity、Clip 或零尺寸删除 Marker；离屏 ItemTemplate 根本
没有创建，视觉扫描无法为全部数据提供一致语义。零尺寸 Item 仍参与同坐标分组。

首版不提供 `IsNavigableBinding` 或运行时业务隐藏协议。未来确有需求时必须增加明确的数据级
投影和集合更新契约，不能扫描视觉树猜测业务资格。

#### Automatic 性能边界

每份有效布局的目标复杂度为：

```text
构建 VirtualLayoutSnapshot    O(K)
形成 PositionGroup            O(K)
查找 Candidate                O(log K) 或 O(K)
CommitActive                  O(1)

K = 当前正常实现容器数量
```

禁止在普通 ScrollChanged 或 LayoutUpdated 中执行 O(N) ItemsSource 扫描、全量 Descriptor 重建、
完整尺寸表维护或离屏模板测量。流式文本可以频繁触发布局，但同一 LayoutEpoch 只提交一份快照，
同一活动身份不产生重复状态通知。

### 方案升级门槛

方案 A 只有通过目标业务场景的原型与 Benchmark 后才具备发布基线资格。至少需要证明：

- 大量、显著变高的问答 Section 下，滚动和 Container 数量保持可接受。
- 当前回答流式增长以及只向逻辑 End 追加时，不出现空白、重复 Container 或失控跳动。
- 从远距离 Marker 跳转后，经过有限次框架布局能够收敛到正确 SourceIndex。
- 快速双向滚动时，Container 不随历史访问数量持续增长。
- 框架对平均尺寸、Extent 和 ScrollBar Thumb 的修正幅度在实际界面中可接受。

在首个可运行原型和 Benchmark 之前不编造数值性能承诺。如果上述关键场景不能通过，必须重新
开启“直接继承 `VirtualizingPanel` 并自研可变尺寸虚拟化”的方案 B 架构评审；不得继续保留
`VirtualizingStackPanel` 基类，同时逐项塞入方案 B 的逐 Item 尺寸结构、离散保留和回收算法。
完整的相对 Avalonia 基线、60 Hz 发布参考机器、长稳压力和强制重审阈值以
[Virtual Items 验证与极限性能门禁](virtual-items-verification.md)为唯一执行口径。

## 第一阶段验证契约

实现阶段至少验证：

- 初始 5 个有效数据项一次提交 5 个 Descriptor，并保持相同稳定顺序。
- 第 6 个有效 Item 向 End 追加时只新增一个 Descriptor，前 5 个实例和映射保持不变。
- `ItemsSource.Count` 与 `MarkerDescriptor.Count` 在每次成功提交后相等。
- Descriptor 全量存在时，ScrollMarkerItem 与 Section 容器数量仍分别受各自实现范围约束。
- `AnchorKeyBinding`、`LabelBinding` 和 `MarkerThemeBinding` 针对原始数据项求值，不把 Descriptor
  作为 ItemTemplate DataContext。
- 数据项属性在入列后变化不修改 Descriptor 快照，但不妨碍 ItemTemplate 的普通 Binding 更新。
- 标准问答数据中 N 表示 ConversationTurn 数量，不表示 Question 与 Answer 消息总数；每个 Turn
  恰好投影一个 Descriptor，流式 Answer 更新不得改变 Descriptor 数量。
- `AnchorKeyBinding` 缺失、无效 Key、重复 Key 和错误投影类型均明确失败，不跳过 Item 或生成
  索引 Key。
- 多项追加批次中任意项无效时不部分提交 Descriptor；控件不反向修改业务集合。
- 初始投影或运行期 End 追加发生上述错误时，Host 在抛出 InvalidOperationException 前原子进入
  Faulted；Debug 与 Release 行为一致。
- 非 End Add、Remove、Move、Replace、Reset、有效初始提交后的 ItemsSource 替换，以及无法解释
  的 Count/Index/映射不一致均进入相同终止路径，不能偷偷全量重建或继续运行。
- Faulted 转换递增 HostGeneration，取消 MarkerNavigation 和 EndFollowExecution，停止 Automatic、
  selected、Navigator Follow 与 Offset 写入；旧布局和 Dispatcher 回调不能提交状态。
- 应用捕获首个异常后，继续修改集合、重新应用模板、修改配置或替换 ItemsSource 都不能使同一
  实例返回 Running；只有新建 Host 实例可以重新开始。
- Faulted 不建立业务集合副本、回滚事务或故障 UI 保证；测试不能把残留视觉当作可用只读模式。
- 一次有效 End 追加只增量更新 Descriptor、Key 映射和两个逻辑 Count，不重建全部逻辑序列。
- ItemsSource 中的 Control 和非空集合缺失 ItemTemplate 均立即失败，不允许 Item 绕过专用
  Section Container。
- 新建 Container 没有业务身份；每次 Prepare 都完整投影当前 Item、Descriptor 和 SourceIndex。
- Container 从 A 复用于 D 时，A 的内容、Host 写入状态、自动化、事件和异步回调均不泄漏到 D。
- Clear 递增 Generation 并注销 Realized 映射；旧回调不能修改已经换绑的新 Item。
- Clear 不删除业务数据、Descriptor 或活动身份；同一 Item 重新实现时从逻辑状态恢复。
- Descriptor 不持有 selected、Automatic/Explicit、Navigator Follow/Browse、ContentEndFollow
  或导航事务；这些状态由每个 Host 独立的 Coordinator 持有。
- 回收活动 Item 的 Section Container 不改变 Coordinator 中的活动 AnchorKey；重新实现的容器
  从 Coordinator 恢复当前视觉投影。
- Clear 只解除 Host 和容器承载链路拥有的状态，不递归重置 ItemTemplate 中的业务控件属性。
- 默认内容 Panel 确实是 `ScrollMarkerItemsPanel : VirtualizingStackPanel`，且 `CacheLength=0.5`；
  不存在同时实现全部 Section 的隐藏旁路。
- 大量、显著变高的问答 Section 下，Container 数量由框架实现范围约束，不随已访问历史或
  `ItemsSource.Count` 持续增长。
- 当前回答流式增长以及逻辑 End 连续追加时，不出现空白、重复 Container 或全部容器重建。
- 远距离 Marker 导航通过框架 `ScrollIntoView(index)` 实现目标，有限次布局后收敛到正确
  SourceIndex；旧 Generation 的迟到回调不能完成新请求。
- Virtual 导航显式经过 RealizingTarget；Direct 导航跳过该 Phase，二者仍由同一个 Coordinator
  状态机和 Generation 契约管理。
- `ScrollIntoView` 从调用前到粗定位产生的 ScrollChanged 均保持 MarkerNavigation Intent，不被
  误判成用户输入或 FrameworkCorrection。
- 已实现目标可以直接进入精确对齐；未实现目标最多发出两次实现请求，失败后以
  TargetRealizationFailed 有界终止，不使用 Timer 轮询。
- 每次布局确认重新解析 Container，并验证 Descriptor、SourceIndex、ContainerGeneration 和
  TemplateGeneration；复用容器与旧模板回调不能完成当前请求。
- End 追加期间目标 Descriptor 仍位于原 SourceIndex 时继续导航，不因集合 Count 增长无条件取消。
- `ContentEndFollowMode=Automatic + FollowingEnd` 下单项和批量 End 追加均使用追加前快照决定
  跟随，只对批次最终 SourceIndex 请求至多一次实现，并在有效布局后停在新的 MaxScrollOffset。
- `ContentEndFollowMode=Automatic + BrowsingHistory` 与 `Disabled` 下 End 追加和尾部流式增长均不
  主动写 Offset、不调用 ScrollIntoView，也不创建第二套滚动锚点或旧绝对 Offset 补偿器。
- FollowingEnd 遇到 Start 方向主内容输入或 ScrollBar Thumb 操作时，在下一次跟随写入前进入
  BrowsingHistory；只有用户实际回到 0.5 DIP End 容差内才恢复 FollowingEnd。
- 尾部流式增长没有 CollectionChanged 时仍能通过有效 Extent/LayoutEpoch 继续跟随；同一
  LayoutEpoch 最多一次 ContentEndFollow Offset 写入，Extent 未变化不形成反馈循环。
- MarkerNavigation 期间追加只提交 Descriptor，ContentEndFollow 不抢占目标；用户输入、Marker
  导航、End 跟随与框架校正产生的 ScrollChanged 均保持正确 Intent。
- End 跟随不直接写 selected；稳定布局后由 Virtual Automatic 快照提交最终活动身份，Navigator
  Follow/Browse 状态与 ContentEndFollowState 保持正交。
- Vertical 和 Horizontal LTR 分别使用目标 Container 的布局 Top 或 Left 计算 RequestedOffset，
  不把 Margin 前沿或 RenderTransform 后的位置作为 TargetStart。
- 精确 Offset 最多写入两次，布局确认使用 0.5 DIP 容差和写入后的 LayoutEpoch；无法收敛时以
  AlignmentDidNotConverge 终止，不形成无限 Layout/Offset 循环。
- 目标已经位于有效位置且布局有效时即使没有 ScrollChanged 也立即完成；首尾范围钳制结果按
  EffectiveTargetOffset 判定，不能错误报告失败。
- ContainerPrepared、LayoutUpdated 和 ScrollChanged 在 UI Dispatcher 合并推进，同一 Generation
  不重复安排检查，模板替换能够统一解除订阅。
- 连续激活 Marker 只保留最新请求；合格用户输入可以在 RealizingTarget、ApplyingOffset 和
  AwaitingLayout 任一阶段立即中断，均不回滚已经产生的 Offset。
- 每个有效 LayoutEpoch 只从当前正常实现范围构造一份 O(K) VirtualLayoutSnapshot；快照 Entries
  的 SourceIndex、Descriptor、ContainerGeneration、Start 和 End 一致且原子提交。
- Probe 位于普通 Section、超高 Section 或 Section 布局间隙时，覆盖证明均得到正确最终候选；
  快照不能证明覆盖时保留旧活动项，不使用平均尺寸猜测 Index。
- 多个零尺寸同坐标 Item 形成一个 PositionGroup，Automatic 稳定选择组内 SourceIndex 最大项；
  容器枚举和复用顺序不能改变结果。
- Virtual 与 Direct 使用相同 0.5 DIP 布局容差和 4 DIP 空间滞回；边界附近子像素波动不重复
  产生 selected、Follow 或自动化通知。
- Start、End、第一次 Automatic、Explicit 解除和不重叠的远距离实现范围绕过旧滞回历史；
  ScrollBar 大跨度跳转只提交最终 SourceIndex，不依次选中路径 Item。
- MarkerNavigation 活跃期间新快照不能抢占 Explicit selected；UserInterrupted 后使用当前最新
  覆盖快照只提交一次 Automatic 结果。
- Coordinator 是 ActiveAnchorKey、ActiveSourceIndex 和 SelectionModel 的唯一写入者；
  AutomaticContent 与 ContainerProjection 的 SelectionChanged 不能反向启动 Marker 导航。
- selected 逻辑身份跨 Marker 和 Section Container 回收保持；Prepare 恢复当前投影，Clear 不
  改变 Coordinator，也不把旧 selected 泄漏给复用后的 Item。
- 0 个 Descriptor 时活动身份为空；1 个 Descriptor 时唯一项成为活动项但 Navigator 仍折叠。
- ItemTemplate 的 IsVisible、Opacity、Clip 和零尺寸不删除 Virtual Marker；首版所有成功提交的
  Descriptor 均可导航，不通过视觉树推断业务隐藏。
- N 从 100 增长到 10,000 且 Viewport 不变时，单次 Automatic 快照和判定成本由 K 决定，不出现
  O(N) ItemsSource 扫描、全量 Descriptor 重建或离屏模板测量。
- 快速正向、反向滚动和连续远距离跳转不产生重复 SourceIndex、永久空白或无界 Container 增长。
- 可变高度样本下，框架平均尺寸估算引起的 Extent、Offset 和 ScrollBar Thumb 修正必须经过人眼
  与 Benchmark 验收；首版不在没有原型数据时承诺固定误差阈值。
- Pointer Capture、局部 ScrollViewer、焦点和 IME 离开框架实现范围后的存活明确不属于首版
  保证；测试不得把它们误写成 ScrollMarker 专用 KeepAlive 契约。
- 实现中不存在逐 Item 精确尺寸结构、自建离散保留集合、第二对象池或第二套 Extent 真相。
- Prepare 中途失败不发布半准备 Realized 映射，并沿统一 Clear 路径恢复 Unbound。

## 设计封板与施工门禁

截至 2026-08-01，方案 A 首版核心设计与验证契约已经封板，没有剩余的阻塞性语义设计项。下一
阶段进入源码、测试、Gallery 和 Performance Runner 施工；施工顺序和发布资格统一受
[Virtual Items 验证与极限性能门禁](virtual-items-verification.md)约束。

“设计封板”不等于“已经验证”或“具备发布能力”。如果原型或 Benchmark 触发
`SchemeARejected`，必须停止方案 A 发布并重新开启方案 B 架构评审。
