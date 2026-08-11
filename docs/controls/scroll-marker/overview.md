# ScrollMarker 控件设计

> 文档状态：规范设计与首轮实现施工中，更新于 2026-08-02。仓库已经存在源码、测试、Gallery
> 和初版 Performance Runner；正式 Benchmark、长稳和人工验收尚未完成，不能据此宣称具备发布能力。

本文记录 `AtomUI.Labs.Controls.ScrollMarker` 的组件域设计。ScrollMarker 用于在长内容区域旁显示可交互的滚动锚点，并让用户快速跳转到明确标记的内容 Section。

## 文档导航

- [双 Host 总体架构](host-architecture.md)：记录 Direct Content 与 Virtual Items 两个公共
  Host 的边界、共享核心和首版关系。
- [导航条 UI 与交互设计](navigator-design.md)：记录共享 Navigator 的方向、呈现模式、Marker
  外观以及无 Cluster 的同步窗口化导航方案。
- [Marker 虚拟化验证设计](marker-virtualization-verification.md)：记录如何用算法测试、真实
  Avalonia Headless 布局和 Release Benchmark 证明 Marker 容器数量受 Viewport 约束。
- [Virtual Items 验证与极限性能门禁](virtual-items-verification.md)：记录方案 A 的内容虚拟化
  极限数据矩阵、严格正确性门禁、相对性能阈值、60 Hz 帧预算和方案 B 强制重审条件。
- [Section Direct Content 设计](section-design.md)：记录实体 Section 身份、增量注册、嵌套与
  滚动边界、AnchorOffset 和活动项判定。
- [Virtual Items 设计](virtual-items-design.md)：记录数据项元数据投影、逻辑 Descriptor 实体化、
  入列快照、逻辑 End 增量追加和内容 End 自动跟随契约。
- [Direct 平面结构图解](structure-visual-guide.md)：使用外层、中间层、内层俯视图解释
  `ScrollMarkerView` 的内容区域、导航条区域和 Section/Item 对应关系。
- 首版 Marker 激活立即设置最终主内容 Offset，不播放平滑动画；Direct 与 Virtual 的程序化导航
  状态机已经进入首轮实现。Marker 虚拟化已有 Headless 结构测试和初版 Runner，但正式
  Benchmark、长稳与真实桌面结果尚未产生，不能描述为已经通过发布门禁。

## 定位

ScrollMarker 不是普通 ScrollBar，也不是树形目录或菜单。

它表达的是：

```text
显式标记的内容 Section
  -> 注册为稳定锚点
  -> 生成对应 Marker
  -> Marker 按稳定顺序进入统一槽位 Track
  -> Navigator 可见窗口跟随当前活动 Marker
  -> 点击 Marker 跳转到对应 Section
```

### 首要业务抽象：ConversationTurn

ScrollMarker 的首要业务模型是持续增长的一问一答内容。设计文档统一把一次完整问答称为
`ConversationTurn`（问答对），它是标准用法中的最小 Section：

```text
Marker × 1
    ↕ 一一对应
ConversationTurn Section × 1
├─ Question                    SectionStart，标准定位点
└─ Answer                      属于同一 Section，不另建 Marker
```

一个 Marker 表示一轮完整问答，而不是一条独立消息。点击 Marker 后定位到承载该问答对的
Section 起始位置；Question 作为 Section 的第一部分，因此默认 `AnchorOffset=0` 时落到问题顶部。
Answer 可以流式增长，但只改变本轮 Section 高度和后续 Section 坐标，不增加第二个 Marker。

这是首要业务语义和标准示例约束，不是运行时可检查的内容类型。控件只识别 Section/Item、
`AnchorKey` 和布局坐标，不能推断任意 Avalonia 视觉树中的控件究竟是 Question 还是 Answer。
长文档、FAQ、设置页和报告仍可把一个完整语义内容单元视为等价 Section 使用。

目标场景包括：

- 只向末尾持续追加、以 ConversationTurn 问答对为单位的长对话内容。
- 长文档、设置页、教程、FAQ 和报告。
- 由多个语义区域组成的 Gallery 或工作台。
- 以完整语义事件为单位的事件流和日志视图。
- 主内容纵向排列的普通滚动页面。
- 主内容横向排列的时间线、画廊或横向文档。

### 首版语言与布局流向范围

首版面向现代中文和英文界面，受支持的有效布局流向限定为
`FlowDirection.LeftToRight`。控件不根据业务语言自动推断或切换流向。

该范围同时适用于 Vertical 和 Horizontal：

- Vertical 的逻辑 Start 为顶部，纵向 Navigator 的 Start/End 分别映射到左侧和右侧。
- Horizontal 的逻辑 Start 为左侧，逻辑 End 为右侧，Section 与 Marker 从左向右排列。
- Horizontal 使用 Section 外框换算到主 Content 布局坐标系后的 X 起点，使用主
  ScrollViewer 的 `Offset.X` 作为同向滚动偏移，不执行 RTL 坐标反转。
- `FlowDirection.RightToLeft` 不属于首版支持和兼容性保证范围。

内部设计仍使用 `LogicalStart`、`LogicalEnd`、`LogicalOffset` 和 `LogicalExtent` 等中性术语，
不把实现命名固化为 Left/Right。未来扩展 RTL 时应增加集中坐标转换层，而不是推翻 Section
排序、活动判定和 Navigator Track 算法。

首版同时覆盖两类内容宿主：

- `ScrollMarkerView` 面向任意内容树中的实体 Section。
- `ScrollMarkerItemsView` 面向当前数量已知、只向 End 持续追加的扁平虚拟内容序列。

两种 Host 都不接管业务数据持久化，也不把对象池当成业务数据存储。

## 目标包与类型

目标包名和项目名：

```text
AtomUI.Labs.Controls.ScrollMarker
```

目标测试项目名：

```text
AtomUI.Labs.Controls.ScrollMarker.Tests
```

计划中的主要公共类型：

```text
ScrollMarkerView
ScrollMarkerItemsView
ScrollMarkerSection
ScrollMarkerItem
ScrollMarkerContentEndFollowMode
```

职责边界：

- `ScrollMarkerView` 是局部组合控件，负责内容视口、导航条布局、主滚动轴和 Marker 同步。
- `ScrollMarkerItemsView` 是数据驱动的虚拟内容控件，通过 `ItemsSource`、`ItemTemplate`
  和容器复用承载可持续追加的扁平 Section 序列。
- `ScrollMarkerSection` 是显式语义包装器，用于给普通 Avalonia 内容提供稳定锚点身份；
  Section 可以包含 Section，但不能包含完整的 ScrollMarker 根 Host。
- `ScrollMarkerItem` 是由控件生成的 Marker 容器。开发者不需要手工创建，但可以通过 ControlTheme 定制它。
- `ScrollMarkerContentEndFollowMode` 控制 Virtual 主内容在逻辑 End 追加或增长时采用 Automatic
  跟随还是 Disabled 保持视口。

两个类型都不是应用根 Host，只管理各自对应的一块滚动区域。Direct Content 的结构是：

```text
Application
└─ MainWindow
   └─ Page
      └─ ScrollMarkerView
         ├─ 内部 ScrollViewer
         │  └─ ScrollMarkerSection × N
         └─ Marker 导航条
```

双 Host 的类型关系、共享 Coordinator 和内容 Adapter 边界见
[双 Host 总体架构](host-architecture.md)。

## Direct Content Golden Path

AtomUI Labs 控件继续使用统一 XML 命名空间 URI，并推荐 `atom.labs` 前缀：

```xml
xmlns:atom="https://atomui.net"
xmlns:atom.labs="https://atomui.net/labs"
```

纵向标准问答示例：

```xml
<atom.labs:ScrollMarkerView
    Orientation="Vertical"
    NavigatorPlacement="End"
    NavigatorDisplayMode="Inline">
    <StackPanel>
        <atom.labs:ScrollMarkerSection
            AnchorKey="turn-001"
            Label="如何使用 ScrollMarker？">
            <StackPanel Spacing="12">
                <Border Classes="question">
                    <TextBlock Text="如何使用 ScrollMarker？" />
                </Border>
                <Border Classes="answer">
                    <TextBlock Text="把一次完整问答放入同一个 Section。" />
                </Border>
            </StackPanel>
        </atom.labs:ScrollMarkerSection>

        <atom.labs:ScrollMarkerSection
            AnchorKey="turn-002"
            Label="回答增长时会怎样？">
            <StackPanel Spacing="12">
                <Border Classes="question">
                    <TextBlock Text="回答增长时会怎样？" />
                </Border>
                <Border Classes="answer">
                    <TextBlock Text="Section 保持同一身份，并在布局后更新后续坐标。" />
                </Border>
            </StackPanel>
        </atom.labs:ScrollMarkerSection>
    </StackPanel>
</atom.labs:ScrollMarkerView>
```

横向主内容示例：

```xml
<atom.labs:ScrollMarkerView
    Orientation="Horizontal"
    FlowDirection="LeftToRight"
    NavigatorPlacement="Start">
    <StackPanel Orientation="Horizontal">
        <atom.labs:ScrollMarkerSection
            AnchorKey="phase-1"
            Label="阶段一" />
        <atom.labs:ScrollMarkerSection
            AnchorKey="phase-2"
            Label="阶段二" />
    </StackPanel>
</atom.labs:ScrollMarkerView>
```

`Orientation` 表示 ScrollMarkerView 导航的主滚动轴。它不会擅自修改开发者 Content 内部的 Panel 排列，因此横向模式仍需由开发者提供横向布局。

标准对话场景不得把 Question 与 Answer 分别包装成两个同级 `ScrollMarkerSection`；那会生成
两个 Marker，并把单轮问答错误拆成两个导航单元。非对话场景仍可使用上面的横向语义阶段示例。

## Virtual Items 使用形态

Virtual Items Host 使用同一 XML 命名空间：

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

`Turns` 中的每一个数据项表示一个完整 `ConversationTurn`，而不是单条 Question 或 Answer 消息；
因此 `ItemsSource.Count`、`MarkerDescriptor.Count` 和逻辑 Marker 数量都等于当前问答对数量。

`ScrollMarkerItemsView` 使用内部密封的 `ScrollMarkerItemsPanel : VirtualizingStackPanel`，根据
框架 Viewport 和 `CacheLength` 实现、回收内部 `ScrollMarkerSectionContainer : ContentControl`；
它不复用 Direct 的公共 `ScrollMarkerSection`，开发者也不提供 `ObjectPoolSize` 或可替换
ItemsPanel。当前时刻的 ItemsSource 数量必须可知，首版只支持向逻辑 End 追加。
ItemsSource 只接受业务数据对象且非空集合必须配置 ItemTemplate；直接放入 Control 会立即失败。

数据项通过 `AnchorKeyBinding`、`LabelBinding` 和 `MarkerThemeBinding` 在入列时生成轻量
MarkerDescriptor 快照；当前 N 个数据项对应 N 个 Descriptor，内容 Section 与 Marker 视觉
容器仍分别按各自 Viewport 实现。完整契约见[Virtual Items 设计](virtual-items-design.md)。
未实现项采用框架平均尺寸估算；语义 `ScrollToIndex` 通过 `ScrollIntoView(index)` 实现，目标
布局采用最多两次实现请求、最多两次精确 Offset 写入和 LayoutEpoch 确认的有界协议；真实场景
表现仍需原型和 Benchmark 验证。

`ContentEndFollowMode` 只属于 Virtual Items 主内容，默认为 `Automatic`：用户原本位于逻辑 End
时，End 追加和最后一项流式增长继续跟随新终点；用户主动向 Start 浏览后锁存为历史浏览，不再
写主 Offset，直到用户主动回到 End。`Disabled` 永不主动跟随。该状态与 Navigator 自身的
Follow/Browse 正交，首版不提供 Always、像素阈值、公共 ScrollToEnd 或内部 ScrollViewer 实例。

## Direct Content 视口所有权

`ScrollMarkerView` 计划继承 `ContentControl`，并在默认模板内部创建 AtomUI ScrollViewer。开发者把普通内容直接放入 View，不再手工提供顶层 ScrollViewer。

```text
ScrollMarkerView : ContentControl                         [public]
├─ Content
│  └─ ScrollMarkerSection : ContentControl × N           [public]
└─ ControlTemplate
   └─ ScrollMarkerViewPanel : Panel                      [internal]
      ├─ PART_ContentScrollViewer : AtomUI ScrollViewer
      │  └─ ContentPresenter
      └─ PART_Navigator : ScrollMarkerNavigator          [internal]
         ├─ MarkerDescriptor × N                         [internal/logical]
         └─ ScrollMarkerTrackPanel                       [internal/sealed]
            : VirtualizingStackPanel
            └─ ScrollMarkerItem : ContentControl,
                                  ISelectable × K         [public/realized]

K 通常等于 Navigator Viewport 与前后 `CacheLength` 覆盖的容器数量，并允许 Avalonia 为
键盘焦点或 ScrollIntoView 暂时保留少量额外容器；K 通常远小于 N，但不是绝对固定值。
```

完整类结构、Section 与 Item 映射、运行链路和模板部件边界见
[导航条 UI 与交互设计](navigator-design.md#整体构成)。

该约束用于保证：

- View 始终知道唯一主滚动视口。
- Section 坐标和活动锚点基于同一视口计算。
- 纵向和横向 Offset、Extent、Viewport 语义明确。
- Navigator 与主内容不会形成两个互相争夺滚轮的同级滚动视口。

Section 内部仍可包含业务需要的局部 ScrollViewer。局部 ScrollViewer 不参与外层 ScrollMarkerView 的锚点位置计算。

### 主内容 ScrollViewer 配置入口

`PART_ContentScrollViewer` 由 `ScrollMarkerView` 的模板创建并保持内部所有。首版不公开
`ContentScrollViewer`、`ScrollViewer` 或其它返回模板部件实例的 CLR 属性，也不要求开发者
为了配置滚动行为而替换整个 Host 模板。

首版在 `ScrollMarkerView` 上公开 `Orientation` 和语义属性 `MainScrollBarVisibility`，由控件
把主轴配置投影到 `PART_ContentScrollViewer`。开发者不直接配置两个物理轴：

```text
ScrollMarkerView
├─ Orientation
└─ MainScrollBarVisibility
             │
             │ 主轴到物理轴投影
             ▼
PART_ContentScrollViewer

PART_NavigatorScrollViewer  不接收这些配置
```

`MainScrollBarVisibility` 默认 `Auto`，只允许 `Auto`、`Visible` 和 `Hidden`；交叉轴由控件固定
为 `Disabled`。同一 UI Dispatcher 同步调用批次内的相关变化合并成完整快照，验证后逻辑原子
提交；跨 `await` 或不同 Dispatcher 回调的变化属于独立更新。该机制不开放内部对象本身，
完整契约见[导航条 UI 与交互设计](navigator-design.md#direct-主内容-scrollviewer-公开控制面)。

### Direct Content 性能边界

`ScrollMarkerView` 不设置 Section 数量硬上限，也不根据数量自动拒绝注册 Section。共享
Navigator 始终按可见范围虚拟化 `ScrollMarkerItem`，因此 Marker 数量增长不会要求同时创建
等量的 Marker UI 容器。

但是，Direct Content 仍然保留开发者创建的全部 `ScrollMarkerSection` 及其业务 Content：

```text
Section 控件实例        = N
MarkerDescriptor        = N
ScrollMarkerItem 实例   ≈ Navigator Viewport + 前后各 0.5 Viewport 缓存
                         + 少量框架特殊保留容器
```

所以“不设置硬上限”只表示不存在人为数量限制，不构成任意规模下的性能保证。首版源码和
Benchmark 尚未产生前，文档不声明一个缺乏依据的推荐数量、警戒数量或最大数量。

开发者预计 Section 数量非常大、持续增长、单项内容复杂，或者无法合理预估最终数量时，应优先
考虑使用 `ScrollMarkerItemsView` Virtual Items 模式，让内容 Section 和 Marker 都按各自
Viewport 实现与复用。控件不会在运行时从 Direct 自动切换为 Virtual；两种模式的内容所有权
和生命周期不同，必须由开发者通过根控件类型显式选择。

第一版实现完成后，应使用具有明确 Section 数量、模板复杂度、视口尺寸和目标硬件条件的
Benchmark 记录实测结果。后续性能建议必须引用这些测试条件，不能把单一测试数字写成所有
应用都适用的硬限制。

## 已确定的导航模型

当前目标设计已经确定：

- 主方向支持纵向和横向，默认纵向。
- 首版只支持 `FlowDirection.LeftToRight`；RTL 不在兼容性保证范围内。
- 首版禁止任意两个 `ScrollMarkerView` / `ScrollMarkerItemsView` 根 Host 嵌套；相同模式和
  不同模式都禁止。
- 同一个 Direct Host 内允许 `ScrollMarkerSection` 包含 Section，但 Section Content 中不得
  嵌入完整的 ScrollMarker 根 Host。
- Direct Section 的导航资格由 Registry、Avalonia `IsEffectivelyVisible`、稳定布局快照和主
  滚动坐标系共同决定；离屏、Clip、Opacity、IsEnabled 和零尺寸不删除 Marker。
- Navigator 使用相对主方向的 `Start` 或 `End` 放置，默认 `End`。
- 纵向的 Start/End 映射到左右；横向的 Start/End 映射到顶部和底部。
- 首版同时支持独立占位的 Inline 和覆盖内容的 Overlay，默认 Inline。
- Marker 对应显式 Section，不从视觉树中猜测标题或普通控件。
- 标准对话用法中，一个 Marker 对应一个完整 `ConversationTurn` Section；Question 位于 Section
  起始位置，Answer 留在同一 Section 内。
- Marker 保持独立身份和可点击命中区域。
- Marker 按 Section 稳定顺序在统一槽位中均匀排列，不表达 Section 之间的实际像素距离。
- Direct Host 通过 `MainScrollBarVisibility` 配置唯一主内容滚动轴；`Auto`、`Visible` 和
  `Hidden` 有效，`Disabled` 在配置入口处抛出 `InvalidOperationException`，不得静默规整。
- Direct Host 的外层主内容只支持单轴滚动；交叉轴唯一有效值为 `Disabled`，Offset 始终为
  0。Section 中需要另一方向滚动的局部内容自行使用局部 ScrollViewer。
- Direct Host 不把设置在 Host 上的 Horizontal/Vertical ScrollBarVisibility Attached Property
  当作公共配置；Orientation 与 `MainScrollBarVisibility` 只投影到内容 ScrollViewer。
  `AllowAutoHide`、`IsLiteMode`、惯性、Scroll Chaining、Deferred Scrolling 和
  `BringIntoViewOnFocusChange` 使用模板固定策略，不提供开发者配置入口。主内容 Scroll Chaining
  固定开启，Navigator Scroll Chaining 固定关闭，惯性、Deferred 和内容焦点自动 BringIntoView
  固定关闭。
- 内容控件仍可获得焦点，但首版不因 Tab、代码 `Focus()` 或其它焦点变化自动滚动主内容；
  Navigator 自身的键盘浏览、Marker `ScrollIntoView` 和激活行为不受此限制。
- 主内容 ScrollChanged 使用二维内部模型解释：`ScrollIntent` 记录 UserInput、MarkerNavigation、
  Virtual `ContentEndFollow` 或 ConfigurationCommit，Offset、Extent、Viewport Delta 与范围钳制
  （clamp）作为可同时存在的 Facts；无来源的纯 Offset 变化不得默认冒充用户输入。
- 每个 Host 实例拥有独立 Marker 导航状态机，同一时刻最多一个 CurrentRequest；首版立即跳转、
  不播放平滑动画、不排队，新 Marker 通过 Generation 立即取代旧请求。事务 Completed 后返回
  Idle，但目标 Explicit 继续保持。
- 活跃 MarkerNavigation 遇到合格的主内容用户滚动意图时立即以 UserInterrupted 终止，即使
  Offset 尚未变化；Idle + Explicit 只有主 Offset 实际变化才进入 Automatic。局部 ScrollViewer
  已消费的输入和 Navigator Browse 不参与此仲裁。
- 范围钳制不是解除 Explicit 的充分条件。稳定布局后重算目标的 EffectiveTargetOffset：实际
  Offset 在容差内仍等于有效目标时保持显式选中锁定；目标失效或二者偏离时进入 Automatic，
  且不得补写 Offset 把内容自动拉回旧目标。
- Direct Automatic 活动判定固定使用内部 4 DIP 双向对称空间滞回，作用于同坐标 PositionGroup；
  内容起点和终点强制绕过滞回，大跨度滚动直接提交最终候选，不逐个播放中间 Marker。该常量
  不作为公共属性。
- Virtual Automatic 只使用当前正常实现范围的 O(K) 原子布局快照；快照证明覆盖 AnchorOffset
  Probe 后才提交活动 SourceIndex，不能根据平均尺寸猜测，也不能在普通滚动中扫描全部
  ItemsSource。Virtual 与 Direct 共享 0.5 DIP 布局容差和 4 DIP 空间滞回。
- Virtual 内容 End 跟随使用追加前稳定快照决定是否跟随；FollowingEnd 下按有效 LayoutEpoch
  跟随结构追加和尾部流式 Extent 增长，BrowsingHistory/Disabled 下不写 Offset，并复用 Avalonia
  原生滚动锚定。用户输入优先于 MarkerNavigation，MarkerNavigation 优先于 ContentEndFollow。
- Virtual Items 的无效投影、非 End Add、不支持的其它集合动作、运行期 ItemsSource 替换或内部
  映射不一致属于开发者硬契约错误。Host 先进入不可恢复的 Faulted，再抛出
  InvalidOperationException；Debug 与 Release 行为一致。
- 空间不足时增长逻辑 Track，Navigator 只显示其中一段并跟随活动 Marker，不把多个 Marker
  折叠成 Cluster。
- `ScrollMarkerTrackPanel` 内部密封继承 Avalonia `VirtualizingStackPanel`，使用框架原生
  EffectiveViewport、容器生成和回收机制；不创建第二套对象池。
- Virtual 内容使用独立的内部 `ScrollMarkerItemsPanel : VirtualizingStackPanel` 和
  `ScrollMarkerSectionContainer : ContentControl`；公共 `ScrollMarkerSection` 只属于 Direct。
- Marker 虚拟化的内部 `CacheLength` 首版默认为 `0.5`，不公开对象池数量、虚拟化开关或
  可替换 ItemsPanel。
- 实际实现容器通常落在 Viewport 与缓存的预期范围内，但允许框架为键盘焦点或
  `ScrollIntoView` 保留额外容器，不承诺严格固定数量。
- 活动 Marker 发生远距离变化时，根据稳定索引与固定槽位直接计算最终 Navigator Offset，
  立即跳转到目标附近而不实现中间 Marker，也不移动键盘焦点。
- 用户可以独立浏览 Navigator；浏览期间暂停自动跟随。
- 全局 Marker 外观通过 `ItemContainerTheme` 定制，单个 Section 可以通过 `MarkerTheme` 完整覆盖。

详细契约见[导航条 UI 与交互设计](navigator-design.md)。

## Direct Section 最小身份契约

当前导航条设计依赖以下最小 Section 元数据：

| 属性 | 类型 | 目标语义 |
|---|---|---|
| `AnchorKey` | `string` | 必填；在单个 `ScrollMarkerView` 内稳定且唯一 |
| `Label` | `string?` | 可选但推荐；用于 Tooltip、导航展示和自动化名称 |
| `MarkerTheme` | `ControlTheme?` | 可选；完整替换该 Section 对应 Marker 的主题 |

`AnchorKey` 非空即表示该 Section 参与导航，不再增加冗余的 `IsAnchor` 开关。`Label` 缺失时，导航展示和自动化名称回退到 `AnchorKey`。

完整的注册、唯一性、嵌套、滚动边界、可见性和活动项契约见
[Section Direct Content 设计](section-design.md)。

Virtual Items 通过入列快照提供等价的稳定身份和 Marker 元数据；完整投影契约见
[Virtual Items 设计](virtual-items-design.md)。它不使用实体 Section 的 Attach/Detach Registry，
而是按 ItemsSource 稳定顺序和 SourceIndex 维护逻辑 Descriptor。

## 非目标

当前不处理：

- 自动推断任意 Grid、Border 或 StackPanel 是否属于锚点。
- 让开发者手工创建并维护 Marker Item 集合。
- Cluster、聚合数量按钮或 Cluster 展开列表。
- Direct Host 达到某个 Section 数量后自动销毁开发者拥有的业务控件。
- 根据未经 Benchmark 验证的固定数量自动警告、拒绝 Direct Section 或切换到 Virtual 模式。
- 公开、替换或由开发者直接持有 `PART_ContentScrollViewer` 模板部件实例。
- 自动把业务内容写入文本文件、数据库或其它持久化存储。
- 跨进程布局 Cache 或像素坐标恢复。
- Virtual Items 中未知总量的随机远端导航、双向分页和嵌套虚拟 Section。
- Virtual Items 无效集合变更后的自动恢复、业务集合回滚、坏 Item 跳过、同实例重新加载或故障
  UI 降级模式。
- 开发者控制的对象池大小或 `EnableObjectPool` 开关。
- 同时导航 X、Y 两个主轴；每个 Host 实例只选择一个主轴。
- `FlowDirection.RightToLeft` 下的 Section 排序、Offset 换算、Marker 排列和键盘方向。

## 当前实现状态

截至 2026-08-01：

- 已创建 `AtomUI.Labs.Controls.ScrollMarker` 项目、独立测试项目、Gallery 页面和初版 Release
  Performance Runner；包继续保持实验性质。
- Direct Host 已实现 Section Registry、严格 AnchorKey、有效隐藏过滤、同坐标稳定选择、零尺寸
  锚点、4 DIP 双向滞回、立即跳转和主轴 ScrollViewer 配置。
- Virtual Host 已实现 Descriptor 投影、专用虚拟化容器、逻辑 End 追加、不可恢复 Faulted、
  有界远距导航、Automatic 活动判定和首轮内容 End 跟随。
- `ScrollMarkerItemsView` 的元数据投影、Descriptor 实体化、逻辑 End 增量追加和框架原生内容
  虚拟化基线已经固定；有界两阶段目标导航、Virtual Automatic 活动项，以及尊重历史浏览意图的
  内容 End 自动跟随和终止性集合故障协议已经确定。
- Marker 与 Section 的 Headless 结构测试以及初版 Performance Runner 已产生；正式 Runner 尚未
  达到多进程、多轮、环境记录、线性内存、长稳和真实桌面全部口径，首轮发布报告尚未形成。
- 2026-08-01 的缩短版原型验证了 1,000 项下两套容器均未全量实现，但其结果不是正式性能证据；
  正式相对性能和 60 Hz 门禁仍未通过，因此当前状态明确为 `ImplementationFailed/施工中`，而非
  `Passed` 或 `SchemeARejected`。
- 缺失或未通过任一发布门禁都不能宣称具备发布能力。
- Marker 激活和 Navigator 自动 Follow 首版均使用无动画的立即 Offset 跳转；两个 Host 的
  Automatic 活动判定内部滞回值均固定为 4 DIP，并明确了首尾绕过和大跨度直接判定规则。
