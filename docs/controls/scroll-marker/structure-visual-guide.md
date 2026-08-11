# ScrollMarkerView Direct 平面结构图解

> 文档状态：附属理解资料，更新于 2026-08-02。本文只把既定设计转换为平面结构图，
> 不新增公共契约；类型职责、属性和交互规则以[导航条 UI 与交互设计](navigator-design.md)为准。

本文从“外层—中间层—内层”的俯视角度解释 `ScrollMarkerView` Direct Content Host。
`ScrollMarkerItemsView` Virtual Items Host 不使用本文的任意 Content 树结构；两个 Host
的总体关系见[ScrollMarker 双 Host 总体架构](host-architecture.md)。

示意图默认使用：

```xml
Orientation="Vertical"
NavigatorPlacement="End"
NavigatorDisplayMode="Inline"
```

即主内容在左侧，Navigator 在右侧，并分别占据布局空间。

本文采用标准对话粒度：一个 `ScrollMarkerSection` 表示一个完整
`ConversationTurn`，内部依次包含 Question 和 Answer，并只对应一个 Marker。

## 标准问答对映射

```text
主内容区域                                        Navigator

+--------------------------------------+          +----------+
| ScrollMarkerSection                  | <------> | Marker 1 |
| AnchorKey="turn-001"                 |          +----------+
| +----------------------------------+ |
| | Question                         | |  <- SectionStart / 点击定位点
| +----------------------------------+ |
| | Answer                           | |  <- 同一 Section，可持续增长
| +----------------------------------+ |
+--------------------------------------+

+--------------------------------------+          +----------+
| ScrollMarkerSection                  | <------> | Marker 2 |
| AnchorKey="turn-002"                 |          +----------+
| +----------------------------------+ |
| | Question                         | |
| +----------------------------------+ |
| | Answer                           | |
| +----------------------------------+ |
+--------------------------------------+
```

标准对话中不为 Answer 再建立 Section 或 Marker。下文使用的 `overview`、`details` 等通用内容
名称表示次要的非对话场景，不改变上述首要映射。

## 完整平面俯视图

```text
+------------------------------------------------------------------------------------+
| ScrollMarkerView                                                     最外层公共控件 |
|                                                                                    |
|  +-------------------------------------------------------------------------------+ |
|  | ScrollMarkerViewPanel                                           中间层布局容器 | |
|  |                                                                               | |
|  |  +--------------------------------------------------+  +--------------------+ | |
|  |  | PART_ContentScrollViewer                         |  | ScrollMarker       | | |
|  |  | 主内容滚动区域                                   |  | Navigator          | | |
|  |  |                                                  |  | 导航条区域         | | |
|  |  |  +--------------------------------------------+  |  |                    | | |
|  |  |  | ContentPresenter                           |  |  | +----------------+ | | |
|  |  |  | 呈现开发者传入的 Content                   |  |  | | NavigatorRoot  | | | |
|  |  |  |                                            |  |  | | 背景、边框     | | | |
|  |  |  |  +--------------------------------------+  |  |  | |                | | | |
|  |  |  |  | StackPanel / Grid / 其他业务 Panel   |  |  | | +------------+ | | | |
|  |  |  |  |                                      |  |  | | | Navigator  | | | | |
|  |  |  |  | +----------------------------------+ |  |  | | | ScrollViewer| | | | |
|  |  |  |  | | ScrollMarkerSection             | |  |  | | |            | | | | |
|  |  |  |  | | AnchorKey="overview"             | |  |  | | |  [●] Item  | | | | |
|  |  |  |  | |                                  | |  |  | | |            | | | | |
|  |  |  |  | |     任意 Avalonia 内容           | |  |  | | |  [●] Item  | | | | |
|  |  |  |  | +----------------------------------+ |  |  | | |            | | | | |
|  |  |  |  |                                      |  |  | | |  [●] Item  | | | | |
|  |  |  |  | +----------------------------------+ |  |  | | |            | | | | |
|  |  |  |  | | ScrollMarkerSection             | |  |  | | +------------+ | | | |
|  |  |  |  | | AnchorKey="details"              | |  |  | +----------------+ | | |
|  |  |  |  | |                                  | |  |  |                    | | |
|  |  |  |  | |     任意 Avalonia 内容           | |  |  +--------------------+ | |
|  |  |  |  | +----------------------------------+ |  |                         | |
|  |  |  |  |                                      |  |                         | |
|  |  |  |  | +----------------------------------+ |  |                         | |
|  |  |  |  | | ScrollMarkerSection             | |  |                         | |
|  |  |  |  | | AnchorKey="summary"              | |  |                         | |
|  |  |  |  | +----------------------------------+ |  |                         | |
|  |  |  |  +--------------------------------------+  |                         | |
|  |  |  +--------------------------------------------+  |                         | |
|  |  +--------------------------------------------------+                         | |
|  +-------------------------------------------------------------------------------+ |
+------------------------------------------------------------------------------------+
```

图中从外向内依次是：

1. `ScrollMarkerView`：整个控件的公共外壳和协调者。
2. `ScrollMarkerViewPanel`：把主内容和 Navigator 安排在正确位置。
3. 左侧主内容 ScrollViewer 与右侧 `ScrollMarkerNavigator`：两个相互独立的滚动区域。
4. `ScrollMarkerSection`：被导航的业务内容；`ScrollMarkerItem`：用于导航的交互入口。

两条 ScrollViewer 不承担相同职责。`PART_ContentScrollViewer` 滚动业务内容；
Navigator ScrollViewer 只在 Marker Track 超出可见空间时浏览 Marker。

## 三层结构

```text
第一层：ScrollMarkerView
+----------------------------------------------------------------+
| 整个控件的外壳                                                 |
| 对外提供 Orientation、Placement、背景、边框等公共属性          |
+----------------------------------------------------------------+
                              |
                              v
第二层：ScrollMarkerViewPanel
+----------------------------------------------------------------+
| 决定内容区域和导航条区域如何摆放                               |
|                                                                |
| +----------------------------+  +----------------------------+ |
| | 主内容区域                 |  | 导航条区域                 | |
| +----------------------------+  +----------------------------+ |
+----------------------------------------------------------------+
                    |                         |
                    v                         v
第三层：两个独立的内部区域
+----------------------------+  +-------------------------------+
| 主 Content ScrollViewer    |  | ScrollMarkerNavigator         |
|                            |  |                               |
| ScrollMarkerSection × N    |  | Logical Marker × N            |
| 全部为实体 Direct Section  |  | Item ≈ Viewport+前后缓存       |
|                            |  |      + 框架特殊保留项          |
|                            |  |                               |
| 保存真正的业务内容         |  | 保存对应的快捷定位点          |
+----------------------------+  +-------------------------------+
```

`ScrollMarkerViewPanel` 只负责布局。它不会判断当前 Section，也不会生成 Marker。
`ScrollMarkerView` 负责两侧协调，`ScrollMarkerNavigator` 负责 Item 的生成、选择和输入。

## 内容区域

```text
PART_ContentScrollViewer
|
+-- ContentPresenter
    |
    +-- 开发者提供的 StackPanel / Grid / 其他 Panel
        |
        +-- ScrollMarkerSection[0] = ConversationTurn[0]
        |   +-- Question
        |   +-- Answer
        |
        +-- ScrollMarkerSection[1] = ConversationTurn[1]
        |   +-- Question
        |   +-- Answer
        |
        +-- ScrollMarkerSection[2] = ConversationTurn[2]
            +-- Question
            +-- Answer
```

`ScrollMarkerSection` 只增加锚点语义，不接管内部业务内容的布局。标准对话场景由开发者使用
Grid、StackPanel 或其它 Panel 把 Question 与 Answer 放入同一 Section。通用内容也可以包含
另一个 `ScrollMarkerSection`，但不包括完整的 `ScrollMarkerView` 或
`ScrollMarkerItemsView` 根 Host。

Section 的身份、嵌套、注册、滚动边界和活动项判定见
[ScrollMarkerSection Direct Content 设计](section-design.md)。

## 导航条区域

```text
ScrollMarkerNavigator
|
+-- PART_NavigatorRoot
|   |
|   +-- 导航条背景
|   +-- 导航条边框
|   +-- 圆角和 Padding
|
+-- PART_NavigatorScrollViewer
    |
    +-- ItemsPresenter
        |
        +-- ScrollMarkerTrackPanel
            : VirtualizingStackPanel [internal/sealed]
            |
            +-- ScrollMarkerItem[0]
            +-- ScrollMarkerItem[1]
            +-- ScrollMarkerItem[2]
            +-- ...
```

`ScrollMarkerNavigator` 生成和管理 Item；内部密封的 `ScrollMarkerTrackPanel` 继承
Avalonia `VirtualizingStackPanel`，按 Section 稳定顺序把 Item 放入统一槽位、计算 Track
长度，并使用框架容器生成与回收机制；`ScrollMarkerItem` 承担点击、焦点、选中状态和自动化
语义。

```text
Marker 可以容纳在 Navigator Viewport 中：

┌────────────── Navigator Viewport ──────────────┐
│      A           B           C           D     │
└────────────────────────────────────────────────┘

Marker 不能全部容纳时：

┌──────────────── 可增长 Marker Track ─────────────────┐
│  A  │  B  │  C  │  D  │  E  │  F  │  G  │  H  │
└───────────────────────────────────────────────────────┘
             └── Navigator 当前显示的一段 ──┘
```

槽位表达 Section 的稳定顺序，不表达 Section 在主内容中的像素间隔。

图中完整 Track 表示逻辑 Marker，不表示所有 `ScrollMarkerItem` 控件同时存在。首版
`CacheLength = 0.5`，通常只实现当前 Viewport 与前后各半个 Viewport；Avalonia 可以为仍持有
键盘焦点或参与 `ScrollIntoView` 的 Marker 暂时保留额外容器，因此实际 Item 数量不是固定值。

```text
全部逻辑 Marker：0 ........................................ 99

未实现              缓存         当前可见         缓存              未实现
0 ... 36          37 38 39       40 ... 45       46 47 48          49 ... 99
                  └──────── ExpectedRealizedRange ────────┘

可能另有：焦点或 ScrollIntoView 暂时保留的特殊 Item
```

## Section 与 Item 的对应关系

```text
主内容区域                                      导航条逻辑对应关系

+--------------------------------+             +------------------+
| ScrollMarkerSection            |             | ScrollMarkerItem |
| AnchorKey="turn-001"            | <---------> | turn-001         |
| Question + Answer               |             |                  |
+--------------------------------+             +------------------+

+--------------------------------+             +------------------+
| ScrollMarkerSection            |             | ScrollMarkerItem |
| AnchorKey="turn-002"            | <---------> | turn-002         |
| Question + Answer               |             |                  |
+--------------------------------+             +------------------+

+--------------------------------+             +------------------+
| ScrollMarkerSection            |             | ScrollMarkerItem |
| AnchorKey="turn-003"            | <---------> | turn-003         |
| Question + Answer               |             |                  |
+--------------------------------+             +------------------+
```

Section 和逻辑 Marker 是一对一关系，但不要求全部 `ScrollMarkerItem` 容器同时存在：

- 标准对话 Section 保存一个完整 ConversationTurn 的 Question、Answer，以及
  `AnchorKey`、`Label`、`MarkerTheme`。
- MarkerDescriptor 保存轻量逻辑映射。
- Item 是 Navigator 在当前实现范围内自动生成或复用的交互容器。
- 开发者只维护 Section，不另外维护一套 Item 集合。

## 最小心智模型

```text
+----------------------- ScrollMarkerView -----------------------+
|                                                                |
|  +---------------- 内容区域 ----------------+  +--- 导航条 ---+ |
|  |                                         |  |              | |
|  |  Turn 1: Question+Answer <-------------------> Marker 1   | |
|  |  Turn 2: Question+Answer <-------------------> Marker 2   | |
|  |  Turn 3: Question+Answer <-------------------> Marker 3   | |
|  |                                         |  |              | |
|  +-----------------------------------------+  +--------------+ |
|                                                                |
+----------------------------------------------------------------+
```

```text
ConversationTurn Section 是被导航的一轮完整问答
Question 是 Section 起点，Answer 留在同一 Section
Item 是用于导航的入口
Navigator 管理所有 Item
TrackPanel 安排 Item 的位置
View 负责 Section 与 Item 的双向协调
```

## 其它布局模式

以上图形只展示默认的 Vertical、End、Inline。其它配置改变的是平面位置，不改变类职责：

| 配置 | 平面变化 |
|---|---|
| `Vertical + Start` | Navigator 从右侧移动到左侧 |
| `Horizontal + Start` | Navigator 移动到内容上方，Marker 横向排列 |
| `Horizontal + End` | Navigator 移动到内容下方，Marker 横向排列 |
| `Overlay` | Navigator 覆盖在主内容上方，不再占据独立布局空间 |

无论采用哪种配置，ConversationTurn Section 与 Item 仍然一对一，主内容 ScrollViewer 仍然是
唯一业务内容视口。非对话场景用其它完整语义内容单元替代 ConversationTurn，映射关系不变。
