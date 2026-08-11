# ScrollMarkerSection Direct Content 设计

> 文档状态：规范设计与首轮实现施工中，更新于 2026-08-02。本文固定
> `ScrollMarkerView` Direct Content 中已经确认的实体 Section 公共契约、注册范围、
> 滚动边界和活动判定；未列出的程序化导航和动画能力不代表现有实现。

本文描述 `AtomUI.Labs.Controls.ScrollMarker` 中 Direct 内容区域的目标设计。导航条布局、
Marker 轨道和 Follow/Browse 交互见[导航条 UI 与交互设计](navigator-design.md)，整体平面
关系见[Direct 平面结构图解](structure-visual-guide.md)。

本文不描述 `ScrollMarkerItemsView` 的虚拟内容容器、Index 导航或对象池。两个公共 Host
的总体关系见[ScrollMarker 双 Host 总体架构](host-architecture.md)。

## 设计目标

Section 需要同时满足：

- 可以包装任意普通 Avalonia 内容，不限制开发者使用 Grid、StackPanel 或其它布局；完整的
  `ScrollMarkerView` / `ScrollMarkerItemsView` 根 Host 除外。
- 使用显式稳定身份建立 Section 与 Marker 的一对一关系。
- 支持动态增加、删除、重排和有效可见性变化。
- 所有可导航 Section 必须属于主内容 ScrollViewer 的同一个滚动坐标系。
- 滚动过程中不反复扫描完整视觉树。
- 无效 Key、重复 Key 和不可兑现的跨滚动区导航必须明确失败。

## 类型与内容模型

```csharp
public class ScrollMarkerSection : ContentControl
{
    public string AnchorKey { get; set; } = string.Empty;

    public string? Label { get; set; }

    public ControlTheme? MarkerTheme { get; set; }
}
```

`ScrollMarkerSection` 是语义包装器，不是布局 Panel。它仍然遵循 `ContentControl` 的单 Content
模型；需要承载多个控件时，开发者在 Content 内使用自己的 Panel：

```xml
<atom.labs:ScrollMarkerSection
    AnchorKey="turn-42"
    Label="为什么一个 Marker 对应一轮问答？">
    <StackPanel Spacing="12">
        <Border Classes="question">
            <TextBlock Text="为什么一个 Marker 对应一轮问答？" />
        </Border>
        <Border Classes="answer">
            <TextBlock Text="因为 Question 和 Answer 共同组成一个 ConversationTurn。" />
        </Border>
    </StackPanel>
</atom.labs:ScrollMarkerSection>
```

默认模板只呈现 Content，不擅自增加业务 Padding、Margin 或固定尺寸。Section 的测量和排列
由开发者所在的父 Panel 决定。

标准对话用法把 `ConversationTurn`（问答对）作为最小 Section：Question 是 Content 的第一部分，
Answer 紧随其后并留在同一个 Section。不得仅为了 Answer 再创建一个同级 Section，否则同一轮
问答会产生两个 Marker。控件不在运行时检查 Content 是否确实包含 Question 和 Answer；该配对由
开发者的数据模型和模板负责。FAQ、长文档等次要场景可以用一个完整语义内容单元替代问答对。

## 身份属性

| 属性 | 类型 | 契约 |
|---|---|---|
| `AnchorKey` | `string` | 必填；在单个 View 中稳定且唯一 |
| `Label` | `string?` | 可选；用于 Tooltip、导航展示和自动化名称 |
| `MarkerTheme` | `ControlTheme?` | 可选；完整替换该 Section 对应 Item 的主题 |

`AnchorKey` 非空即表示 Section 参与导航，不增加重复语义的 `IsAnchor` 属性。`Label` 缺失时
回退到 `AnchorKey`。

### AnchorKey 字面规则

`AnchorKey` 使用 `StringComparer.Ordinal`：

- 大小写敏感，`chapter` 与 `Chapter` 是两个不同 Key。
- 拒绝 `null`、空字符串和全空白字符串。
- 拒绝带有首尾空白的值。
- 不隐式 Trim，不转换大小写，也不自动生成后缀。

Section 注册时 Key 无效，抛出 `InvalidOperationException`。异常需要包含无效 Section 和
所属 `ScrollMarkerView` 的诊断信息。

### 唯一性与稳定性

同一个 `ScrollMarkerView` 中不得存在重复 `AnchorKey`。第二个 Section 注册时立即抛出
`InvalidOperationException`；Debug 和 Release 行为一致。

Key 在 Section 已注册期间不可修改。运行时直接修改已注册 Section 的 `AnchorKey` 同样抛出
`InvalidOperationException`。需要更换身份时，开发者必须先将 Section 从 View 中移除，
在注销完成后修改 Key，再重新加入。

该限制使 Key 可以稳定用于：

- Section 与 `ScrollMarkerItem` 的一对一映射。
- Item 复用和焦点保持。
- 当前活动项追踪。
- 未来可能增加的程序化跳转和位置恢复。

## 注册模型

开发者可以把 Section 放在主内容的任意布局深度中，不要求它是内容根 Panel 的直接子控件：

```text
ScrollMarkerView
└─ 主 Content ScrollViewer
   └─ Grid
      ├─ ScrollMarkerSection A
      └─ Border
         └─ StackPanel
            ├─ ScrollMarkerSection B
            └─ ScrollMarkerSection C
```

注册使用增量生命周期，不把持续扫描视觉树作为运行算法：

```text
ScrollMarkerSection Attach
        │
        ├─ 验证 View 和滚动坐标系
        ├─ 验证 AnchorKey
        └─ 注册到 ScrollMarkerView 的内部 Section Registry

ScrollMarkerSection Detach
        │
        └─ 从原 Registry 注销

ScrollMarkerSection Reparent
        │
        ├─ 从旧 Registry 注销
        └─ 在新祖先链中重新验证和注册
```

`ScrollMarkerView` 最终维护自己的 Section 集合，但滚动事件只消费这份已维护的集合，不能在
每次 Offset 变化时遍历完整视觉树。

## Section 嵌套

同一个 View 内允许 `ScrollMarkerSection` 包含另一个 `ScrollMarkerSection`。Navigator
不表达层级，所有有效 Section 扁平化为一条 Marker 序列：

```text
ScrollMarkerView
├─ ScrollMarkerSection "chapter"      -> Marker 1
│  └─ StackPanel
│     ├─ ScrollMarkerSection "part-a" -> Marker 2
│     └─ ScrollMarkerSection "part-b" -> Marker 3
└─ ScrollMarkerSection "summary"      -> Marker 4
```

Section 嵌套不会创建新的滚动坐标系。父子 Section 都由同一个主 ScrollViewer 导航。

这一通用能力不改变标准对话粒度：普通 ConversationTurn 的 Answer 不创建子 Section。只有业务
确实需要把某个子内容作为独立导航单元时才使用嵌套，并接受它会生成额外 Marker。

如果多个 Section 的主轴起始坐标相同，按照下节的同坐标规则确定活动项；它们仍按稳定内容
顺序占用相邻的统一 Marker 槽位，不会在 Navigator 中重叠。

## SectionStart 坐标定义

`SectionStart` 表示 `ScrollMarkerSection` 外框在主 Content 布局坐标系中的主轴起始位置。
它是 Marker 目标偏移、Section 排序、同坐标分组和 Automatic 活动判定的唯一 Section 坐标
来源，但不决定 Marker 在统一槽位 Track 中的视觉坐标。

```text
Vertical：
SectionStart = Section Layout Bounds 的 Top

Horizontal（首版 LTR）：
SectionStart = Section 外框换算到主 Content 布局坐标系后的 Left
             = SectionLayoutRectInMainContent.X
```

首版面向现代中文和英文界面，只支持 `FlowDirection.LeftToRight`。Horizontal 的逻辑 Start
固定为左侧，`SectionStart` 不执行 RTL 坐标反转。内部仍使用 `SectionStart` 和
`LogicalOffset` 等中性术语，为未来增加集中 RTL 坐标转换层保留扩展空间。

### 三条候选起始线

Section 同时存在 Margin、正式布局位置和 RenderTransform 时，可能出现三条看起来都像“顶部”
的线：

```text
主 Content 坐标

Y=1000  ─────────────────────────  Margin 前沿
          外部 Margin：24 DIP

Y=1012  ─────────────────────────  RenderTransform 后的视觉顶部
          Section 临时向上移动 12 DIP

Y=1024  ─────────────────────────  Layout Bounds 顶部
+--------------------------------+
| ScrollMarkerSection            |
| Question                       |  <- 标准 ConversationTurn 起点
| Answer                         |
+--------------------------------+
```

目标设计固定使用 `Layout Bounds 顶部`，不使用 Margin 前沿或变换后的视觉顶部。

### 换算到主 Content 坐标系

Section 的本地 Bounds 相对于直接布局父级，不能直接作为主 ScrollViewer Offset：

```text
主 Content
└─ Grid，布局起点 Y=200
   └─ Border，布局起点 Y=50
      └─ StackPanel，布局起点 Y=30
         └─ Section，本地 Bounds.Y=20

SectionStart =
    200 + 50 + 30 + 20
    = 300
```

实现需要沿布局祖先链把 Section 外框起始边换算到主 Content 的布局坐标。不能只读取
`section.Bounds.Y` 或 `section.Bounds.X`，也不能把相对 View、Window 或屏幕的坐标混入
ScrollViewer 内容坐标。

### Margin

Section 自身 Margin 的外沿不是 SectionStart：

```text
+--------------------------------+  <- Margin 前沿，不是锚点
|         Margin 24 DIP          |
+--------------------------------+  <- Layout Bounds 顶部，SectionStart
| ScrollMarkerSection            |
| 对话内容                       |
+--------------------------------+
```

Margin 仍然参与父 Panel 的 Measure/Arrange。开发者改变 Margin 后，如果父布局把 Section
安排到新的位置，新的 Layout Bounds 会自然产生新的 SectionStart；但实现不能在最终 Bounds
之外再减去 Margin，不能让 Margin 和 `AnchorOffset` 形成两套顶部补偿。

Border、Padding 和 Content 都位于 Section 外框内部，不改变锚点取外框起始边的定义。

### RenderTransform 与视觉动画

RenderTransform 和不参与 Measure/Arrange 的滑入、缩放或临时视觉动画不得改变
SectionStart：

```text
LayoutBoundsStart = 1024

动画帧 1：VisualStart = 1048
动画帧 2：VisualStart = 1036
动画帧 3：VisualStart = 1024

SectionStart 始终为 1024
```

否则内容没有滚动时，Marker 位置和活动项也会跟随动画抖动。Section 或任一布局祖先的纯
RenderTransform 都不能进入 Layout Bounds 坐标累加。

真正参与 Measure/Arrange 的布局变化必须更新 SectionStart。ChatGPT 式流式内容增长属于
真实布局变化：

```text
生成前：
Turn 2 Height = 200
Turn 3 SectionStart = 600

生成后：
Turn 2 Height = 700
Turn 3 SectionStart = 1100
```

完成新一轮有效布局后，Turn 3 的目标偏移和活动判定必须使用 1100。只要 Section 的稳定顺序
没有变化，Turn 3 对应 Marker 仍留在原来的顺序槽位，不随内容像素位置移动。

### 有效布局快照

只有已经完成有效 Measure/Arrange、仍属于当前主滚动坐标系且主轴坐标有限的 Section 才能
提交新的位置快照。从未完成 Arrange 的 Section 暂不参与位置排序和活动判定；已拥有稳定快照
但当前布局暂时失效的 Section 继续使用旧快照，直到新布局原子提交。已 Detach、业务隐藏，
或者新布局提交时换算结果为 `NaN`、Infinity 的 Section 不再使用旧快照参与导航。完整状态规则
见[导航资格与有效可见性](#导航资格与有效可见性)。

同一布局版本中的 Section 排序、同坐标分组、Automatic 判定和目标跳转必须使用同一份
SectionStart 快照，不能分别读取不同动画帧或不同布局阶段的坐标。Marker Track 使用该排序
结果生成统一槽位，不使用 SectionStart 的数值比例。

## 同坐标 Section

同坐标问题不是内容被重复绘制，而是多个不同 Section 在当前导航主轴上具有相同的原始起始
位置：

```text
chapter.SectionStart = 100
part-1.SectionStart  = 100

chapter -> TargetOffset 100
part-1  -> TargetOffset 100
```

正向导航仍然能够根据 `AnchorKey` 找到目标，但仅凭主 ScrollViewer 的 Offset 无法唯一反推出
当前活动 Section。

### 同坐标组

完成有效布局后，将原始主轴起始位置在内部布局容差内相等的 Section 视为一个同坐标候选组：

```text
PositionGroup 100
├─ chapter
└─ part-1

PositionGroup 220
└─ part-2
```

坐标比较不能使用严格的 `double ==`，而应使用项目统一的有限浮点比较策略：

```text
AreClose(A.SectionStart, B.SectionStart)
```

布局容差是内部数值策略，不开放 StyledProperty。

同坐标组只用于活动项计算：

- 不形成 Cluster。
- 不合并或删除 `ScrollMarkerItem`。
- 不改变 `AnchorKey`、`Label` 或 `MarkerTheme`。
- 不增加公开的 Group、Index 或 IsSamePosition API。

需要严格区分原始坐标相同与末尾范围钳制（clamp）：

```text
Section A 原始位置 = 900
Section B 原始位置 = 1000

由于 MaxScrollOffset = 800：
A.EffectiveTargetOffset = 800
B.EffectiveTargetOffset = 800
```

这种情况不属于同坐标组。A、B 在内容中仍有明确的先后位置，只是最终可达目标相同。

### 稳定内容顺序

同坐标候选项不能使用注册、Attach、Item 创建或异步加载先后排序。稳定顺序来自内容树前序：

```text
父 Section
└─ Content Panel
   ├─ 第一子 Section
   └─ 第二子 Section

稳定顺序：
父 Section -> 第一子 Section -> 第二子 Section
```

兄弟 Section 使用逻辑内容顺序；父 Section 位于其后代之前。动态重排内容树后重新生成稳定
顺序，但相同布局与内容树必须得到相同结果。

### Automatic 与 Explicit

活动项判定使用独立的内部状态：

```text
Automatic
    根据主内容位置自动判定活动 Section

Explicit(anchorKey)
    保持用户明确激活的 Section
```

本文将 `clamp` 统一称为“范围钳制”：把请求值限制在合法滚动区间内；它不是视觉 Clip，也不
删除任何内容。`Explicit(anchorKey)` 的正式中文名称为“显式选中锁定”，`Automatic` 为
“自动判定模式”。Explicit 表示当前活动项来自一次明确的 Marker 激活，不能仅因随后发生布局
或范围钳制就被 Automatic 覆盖。

Automatic 状态下，继续使用“最后一个已经越过 AnchorOffset 线的 Section”规则。同坐标组
按稳定内容顺序排列，因此默认活动项是组中的最后一个候选项：

```text
同坐标组：
[0] chapter
[1] part-1
[2] part-2

Automatic：
ActiveSection = part-2
```

用户明确激活同坐标组中的任意 Marker 时，先进入 `Explicit(anchorKey)`，再设置目标滚动偏移：

```text
用户激活 chapter
        │
        ├─ 进入 Explicit("chapter")
        ├─ 滚动到共同目标位置
        └─ chapter 保持 selected
```

Marker 导航产生的 Offset 变化不得立即使用 Automatic 的默认候选覆盖用户选择，避免出现
“点击 chapter，最终 part-2 高亮”。

### 状态转换

```text
                      用户激活 Marker A
+----------------+ --------------------------> +--------------------+
|   Automatic    |                             | Explicit("A")      |
| 位置自动判定   | <-------------------------- | 显式选择锁定       |
+----------------+   用户主动滚动主内容        +--------------------+
        ^                                             |
        |                                             |
        +---------------------------------------------+
          A 失效，或实际 Offset 偏离重算后的有效目标
```

状态规则：

- Automatic 下激活 Marker A，进入 `Explicit(A)`。
- Explicit(A) 下激活 Marker B，直接切换为 `Explicit(B)`。
- 导航事务活跃时，合格的主内容用户滚动意图立即解除锁定并进入 Automatic；导航事务已结束
  时，只有主 Offset 实际变化才解除锁定。
- A 退出当前可导航序列，解除锁定并基于当前 Offset 重新判定。
- 稳定布局更新后重新计算 A 的 `EffectiveTargetOffset`；实际 Offset 仍与该有效目标在布局容差
  内相等时保持 Explicit，否则解除锁定并基于当前 Offset 进入 Automatic。
- 普通主题变化、重绘、Navigator Follow 或不影响 A 位置的布局更新不解除锁定。

未来如果增加程序化 `NavigateTo(anchorKey)`，必须与 Marker 激活使用同一 Explicit 路径，
不能形成第三套选中规则。

### 滚动来源

不能把所有 OffsetChanged 都解释为用户滚动。Avalonia ScrollChanged 只报告 Offset、Extent 和
Viewport 的变化结果，不直接给出用户输入、Marker 导航或配置提交等业务来源。实现必须在
Offset 变化发生前登记意图，再把该意图与 ScrollChanged 的布局事实组合起来。

首版不使用一个互斥的“来源枚举”承载全部信息。一次 Marker 导航可能恰好与 Viewport 变化
发生在同一 ScrollChanged 中，所以来源模型分成两个正交维度：

```text
ScrollChangeContext
├─ ScrollIntent             谁发起了滚动意图
│  ├─ None
│  ├─ UserInput
│  ├─ MarkerNavigation
│  ├─ ContentEndFollow
│  └─ ConfigurationCommit
│
└─ ScrollChangeFacts        实际发生了哪些变化
   ├─ OffsetDelta
   ├─ ExtentDelta
   ├─ ViewportDelta
   └─ OffsetWasClamped
```

`ScrollIntent` 的语义固定为：

| Intent | 进入条件 | 首要语义 |
|---|---|---|
| `None` | 当前没有用户或内部操作登记滚动意图 | 由布局事实和目标有效性继续判断 |
| `UserInput` | 用户直接操纵主内容视口 | 不得冒充内部导航 |
| `MarkerNavigation` | Marker 激活路径在设置 Offset 前登记 | OffsetChanged 不得立即解除 Explicit |
| `ContentEndFollow` | Virtual Host 在逻辑 End 追加或尾部增长后跟随新终点 | 不得冒充 Marker 激活或用户滚动；Direct Host 不产生该 Intent |
| `ConfigurationCommit` | Orientation 原子提交重投影物理轴或归零 Offset | 不得冒充用户滚动 |

以下主内容输入登记为 `UserInput`：

- 鼠标滚轮。
- 触控或触控板滚动手势。
- 主 ScrollViewer 的 ScrollBar 小步、大步、ThumbTrack 和 EndScroll 交互。
- PageUp、PageDown、Home、End、方向键或其它主视口滚动键。

输入处理器必须在 ScrollViewer 消费输入并改变 Offset 之前登记 Intent。Marker 激活、Virtual
内容 End 跟随和 Orientation 配置提交同样必须通过内部 Coordinator 建立作用域，不能由任意代码
直接写 Offset。
首版已经关闭内容焦点 BringIntoView 和惯性，因此这两类路径不进入 Intent；未来开放时必须
扩展模型，不能复用一个不相符的现有值。

`ScrollChangeFacts` 保留 ScrollChanged 中的三个 Delta，并在旧 Offset 超出新合法范围、最终
Offset 被限制到新范围时标记 `OffsetWasClamped`。Facts 不是 Intent：例如下面的组合必须能够
同时表达，而不能被压缩成一个枚举值：

```text
用户滚轮：
    Intent = UserInput
    OffsetDelta != 0

Marker 导航同时窗口缩放：
    Intent = MarkerNavigation
    OffsetDelta != 0
    ViewportDelta != 0
    OffsetWasClamped = true 或 false

Virtual 尾部流式增长并保持 End：
    Intent = ContentEndFollow
    OffsetDelta != 0
    ExtentDelta != 0 或已在上一布局事实中观察到

内容缩短导致框架校正：
    Intent = None
    OffsetDelta != 0
    ExtentDelta != 0
    OffsetWasClamped = true
```

建议内部值对象至少等价于：

```csharp
internal readonly record struct ScrollChangeContext(
    ScrollIntent Intent,
    Vector OffsetDelta,
    Vector ExtentDelta,
    Vector ViewportDelta,
    bool OffsetWasClamped);
```

ScrollChanged 是结果观察点，不是来源真相。不能在收到 OffsetDelta 后无条件登记
`UserInput`，也不能仅依赖 RoutedEventArgs.Source 猜测发起者。Intent 登记、ScrollChanged
观察和 Automatic/Explicit 状态更新必须集中在同一个内部 Coordinator。

如果出现 `Intent=None`、OffsetDelta 非零且 ExtentDelta、ViewportDelta 均为零的变化，首版
按 `FrameworkCorrection` 处理：Debug 记录诊断信息，重新检查当前 Offset 与活动 Section，
但不得默认冒充用户输入并无条件解除 Explicit。该分支用于容纳框架校正并暴露遗漏的来源
登记；测试确认稳定的新输入路径后，应补充正式识别，而不是永久依赖猜测。

首版测试至少覆盖：

- 用户滚轮、触控手势和主 ScrollBar 操作均形成 `UserInput`。
- Marker 导航产生的 OffsetDelta 保持 `MarkerNavigation`，不会立即解除 Explicit。
- Virtual 内容 End 跟随产生的 OffsetDelta 保持 `ContentEndFollow`，不会被误判为 Marker 激活或
  用户输入；Direct Host 永远不产生该值。
- Orientation 提交归零 Offset 时形成 `ConfigurationCommit`。
- Extent 缩短造成的 Offset 范围钳制表达为 `Intent=None + OffsetWasClamped=true`。
- Marker 导航与 Viewport 变化同批发生时，同时保留 Intent 和 Facts。
- 无来源的纯 OffsetDelta 不被误判为 `UserInput`，并产生 Debug 诊断。

本节只固定来源模型和识别边界。`UserInput` 如何打断 MarkerNavigation，以及布局范围钳制后
是否继续保持 Explicit，分别由后续规则确定。不能仅在 ScrollViewer.Offset 变化时无条件解除
Explicit。Marker 导航事务生命周期由下一节固定。

### Marker 导航状态机

每个 `ScrollMarkerView` 或 `ScrollMarkerItemsView` Host 实例拥有且只拥有一个 Marker 导航
状态机；它由该 Host 的 `ScrollMarkerCoordinator` 所有，不是应用级单例，也不属于可被模板
替换的 `PART_ContentScrollViewer`：

```text
Application
├─ ScrollMarkerView A
│  └─ Coordinator A
│     └─ NavigationStateMachine A
└─ ScrollMarkerView B
   └─ Coordinator B
      └─ NavigationStateMachine B
```

多个 Host 可以作为兄弟控件或位于不同窗口，各自独立导航。共享的是状态机实现和契约，不是
运行时状态实例。单个状态机同一时刻最多保留一个 `CurrentRequest`。

首版 Marker 导航立即设置最终 Offset，不播放主内容平滑滚动动画。立即跳转仍然需要区分目标
登记、Virtual 目标实现、Offset 写入和布局确认；使用五个可观察 Phase 加一个瞬时终止阶段：

```text
Idle
  -> Requested
  -> RealizingTarget                 Virtual 专用；Direct 跳过
  -> ApplyingOffset
  -> AwaitingLayout
       ├─ Virtual 未收敛且仍有预算 -> ApplyingOffset
       └─ 完成或取消 -> Terminated
              ├─ Completed
              └─ Cancelled(reason)
  -> Idle
```

中文语义和职责固定为：

| Phase | 中文名称 | 进入后的职责 | 离开条件 |
|---|---|---|---|
| `Idle` | 空闲 | 当前没有 Marker 导航事务；Automatic 或 Explicit 选择仍可存在 | 收到有效 Marker 激活请求 |
| `Requested` | 已接收导航请求 | 登记 AnchorKey、Generation，做最低限度目标校验，并原子建立或替换 Explicit | Direct 目标有效则计算目标；Virtual 目标有效则进入实现阶段；无效则终止 |
| `RealizingTarget` | 正在实现目标 | Virtual Host 先登记 `ScrollIntent=MarkerNavigation`，再调用框架 ScrollIntoView 并等待身份正确的目标容器；Direct Host 跳过 | 目标布局有效、请求失败或被取代 |
| `ApplyingOffset` | 正在应用偏移 | Direct 登记、Virtual 延续 `MarkerNavigation` Intent，计算并写入最终有效 Offset | Offset 写入完成 |
| `AwaitingLayout` | 等待最终位置确认 | 接收 ScrollChanged、布局和范围钳制结果，只接受当前 Generation | Direct 达到有效目标；Virtual 有界收敛成功或失败；请求被取代 |
| `Terminated` | 导航终止 | 记录 Completed 或 Cancelled 结果、清理短期事务 | 立即返回 Idle |

`Idle` 只表示没有导航事务，不表示没有 selected 或 Explicit。正常完成后的典型状态是：

```text
NavigationPhase = Idle
SelectionMode   = Explicit(C)
SelectedMarker  = C
```

建议内部模型至少等价于：

```csharp
internal enum MarkerNavigationPhase
{
    Idle,
    Requested,
    RealizingTarget,
    ApplyingOffset,
    AwaitingLayout
}

internal readonly record struct MarkerNavigationRequest(
    string AnchorKey,
    long Generation,
    long TemplateGeneration,
    double? EffectiveTargetOffset);
```

Direct Host 在 Requested 中即可得到 SectionStart 和 EffectiveTargetOffset，因此跳过
RealizingTarget。Virtual Host 在目标容器实现前不能伪造精确 Offset，`EffectiveTargetOffset`
保持 null；其 SourceIndex、Descriptor、尝试次数、LayoutEpoch 和精确写入次数由
[Virtual Items 设计](virtual-items-design.md#有界两阶段导航协议)中的执行状态保存。

Completed 和 Cancelled 是瞬时终止结果，不作为长期 Phase。首版已经确定的取消原因包含：

- `Superseded`：更新的 Marker 请求取代旧请求。
- `UserInterrupted`：合格的主内容用户滚动意图取回控制权。
- `LayoutDiverged`：稳定布局重算后的有效目标已经偏离实际 Offset；不执行补偿滚动。

Virtual Host 还使用 TemplateChanged、TargetInvalidated、TargetRealizationFailed、InvalidLayout
和 AlignmentDidNotConverge 等内部取消原因，完整定义见
[Virtual Items 设计](virtual-items-design.md#完成取消与失败出口)。

配置切换是否形成其它取消原因由对应专题确定，不能由实现者临时扩张公共行为。

#### 立即导航与完成判定

目标 Offset 必须先经过主轴合法范围钳制（clamp）：

```text
RequestedOffset = SectionStart - EffectiveAnchorOffset
EffectiveTargetOffset = clamp(RequestedOffset, 0, MaxScrollOffset)
```

`ApplyingOffset` 设置的是 `EffectiveTargetOffset`。`AwaitingLayout` 比较实际 Offset 与有效目标，
不能把内容末尾无法严格顶对齐的范围钳制结果错误判成失败。比较使用统一布局容差，不要求浮点
逐位相等。

如果当前 Offset 已经等于有效目标且布局有效，赋值可能不产生 ScrollChanged；此时请求直接
Completed，不能永久停在 AwaitingLayout。如果布局尚未稳定，则继续等待最终布局快照。

事务 Completed 后只清除短期 `MarkerNavigation` Intent 和 CurrentRequest，保留目标
`Explicit(anchorKey)`。导航事务生命周期与选择锁定生命周期相互独立。

#### 最新请求优先

首版不建立导航请求队列，也不对 Marker 激活增加 debounce。更新请求立即取代旧请求：

```text
Requested/Realizing/Applying/Awaiting C, Generation=41
                    │
                    │ 激活 D
                    ▼
Terminated C, Cancelled(Superseded)
                    │
                    ▼
Requested D, Generation=42
```

Coordinator 递增 Generation，并把 `Explicit(C)` 直接替换为 `Explicit(D)`；中间不得进入
Automatic。旧请求已经产生的 Offset 不回滚，旧回调只需在发现 Generation 不匹配时停止处理：

```text
Callback.Generation = 41
Current.Generation  = 42
        -> 忽略旧回调
```

重复激活同一个仍在执行的请求不重复安排工作。旧请求与新请求不并行、不开队列，也不按历史
点击顺序快速播放，否则内容和 selected Marker 会产生无意义闪跳。

首版不设计“导航过程中 Section 突然删除后选择前项或后项”的恢复策略。Marker 激活入口仍须
确认目标当前存在、可导航且具有有效布局；入口已无效的请求以 InvalidTarget 终止且不写 Offset。
这属于最低限度不变量保护，不扩展为动态删除业务。

#### 用户输入打断

只有“合格的主内容滚动意图”参与 MarkerNavigation 仲裁。它必须同时满足：

```text
输入到达 PART_ContentScrollViewer
    + 输入方向匹配当前主轴
    + 没有被 Section 内局部 ScrollViewer 完整消费
    + 不是 PART_NavigatorScrollViewer 的 Browse 输入
```

局部 ScrollViewer 已经改变自己的 Offset、主 Offset 未变化且没有把输入链到主视口时，不打断
主导航。局部滚动到边界后通过 Scroll Chaining 把输入交给主视口，并由主视口消费时，则形成
合格主内容输入。Navigator 的滚轮、触控或 ScrollBar 只改变 Browse/Follow，不进入本状态机的
UserInput。

导航事务处于 Requested、RealizingTarget、ApplyingOffset 或 AwaitingLayout 时，合格输入在主
ScrollViewer 消费并改变 Offset 前立即执行：

```text
Active MarkerNavigation
    -> Terminated(Cancelled(UserInterrupted))
    -> Generation++，使旧回调失效
    -> CurrentRequest = null
    -> MarkerNavigation Intent 结束，UserInput Intent 开始
    -> Explicit(anchorKey) 解除并进入 Automatic
    -> 不回滚旧导航已经产生的 Offset
```

此规则不要求最终 OffsetDelta 非零。即使主视口已经位于边界，用户意图也必须终止仍可能在
稍后继续写 Offset 的旧导航；若该次输入没有产生 ScrollChanged，Coordinator 立即基于当前位置
执行一次 Automatic 判定，不能留下旧 Explicit。

导航事务已经结束、状态为 `Idle + Explicit(anchorKey)` 时采用不同边界：只有主内容
ScrollChanged 的主轴 OffsetDelta 非零才解除 Explicit 并进入 Automatic。边界滚动被主视口
拒绝或链到外层、主 Offset 未变化时保持 Explicit，避免内容不动但 selected Marker 闪烁。

| 当前状态 | 合格用户意图 | 主轴 OffsetDelta | 结果 |
|---|---|---:|---|
| Requested / RealizingTarget / ApplyingOffset / AwaitingLayout | 有 | 非零 | UserInterrupted，进入 Automatic |
| Requested / RealizingTarget / ApplyingOffset / AwaitingLayout | 有 | 零或无 ScrollChanged | 仍然 UserInterrupted，立即按当前位置进入 Automatic |
| Idle + Explicit | 有 | 非零 | 解除 Explicit，进入 Automatic |
| Idle + Explicit | 有 | 零或无 ScrollChanged | 保持 Explicit |
| Idle + Automatic | 有 | 非零 | 保持 Automatic 并重新计算活动项 |
| Idle + Automatic | 有 | 零或无 ScrollChanged | 状态不变 |

Idle 下的 UserInput Intent 在对应滚轮处理、ScrollGestureEnded 或 ScrollBar EndScroll 后清除；
不能因为没有 OffsetDelta 就永久残留。活跃导航被打断时，旧 MarkerNavigation 和新的 UserInput
不得同时成为当前主 Intent。

#### 布局范围钳制与 Explicit

`OffsetWasClamped` 是一次滚动变化的布局事实，不是解除显式选中锁定的充分条件。每次稳定布局
提交后，如果当前为 `Explicit(anchorKey)`，Coordinator 必须使用同一份布局快照重新计算：

```text
RequestedOffset = SectionStart(anchorKey) - EffectiveAnchorOffset
EffectiveTargetOffset = clamp(RequestedOffset, 0, MaxScrollOffset)
```

随后按以下顺序决定状态：

1. 目标已经不在当前可导航序列中，解除 Explicit，并按当前 Offset 进入 Automatic。
2. 目标仍有效，且 `AreClose(ActualOffset, EffectiveTargetOffset)`，保持 Explicit。
3. 目标仍有效，但实际 Offset 与重算后的有效目标超出布局容差，解除 Explicit，并按当前 Offset
   进入 Automatic。

因此，SectionStart、Extent、Viewport 或 MaxScrollOffset 发生变化本身都不能直接解除 Explicit。
即使发生范围钳制，只要实际 Offset 仍等于重算后的有效目标，显式选择就继续有效：

```text
内容末尾变短：
ActualOffset                 = 800
重算 EffectiveTargetOffset  = 800
结果                         = 保持 Explicit

流式布局改变目标：
ActualOffset                 = 800
重算 EffectiveTargetOffset  = 620
结果                         = 解除 Explicit，进入 Automatic
```

当第二种情况发生时，控件不得为了维持旧 Explicit 而再次写入 Offset、把内容自动拉回目标。布局
更新只重新验证锁定，不制造新的导航请求；这可以避免流式内容增长与补偿滚动形成反馈循环，也
避免控件和用户争夺视口控制权。

该规则同时适用于活跃导航和已完成导航。`AwaitingLayout` 在稳定布局后若实际 Offset 与重算目标
相等，则 Completed 并保留 Explicit；若二者已经偏离，则以 `Cancelled(LayoutDiverged)` 终止
当前请求、解除 Explicit 并按当前位置进入 Automatic。末尾多个 Section 因范围钳制得到同一
有效目标时，仍按原 AnchorKey 保持显式选择，不把这些 Section 合并为同坐标组。

#### 模板生命周期

状态机属于 Host/Coordinator，而不是模板部件。重新应用模板时必须递增 Generation、使当前
请求和旧布局回调失效，解绑旧 ScrollViewer，绑定新部件并回到 Idle；不能让旧回调修改新模板
部件，也不能把状态机做成跨 Host 的静态单例。

首版测试至少覆盖：

- 每个 Host 有独立状态机，一个 Host 的新请求不能取消另一个 Host 的请求。
- Direct 正常立即导航依次经过 Requested、ApplyingOffset、AwaitingLayout 并以 Completed 终止；
  Virtual 导航在 Requested 后额外经过 RealizingTarget。
- 零 OffsetDelta 的已到达目标不会卡在 AwaitingLayout。
- 内容末尾范围钳制后以 EffectiveTargetOffset 完成，不要求原始 SectionStart 顶对齐。
- C 尚未完成时激活 D，C 以 Superseded 终止，旧回调失效且 Explicit 直接变为 D。
- 活跃导航期间合格主内容输入即使没有 OffsetDelta，也以 UserInterrupted 终止并进入 Automatic。
- Idle + Explicit 下的边界输入没有主 OffsetDelta 时保持 Explicit；实际移动时进入 Automatic。
- 局部 ScrollViewer 完整消费的输入和 Navigator Browse 不打断主导航。
- 稳定布局重算后实际 Offset 仍等于有效目标时，即使发生范围钳制也保持 Explicit。
- 稳定布局重算后实际 Offset 偏离有效目标时进入 Automatic，且不得补写 Offset 自动回拉。
- 模板重新应用后旧 Generation 不能写入新 ScrollViewer。
- Completed 后 Phase 返回 Idle，但目标 Explicit 继续保持。

### 焦点与自动化

Marker 激活后，键盘焦点默认留在被激活的 `ScrollMarkerItem`，不自动抢到
`ScrollMarkerSection`。焦点和选中是不同语义：

```text
:focus-visible
    键盘当前操作的 Marker

:selected
    内容当前活动的 Marker
```

进入 Automatic 后，焦点可以继续停留在原 Item，而 selected 移动到新的活动 Item。任意时刻
只能有一个 Item 向自动化系统暴露选中状态；状态变化需要产生正常的选择通知，但不能因浮点
抖动反复宣布。

首版不支持由主内容焦点变化驱动滚动。`PART_ContentScrollViewer` 的
`BringIntoViewOnFocusChange` 固定为 `false` 且不公开配置：内容控件仍可获得焦点，但 Tab、
代码 `Focus()` 或其它焦点变化不能间接改变主 Offset。该边界不影响 Navigator 自身的键盘
浏览与 Marker 激活。后续若支持内容焦点 BringIntoView，必须把它作为独立滚动来源重新设计，
不能将其冒充用户滚动或 Marker 内部导航。

## 禁止 ScrollMarker Host 嵌套

首版禁止任意两个 ScrollMarker 根 Host 形成祖先与后代关系。相同模式和不同模式的嵌套都
不合法：

```text
Outer ScrollMarkerView
└─ Content
   └─ Inner ScrollMarkerView            禁止

Outer ScrollMarkerView
└─ Content
   └─ ScrollMarkerItemsView              禁止

Outer ScrollMarkerItemsView
└─ ItemTemplate / Content
   └─ ScrollMarkerView                   禁止

Outer ScrollMarkerItemsView
└─ ItemTemplate / Content
   └─ Inner ScrollMarkerItemsView         禁止
```

任一内层 Host 附加到树或运行时重新挂载时，必须检查祖先链；发现
`ScrollMarkerView` 或 `ScrollMarkerItemsView` 后立即抛出 `InvalidOperationException`。
异常行为在 Debug 和 Release 中一致，不能静默禁用内层导航。

### Section 嵌套不是 Host 嵌套

同一个 Direct Host 内，`ScrollMarkerSection` 可以包含另一个
`ScrollMarkerSection`。两者仍属于同一个主 ScrollViewer 和同一个 Coordinator，按照
[Section 嵌套](#section-嵌套)规则扁平化为独立 Marker：

```text
ScrollMarkerView
└─ ScrollMarkerSection "chapter"
   └─ ScrollMarkerSection "part-a"       允许
```

Section 的 Content 子树中不能嵌入完整的 `ScrollMarkerView` 或
`ScrollMarkerItemsView`：

```text
ScrollMarkerView
└─ ScrollMarkerSection "chapter"
   └─ ScrollMarkerView                   禁止
```

即使 `ScrollMarkerSection` 暂时没有注册到外层 Host，它也不能充当另一个完整 Host 的语义
外壳。任一 Host 附加或重新挂载时如果在祖先链中发现 `ScrollMarkerSection`，立即抛出
`InvalidOperationException`。

该限制保证每个 ScrollMarker 子树始终只有一个 Host、一个主滚动坐标系和一根 Navigator，
不需要定义父子 Host 之间的滚轮路由、焦点归属或两套 Follow/Browse 状态。Direct 和 Virtual
可以作为兄弟控件或位于应用中互不构成祖先关系的区域。

## 普通 ScrollViewer 边界

`ScrollMarkerView` 的 Content 可以包含普通 ScrollViewer。禁止的不是 ScrollViewer 嵌套，
而是 Section 跨越独立滚动坐标系注册。

### 合法：Section 包含普通 ScrollViewer

```text
ScrollMarkerView
└─ 主 Content ScrollViewer
   └─ ScrollMarkerSection "data-list"       外层导航目标
      └─ 普通内层 ScrollViewer              合法
         └─ 普通业务内容
```

外层 Marker 只把整个 `data-list` Section，也就是内层 ScrollViewer 的外框，带到主视口的
锚点线上。内层内容继续使用自己的 Offset。

### 非法：普通 ScrollViewer 包含 Section

```text
ScrollMarkerView
└─ 主 Content ScrollViewer
   └─ 普通内层 ScrollViewer                 独立滚动坐标系
      └─ ScrollMarkerSection "row-100"       禁止注册到外层
```

外层 View 只能修改主 ScrollViewer 的 Offset，不能保证 `row-100` 在内层 ScrollViewer 中
出现。Section 附加时如果其自身与所属 View 的主内容视口之间存在额外 ScrollViewer，拒绝注册
并抛出 `InvalidOperationException`。

准确约束是：

> `ScrollMarkerSection` 可以包含普通 ScrollViewer，但不能被主内容视口之外的额外
> ScrollViewer 包含。

## 排序

Registry 的注册先后不表示导航顺序。完成有效布局后，View 按 Section 在主滚动坐标系中的
实际起始位置排序：

- Vertical 使用有效 Y 轴顺序。
- Horizontal 在首版 LTR 范围内使用换算后的 `SectionStart` 升序，从左向右排序。
- 主轴坐标相同时使用稳定内容树顺序。
- Marker 只使用最终稳定顺序取得槽位序号，不使用 SectionStart 或经过范围钳制的有效目标偏移
  计算视觉位置。

增加、删除、重排、尺寸变化和主视口尺寸变化会使位置版本失效并重新排序。普通滚动只更新
活动项和主内容 Offset 状态，不重新创建全部 Item，也不重新计算 Marker 槽位。

## 导航资格与有效可见性

“当前是否绘制出像素”不能作为 Marker 存在条件。ScrollMarker 的用途正是导航到主视口外的
内容，因此需要严格区分：

```text
业务可见性       Section 自身及全部视觉祖先是否 IsVisible
位置就绪         是否拥有可供本轮导航使用的稳定布局快照
当前像素可见性   是否与 Viewport、Clip 或屏幕区域相交
```

只有业务可见性和位置就绪参与导航资格；当前像素可见性不参与。Direct Section 的正式判定式为：

```text
IsNavigable =
    IsRegistered
    && IsEffectivelyVisible
    && HasStableLayoutSnapshot
    && BelongsToMainScrollCoordinateSpace
```

四个条件的语义是：

- `IsRegistered`：Section 已通过 Key、所属 Host 和嵌套边界验证，仍在当前 View 的 Registry 中。
- `IsEffectivelyVisible`：直接使用 Avalonia 的有效可见性；Section 自身或任意视觉祖先
  `IsVisible=false` 时结果均为 false。
- `HasStableLayoutSnapshot`：Section 已经至少取得一份有效布局快照；快照包含换算到主 Content
  坐标系后的有限 `SectionStart`。
- `BelongsToMainScrollCoordinateSpace`：Section 与所属 View 的主内容视口之间不存在额外
  ScrollViewer，且仍属于同一个主滚动坐标系。

### 业务隐藏与恢复

```text
Section.IsVisible = false
或任意视觉祖先 IsVisible = false
        │
        ▼
IsEffectivelyVisible = false
        │
        ├─ 保留 Registry 身份和 AnchorKey 占用
        ├─ 退出当前可导航序列
        └─ 移除对应逻辑 Marker
```

恢复 `IsEffectivelyVisible=true` 后不能立即复用隐藏前的坐标参加导航。隐藏期间布局可能已经
变化，Section 必须等待新的有效 Arrange 和主 Content 坐标换算，再以新快照重新进入导航序列。

### 不构成隐藏的状态

以下状态不改变 `IsNavigable`，也不移除 Marker：

- Section 只是在主 ScrollViewer Viewport 之外。
- Section 被主视口或其它布局 Clip 完全裁掉。
- `Opacity="0"`。
- `IsEnabled="False"`。
- RenderTransform 暂时把 Section 绘制到其它视觉位置。

Viewport 和 Clip 只描述当前绘制范围。若完全裁剪也移除 Marker，长内容中绝大多数离屏 Section
会随着滚动反复消失，直接破坏锚点导航。开发者把 Section 永久放在错误裁剪区域属于业务布局
问题，控件不遍历任意 Clip 和 RenderTransform 推断像素可见性。

### 零尺寸 Section

已经完成有效布局且 `SectionStart` 有限时，宽度、高度或主轴尺寸为 0 不会使 Section 退出
导航。零尺寸 Section 可以表示空内容锚点、流式内容的初始状态或与其它 Section 同坐标的语义
位置：

```text
Vertical：Bounds.Height = 0      仍可导航
Horizontal：Bounds.Width = 0     仍可导航
```

零尺寸导致的相同 `SectionStart` 使用既定同坐标 Section 规则，不增加第二套选择算法。

### 首次布局与稳定快照

新 Section Attach 后已经注册但从未完成有效 Arrange 时进入内部 `PendingLayout` 状态：

```text
Registry 中保留 AnchorKey
尚无稳定 SectionStart
暂不进入可导航序列
暂不生成 Marker
```

首次获得有效 Arrange、有限 `SectionStart` 且属于主滚动坐标系后，原子加入可导航序列并生成
Marker。

已经拥有稳定快照的 Section 因窗口缩放、流式内容增长、Margin 变化或父 Panel 重新布局而
暂时 `IsArrangeValid=false` 时，不立即退出导航。Coordinator 继续使用上一份有效快照和 Marker，
直到新一轮布局提交：

```text
普通布局失效
    -> 保留上一份稳定快照
    -> 不闪烁或重排 Marker

新布局成功
    -> 原子替换快照
    -> 更新排序、目标偏移和活动项

新布局完成但坐标无效或离开主滚动坐标系
    -> 退出可导航序列
```

Detach/注销和 `IsEffectivelyVisible=false` 不使用旧快照兜底，必须立即退出当前导航序列。普通
布局失效与业务隐藏是两种不同状态，不能共用“立即移除 Marker”规则。

## Navigator 显示阈值

首版把 Navigator 定位为两个及以上可导航 Section 之间的导航工具，不承担单个孤立锚点的快捷
入口职责。

Navigator 的固定显示规则：

```text
EffectiveNavigableSectionCount <= 1
    -> Navigator Collapsed

EffectiveNavigableSectionCount >= 2
    -> Navigator Visible
```

`EffectiveNavigableSectionCount` 只统计满足上节 `IsNavigable` 判定式的 Section，不能使用
Registry 中包含业务隐藏项、首次布局待定项或无效坐标项的原始数量。同坐标及零尺寸 Section
仍然具有独立身份，分别计数，不能按一个坐标组折叠计数。

Collapsed 表示：

- Inline 模式不再为 Navigator 保留布局空间或 `NavigatorSpacing`，主 ScrollViewer 使用完整
  可用尺寸。
- Overlay 模式不绘制 Navigator，也不保留透明命中区域。
- 只隐藏 Navigator，不删除业务 Content、不注销仍在 Registry 中的 Section，也不重建主
  ScrollViewer。

可导航数量运行时从 1 增加到 2 时，按当前稳定 Section 顺序和位置显示 Navigator；从 2 降到 1
或 0 时直接折叠。首版不提供 `ShowSingleMarker`、`MinimumNavigatorItemCount` 或
`NavigatorVisibilityMode` 等覆盖属性。

## AnchorOffset

`ScrollMarkerView` 提供统一锚点线属性：

```csharp
public double AnchorOffset { get; set; } = 0d;
```

| 项目 | 契约 |
|---|---|
| 单位 | DIP |
| 默认值 | `0` |
| 起点 | 主内容视口的逻辑 Start 边 |
| Vertical | 沿 Y 轴计算 |
| Horizontal | 首版 LTR 下从视口左侧沿 X 轴计算，`LogicalOffset = Offset.X` |

`AnchorOffset` 同时用于活动 Section 判定和 Marker 激活后的目标对齐，不能拆成两套互相冲突的
偏移：

```text
主内容滚动：
Section 越过 AnchorOffset 线
    -> 成为活动 Section

用户激活 Marker：
目标 Section 滚到 AnchorOffset 线
    -> 跳转结果与活动状态一致
```

有效值计算：

```text
EffectiveAnchorOffset =
    clamp(Sanitize(AnchorOffset), 0, ViewportExtent)

活动判定位置 =
    CurrentScrollOffset + EffectiveAnchorOffset

目标滚动偏移 =
    clamp(SectionStart - EffectiveAnchorOffset, 0, MaxScrollOffset)
```

这里的 `clamp` 指范围钳制。负数、`NaN` 和 Infinity 按 0 规整。大于当前 ViewportExtent 的值
只在计算阶段进行范围钳制，不回写
绑定源。该属性可以直接补偿固定页头，例如 `AnchorOffset="64"`。

### Marker 激活后的目标对齐

在 Vertical 模式下，用户激活 `ScrollMarkerItem` 后，主内容 ScrollViewer 应将对应
`ScrollMarkerSection` 的顶部导航至 `AnchorOffset` 指定的锚点线。

这里的对齐参照是**主内容 ScrollViewer 的视口**，不是 Section 的直接父容器。
Section 可能位于 Grid、Border 或 StackPanel 等多层业务布局中，直接父容器的顶部不具有
统一的导航语义。

默认 `AnchorOffset="0"`，且目标没有触及滚动范围边界时：

```text
主内容 ScrollViewer 视口顶部
             │
             ▼
+--------------------------------+
| ScrollMarkerSection 顶部       |
| Question                       |
| Answer                         |
+--------------------------------+
```

配置非零偏移时，Section 顶部对齐偏移后的锚点线，而不是强制贴到视口顶部：

```text
主内容 ScrollViewer 视口顶部
+--------------------------------+
| 固定页头或保留区域             |
+--------------------------------+  <- AnchorOffset 锚点线
| ScrollMarkerSection 顶部       |
| Question                       |
| Answer                         |
+--------------------------------+
```

对齐采用“可达位置优先”，不能承诺所有 Section 都无条件严格落在锚点线上。尤其是内容末尾
空间不足时，目标偏移必须范围钳制到 `MaxScrollOffset`：

```text
RequestedOffset =
    SectionStart - EffectiveAnchorOffset

EffectiveTargetOffset =
    clamp(RequestedOffset, 0, MaxScrollOffset)
```

控件不得为了让最后一个短 Section 强制顶对齐而偷偷扩展内容 Extent、插入尾部占位元素或制造
大块空白。业务确实要求末尾 Section 严格顶对齐时，开发者应在内容布局中显式提供足够的底部
Padding。

正式契约是：

> 在 Vertical 模式下，用户激活 `ScrollMarkerItem` 后，主内容 ScrollViewer 应将对应
> `ScrollMarkerSection` 的顶部导航至 `AnchorOffset` 指定的锚点线。`AnchorOffset`
> 默认为 0，因此正常情况下 Section 顶部与主内容视口顶部对齐。目标偏移必须限制在有效
> 滚动范围内；内容首尾空间不足时采用最接近的可达位置，不凭空扩展内容尺寸。

在标准对话模型中，这个 Section 顶部就是 ConversationTurn 的 Question 顶部。末尾问答对下方
空间不足时，范围钳制可能使 Question 无法严格贴到锚点线；这属于既定滚动边界，不通过虚构尾部
空白改变内容尺寸。

该规则与状态机共同确定首版使用无动画的立即 Offset 跳转；合格的主内容用户输入按
[用户输入打断](#用户输入打断)规则取得优先权。未来版本如果重新引入平滑动画，必须扩展同一
状态机，不能建立第二套目标偏移规则。

## 活动 Section 判定

主内容滚动时使用固定锚点线：

1. 按稳定内容顺序取得当前可导航 Section。
2. 选择最后一个已经越过 AnchorOffset 线的 Section。
3. 尚无 Section 越线时选择第一项。
4. 主 ScrollViewer 到达最大滚动偏移时选择最后一项，避免末尾 Section 因内容不足而永远
   无法活动。
5. 切换边界使用内部滞回区，避免布局取整和微小反向滚动造成选中项抖动。

以上规则用于 Automatic 状态。同坐标候选按稳定内容树顺序排列，因此最后候选项成为默认
活动项；Explicit 状态按照[同坐标 Section](#同坐标-section)中的锁定与解除规则处理。

### Automatic 活动项滞回算法

首版固定使用空间滞回，不使用 debounce、计时器、滚动速度或动画。内部常量为：

```text
ActivationHysteresis = 4 DIP
```

该值在逻辑主轴坐标中计算，与像素缩放无关；它独立于仅用于浮点比较的 `LayoutTolerance`。
`ActivationHysteresis` 不注册为 StyledProperty，不允许每个 Host 或 Section 单独覆盖。后续只有
真实交互测试证明 4 DIP 不合适时，才能统一修改内部常量，不能在首版公共 API 中暴露试验参数。

滞回作用于完成容差分组后的 `PositionGroup`，不作用于组内单个 Section。同一位置组在
Automatic 下仍以稳定内容顺序中的最后候选项作为代表。定义：

```text
Probe = CurrentScrollOffset + EffectiveAnchorOffset

BaseGroup(position) =
    最后一个 SectionStart <= position 的 PositionGroup；
    如果尚无 PositionGroup 越线，则返回第一个 PositionGroup
```

Coordinator 在每份稳定布局快照中，通过当前 Automatic 活动 AnchorKey 找到它现在所属的
位置组。首版的完整判定优先级为：

```text
0 个可导航组
    -> ActiveSection = null

1 个可导航组
    -> 选择该组的 Automatic 代表项

Explicit(anchorKey)
    -> 不进入本算法，继续服从 Explicit 规则

Automatic + IsAtStart
    -> BaseGroup(Probe)，绕过滞回

Automatic + IsAtEnd
    -> 最后一个 PositionGroup，绕过滞回

Automatic + 没有有效的上一 Automatic 活动组
    -> BaseGroup(Probe)，不使用历史滞回

其它 Automatic 状态
    -> 执行下面的双向对称滞回
```

`IsAtStart` 与 `IsAtEnd` 使用统一布局容差，不能依赖严格的 `double ==`：

```text
IsAtStart = CurrentScrollOffset <= LayoutTolerance

IsAtEnd =
    CurrentScrollOffset >= MaxScrollOffset - LayoutTolerance
```

当 `MaxScrollOffset` 本身位于布局容差内时，起点和终点同时成立；起点规则优先，避免内容完全
无需滚动时无条件选中最后一项。`AnchorOffset` 大于 0 时，起点规则仍使用 `BaseGroup(Probe)`，
因此锚点线以内已经越线的 Section 会按正常语义参与判定。

设当前组为 `CurrentGroup`，先计算未经滞回的 `RawGroup = BaseGroup(Probe)`：

```text
RawGroup.Index > CurrentGroup.Index       向逻辑 End 前进
    Candidate = BaseGroup(Probe - ActivationHysteresis)
    Candidate 在 CurrentGroup 之后时才切换，否则保持 CurrentGroup

RawGroup.Index < CurrentGroup.Index       向逻辑 Start 返回
    Candidate = BaseGroup(Probe + ActivationHysteresis)
    Candidate 在 CurrentGroup 之前时才切换，否则保持 CurrentGroup

RawGroup.Index == CurrentGroup.Index
    保持 CurrentGroup
```

这等价于在每个位置组边界两侧各建立 4 DIP 的保持区。例如 B 的 `SectionStart=500`：当前为 A
时，`Probe >= 504` 才前进到 B；当前为 B 时，`Probe < 496` 才返回 A。边界内的微小反向滚动、
布局取整和子像素波动不得反复改变 selected。

大跨度滚动不逐组播放状态。一次 Offset 或布局提交跨越多个位置组时，以上公式直接求出最终
Candidate，只产生最终活动项变化；不能依次选择路径上的每个 Marker。进入 Automatic 的第一
次判定、从 Explicit 解除后首次判定，以及上一活动项已经退出可导航序列时，都没有可继承的
Automatic 历史，直接使用 `BaseGroup(Probe)`。

内容首尾必须绕过滞回。终点强制选择最后一个位置组，避免最后一个短 Section 因无法严格越过
锚点线而永远不能活动；起点立即使用起点候选，避免保留已经离开顶部的旧活动项。这里只绕过
空间滞回，不绕过同坐标分组和稳定内容顺序，也不覆盖 Explicit。

实现阶段至少验证：

- 当前 A 时在 B 边界前后小于 4 DIP 的波动保持 A，到达 `B.SectionStart + 4` 后切换 B。
- 当前 B 时回滚到 `B.SectionStart - 4` 仍保持 B，严格越过该边界后才切回 A。
- 边界附近连续子像素波动不重复产生 selected、Follow 或自动化选择通知。
- Offset 到达起点时立即按 `BaseGroup(Probe)` 判定；到达终点时立即选择最后一个位置组。
- `MaxScrollOffset` 位于布局容差内、起点和终点同时成立时执行起点规则。
- 同坐标 Section 只形成一个滞回位置组，Automatic 代表项仍是组内稳定顺序的最后候选。
- 一次大跨度滚动跨越多个组时只提交最终活动项，不产生中间 Marker 选择序列。
- 从 Explicit 进入 Automatic 或上一活动项失效后的首次判定不继承旧滞回状态。
- Vertical 与首版 Horizontal LTR 均使用逻辑主轴 `SectionStart` 执行同一算法。

## 动态更新链路

```text
Section Attach / Detach / Reparent / IsEffectivelyVisible / LayoutSnapshot 变化
          │
          ▼
更新内部 Registry 和可导航序列
          │
          ▼
重新排序并计算 EffectiveTargetOffset
          │
          ▼
增量更新 ScrollMarkerItem 映射
          │
          ▼
Section 数量或顺序变化时更新统一槽位 Track；
更新当前活动 Section
```

所有更新都必须保持 `AnchorKey` 到 Item 的稳定映射。普通 Offset 更新不能触发完整视觉树扫描，
也不能无条件重建全部 Marker。

## 同坐标实现验证

实现阶段至少验证：

- 父 Section 与首个子 Section 同坐标时，主内容用户滚动稳定选择最后候选项。
- 用户激活同坐标组中的非默认项时，被激活项保持 selected。
- Marker 导航产生的 OffsetChanged 不解除 Explicit。
- 活跃 MarkerNavigation 被合格主内容输入打断时，即使 OffsetDelta 为零也解除 Explicit；
  Idle + Explicit 只有主 Offset 实际变化才恢复 Automatic。
- Explicit 目标退出可导航序列时进入 Automatic；目标仍有效时，只有实际 Offset 偏离重算后的
  EffectiveTargetOffset 才进入 Automatic。
- SectionStart、Extent、Viewport 或 MaxScrollOffset 改变后，如果实际 Offset 仍在容差内等于
  重算目标，则保持 Explicit；若已偏离则进入 Automatic，且不自动回拉。
- 坐标存在有限浮点误差时不拆分错误分组，也不发生 selected 闪烁。
- 原始 SectionStart 不同、仅因末尾范围钳制得到相同 EffectiveTargetOffset 的 Section 不被分组。
- 同坐标 Marker 保持独立命中区、主题、焦点、Label 和自动化名称。
- 任意时刻只有一个 Marker 向自动化系统暴露选中状态。
- 可导航 Section 数量为 0 或 1 时 Navigator Collapsed，数量达到 2 时恢复显示。
- Inline 折叠后不保留布局空间，Overlay 折叠后不保留绘制或命中区域。

## SectionStart 实现验证

实现阶段至少验证：

- Section 位于多层 Grid、Border 和 StackPanel 中时，正确换算到主 Content 布局坐标。
- Section 带有 Top/Start Margin 时，不把 Margin 前沿当作 SectionStart。
- Margin 改变并影响最终 Arrange 时，使用新的 Layout Bounds 更新 SectionStart。
- Section 或布局祖先的 RenderTransform 动画不改变 SectionStart、Marker 顺序或活动项。
- 流式回答高度增长后，下游 SectionStart、目标偏移和活动项在有效布局后更新；稳定顺序未变时
  Marker 槽位不因内容像素增长而移动。
- 标准对话模板中每个 ConversationTurn 只注册一个 Section；Question 与 Answer 不分别生成
  Descriptor 或 Marker，点击后以 Question 所在的 SectionStart 为目标。
- 未 Arrange、已 Detach、`NaN` 或 Infinity 坐标不进入位置快照。
- 同一布局版本中的排序、目标偏移、同坐标分组和活动判定使用同一份 SectionStart 快照。

## 导航资格实现验证

实现阶段至少验证：

- Section 自身 `IsVisible=false` 或任意视觉祖先 `IsVisible=false` 时退出可导航序列，但 Registry
  继续保留稳定 AnchorKey；恢复后等待新布局快照再重新加入。
- Section 滚出主 Viewport、被 Clip 完全裁掉、`Opacity=0` 或 `IsEnabled=false` 时仍保留
  Marker 和导航资格。
- Vertical 的零高度 Section 与 Horizontal 的零宽度 Section 在坐标有限时仍分别生成独立
  Marker；同坐标选择继续服从稳定顺序和 Explicit 规则。
- 新注册 Section 在首次有效 Arrange 前处于 PendingLayout，不生成无序 Marker；首次快照提交
  后原子加入正确槽位。
- 已有 Section 因窗口缩放、流式增长或父布局失效而暂时 `IsArrangeValid=false` 时继续使用上一
  份快照，Marker 不闪烁；新快照提交后一次性更新。
- Detach、注销或 `IsEffectivelyVisible=false` 立即退出，不能用旧快照继续维持 Marker。
- 新布局完成但 SectionStart 为 `NaN`、Infinity 或已经离开主滚动坐标系时退出可导航序列。
- `EffectiveNavigableSectionCount` 只统计 `IsNavigable` Section，并在 0/1/2 边界正确折叠或
  恢复 Navigator。

## 当前不提供

`ScrollMarkerView` Direct Content 本轮不提供：

- 可写的 `Sections` 集合或 `ItemsSource`；数据驱动入口属于独立的
  `ScrollMarkerItemsView`。
- 程序化 `NavigateTo` 方法。
- 双向可写的 `ActiveAnchorKey`。
- Center、End 或 Nearest 等其它目标对齐模式。
- 每个 Section 独立覆盖 `AnchorOffset`。
- 公开的同坐标 Group 或布局容差属性。
- `ShowSingleMarker`、`MinimumNavigatorItemCount` 或 `NavigatorVisibilityMode`。
- 首版之后可能增加的平滑滚动时长、缓动和动画打断规则。
- 返回或替换内部 `PART_ContentScrollViewer` 实例的公共属性；滚动配置只通过 Host 的
  Orientation 和语义属性 `MainScrollBarVisibility` 投影到内部物理轴。

除 `ItemsSource` 已明确归属 Virtual Items Host 外，其余能力需要真实原型或业务需求后再
设计，不能由实现者临时添加。
