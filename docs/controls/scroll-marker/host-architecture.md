# ScrollMarker 双 Host 总体架构

> 文档状态：规范架构与首轮实现施工中，更新于 2026-08-02。本文固定 Direct Content 与
> Virtual Items 两种公共 Host 的边界和共享关系；Virtual Items 的元数据投影和框架原生内容
> 虚拟化基线已经固定，目标实现后的有界布局收敛与偏移确认也已经落入独立设计。

本文描述 `AtomUI.Labs.Controls.ScrollMarker` 首版的双 Host 架构。首要业务抽象是
`ConversationTurn`：一个 Question 与其 Answer 共同组成一个导航 Section，并且只对应一个
Marker。组件域仍覆盖任意内容树导航与可持续追加的虚拟内容序列，但不让一个根控件在运行时
切换两套互斥语义，也不在运行时推断业务内容是否真的是问答。

## 架构结论

首版提供两个公共根控件：

```text
AtomUI.Labs.Controls.ScrollMarker
│
├─ ScrollMarkerView : ContentControl
│  └─ Direct Content Host
│
├─ ScrollMarkerItemsView : ItemsControl
│  └─ Virtual Items Host
│
├─ ScrollMarkerSection : ContentControl
│  └─ 仅 Direct 中由开发者创建
│
├─ ScrollMarkerSectionContainer : ContentControl
│  └─ Virtual 中由 Items Host 生成或复用，仅内部可见
│
├─ ScrollMarkerItemsPanel : VirtualizingStackPanel
│  └─ Virtual 内容区域不可替换的内部 ItemsPanel
│
└─ ScrollMarkerItem : ContentControl, ISelectable
   └─ 两种 Host 共用的 Navigator 容器
```

`ScrollMarkerView` 与 `ScrollMarkerItemsView` 是两个独立类型，不提供
`ContentMode`、`UseVirtualization` 或 `EnableObjectPool` 开关。开发者通过选择根控件类型
明确选择内容模型。

两个公共类型不建立公共抽象基类：

- `ScrollMarkerView` 需要继承 `ContentControl`，承载任意单 Content。
- `ScrollMarkerItemsView` 需要继承 `ItemsControl`，取得 `ItemsSource`、`ItemTemplate`
  和容器生成语义。
- 公共属性通过 Avalonia Property owner 复用或等价的共享属性定义保持一致。
- 导航状态和行为通过内部组合复用，不复制两套 Coordinator。

## 总体模块图

```text
                    +----------------------------------+
                    | 共享 ScrollMarker 导航契约       |
                    | Orientation / Placement          |
                    | Inline / Overlay / AnchorOffset   |
                    | Theme / Input / Accessibility    |
                    +----------------+-----------------+
                                     |
             +-----------------------+-----------------------+
             |                                               |
             v                                               v
+------------------------------+             +--------------------------------+
| ScrollMarkerView             |             | ScrollMarkerItemsView          |
| Direct Content Host          |             | Virtual Items Host             |
| : ContentControl             |             | : ItemsControl                 |
+---------------+--------------+             +----------------+---------------+
                |                                             |
                v                                             v
+------------------------------+             +--------------------------------+
| Direct Content Adapter       |             | Virtual Items Adapter          |
| 实体 Section Registry        |             | ItemsSource / ItemTemplate     |
| Layout Bounds / SectionStart |             | Index / Realized Range         |
| ScrollToOffset               |             | ScrollIntoView + 布局确认      |
+---------------+--------------+             +----------------+---------------+
                |                                             |
                +----------------------+----------------------+
                                       |
                                       v
                    +----------------------------------+
                    | ScrollMarkerCoordinator          |
                    | MarkerDescriptor 映射            |
                    | Automatic / Explicit             |
                    | Follow / Browse                  |
                    +----------------+-----------------+
                                     |
                                     v
                    +----------------------------------+
                    | ScrollMarkerNavigator            |
                    | 虚拟化 ScrollMarkerItem          |
                    | 统一槽位 Marker Track            |
                    +----------------------------------+
```

图中的 Adapter 和 Coordinator 是内部职责边界。正式实现可以使用内部接口和组合对象，但
不能通过在两个公共 Host 中复制选择、导航、Marker Track 或输入逻辑实现共享。

这里的“共享 Coordinator”表示 `ScrollMarkerView` 与 `ScrollMarkerItemsView` 复用同一套内部
实现和行为契约，不表示应用级单例。每个 Host 控件实例必须拥有独立 Coordinator、状态机和
当前导航请求；单个 Host 同时最多有一个 CurrentRequest，更新请求使用 Generation 取代旧请求。

## Direct Content Host

`ScrollMarkerView` 保留当前已经确定的 Direct Content 设计：

```text
ScrollMarkerView
└─ Content
   └─ Grid / StackPanel / 任意业务内容树
      ├─ ScrollMarkerSection            ConversationTurn 1: Question + Answer
      ├─ 普通 Avalonia Control
      └─ ScrollMarkerSection            ConversationTurn 2: Question + Answer
```

Direct 模式的固定特征：

- 开发者显式创建 `ScrollMarkerSection`。
- Section 可以位于任意 Panel 中，也可以按既定规则嵌套。
- Section 使用 Attach、Detach 和 Reparent 维护实体 Registry。
- Section 排序、活动判定和目标偏移来自主内容坐标系中的 `SectionStart`。
- Marker 激活使用 `ScrollToOffset`，目标位置经过主 ScrollViewer 有效范围钳制（clamp）。
- 主 ScrollViewer 由 Host 模板保持内部所有；开发者通过 Host 的 Orientation 和语义属性
  `MainScrollBarVisibility` 配置主轴，不取得或直接配置内部实例。
- `ScrollMarkerSection` 是开发者拥有的实体控件，不被 ScrollMarker 回收或改绑到其它业务
  Content。
- Navigator 可以独立虚拟化 `ScrollMarkerItem`，但 Direct 模式不虚拟化业务 Section。

Direct 模式首先适合数量可控、由完整问答对组成的对话内容，也适合普通文档、设置页、FAQ、
报告、教程、Gallery 和需要任意内容树的页面。标准对话用法不把 Question 与 Answer 拆成两个
Section；其它场景由开发者确定完整语义内容单元。
详细契约见[ScrollMarkerSection Direct Content 设计](section-design.md)。

### Direct 性能与模式选择

Direct 模式不设置 Section 数量硬上限，但这不是大规模内容下的性能保证：

- Navigator 通过内部密封的 `ScrollMarkerTrackPanel : VirtualizingStackPanel` 通常只实现
  Viewport 与前后 `CacheLength` 缓存范围内的 `ScrollMarkerItem`；框架可以为键盘焦点或
  `ScrollIntoView` 暂时保留少量额外容器。
- 全部轻量 `MarkerDescriptor` 仍然存在。
- 开发者创建的全部 `ScrollMarkerSection` 及其业务 Content 仍然存在，并继续参与 Avalonia
  的对象生命周期和必要布局。

首版实现和 Benchmark 完成前，不规定“超过多少个 Section 必须切换模式”的固定阈值。预计
Section 数量非常大、持续增长、单项内容复杂或最终数量不可预估时，开发者应主动选择
`ScrollMarkerItemsView`。Host 不按运行时数量在 Direct 与 Virtual 之间自动切换，也不把
未经验证的经验数字固化为公共契约。

第一版完成后的 Benchmark 应分别记录 Section 数量、内容模板复杂度、视口、目标框架和测试
硬件。由此得到的建议是带条件的选型依据，不是控件的语义硬上限。

## Virtual Items Host

`ScrollMarkerItemsView` 面向数据驱动、可持续追加的大量内容：

```text
ItemsSource：当前有限且已知的 N 个 ConversationTurn（Question + Answer）
        │
        ├─ Content Virtualizer
        │  └─ ScrollMarkerItemsPanel : VirtualizingStackPanel
        │     └─ ScrollMarkerSectionContainer × (Viewport + Framework Cache)
        │
        └─ Navigator Virtualizer
           └─ ScrollMarkerTrackPanel : VirtualizingStackPanel
              └─ ScrollMarkerItem 容器
                 ≈ Viewport + 前后各 0.5 Viewport 缓存
                   + 少量框架特殊保留项
```

两条 Virtualizer 不能混为同一套机制。Content Virtualizer 已经固定使用内部密封的
`ScrollMarkerItemsPanel : VirtualizingStackPanel` 和内部
`ScrollMarkerSectionContainer : ContentControl`；其平均尺寸估算、`CacheLength`、连续实现
范围和回收池采用框架原生机制，首版不叠加第二套内容虚拟化引擎。共享 Navigator 的 Marker
虚拟化使用独立
`ScrollMarkerTrackPanel : VirtualizingStackPanel`，Direct 与 Virtual 两种 Host 共享 Marker
容器机制，但不共享内容 Panel。

首版 Virtual 模式的固定边界：

- 使用 `ItemsSource` 和必填 `ItemTemplate`，内容是一维扁平数据序列；ItemsSource 中的 Control
  立即失败，业务视觉只能由 ItemTemplate 按实现范围创建。
- 标准对话数据的一项是一整个 ConversationTurn；Question 和 Answer 由同一个 ItemTemplate
  呈现，并只生成一个 Descriptor、一个逻辑 Marker 和至多一个已实现 Section Container。
- 当前时刻的 Item 数量有限且可知，运行期间只允许向逻辑 End 持续追加。
- 不支持父子 Section 嵌套、任意视觉树扫描或 Grid 中自由摆放的 Section。
- `ScrollMarkerSectionContainer` 由 Items Host 根据框架 Viewport 和 `CacheLength` 自动实现与
  复用。
- 开发者不设置固定对象池大小，也不负责清空和改绑容器。
- 业务数据始终由 `ItemsSource` 或业务数据提供器持有；对象池不保存业务历史。
- Virtual 模式的稳定身份属于 MarkerDescriptor 和数据项，不属于可回收的
  `ScrollMarkerSectionContainer` 控件实例。
- Virtual Container 不进入 Direct Registry。容器复用前必须完成旧 Descriptor、SourceIndex 和
  临时视觉状态清理，随后才能绑定新数据；公共 Direct Section 的 AnchorKey 不可变契约不适用
  于该内部回收外壳。
- Virtual Container 使用统一 Create、Prepare、Clear 和 ContainerGeneration 契约；旧异步回调
  不能修改已经换绑到其它 SourceIndex 的容器，Clear 也不能删除业务数据或 Descriptor。
- Marker 激活先按稳定 Index 发出语义 `ScrollToIndex` 请求，由内容 Panel 落实为框架
  `ScrollIntoView(index)`；目标容器实现后按真实布局精确对齐，最多请求实现两次、写入精确
  Offset 两次。
- Virtual Automatic 只从当前正常实现范围构造 O(K) 原子布局快照；快照证明覆盖 AnchorOffset
  Probe 后才提交活动身份，不根据框架平均尺寸猜测 SourceIndex。
- `ContentEndFollowMode` 默认为 Automatic。追加前处于 FollowingEnd 时，逻辑 End 追加和尾部
  流式增长按有效 LayoutEpoch 跟随新终点；用户向 Start 浏览后进入 BrowsingHistory，停止主
  Offset 写入，直到用户主动回到 End。Disabled 永不主动跟随。
- ContentEndFollowState 与 Navigator Follow/Browse 正交；主 Offset 由 Coordinator 在
  UserInput、MarkerNavigation、ContentEndFollow 和框架校正之间串行仲裁。
- 无效投影、非 End Add、Remove、Move、Replace、Reset、运行期 ItemsSource 替换或内部映射
  不一致均属于开发者硬契约错误；当前 Host 先进入不可恢复的 Faulted，再抛出
  InvalidOperationException。首版没有恢复、回滚或降级运行入口。
- 逻辑 Marker 只对应当前已经存在的数据项，不为尚未追加的未来项预创建 Marker。

Virtual 模式首版不处理：

- 未知总量的任意远端 Index 导航。
- 从逻辑 Start 加载更早历史的双向分页。
- 同时向 Start 和 End 插入造成的滚动锚点保持。
- 嵌套虚拟 Section。
- 开发者可见的对象池、Cache 文件或持久化开关。

`AnchorKey`、`Label` 和 MarkerTheme 已经确定通过三个 `BindingBase` 属性在数据项入列时投影；
当前 N 个数据项对应 N 个轻量 Descriptor，有效 End 追加只增量生成新增 Descriptor。完整契约见
[Virtual Items 设计](virtual-items-design.md)。未测量 Item 采用框架平均尺寸估算，内容容器采用
框架原生实现与回收；实际布局后的 Offset 使用 LayoutEpoch 和 0.5 DIP 容差有界确认，目标场景
表现仍需原型和 Benchmark 验证。内容 End 跟随只开放 Automatic/Disabled 语义策略，不公开内部
ScrollViewer、Always 强制粘底或像素阈值。Faulted 实例只能由修正数据后的新 Host 实例替代；
替换 ItemsSource、重新应用模板或后续合法 Add 均不能复活原实例。

## 共享导航核心

两个 Host 必须共享以下语义：

```text
内容来源
    │
    ▼
稳定 MarkerDescriptor 序列
    │
    ├─ AnchorKey
    ├─ Label
    ├─ MarkerTheme
    └─ StableOrdinal
    │
    ▼
ScrollMarkerNavigator
    │
    ├─ 统一槽位 Marker Track
    ├─ VirtualizingStackPanel 原生容器虚拟化与复用
    ├─ ExpectedRealizedRange + 框架特殊保留项
    ├─ selected / focus / automation
    └─ Follow / Browse
```

共享契约包括：

- `Orientation`、`NavigatorPlacement`、`NavigatorDisplayMode` 和外层视觉属性。
- 首版只支持 `FlowDirection.LeftToRight`；两个 Host 均不承诺 RTL 的坐标、排列或输入行为。
- `AnchorOffset` 的活动判定与目标对齐语义。
- Marker 一对一身份、稳定顺序和统一槽位布局。
- Marker Track 使用内部密封的 `VirtualizingStackPanel`，`CacheLength` 首版内部默认为
  `0.5`；不公开虚拟化开关、对象池大小或可替换 ItemsPanel。
- MarkerDescriptor 和 SelectionModel 保存逻辑状态，回收的 `ScrollMarkerItem` 只作为呈现
  外壳，必须通过标准 Create/Prepare/Clear 生命周期完整换绑和清理。
- Automatic/Explicit 活动状态。
- Coordinator 是活动身份和 SelectionModel 的唯一写入者；Host Adapter、Navigator 和回收容器
  只能提交语义输入或接受状态投影，不能各自维护第二份 selected 真相。
- Navigator Follow/Browse、输入来源区分和无障碍行为。
- 远距离 Follow 根据稳定索引与固定槽位立即设置最终 Navigator Offset，不实现中间 Marker，
  不播放 Navigator 动画，也不抢夺键盘焦点。
- `ItemContainerTheme`、单项 MarkerTheme 和默认 Marker 状态。

Host Adapter 只负责回答：

```text
当前有哪些逻辑 Section？
它们的稳定身份和顺序是什么？
当前活动 Section 是谁？
如何导航到指定身份？
```

Navigator 不读取 Direct Section Bounds，也不直接操作 Virtual ItemsSource；Coordinator
不负责业务数据持久化。

### 统一通信契约

Direct 与 Virtual 的内容所有权不同，但 Navigator 的公共交互和内部通信语义必须一致。共享链路
固定为：

```text
ScrollMarkerNavigator
        │
        │ Marker 激活、Browse / Follow 输入
        ▼
ScrollMarkerCoordinator
        │
        │ 基于 AnchorKey + Generation 的导航意图
        ▼
Host Adapter
        ├─ Direct Content Adapter
        │      AnchorKey -> SectionStart -> ScrollToOffset
        │
        └─ Virtual Items Adapter
               AnchorKey -> SourceIndex -> 语义 ScrollToIndex
                         -> Framework ScrollIntoView(index)
                         -> 实现并测量目标容器 -> Offset 确认
```

Navigator 只能接收或产生以下逻辑信息：

- 接收当前稳定的 `MarkerDescriptor` 序列、活动 `AnchorKey` 和共享选择状态。
- 产生 Marker 激活、Navigator Browse、恢复 Follow 等语义输入。
- Marker 激活只表达“导航到这个稳定身份”，不携带 Direct Bounds、像素 Offset、ItemsSource
  数据项或已实现的 Virtual Container。

Coordinator 负责把这些输入纳入统一的 Automatic/Explicit、Follow/Browse 和 Generation 仲裁，
再将当前导航意图交给对应 Host Adapter。两个 Adapter 的执行方式不同，但不能自行建立第二套
Marker 选择、输入仲裁或 Navigator 状态机：

| 通信维度 | Direct Content Adapter | Virtual Items Adapter |
|---|---|---|
| 逻辑集合来源 | 实体 Section Registry | ItemsSource 投影后的 Descriptor 序列 |
| 稳定身份 | `ScrollMarkerSection.AnchorKey` | `MarkerDescriptor.AnchorKey` |
| 活动项依据 | `SectionStart` 与主内容 Offset | 当前正常实现范围的原子布局快照、Probe 覆盖证明与 SourceIndex |
| 导航执行 | 计算并立即写入有效 Offset | `ScrollToIndex` 经 `ScrollIntoView` 后等待实现、布局并确认 Offset |
| 对 Navigator 的结果 | 活动身份及导航事务结果 | 相同语义的活动身份及导航事务结果 |

Virtual 导航可能跨越多个布局周期，统一通信协议因此必须能表达：请求仍在执行、成功完成、被更新
Generation 取代，以及目标无效或无法完成而终止。Direct 即使通常可以立即写 Offset，也必须进入
同一类事务语义，不能使用只适用于同步路径的 `void ScrollToMarker(...)` 旁路 Coordinator。

本节固定行为边界，不提前固定内部 C# 接口名称、方法签名、同步或异步返回类型。具体
`IScrollMarkerHostAdapter` 是否存在、是否使用 `ValueTask`、完成回调或内部操作对象，仍由实现
原型决定；Virtual Items 的语义 `ScrollToIndex`、框架 `ScrollIntoView`、RealizingTarget、
LayoutEpoch 和有界 Offset 确认契约已经固定。任何实现都必须保留 AnchorKey、Generation 和
“最新请求优先”的既定语义。

### 公共 API 边界

两个公共 Host 不共享内容类型，但共享 Navigator 能力必须同名、同义，并通过 Avalonia Property
owner 复用或等价的共享属性定义保持一致：

| API 类别 | `ScrollMarkerView` | `ScrollMarkerItemsView` |
|---|---|---|
| 共享 Navigator API | `Orientation`、`NavigatorPlacement`、`NavigatorDisplayMode`、`AnchorOffset`、`ItemContainerTheme` 及外层 Navigator 视觉属性 | 与 Direct 同名、同默认值、同验证和交互语义 |
| 内容入口 | `Content` | `ItemsSource` + 必填 `ItemTemplate` |
| Marker 元数据入口 | `ScrollMarkerSection.AnchorKey`、`Label`、`MarkerTheme` | `AnchorKeyBinding`、`LabelBinding`、`MarkerThemeBinding` |
| 内容容器 | 开发者创建的公共 `ScrollMarkerSection` | Host 创建和复用的内部 `ScrollMarkerSectionContainer` |
| 内容导航能力 | Direct Section Registry 与像素 Offset | Virtual SourceIndex、框架目标实现与 Offset 确认 |

因此，“统一 API”只指 Navigator 的配置、身份和交互契约统一，不表示两个 Host 暴露相同的内容
属性，也不表示开发者可以在运行时把 Direct Host 切换成 Virtual Host。

## 生命周期隔离

同一个控件实例不会在 Direct 与 Virtual 之间切换：

```text
ScrollMarkerView
    -> 永远使用 Direct Content Adapter

ScrollMarkerItemsView
    -> 永远使用 Virtual Items Adapter
```

因此不存在以下运行时状态：

```text
Content 与 ItemsSource 同时生效
Direct Registry 与 Virtual Index 合并排序
实体 Section 被自动转移到虚拟对象池
切换模式时复用另一模式的内容视觉树
```

### Host 嵌套约束

首版禁止任意两个 ScrollMarker 根 Host 形成祖先与后代关系，不区分相同模式或不同模式：

```text
ScrollMarkerView
└─ ScrollMarkerView             禁止：Direct 包含 Direct

ScrollMarkerView
└─ ScrollMarkerItemsView        禁止：Direct 包含 Virtual

ScrollMarkerItemsView
└─ ScrollMarkerView             禁止：Virtual 包含 Direct

ScrollMarkerItemsView
└─ ScrollMarkerItemsView        禁止：Virtual 包含 Virtual
```

任一 Host 附加或运行时重新挂载时必须检查祖先链；发现 `ScrollMarkerView` 或
`ScrollMarkerItemsView` 祖先后立即抛出 `InvalidOperationException`。Debug 和 Release
行为一致，不能静默禁用内层 Host，也不能尝试合并两个 Coordinator、主滚动坐标系或
Follow/Browse 状态。

`ScrollMarkerSection` 不是根 Host。同一个 Direct Host 内允许 Section 包含 Section，嵌套
Section 继续扁平化为同一条 Marker 序列。但是，Section 的 Content 子树中不得出现
`ScrollMarkerView` 或 `ScrollMarkerItemsView`；Section 不能作为建立第二个完整滚动导航区域
的外壳。任一 Host 发现 `ScrollMarkerSection` 祖先时同样立即抛出
`InvalidOperationException`。

该约束不禁止：

- 两种 Host 作为兄弟控件出现在同一页面。
- 两种 Host 出现在应用的不同页面或互不构成祖先关系的布局区域。
- Direct Host 内按既定规则嵌套 `ScrollMarkerSection`。
- Section Content 内包含不参与外层锚点注册的普通局部 ScrollViewer。

## 文档边界

- [ScrollMarker 控件总览](overview.md)记录组件域定位和两个 Host 的 Golden Path。
- [ScrollMarkerSection Direct Content 设计](section-design.md)只描述实体 Section。
- [ScrollMarkerItemsView Virtual Items 设计](virtual-items-design.md)记录数据元数据投影、逻辑
  Descriptor、End 增量追加和终止性集合故障契约，并继续承载内容虚拟化专题。
- [导航条 UI 与交互设计](navigator-design.md)记录共享 Navigator，并明确标注 Direct 集成图。
- [Marker 虚拟化验证设计](marker-virtualization-verification.md)记录共享 Navigator 的算法测试、
  Avalonia Headless 结构门禁和 Release Benchmark 口径，不覆盖 Virtual Section 内容虚拟化。
- [Virtual Items 验证与极限性能门禁](virtual-items-verification.md)记录方案 A 内容虚拟化的测试
  实现、极限矩阵、性能阈值、发布资格和方案 B 强制重审条件。
- [平面结构图解](structure-visual-guide.md)是 `ScrollMarkerView` Direct 模式的附属资料。
- `ScrollMarkerItemsView` 已确认的元数据 API、增量投影和框架原生内容虚拟化基线已经落入
  专题；有界布局收敛状态机、End 跟随、终止性故障协议和目标验证门禁已经确定。测试、Runner、
  Gallery 和实测报告仍需随源码施工交付。
