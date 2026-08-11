# ScrollMarker 导航条 UI 与交互设计

> 文档状态：规范设计与首轮实现施工中，更新于 2026-08-02。本文固定当前已经讨论确认的导航条契约；未标记为已确定的数值和算法细节不代表现有实现。

本文描述 `AtomUI.Labs.Controls.ScrollMarker` 中两个公共 Host 共享的导航条 UI、布局属性、
Marker 主题能力，以及取代 Cluster 的同步窗口化导航方案。本文的完整模板树和 Section
注册链路以 `ScrollMarkerView` Direct Content 为例；双 Host 边界见
[ScrollMarker 双 Host 总体架构](host-architecture.md)。

首要对话语义固定为“一个 Marker 对应一个 ConversationTurn 问答对”，而不是一个 Marker
对应一条 Question 或 Answer 消息。Direct 与 Virtual Host 都向 Navigator 输出按问答对排列的
Descriptor；Navigator 不解析业务内容，只消费这个一一映射结果。非对话场景可以把其它完整
语义内容单元作为等价 Section。

Direct 内容 Section 的身份、注册、滚动边界、`AnchorOffset` 和活动项判定见
[ScrollMarkerSection Direct Content 设计](section-design.md)。Virtual Items 使用等价的共享
Navigator 契约，但内容区域使用独立的内部 `ScrollMarkerItemsPanel` 和
`ScrollMarkerSectionContainer`；其 Index 导航、框架原生回收边界和验证门槛见并继续落入
[Virtual Items 设计](virtual-items-design.md)。

## 设计目标

导航条需要同时满足：

- 支持纵向和横向主内容。
- 首版面向中英文界面，只支持 `FlowDirection.LeftToRight`。
- 可以位于主轴对应的 Start 或 End 一侧。
- 支持独立占位和悬浮覆盖两种呈现方式。
- 外层容器和 Marker 可以主题化。
- Marker 有足够的鼠标、触控和键盘命中区域。
- Marker 数量超过可见容量时仍保持独立，不折叠成 Cluster。
- 内容滚动导致活动 Section 变化时，Navigator 的可见窗口跟随对应 Marker。
- 用户可以暂停自动跟随，独立浏览远处 Marker。

## 整体构成

ScrollMarker 由内容语义、整体协调、导航项生成和 Marker 轨道布局四部分组成。下图中的
`ScrollMarker*` 是本组件计划实现的 C# 类；`Panel`、`VirtualizingStackPanel`、
`ScrollViewer`、`ContentPresenter` 和 `ItemsPresenter` 是 AtomUI/Avalonia 框架类型。

下图专门展示 `ScrollMarkerView` Direct Content Host，不表示
`ScrollMarkerItemsView` 会复制一套 Navigator：

如果需要先从外层、中间层、内层的平面俯视角度理解控件，请参阅
[ScrollMarkerView Direct 平面结构图解](structure-visual-guide.md)。

```text
ScrollMarkerView : ContentControl                         [public]
│
├─ Content：开发者提供的普通内容
│  └─ Panel / Grid / StackPanel                          [框架类型]
│     ├─ ScrollMarkerSection : ContentControl            [public]
│     │  └─ 任意 Avalonia 内容
│     ├─ ScrollMarkerSection : ContentControl
│     │  └─ 任意 Avalonia 内容
│     └─ ScrollMarkerSection × N
│
└─ ControlTemplate
   └─ ScrollMarkerViewPanel : Panel                      [internal]
      │
      ├─ AtomUI ScrollViewer                             [框架类型]
      │  └─ ContentPresenter                             [框架类型]
      │     └─ 呈现 ScrollMarkerView.Content
      │
      └─ ScrollMarkerNavigator : SelectingItemsControl   [internal]
         ├─ MarkerDescriptor × N                         [internal/logical]
         └─ Navigator ScrollViewer                       [框架类型]
            └─ ItemsPresenter                            [框架类型]
               └─ ScrollMarkerTrackPanel                 [internal/sealed]
                  : VirtualizingStackPanel
                  ├─ ScrollMarkerItem : ContentControl,
                  │                     ISelectable       [public/realized]
                  ├─ ScrollMarkerItem
                  └─ ScrollMarkerItem × K

K 通常等于 Navigator Viewport 与前后缓存覆盖的容器数量，并允许 Avalonia 为键盘焦点或
ScrollIntoView 暂时保留少量额外容器；K 通常远小于 N，但不是绝对固定值。
```

这里的两条分支不是两份业务内容：

- `Content` 分支表示开发者在 XAML 中提供的逻辑内容。
- `ControlTemplate` 分支表示控件如何通过 `ContentPresenter` 把同一份 Content
  呈现在唯一的主 ScrollViewer 中，并在旁边布置 Navigator。

### 类模块及职责

#### ScrollMarkerView

`ScrollMarkerView : ContentControl` 是公共根控件和整体协调者。它负责公共属性、唯一主
ScrollViewer、Section 生命周期、当前活动 Section、主内容滚动状态，以及 Marker 激活后的
目标跳转。

开发者直接把普通内容交给它，而不是再提供一个顶层 ScrollViewer。它是局部组合控件，不是
应用级 Host。

#### ScrollMarkerItemsView

`ScrollMarkerItemsView : ItemsControl` 是另一个公共根控件。它面向一维
`ItemsSource`，使用 `ItemTemplate` 和虚拟化内容容器承载当前数量已知、只向 End 追加的
Section 序列。

它不使用 Direct Section Registry 或视觉树扫描，也不通过 `SectionStart` 预先定位尚未实现
的 Item。Marker 激活交给 Virtual Items Adapter，把语义 `ScrollToIndex` 落实为框架
`ScrollIntoView(index)`，容器实现并完成布局后再确认实际 Offset。元数据投影、Descriptor
增量追加、框架原生内容虚拟化边界和验证门槛见
[Virtual Items 设计](virtual-items-design.md)；目标实现与 Offset 确认采用已确定的有界两阶段协议。

#### ScrollMarkerSection

`ScrollMarkerSection : ContentControl` 是公共语义包装器。它把普通 Avalonia 内容声明为
一个可导航 Section，并通过 `AnchorKey`、`Label` 和 `MarkerTheme` 提供稳定身份与 Marker
元数据。Section 可以包含另一个 Section，但不能包含 `ScrollMarkerView` 或
`ScrollMarkerItemsView` 根 Host。

Section 仍然参与开发者选择的 Grid、StackPanel 或其它 Panel 布局；它不会自行决定业务内容
的排列方式。

#### ScrollMarkerItem

`ScrollMarkerItem : ContentControl, ISelectable` 是公共的 Marker 容器类型，但实例由
`ScrollMarkerNavigator` 自动生成，开发者不手工维护它。它负责单个锚点的选择、焦点、点击、
命中区域、视觉状态和自动化语义。

该类型必须公开，开发者才能在 XAML 中为它声明 `ControlTheme`。公开不等于要求开发者直接
实例化。

#### ScrollMarkerViewPanel

`ScrollMarkerViewPanel : Panel` 是内部整体布局类。它只安排两个直接布局对象：
主 ScrollViewer 与 `ScrollMarkerNavigator`。

它负责 `Orientation`、Start/End、Inline/Overlay、Spacing 和 Margin 对整体 Measure/Arrange
的影响，不负责 Section 注册、Marker 生成或滚动状态。

#### ScrollMarkerNavigator

`ScrollMarkerNavigator : SelectingItemsControl` 是内部导航容器。它接收 Coordinator 提供的
MarkerDescriptor 序列，生成或复用 `ScrollMarkerItem`，维护单选状态，并处理 Marker 的
鼠标、触控和键盘输入。

它使用 AtomUI 已有的 `SelectingItemsControl` 容器生成模式，不把选择和容器生命周期逻辑
重复塞入任一公共 Host。

两个 Host 共享同一个 Navigator 设计。Navigator 消费稳定 `MarkerDescriptor` 序列，不直接
读取 Direct Section Bounds，也不直接访问 Virtual ItemsSource。

#### ScrollMarkerCoordinator

`ScrollMarkerCoordinator` 是计划中的内部共享协调职责。它维护 MarkerDescriptor 映射、
Automatic/Explicit 和 Follow/Browse 状态，并通过 Host Adapter 请求当前活动身份或导航目标。

它由两个 Host 类型组合复用，不作为公共抽象基类，也不能在两个 Host 类型中复制实现。每个
Host 控件实例都创建自己的 Coordinator 和 Marker 导航状态机；“共享”指共享实现，不是应用内
所有 Host 共用一个运行时单例。完整状态机见
[ScrollMarkerSection Direct Content 设计](section-design.md#marker-导航状态机)。

#### ScrollMarkerTrackPanel

`ScrollMarkerTrackPanel : VirtualizingStackPanel` 是内部密封的 Marker 轨道布局类，也是
Navigator 不可替换的 ItemsPanel。它负责按 Section 稳定顺序把 Marker 安排到统一槽位、
计算可超过 Viewport 的逻辑 Track 长度，并支持横向和纵向排列。

该类型继承 Avalonia 12 的 `VirtualizingStackPanel`，复用框架的 EffectiveViewport、
`CacheLength`、`ItemContainerGenerator`、回收池、集合变化和 `ScrollIntoView` 机制；它不再
实现第二套容器池，也不公开 `EnableVirtualization`、`ObjectPoolSize` 或可替换 ItemsPanel。
`CacheLength` 的首版内部默认值为 `0.5`，表示在 Navigator Viewport 逻辑 Start 和 End
方向各缓存半个 Viewport。该数值不是公共 API，后续可以依据 Benchmark 内部调整。

它只处理确定性的几何布局；当前 Section 是谁、是否处于 Follow/Browse，以及点击后滚到哪里
由上层控件决定。

### Section 与 Marker 的对应关系

Direct 模式中，每个有效实体 Section 对应一个逻辑 Marker：

```text
ScrollMarkerSection[0]  <──> MarkerDescriptor[0]  <──> Logical Marker[0]
ScrollMarkerSection[1]  <──> MarkerDescriptor[1]  <──> Logical Marker[1]
ScrollMarkerSection[N]  <──> MarkerDescriptor[N]  <──> Logical Marker[N]

Section 提供身份和元数据：
AnchorKey + Label + MarkerTheme

Logical Marker 进入 Navigator 实现范围时生成或复用 ScrollMarkerItem 容器。
```

Section 是业务内容容器，MarkerDescriptor 是轻量逻辑映射，Item 是按需实现的导航交互
容器。三者不能合并成同一个控件，也不能让开发者同时维护两套集合。动态增加、删除或重排
Section 时，Navigator 必须保持逻辑映射同步。

Virtual 模式中，一对一关系存在于数据项、MarkerDescriptor 和逻辑 Marker 之间；对应的内部
`ScrollMarkerSectionContainer` 与 `ScrollMarkerItem` 都可以只在各自 Viewport 范围内实现：

```text
DataItem[N] <──> MarkerDescriptor[N] <──> Logical Marker[N]
     │                                      │
     └─ realized 时生成 Section             └─ visible 时生成 Item
```

### 运行链路

Direct Content 链路：

```text
Section 增加、删除或重排
          │
          ▼
ScrollMarkerView 更新 Section 映射
          │
          ▼
ScrollMarkerNavigator 更新 MarkerDescriptor，
为当前实现范围生成或复用 ScrollMarkerItem
          │
          ▼
ScrollMarkerTrackPanel 通过 VirtualizingStackPanel
实现当前 Viewport、缓存区和框架保留项，并计算 Marker 位置
          │
          ▼
主内容滚动 ──> 更新活动 Section ──> 更新选中 Marker
                                              │
用户激活 Marker <─────────────────────────────┘
          │
          ▼
ScrollMarkerView 滚动到对应 ScrollMarkerSection
```

这条链路形成双向同步：主内容滚动驱动活动 Marker，Marker 激活驱动主内容跳转。负责几何
排列的 Panel 不直接发起业务导航，负责业务协调的 View 也不替代 ItemsControl 的容器生成职责。

Virtual Items 链路：

```text
ItemsSource 向 End 追加
          │
          ▼
Virtual Adapter 更新逻辑数据项和 MarkerDescriptor
          │
          ▼
Content Viewport 实现或复用 ScrollMarkerSectionContainer
          │
          ▼
Navigator Viewport 实现或复用 ScrollMarkerItem
          │
用户激活 Marker[Index]
          │
          ▼
语义 ScrollToIndex -> RealizingTarget
                  -> Framework ScrollIntoView(index)，最多两次
                  -> 实现目标 Section
                  -> 精确 Offset 写入，最多两次
                  -> LayoutEpoch + 0.5 DIP 确认
```

活动 Item 还需要区分两种内部来源状态：

```text
Automatic
    主内容用户滚动时，按 AnchorOffset 和 Section 位置自动判定

Explicit(anchorKey)
    用户明确激活 Marker 后，保持该 Marker selected
```

Automatic/Explicit 决定“哪个 Item 被选中”；后文的 Follow/Browse 决定“Navigator 当前显示
Track 的哪一段”。两套状态彼此正交，不能合并为一个枚举。完整的同坐标候选、显式锁定和
滚动来源规则见[ScrollMarkerSection Direct Content 设计](section-design.md#滚动来源)；Direct
Automatic 的 4 DIP 空间滞回见[Automatic 活动项滞回算法](section-design.md#automatic-活动项滞回算法)。
Virtual Automatic 复用相同布局容差与空间滞回，但只使用当前正常实现范围的原子布局快照和
Probe 覆盖证明，见[Virtual Items 设计](virtual-items-design.md#automatic-活动项与-selected-同步)。

无论 Navigator 当时处于 Follow 还是 Browse，Marker 激活都进入对应的
`Explicit(anchorKey)`；Follow 状态保持 Follow，Browse 状态在完成激活后返回 Follow。

### 类名与模板部件名

类名和 `PART_*` 名称属于不同层次。类名定义职责和可测试边界；`PART_*` 只是默认
ControlTemplate 中供代码查找的命名元素，不是 C# 类型。

```text
ScrollMarkerViewPanel : Panel
├─ PART_ContentScrollViewer : AtomUI ScrollViewer
└─ PART_Navigator : ScrollMarkerNavigator
   └─ PART_NavigatorRoot : Border
      └─ NavigatorLayout : Panel
         ├─ PART_PreviousMarkerButton : AtomUI Button
         ├─ PART_NavigatorScrollViewer : AtomUI ScrollViewer
         │  └─ ItemsPresenter
         │     └─ ScrollMarkerTrackPanel : VirtualizingStackPanel
         │        └─ ScrollMarkerItem
         │           ├─ 透明交互命中区
         │           └─ PART_Marker : Border
         └─ PART_NextMarkerButton : AtomUI Button
```

`PART_ContentScrollViewer` 是唯一主内容视口；Navigator 内部的
`PART_NavigatorScrollViewer` 只在 Marker Track 超出可见空间时浏览 Marker，不能滚动业务内容。
`PART_NavigatorRoot` 承载导航条背景、边框、圆角和 Padding，`PART_Marker` 表示 Item
模板中的可见 Marker。`PART_PreviousMarkerButton` 和 `PART_NextMarkerButton`
位于 Marker Viewport 的逻辑 Start 和 End。按钮自身不属于 MarkerDescriptor 集合；它们分别
请求导航到当前活动 Marker 的前一个或后一个稳定邻居，并由 Coordinator 复用普通 Marker
激活链路定位对应 Section。

`ScrollMarkerView` 不继承 Grid 或 DockPanel。布局容器属于默认 ControlTemplate 的内部实现，避免开发者向 View 任意加入同级子控件并破坏视口与导航条的结构约束。

## 主方向与相对位置

`Orientation` 使用 Avalonia 的 `Orientation`：

```csharp
Orientation.Vertical
Orientation.Horizontal
```

它同时决定：

1. Section 按 X 轴还是 Y 轴计算锚点位置。
2. Marker Track 按横向还是纵向排列。
3. 导航时修改主 ScrollViewer 的 `Offset.X` 还是 `Offset.Y`。

它不改变开发者 Content 内部的布局方向。

Navigator 位置使用相对主方向的枚举：

```csharp
public enum ScrollMarkerPlacement
{
    Start,
    End
}
```

默认值为 `End`。

首版映射规则：

| Orientation | Placement | 物理位置 | 主滚动偏移 |
|---|---|---|---|
| `Vertical` | `Start` | 左侧 | `Offset.Y` |
| `Vertical` | `End` | 右侧 | `Offset.Y` |
| `Horizontal` | `Start` | 顶部 | `Offset.X` |
| `Horizontal` | `End` | 底部 | `Offset.X` |

首版受支持的有效流向仅为 `FlowDirection.LeftToRight`。因此 Horizontal 的逻辑 Start 固定为
左侧，逻辑 End 固定为右侧；Section 和 Marker 从左向右排序，`LogicalOffset` 与
`Offset.X` 同向。`FlowDirection.RightToLeft` 不属于首版支持和兼容性保证范围，不实现
Start/End 物理映射反转、X 坐标反转或 RTL 键盘方向。

该限制是面向现代中英文界面的产品范围，不是声称 RTL 已经实现。内部仍必须保留
`LogicalStart`、`LogicalEnd` 和 `LogicalOffset` 等中性命名，以便未来通过集中坐标转换层扩展
RTL，而不改写上层排序、活动判定和 Track 算法。

不再同时提供可写的“四边 Placement + Orientation”组合，从类型设计上避免 `Left + Horizontal` 等冲突状态。

## Inline 与 Overlay

```csharp
public enum ScrollMarkerDisplayMode
{
    Inline,
    Overlay
}
```

`NavigatorDisplayMode` 默认值为 `Inline`。

无论使用 Inline 还是 Overlay，Navigator 只在当前可导航 Section 数量不少于 2 时显示：

```text
EffectiveNavigableSectionCount <= 1
    -> Navigator Collapsed

EffectiveNavigableSectionCount >= 2
    -> Navigator Visible
```

首版不提供强制显示单 Marker 的公共属性。完整计数口径和动态可见性规则见
[ScrollMarkerSection Direct Content 设计](section-design.md#navigator-显示阈值)。

Direct Host 的 `EffectiveNavigableSectionCount` 只统计满足 Section `IsNavigable` 判定式的项。
业务隐藏、首次布局待定或无效坐标项不计数；离屏、Clip、Opacity、IsEnabled 和零尺寸不减少
计数。Virtual Host 首版每个成功提交的 MarkerDescriptor 都可导航，不根据仅存在于已实现
ItemTemplate 中的 `IsVisible`、`Opacity`、`Clip` 或零尺寸改变计数；0 个和 1 个 Descriptor 时
Navigator 均折叠，但单 Item 仍保留逻辑活动身份。

### Inline

Navigator 占据独立布局空间，不遮挡主内容。

纵向 Start：

```text
┌──────── ScrollMarkerView ──────────────────┐
│ ┌───────┐  ┌─────────────────────────────┐ │
│ │       │  │                             │ │
│ │  Nav  │  │        ScrollViewer         │ │
│ │       │  │                             │ │
│ └───────┘  └─────────────────────────────┘ │
└────────────────────────────────────────────┘
```

横向 End：

```text
┌──────── ScrollMarkerView ──────────────────┐
│ ┌────────────────────────────────────────┐ │
│ │              ScrollViewer              │ │
│ └────────────────────────────────────────┘ │
│ ┌────────────────────────────────────────┐ │
│ │                Navigator               │ │
│ └────────────────────────────────────────┘ │
└────────────────────────────────────────────┘
```

Inline 测量规则：

- 先测量 Navigator 的自然厚度。
- 从主轴的交叉方向可用空间中扣除 Navigator 厚度和 `NavigatorSpacing`。
- 将剩余空间交给 ScrollViewer。
- Navigator Margin 参与其自身布局。
- Navigator Collapsed 时不扣除其厚度、Margin 或 `NavigatorSpacing`。

### Overlay

ScrollViewer 使用 ScrollMarkerView 的完整可用空间，Navigator 在更高视觉层贴近 Start 或 End。

```text
┌──────── ScrollMarkerView ──────────────────┐
│ ┌───────┐                                  │
│ │  Nav  │   ScrollViewer 使用完整尺寸      │
│ │       │                                  │
│ └───────┘                                  │
└────────────────────────────────────────────┘
```

Overlay 规则：

- Navigator 只占据自身实际区域，不创建覆盖整个内容面的透明命中层。
- Navigator 之外的透明区域不能阻止 ScrollViewer 接收鼠标、触控或滚轮输入。
- `NavigatorMargin` 控制 Navigator 相对 View 边缘的内缩。
- Overlay 可能遮挡内容，因此不作为默认模式。
- Navigator Collapsed 时不绘制，也不保留透明命中区域。

运行时切换 Inline/Overlay 只触发布局更新，不销毁 Section、不替换业务 Content，也不重建主 ScrollViewer。

## Host 共享导航条属性

以下目标公共属性由 `ScrollMarkerView` 和 `ScrollMarkerItemsView` 共同提供：

| 属性 | 类型 | 默认值或来源 | 作用 |
|---|---|---|---|
| `Orientation` | `Orientation` | `Vertical` | 主滚动轴和 Marker 排列方向 |
| `NavigatorPlacement` | `ScrollMarkerPlacement` | `End` | Navigator 位于主轴的 Start 或 End |
| `NavigatorDisplayMode` | `ScrollMarkerDisplayMode` | `Inline` | 独立占位或悬浮覆盖 |
| `NavigatorSpacing` | `double` | 主题/代码默认值待原型量化 | Inline 下 Navigator 与 ScrollViewer 的间隔 |
| `NavigatorMargin` | `Thickness` | `0` | Navigator 相对 View 布局边缘的外边距 |
| `NavigatorBackground` | `IBrush?` | 由默认主题提供 | 导航条外层背景 |
| `NavigatorBorderBrush` | `IBrush?` | 由默认主题提供 | 导航条外层边框画刷 |
| `NavigatorBorderThickness` | `Thickness` | 由默认主题提供 | 导航条外层边框粗细 |
| `NavigatorCornerRadius` | `CornerRadius` | 由默认主题提供 | 导航条外层圆角 |
| `NavigatorPadding` | `Thickness` | 由默认主题提供 | 导航条外层与 Marker Viewport 的间距 |
| `MaxVisibleMarkerCount` | `int` | `int.MaxValue`，必须不小于 `1` | Navigator 同时可见 Marker 数量的开发者上限 |
| `MarkerSlotExtent` | `double` | 主题/代码默认值，不小于默认命中尺寸 | Marker 统一槽位在逻辑主轴上的最小长度 |
| `ItemContainerTheme` | `ControlTheme?` | 默认 Marker 主题 | 所有生成 Marker 的统一主题 |
| `AnchorOffset` | `double` | `0` | 从主视口逻辑 Start 边计算的统一活动判定与目标对齐偏移 |

画刷属性使用 `IBrush`，不使用 `Color`，以支持 Shared Token、动态资源和渐变。

`MaxVisibleMarkerCount = int.MaxValue` 表示默认不施加人为数量上限，以保持根据 Navigator
实际空间和 `MarkerSlotExtent` 自动计算容量的既有行为；它不表示可以绕过几何容量。只要 Marker
数量超过实际可见容量，仍然显示 Previous/Next 相邻导航按钮。开发者设置有限正整数后，该值与
几何容量共同约束最终可见数量。

所有 Thickness 和 double 布局值进入测量前必须规整：

- `NaN`、Infinity 和负数不能进入 Measure/Arrange。
- 负间距按 0 处理。
- 规整只作用于有效值，不回写绑定源。

## Direct 主内容 ScrollViewer 公开控制面

### 已确定的所有权边界

`PART_ContentScrollViewer` 是 `ScrollMarkerView` 的内部模板部件。公共 API 不提供以下入口：

```csharp
public ScrollViewer ContentScrollViewer { get; }
```

开发者不能替换、直接持有或把自己的顶层 ScrollViewer 注入 `ScrollMarkerView`。内部实例在
模板应用前不存在，重新应用 ControlTheme 时也可能被替换；暴露实例会泄漏模板生命周期，
并允许外部代码绕过 Coordinator 直接修改 Offset 或 Content。

首版不转发 Avalonia 的两个物理轴 ScrollBarVisibility Attached Property，而是在
`ScrollMarkerView` 上公开语义属性：

| 属性 | 类型 | 默认值 | 语义 |
|---|---|---|---|
| `Orientation` | `Orientation` | `Vertical` | 选择唯一主内容滚动轴 |
| `MainScrollBarVisibility` | `ScrollBarVisibility` | `Auto` | 只配置当前主轴滚动条 |

`MainScrollBarVisibility` 是 `ScrollMarkerView` 自己的非继承 StyledProperty，接受本地值、直接
Binding 和正常 Style。它只允许 `Auto`、`Visible` 和 `Hidden`；`Disabled` 会使 Marker 无法
到达远处 Section，因此在配置入口处抛出 `InvalidOperationException`，不得静默规整。

```xml
<atom.labs:ScrollMarkerView
    Orientation="Vertical"
    MainScrollBarVisibility="Auto">
    <!-- Direct Content -->
</atom.labs:ScrollMarkerView>
```

开发者不配置物理交叉轴。控件根据 Orientation 把语义配置投影到内部 ScrollViewer：

```text
ScrollMarkerView
├─ Orientation
└─ MainScrollBarVisibility
            │
            ▼
      主轴到物理轴投影
            │
            ▼
PART_ContentScrollViewer
```

### 主轴到物理轴投影

| Orientation | 主轴物理属性 | 交叉轴物理属性 |
|---|---|---|
| `Vertical` | Vertical = `MainScrollBarVisibility` | Horizontal = `Disabled` |
| `Horizontal` | Horizontal = `MainScrollBarVisibility` | Vertical = `Disabled` |

```text
Vertical：

X 交叉轴 = Disabled，Offset.X = 0
Y 主轴   = MainScrollBarVisibility

        ↑
        │ 主内容滚动
        ↓

Horizontal：

X 主轴   = MainScrollBarVisibility
Y 交叉轴 = Disabled，Offset.Y = 0

←────── 主内容滚动 ──────→
```

`Auto` 只在主轴内容超过 Viewport 时显示并启用滚动条；`Visible` 始终显示；`Hidden` 隐藏
ScrollBar 但仍允许滚轮、触控和内部 Marker 导航改变主轴 Offset。交叉轴固定为 `Disabled`，
不能用 `Hidden` 代替，因为 `Hidden` 仍允许该方向滚动。

运行时改变 Orientation 时，控件把同一个 `MainScrollBarVisibility` 重新投影到新主轴，开发者
不需要同步改写两个物理轴属性。原主轴变成新交叉轴后，其 Offset 归零；新主轴从 0 开始。
ScrollMarker 不保存二维像素位置，也不修改开发者 Content 内部 Panel 的排列方向，Horizontal
内容仍需由开发者提供横向布局。

该限制只作用于外层 `PART_ContentScrollViewer`。Navigator 使用独立内部 ScrollViewer；某个
Section 内的宽表格、代码区或其它特殊内容可以自行使用局部 ScrollViewer 承担交叉轴滚动：

```text
PART_ContentScrollViewer       单轴滚动
└─ ScrollMarkerSection
   └─ 局部 ScrollViewer       可以按业务需要使用另一条轴
```

本节当前固定 `ScrollMarkerView` Direct Content 的公共滚动配置面。`ScrollMarkerItemsView` 的
公共滚动配置面尚未设计；它最终也必须保持用于 Section 导航的内容主轴可滚动，但不能在专题
设计完成前自动照搬 Direct Host API。

### 配置更新与逻辑原子提交

Avalonia 控件属性只在所属 Dispatcher 的 UI 线程上读取和写入，因此这里不使用线程锁。
“原子”表示 ScrollMarker 自身不能观察或响应只投影了一半的内部配置，不表示多条属性写入会
变成一条 CPU 指令。

首次加载时，在模板应用且初始属性解析完成后构造完整配置快照。运行时 Orientation 或
`MainScrollBarVisibility` 变化时，同一个 UI Dispatcher 同步调用批次内的变化合并为一次更新：

```text
属性变化
    -> 仅安排一次待提交回调
    -> 当前同步调用返回
    -> 读取 Orientation + MainScrollBarVisibility 最终值
    -> 构造完整候选快照
    -> 验证
    -> 在内部事件屏蔽区一次投影并提交
```

候选快照至少包含 Orientation、`MainScrollBarVisibility` 和模板代次。验证通过前不能改写
`PART_ContentScrollViewer`、Offset、活动 Section 或 Navigator。提交期间必须屏蔽内部物理
属性和 Offset 变化产生的中间通知；提交完成后只执行一次布局失效、Section 重新计算和
Navigator 更新。模板在待提交期间被替换时，旧模板代次的回调失效，并针对新模板重新安排。

同一同步调用中的连续赋值在回调执行前已经全部完成，因此只提交最终快照。跨 `await`、计时器
或不同 Dispatcher 回调发生的变化属于多个独立更新；控件不等待或猜测开发者未来还会修改
属性。首版不公开 `BeginUpdate`、`EndUpdate`、事务对象或配置回滚 API。

由于交叉轴完全由控件推导，任意受支持的 Orientation 与
`MainScrollBarVisibility=Auto|Visible|Hidden` 组合都合法。原子提交主要用于避免内部物理属性、
Offset 和 Section 状态出现撕裂，也用于把多次同步变化合并为一次布局，而不是修补公开 API
制造的物理轴冲突。

首版实现测试必须证明：

- Vertical 和 Horizontal 都只把 `MainScrollBarVisibility` 投影到主轴，并把交叉轴固定为
  `Disabled`、交叉轴 Offset 固定为 0。
- 同一同步调用中连续改变 Orientation 与 `MainScrollBarVisibility` 时只提交和重新布局一次，
  OffsetChanged、活动 Section 和 Navigator 不能观察到半投影状态。
- 跨 `await` 或不同 Dispatcher 回调的变化分别提交，不被错误合并为一个无限等待的事务。
- `MainScrollBarVisibility=Disabled` 抛出 `InvalidOperationException`，内部 ScrollViewer 和已提交
  配置保持不变。
- 模板重新应用会使旧模板代次的待提交回调失效，旧回调不能改写新模板部件。

### 首版固定滚动视觉与手势策略

除 `MainScrollBarVisibility` 外，首版不向开发者开放主内容 ScrollViewer 的其它滚动视觉或
手势开关。`PART_ContentScrollViewer` 使用以下内部固定值：

| 属性 | 首版内部值 | 语义 |
|---|---:|---|
| `AllowAutoHide` | `true` | 使用 Avalonia/AtomUI 普通自动隐藏策略，不开放定制 |
| AtomUI `IsLiteMode` | `false` | 使用普通滚动条，不启用 AtomUI 极简模式 |
| `IsScrollInertiaEnabled` | `false` | 首版关闭惯性滚动；后续迭代再评估行为与开放方式 |
| `IsScrollChainingEnabled` | `true` | 主内容到达边界时允许滚动意图继续传向可滚动祖先 |
| `IsDeferredScrollingEnabled` | `false` | 拖动 ScrollBar Thumb 时内容与活动 Marker 实时更新 |
| `BringIntoViewOnFocusChange` | `false` | 内容焦点变化不自动修改主视口 Offset |

这些值必须由默认模板或控件代码显式设置为内部有效值。开发者在 `ScrollMarkerView` 上设置
同名 Attached Property 不构成受支持配置，也不能改变 `PART_ContentScrollViewer` 的有效值。

`IsScrollChainingEnabled=true` 只控制 `PART_ContentScrollViewer` 到可能存在的可滚动祖先之间的
链路。Section 内局部 ListBox、TreeView 或 ScrollViewer 是否在到达边界后把滚动继续交给
主内容视口，由该局部控件自己的 `IsScrollChainingEnabled` 决定；ScrollMarker 不覆盖它。

Navigator 使用独立内部策略：

```text
PART_ContentScrollViewer.IsScrollChainingEnabled   = true
PART_NavigatorScrollViewer.IsScrollChainingEnabled = false
```

用户手动 Browse Marker Track 到达边界后停止，不能把剩余滚动意图链到主内容或其它祖先
滚动区域。主内容的 `AllowAutoHide`、`IsLiteMode`、惯性和 Deferred 设置同样不能传播到
Navigator；Navigator 的滚动条呈现和 Previous/Next 相邻导航按钮由其内部 Theme 独立控制。

首版固定 `IsDeferredScrollingEnabled=false`，因此拖动主内容 ScrollBar Thumb 时 Offset、活动
Section、selected Marker 和 Navigator Follow 都实时更新。未来若开放 Deferred，必须另外
定义拖动期间的活动项、释放时跳转和用户输入事务，不能只转发一个 bool 后结束设计。

首版固定 `BringIntoViewOnFocusChange=false`。内容区域内的控件仍可获得焦点，但通过 Tab、
代码 `Focus()` 或其它焦点变化不能间接驱动 `PART_ContentScrollViewer` 滚动，也不参与活动
Section 计算或 Marker 选择。首版不承诺把视口外的新焦点自动带入可视范围；需要定位 Section
的业务代码首版不能依赖 `Focus()` 间接完成。未来增加程序化定位时，必须提供 ScrollMarker
语义导航 API，不能把焦点当作像素滚动 API。

该限制只针对主内容视口，不取消 `ScrollMarkerNavigator` 自身已经定义的键盘浏览、
`ScrollIntoView(index)` 和 Marker 激活行为。后续若开放内容焦点 BringIntoView，必须将它定义
为独立滚动来源，并补齐与 Explicit 导航、活动 Section、无障碍通知之间的仲裁规则。

### 与 Navigator ScrollViewer 隔离

Orientation 与 `MainScrollBarVisibility` 只投影到 `PART_ContentScrollViewer`。
`PART_NavigatorScrollViewer` 使用独立内部滚动策略；默认模板或控件代码必须显式设置其关键
滚动属性，避免主内容配置或全局 ScrollViewer Style 意外改变 Marker Track。

```text
Host Orientation + MainScrollBarVisibility
                         └────> PART_ContentScrollViewer
                         X────> PART_NavigatorScrollViewer
```

### 不属于公共滚动控制面的状态

首版不公开：

- 设置在 `ScrollMarkerView` 上的 `ScrollViewer.HorizontalScrollBarVisibility` 和
  `ScrollViewer.VerticalScrollBarVisibility`；这两个物理轴 Attached Property 不参与投影。
- `AllowAutoHide`、`IsLiteMode`、`IsScrollInertiaEnabled`、`IsScrollChainingEnabled`、
  `IsDeferredScrollingEnabled` 和 `BringIntoViewOnFocusChange`。
- 可写 `Offset`。
- `Extent`、`Viewport`、`CurrentAnchor` 等运行时内部状态。
- 水平或垂直 ScrollBar 实例。
- `LineUp`、`PageDown`、`ScrollToEnd` 等命令式方法。
- Content、ControlTheme 或 ScrollViewer 模板替换。
- Avalonia SnapPoints 和第二套 Anchor 注册机制。

这些能力会绕过 `Automatic/Explicit`、滚动来源识别、`AnchorOffset` 或唯一主视口约束。未来
如果需要程序化导航，应设计 `NavigateTo(anchorKey)` 等语义 API，而不是公开像素级内部
ScrollViewer 控制权。

## ScrollMarkerItem

`ScrollMarkerItem` 是自动生成的可交互容器。开发者不应为了建立导航关系手工创建它。

默认 Item 分离视觉尺寸和交互尺寸：

```text
ScrollMarkerItem 命中区域：至少 24 × 24 DIP
└─ 可见 Marker：默认约 8 × 8 DIP
```

小圆点不能直接充当完整命中区域。否则 Marker 数量稍多时将难以准确点击，也无法形成清晰的键盘焦点轮廓。

目标视觉属性：

| 属性 | 类型 | 语义 |
|---|---|---|
| `Shape` | `ScrollMarkerShape` | 默认 `Circle`；内建 `Circle`、`Square` |
| `MarkerSize` | `double` | 可见 Marker 的理想尺寸，不等于命中区尺寸 |
| `Background` | `IBrush?` | 可见 Marker 背景 |
| `BorderBrush` | `IBrush?` | 可见 Marker 边框画刷 |
| `BorderThickness` | `Thickness` | 可见 Marker 边框粗细 |
| `CornerRadius` | `CornerRadius` | Square 形状的可选圆角 |

内建 Shape 只覆盖高频形状。胶囊、菱形、图标、数字或文本 Marker 通过 `ControlTheme` 重做模板，不继续扩充 Shape 枚举。

默认主题状态：

```text
:pointerover
:pressed
:selected
:focus-visible
```

`:selected` 表示当前活动 Section。当前设计不包含 Cluster，因此不定义 `:clustered` 状态。

## 统一主题与单项覆盖

统一外观通过 Host 的 `ItemContainerTheme` 提供。Direct 示例：

```xml
<atom.labs:ScrollMarkerView
    ItemContainerTheme="{StaticResource DocumentMarkerTheme}">
    ...
</atom.labs:ScrollMarkerView>
```

单个 Section 可以完整替换对应 Item 的 ControlTheme：

```xml
<atom.labs:ScrollMarkerSection
    AnchorKey="warning"
    Label="重要警告"
    MarkerTheme="{StaticResource WarningMarkerTheme}">
    ...
</atom.labs:ScrollMarkerSection>
```

主题优先级：

```text
Section.MarkerTheme
        ↓ 未设置
Host.ItemContainerTheme
        ↓ 未设置
AtomUI.Labs 默认 ScrollMarkerItem 主题
```

`MarkerTheme` 是完整主题替换，不是与全局主题进行隐式属性合并。自定义主题需要为 `ScrollMarkerItem` 提供有效模板，并处理必要的交互状态。推荐通过 `BasedOn` 复用默认 Item Theme 后只覆盖需要变化的 Setter。

不在 `ScrollMarkerSection` 上增加 `MarkerHoverBackground`、`MarkerSelectedBorderBrush` 等大量状态属性。状态视觉属于 ControlTheme。

## 为什么不使用 Cluster

有限空间无法同时保证：

```text
A. 所有 Marker 同时可见
B. Marker 使用稳定、统一且足够大的槽位
C. 每个 Marker 独立、具有足够命中区且互不重叠
```

当前设计选择：

- 保留 Marker 的独立身份、命中区域和稳定顺序。
- 使用统一槽位表达 Section 次序，不表达 Section 之间的实际像素距离。
- 放弃“所有 Marker 必须同时出现在导航条可见区域”。

当 Marker 超过可见容量时，Track 的逻辑长度可以大于 Navigator Viewport：

```text
全部逻辑 Marker：

01 02 03 04 ... 38 39 40 41 42 43 44 ... 98 99 100
                 └── 当前可见窗口 ──┘

界面：

┌──────── Navigator Viewport ────────┐
│ 38  39  40  41  42  43  44       │
└────────────────────────────────────┘
```

不创建：

- 多个锚点合并后的数量按钮。
- Cluster 展开列表。
- 重叠 Marker 的后绘制抢占。
- 静默隐藏且无法访问的 Marker。

## Marker 逻辑轨道

### 位置语义

Marker 只表达 Section 的稳定先后顺序，不表达 Section 在主 Content 中的像素位置或滚动比例：

```text
主 Content：

A B C                                      D
│ │ │                                      │
▼ ▼ ▼                                      ▼

Marker Track：

┌────────┬────────┬────────┬────────┐
│   A    │   B    │   C    │   D    │
└────────┴────────┴────────┴────────┘
```

因此：

- `SectionStart` 继续用于 Section 排序、Automatic 活动判定和 Marker 激活后的目标滚动偏移。
- `SectionStart` 和 `EffectiveTargetOffset` 不进入 Marker 视觉坐标公式。
- 内容高度增长但 Section 稳定顺序不变时，已有 Marker 的顺序不变。
- 同坐标 Section 仍各自生成独立 Marker，并按稳定内容树顺序占用相邻槽位。

`SectionStart` 的完整定义见
[ScrollMarkerSection Direct Content 设计](section-design.md#sectionstart-坐标定义)。

### 统一槽位

`ScrollMarkerTrackPanel` 沿逻辑主轴把全部 Marker 放入等长槽位。实现范围内的
`ScrollMarkerItem` 占据自己的槽位，Item 模板把可见 Marker 居中放入命中区域；Item
不允许跨槽位覆盖相邻 Item。

```text
逻辑 Track：

┌────────────┬────────────┬────────────┬────────────┐
│     A      │     B      │     C      │     D      │
└────────────┴────────────┴────────────┴────────────┘
<----------->  一个 EffectiveMarkerSlotExtent
```

槽位最小长度必须在任何 Marker 容器实现之前确定，不能通过测量全部 Item 的 DesiredSize
反推。否则虚拟化范围外的 Marker 没有容器，也就没有 DesiredSize。

```text
MinimumSlotExtent =
    max(
        DefaultMinimumHitTargetExtent,
        SanitizePositive(MarkerSlotExtent)
    )
```

然后根据有效 Marker 数量、开发者上限和 Marker Viewport 长度确定统一槽位。这里的
`MarkerViewportExtent` 是已经扣除外层 `NavigatorPadding`、前后相邻导航按钮及其间距后的主轴
长度：

```text
MarkerCount > 0：

CapacityByGeometry =
    max(
        1,
        floor(MarkerViewportExtent / MinimumSlotExtent)
    )

EffectiveVisibleMarkerCount =
    min(
        MarkerCount,
        MaxVisibleMarkerCount,
        CapacityByGeometry
    )

EffectiveMarkerSlotExtent =
    max(
        MinimumSlotExtent,
        MarkerViewportExtent / EffectiveVisibleMarkerCount
    )

MarkerTrackExtent =
    MarkerCount * EffectiveMarkerSlotExtent

MarkerSlotStart[i] =
    i * EffectiveMarkerSlotExtent
```

`MaxVisibleMarkerCount` 是上限，不是强制压缩数量。当可用空间按最小合法命中尺寸只能容纳
4 项时，即使开发者配置为 6，`EffectiveVisibleMarkerCount` 也只能是 4。实现不得为了塞入
6 项而缩小命中区；相邻导航按钮的索引步长始终是 1，与有效可见容量无关。

是否溢出先以不保留相邻导航按钮的可用空间计算；没有溢出时两个按钮整体折叠，Marker Viewport
取得完整长度。确认溢出后，两个按钮共同参与布局，再以扣除按钮后的实际
`MarkerViewportExtent` 计算最终容量。默认模板必须至少为两个按钮和一个合法 Marker 槽位
提供期望尺寸；父布局强行给出更小空间时仍不能压缩 Marker 命中区。

Track 只包含真实 Marker 对应的稳定槽位，不补分页空槽，也不因活动 Marker 到达尾部而重新
拉伸剩余 Marker。尾部视口位置由 ScrollViewer 的合法 Offset 规整自然决定。

这套规则产生两种自然状态：

```text
Marker 较少，可以放入 Viewport：

┌──────────── Navigator Viewport ────────────┐
│     A          B          C          D     │
└────────────────────────────────────────────┘
TrackExtent = ViewportExtent

Marker 较多，不能放入 Viewport：

┌────────────── 可增长的逻辑 Track ───────────────────┐
│ A │ B │ C │ D │ E │ F │ G │ H │ I │ J │ K │ L │
└──────────────────────────────────────────────────────┘
          └──── Navigator Viewport ────┘
TrackExtent > ViewportExtent
```

Marker 太多时必须增长 Track，不能缩小 Item 命中区、让 Item 重叠、隐藏部分 Marker
或合并成 Cluster。

### 有界可见窗口与相邻导航按钮

`MaxVisibleMarkerCount` 只约束用户肉眼同时可见的 Marker 数量，不裁剪逻辑集合，不改变
MarkerDescriptor、Section 映射或 Marker 虚拟化模型：

```text
100 个 MarkerDescriptor
        -> 100 个稳定逻辑槽位
        -> 最多显示 EffectiveVisibleMarkerCount 个 Marker
        -> VirtualizingStackPanel 只实现可见范围、缓存和框架保留容器
```

因此“最多显示 6 个”不等于运行时严格只能存在 6 个 ScrollMarkerItem。缓存、焦点和
ScrollIntoView 可以让框架临时保留额外容器，但 Viewport 中可见 Marker 不能超过有效容量。

Vertical 模式在逻辑 Start/End 使用向上和向下按钮；Horizontal LTR 模式使用向左和向右
按钮。默认主题使用 AtomUI `ButtonType.Text`，保留 24 DIP 交互命中区，但不绘制常驻的实体
按钮边框和底色；方向标识使用 AtomUI AntDesign 的 `UpOutlined`、`DownOutlined`、
`LeftOutlined` 和 `RightOutlined` 矢量 Icon，并进入 AtomUI Button 的 `:icon-only` 布局槽位，
服从标准 Icon 尺寸且在 24 DIP 背景命中区内水平、垂直居中，不能依赖字体中的 Unicode 箭头
字形，同时保留 AtomUI Text Button 的交互反馈：

```text
Vertical                         Horizontal

┌──────────┐                    ┌────┬───────────────────────┬────┐
│    ▲     │ Previous          │ ◀  │ ●  ●  ●  ●  ●  ● │ ▶  │
├──────────┤                    └────┴───────────────────────┴────┘
│    ●     │                           Previous        Next
│    ●     │
│    ●     │ Marker Viewport
│    ●     │
│    ●     │
│    ●     │
├──────────┤
│    ▼     │ Next
└──────────┘
```

相邻导航严格基于当前活动项的稳定索引，不依赖 Navigator 当前 Offset 或可见窗口边界：

```text
PreviousTargetIndex = ActiveIndex - 1
NextTargetIndex     = ActiveIndex + 1
合法范围            = [0, MarkerCount - 1]
```

例如有效可见容量为 6、当前活动 Marker 为第 6 个（零基索引 5）时，Next 的目标是第 7 个
（零基索引 6），绝不按照可见容量跳过中间 Marker。Coordinator 使用与点击 Marker 相同的 Explicit
导航事务：立即定位目标 Section、更新活动与 selected Marker，并令 Navigator 恢复 Follow，
使新的活动 Marker 进入可见安全区。主内容的 Explicit 定位始终立即完成，不播放平滑滚动；当
Previous/Next 的相邻目标越过当前 Marker Viewport 的 Start 或 End 边缘时，Navigator 单独复用
自动 Follow 的约 160ms Track ease-out 过渡，用视觉位移说明 Marker 窗口刚刚移动的方向。目标
邻居仍完整位于当前 Marker Viewport 内时不移动 Track，也不伪造位移动画。直接点击 Marker、
远距离跳转以及其他 Explicit 导航仍立即呈现最终 Navigator 位置。

即使用户先通过滚轮、触控或拖动让 Navigator 处于 Browse，点击 Previous/Next 也以活动
Marker 而非首个可见 Marker 为基准，并放弃手动 Browse Offset。按钮不维护页索引、页偏移或
分页缓存。

发生溢出时两个按钮同时保留布局位置：活动项为第一个 Marker 时禁用 Previous，活动项为最后
一个 Marker 时禁用 Next，其余位置两个按钮都可用；边界按钮只禁用，不能折叠，否则 Marker
Viewport 会改变长度并导致槽位跳动。
没有溢出时两个按钮整体折叠。0 个或 1 个有效 Section 仍遵守 Host 计数规则，整个 Navigator
隐藏。

直接滚轮、触控、拖动和键盘 Browse 继续使用现有 Marker Track；相邻导航按钮属于 Section
语义导航，不把 Navigator 改造成独立数据分页器。自动 Follow 仍确保活动 Marker 可见；Browse
期间用户重新主动滚动主内容时，Navigator 放弃手动位置并回到活动 Marker。

`MarkerSlotExtent` 是布局和虚拟化共同使用的固定主轴度量。不同 MarkerTheme 不能通过未实现
Item 的 DesiredSize 改变 Track；自定义模板必须在槽位内布置可见内容，需要更大命中区域时
由开发者统一增大 `MarkerSlotExtent`。

Navigator 必须把 `EffectiveMarkerSlotExtent` 作为生成容器的确定主轴尺寸传入布局：

- Vertical 时，已实现 `ScrollMarkerItem` 的布局 `Height` 等于有效槽位长度。
- Horizontal 时，已实现 `ScrollMarkerItem` 的布局 `Width` 等于有效槽位长度。
- 交叉轴由 Navigator 厚度和拉伸规则决定，不允许 MarkerTheme 反向改变 Track 主轴度量。
- Item 模板把 `PART_Marker` 布置在槽位内部；Item 边界负责裁剪，主题内容不能覆盖相邻槽位。
- Marker 数量、Viewport、方向或 `MarkerSlotExtent` 改变时，更新已实现容器的槽位约束并
  使 Panel 重新测量；不能为了取得槽位尺寸预先实现全部 Item。

主轴上的统一槽位与交叉轴上的 Navigator 厚度是两套独立测量。Horizontal 和 Vertical
使用同一逻辑公式；首版 LTR 横向模式不执行 X 坐标或 Offset 反转。

### Marker 容器实现范围

Navigator 持有全部轻量 MarkerDescriptor，但由 `VirtualizingStackPanel` 只实现当前
EffectiveViewport 与 `CacheLength` 覆盖的 `ScrollMarkerItem`。以下公式描述正常情况下的
预期连续范围，不是对 Avalonia 实际容器集合的严格等式：

```text
FirstVisibleIndex =
    floor(NavigatorOffset / EffectiveMarkerSlotExtent)

LastVisibleIndexExclusive =
    ceil(
        (NavigatorOffset + NavigatorViewportExtent)
        / EffectiveMarkerSlotExtent
    )

CacheExtent =
    NavigatorViewportExtent * 0.5

ExtendedStart =
    max(0, NavigatorOffset - CacheExtent)

ExtendedEnd =
    min(
        MarkerTrackExtent,
        NavigatorOffset + NavigatorViewportExtent + CacheExtent
    )

ExpectedFirstIndex =
    floor(ExtendedStart / EffectiveMarkerSlotExtent)

ExpectedLastIndexExclusive =
    ceil(ExtendedEnd / EffectiveMarkerSlotExtent)

ExpectedRealizedRange =
    clamp(
        [ExpectedFirstIndex, ExpectedLastIndexExclusive),
        [0, MarkerCount)
    )
```

`ExpectedRealizedRange` 表示 Viewport 与缓存产生的预期连续索引范围。Avalonia 可以额外保留
已经离开该范围但仍持有键盘焦点，或正参与 `ScrollIntoView` 的容器：

```text
ActualRealizedContainers =
    ExpectedRealizedRange 对应容器
    + FrameworkRetainedContainers
```

因此实现和测试都不能把 K 或实际容器集合写成严格连续、严格固定的数量。容器离开预期范围后
可以回收，但 selected 身份、Label、Theme 和自动化状态以 MarkerDescriptor 和
SelectionModel 为准，不能依赖被回收的 Control 实例保存。

### Marker 容器生命周期与状态所有权

`ScrollMarkerNavigator : SelectingItemsControl` 的逻辑 Items 始终是内部
`MarkerDescriptor`。Navigator 使用 Avalonia 标准容器生命周期统一生成
`ScrollMarkerItem`：

1. `NeedsContainerOverride` 和 `CreateContainerForItemOverride` 始终选择同一种
   `ScrollMarkerItem` 容器和稳定回收键；开发者不能向内部 Items 混入手工 Item。
2. `PrepareContainerForItemOverride` 每次完整应用当前 Descriptor 的 AnchorKey、Label、
   MarkerTheme、有效状态、自动化信息、方向与槽位尺寸，并从 SelectionModel 恢复 selected。
3. `ClearContainerForItemOverride` 清除旧 Descriptor、单项 Theme、Label、Tooltip、自动化名称、
   槽位元数据、事件或命令关联，以及 pressed/pointerover 等临时交互状态。

状态所有权必须保持：

```text
MarkerDescriptor / Coordinator
    -> 身份、Label、单项 Theme、Section 映射和导航资格

SelectionModel
    -> selected 身份和当前索引

ScrollMarkerItem
    -> 当前呈现、临时 Pointer 状态和键盘焦点
```

Coordinator 是 `ActiveAnchorKey`、`ActiveSourceIndex` 和 SelectionModel 的唯一写入者。Navigator
用户激活、Direct/Virtual Automatic 和容器 Prepare 都必须携带内部 `SelectionCommitOrigin`
进入同一提交路径；`AutomaticContent` 与 `ContainerProjection` 引起的 SelectionChanged 不能
反向启动 Marker 导航。活动身份未变化时不重复产生 SelectionChanged、Follow 或自动化通知。

同一个回收容器可以先呈现 Marker A，随后呈现 Marker K。Prepare/Clear 不能假设容器是新建
实例，也不能让旧 Marker 的颜色、Label、选择、自动化或事件订阅泄漏到新 Marker。

### 确定性与更新

槽位算法必须满足：

1. 使用 Section 的稳定内容顺序，不使用注册或异步创建先后。
2. 相同输入产生相同 TrackExtent 和 Marker 坐标。
3. 结果只能包含有限非负坐标。
4. 普通主内容 Offset 变化不重新 Measure Marker Track。
5. Marker 数量、稳定顺序、Navigator Viewport 或 `MarkerSlotExtent` 变化时重新布局。
6. Section Bounds 变化但稳定顺序未变时，只更新目标偏移和活动判定，不因内容像素变化移动
   Marker 槽位。
7. 普通 Navigator Offset 变化只让框架更新 EffectiveViewport、预期实现范围和容器映射，
   不重新测量全部逻辑 Marker。

## 自动跟随

Navigator 默认处于 Follow 状态。

Follow 不把主内容滚动百分比直接映射成 Navigator Offset，而是跟随当前活动 Marker：

```text
1. 取得当前 :selected Marker 的最终槽位。
2. 如果 Marker 完整位于 Navigator 安全区内，保持当前 Navigator Offset。
3. 如果 Marker 越过安全区逻辑 Start，只向 Start 修正到刚好重新进入安全区。
4. 如果 Marker 越过安全区逻辑 End，只向 End 修正到刚好重新进入安全区。
5. 最终 Offset 范围钳制到 [0, max(0, MarkerTrackExtent - NavigatorViewportExtent)]。
```

示意：

```text
Navigator Viewport

┌──────────────────────────────────────┐
│ 边缘区 │       活动安全区       │ 边缘区 │
└──────────────────────────────────────┘
                 ● selected
```

这是一种“最小必要移动”，不是每次活动项变化都强制把 Marker 居中。它能减少主内容滚动时
Navigator 的跳动，也能在连续活动项之间保留视觉上下文。

Track 没有溢出时 Navigator Offset 固定为 0。进入 Follow、退出 Browse、Marker Track
重新布局或活动 Marker 变化时，都执行同一安全区校正，不创建第二套定位规则。

活动 Marker 发生远距离变化时，Follow 根据稳定索引直接计算目标槽位：

```text
TargetSlotStart = TargetIndex * EffectiveMarkerSlotExtent
```

Navigator 的远距离 Follow 一次设置安全区校正后的最终 Offset，不逐个实现中间 Marker，也不播放
跨越中间 Marker 的滚动动画。连续变化只处理最新目标；自动 Follow 可以更新 selected 和
Navigator Viewport，但不能移动键盘焦点。Browse 期间继续暂停 Follow，退出 Browse 后立即跳到
当前活动 Marker。

当主内容的 Automatic 活动项只向相邻 Marker 变化，且活动 Marker 越过 Navigator Viewport
边缘时，Navigator 对“最小必要 Offset 修正”播放约 160ms 的短距离 ease-out 过渡。它用于明确
展示 Track 移动方向，使新活动 Marker 从 Start 或 End 边缘进入，避免可见容量末位持续点亮所造成
的身份错觉。Navigator 先使用标准 `ScrollIntoView(index)` 一次性实现目标容器并提交正确的最终
Offset，再用 Track 的反向视觉位移到零来表达刚刚发生的最小移动；不得逐帧写 ScrollViewer
Offset，否则未实现目标项时会受到虚拟化 Extent 钳制。

过渡只作用于 Navigator Track，不延迟主内容滚动、selected 更新或最终 Offset。连续 Automatic
变化取消旧视觉动画，继承其当前视觉位移并直接重定向到最新相邻目标，不排队。Previous/Next
使用相同机制，但仅限其相邻目标确实越过 Marker Viewport 边缘的情况；按钮点击后主内容仍立即
定位，Navigator 先提交最终 Offset，再从反向视觉位移回到零。Marker 点击、进入或退出 Browse、
非相邻变化以及远距离跳转会取消并清零视觉位移，立即保留最终位置。任何路径都禁止为了视觉
动画逐个实现或经过中间 Marker。

安全区的具体比例需要由 Gallery 原型和交互测试量化。第一版不把它暴露为公共属性，避免在缺少验证时固化 API。

## Marker Track 实现验证

本节只列出共享 Navigator 的核心验收摘要。完整的固定输入、Headless 测试拓扑、容器观测、
远距离跳转、状态复用、Benchmark 场景和失败判定见
[Marker 虚拟化验证设计](marker-virtualization-verification.md)。该文档定义目标验证契约，
不表示测试或性能结果已经产生。

实现阶段至少验证：

- Marker 数量能够容纳在 Viewport 中时，TrackExtent 等于 ViewportExtent，所有 Marker
  使用相等槽位均匀铺开。
- Marker 数量超过可见容量时，TrackExtent 随数量增长，Item 命中区域不缩小、不重叠。
- 主轴 DesiredSize 不同的 MarkerTheme 同时存在时，未实现 Item 不参与 Track 测量；所有
  Item 模板都受同一个 `MarkerSlotExtent` 约束。
- 同坐标父子 Section 仍按稳定内容顺序取得相邻槽位，彼此独立可点击。
- Section 流式增长但数量和顺序未变时，Marker 槽位不随 Section 像素位置移动。
- Marker 数量、稳定顺序、ViewportExtent 或 `MarkerSlotExtent` 变化后，只执行必要的 Track
  重新布局，不重新创建未变化的 Item。
- `MarkerSlotExtent` 为负数、`NaN` 或 Infinity 时回退到有效默认值，不产生非有限布局结果。
- Navigator Offset 变化时通常只实现 Viewport + `CacheLength` 对应范围；允许框架保留焦点或
  ScrollIntoView 特殊容器，回收复用不能丢失或串用 selected、Theme、Label 和自动化状态。
- 从第 5 项远距离跟随到第 5000 项时，只回收旧范围并实现目标附近范围，不生成中间 Marker；
  自动 Follow 立即设置最终 Offset，且不抢夺键盘焦点。
- Vertical 和 Horizontal 在 LTR 下使用相同逻辑槽位公式；Horizontal 的 Marker 按稳定顺序
  从左向右排列，Navigator Offset 与 `Offset.X` 同向。
- Follow 只做使活动 Marker 回到安全区的最小 Offset 修正；Track 未溢出时 Offset 始终为 0。
- Automatic 相邻活动项和 Previous/Next 相邻目标越过可见边缘时只过渡最小 Offset 修正；测试
  必须证明向 End 连续越界的
  第 6→7、7→8、8→9，以及活动项走回并越过 Start 边界时，都经过非零 Track 中间位移并最终
  归零，而不是在同一屏幕坐标瞬间替换 selected 身份。仍位于窗口内部的反向相邻项不得强制移动
  Track。Vertical 与 Horizontal 都必须覆盖；普通 Explicit 与远距离 Follow 必须取消该过渡，
  Previous/Next 是 Explicit 中唯一允许复用相邻 Track 过渡的入口。
- Browse 期间布局更新不抢夺用户的 Navigator Offset；退出 Browse 后恢复同一套 Follow 校正。

## 手动 Browse

Navigator 允许用户独立滚动，以寻找当前窗口外的 Marker。

状态转换：

```text
Follow
  └─ 用户滚动/拖动/键盘浏览 Navigator
       -> Browse

Follow 或 Browse
  └─ 点击 Previous/Next 相邻导航按钮
       -> 以 ActiveIndex ± 1 发起 Explicit Section 导航
       -> Follow

Browse
  ├─ 用户激活某个 Marker
  │    -> 导航主内容
  │    -> 活动项进入 Explicit(anchorKey)
  │    -> Follow
  ├─ 用户重新主动滚动主内容
  │    -> 活动项进入 Automatic
  │    -> Follow
  └─ Escape
       -> 放弃手动浏览位置
       -> Follow
```

Browse 期间：

- 主内容的布局变化或非用户 Offset 更新不能与用户争夺 Navigator Offset。
- Marker 不能因为后台自动跟随从当前指针或键盘焦点下移走。
- 业务 Section 和 Marker 注册关系保持不变。
- 用户激活 Marker 后，主 ScrollViewer 滚动到对应 Section，Navigator 再恢复跟随。

“重新主动滚动主内容”遵循两阶段边界：MarkerNavigation 活跃时，未被局部 ScrollViewer 消费的
合格主轴用户意图立即取消导航并进入 Automatic；状态已经 Idle 时，只有主 Offset 实际变化才
从 Explicit 进入 Automatic。Navigator Browse、普通 Measure/Arrange、窗口缩放或主题更新不
属于该条件。

Marker 激活造成的主 ScrollViewer OffsetChanged 属于内部导航事务，不能被误判成“重新主动
滚动主内容”，否则 Explicit 会在刚建立后立即失效。用户滚动来源见
[滚动来源](section-design.md#滚动来源)；布局范围钳制后的锁定判据见
[布局范围钳制与 Explicit](section-design.md#布局范围钳制与-explicit)。

## 输入与可访问性

Marker 必须支持鼠标、触控和键盘：

| 输入 | 行为 |
|---|---|
| 点击或轻触 Marker | 导航到对应 Section |
| 方向键 | Vertical 使用上/下键，Horizontal 使用左/右键，按 LTR 稳定顺序移动 Marker 焦点 |
| `Home` / `End` | 移动到第一个或最后一个 Marker |
| `Enter` / `Space` | 激活当前焦点 Marker |
| `Escape` | 退出 Browse，恢复 Follow |
| Navigator 上的滚轮、触控或拖动 | 浏览 Marker Track，不滚动主内容 |
| Previous/Next 相邻导航按钮 | 定位到当前活动 Marker 的前一/后一稳定邻居，立即滚动主内容、更新 selected 并恢复 Follow；越过 Marker Viewport 边缘时播放短 Track 过渡 |

普通鼠标滚轮通常只提供纵向 `Delta.Y`。当 Navigator 为 Horizontal 时，控件必须把作用在
Marker Viewport 上的主导滚轮增量映射为 Navigator `Offset.X`，每个标准滚轮刻度移动一个有效
Marker 槽位；高精度触控板的分数增量按比例移动。该输入只进入 Navigator Browse，不能滚动
主内容，也不能通过 Scroll Chaining 继续传给外层视口。Horizontal 触控板原生 `Delta.X` 使用
同一条路径，若同时存在 X/Y 增量则采用绝对值较大的主导轴。

自动化名称优先使用 MarkerDescriptor 的 Label，缺失时回退到 `AnchorKey`。Direct 模式的
Descriptor 来自 `ScrollMarkerSection`；Virtual 模式通过已确认的三个 `BindingBase` 属性在
数据项入列时生成 Descriptor 快照，见[Virtual Items 设计](virtual-items-design.md)。
活动 Marker 需要向自动化系统暴露当前选择状态，不能只依赖颜色变化。

相邻导航按钮必须暴露“上一个 Marker”和“下一个 Marker”的自动化名称及边界禁用状态，不能
只使用无语义箭头。Marker Label 和按钮 Tooltip 元数据仍保留在对应控件上，但首版默认
Marker Theme 及两个按钮模板实例分别设置 `ToolTip.ServiceEnabled=false`，因此鼠标悬停
Navigator 时不弹出 Tooltip；该 UI 抑制不能清空 Label、自动化名称或 Tooltip 数据，也不能影响
主内容区域中开发者自己的 Tooltip。触控命中区域不得小于默认最小命中尺寸，视觉图标可以小于
命中区域。

焦点轮廓不能被 Navigator Clip 裁掉。默认主题必须同时考虑普通主题、高对比度和减少动效设置。

方向键到达第一个或最后一个 Marker 后停在边界，不执行首尾循环。方向键、`Home` 或 `End`
把焦点移向尚未实现的 Marker 时，Navigator 调用标准 `ScrollIntoView(index)`，等待布局实现
目标容器后再聚焦；该键盘操作进入 Browse，但不会激活 Section，只有 `Enter` 或 `Space`
执行激活。

已经离开 `ExpectedRealizedRange` 但仍持有键盘焦点的 Item，允许
`VirtualizingStackPanel` 暂时保留。Automatic 活动变化和自动 Follow 只能改变 selected，
不能把焦点从用户当前控件抢走。

## 动态生命周期

Direct Section 增加、删除、重排、有效可见性、稳定布局快照或主视口尺寸变化时：

```text
Section Registry 或 IsNavigable 结果变化
  -> 更新稳定 Section 顺序和有效目标偏移
  -> 按统一槽位更新 Marker Track
  -> 保持 AnchorKey 到 Item 的稳定映射
  -> 使用活动 Marker 校正 Navigator 可见窗口
```

更新必须增量合并到当前状态，不能在每次主内容滚动时扫描完整视觉树或重新创建所有 Marker。
AnchorKey 相同的 MarkerDescriptor 尽可能保留原实例；新增、删除和重排分别映射为集合的
Add、Remove 和 Move，只有无法可靠形成增量差异时才使用 Reset。活动身份按 AnchorKey 保持，
不能依赖变化后的数组索引。

远距离 `ScrollIntoView` 或焦点请求同时记录 AnchorKey 与 Descriptor 集合版本。布局完成后重新
解析目标；期间目标移动、隐藏、删除或请求被更新目标替代时，旧请求必须失效，不能把焦点或
Offset 应用到另一个 Marker。

Section 流式增长只要没有改变稳定顺序，就不得使 Marker 因内容像素比例变化而重新分布。
Marker Track 只在 Marker 数量、顺序、测量尺寸、Viewport 或布局属性变化时重新布局。

Marker Track 的运行时布局结果可以在进程内缓存并按布局版本失效，但不提供开发者可见的 Cache 开关。

Section 的 Attach/Detach 注册、嵌套、导航资格和额外 ScrollViewer 边界见
[ScrollMarkerSection Direct Content 设计](section-design.md)。

Virtual Items 的追加、实现范围变化和容器复用走独立 Host Adapter，但输出同一稳定
MarkerDescriptor 序列。它不能把 ItemsSource 变化伪装为 Direct Section Attach/Detach。

## 不持久化布局

应用重启后，Direct Host 必须重新创建 Section、执行 Avalonia Measure/Arrange，并根据当前
Bounds 计算 Section 目标偏移和活动状态。Virtual Host 必须由应用重新提供 ItemsSource，
重建当前逻辑序列和实现范围。两种 Host 都根据当前稳定顺序重新生成 Marker 槽位。

这里的 Bounds 指换算到主 Content 布局坐标系的 Layout Bounds，不包含 Margin 前沿或
RenderTransform 后的临时视觉位置。

禁止持久化：

- Marker 像素坐标。
- Section 的历史 Bounds。
- Navigator Track 的历史像素长度。
- 控件实例或视觉树对象。

未来如果需要恢复用户位置，只应保存稳定 `AnchorKey` 和相对 Section 偏移；当前设计不提供跨进程 Cache API，也不让控件自行写文件、数据库或云存储。

## 待后续设计

以下内容尚未成为公共契约：

- Virtual Items 有界两阶段导航在目标问答场景中的原型和 Benchmark 验收结果。
- Navigator 安全区的具体比例。
- 默认 Marker、Padding、Spacing 和轨道厚度对应的 AtomUI Token。

这些项目需要在后续实现设计或 Gallery 原型中单独决策，不能由实现者临时猜测后反向写成既定设计。
