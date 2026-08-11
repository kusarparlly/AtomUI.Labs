# ScrollMarker Marker 虚拟化验证设计

> 文档状态：验证契约部分施工，更新于 2026-08-02。仓库中已经存在 Headless 结构测试和初版
> Performance Runner；正式 Benchmark、长稳、环境报告和真实桌面验收尚未完成。

本文回答一个核心问题：`ScrollMarkerTrackPanel` 即使声明继承
`VirtualizingStackPanel`，如何证明运行时真的只创建 Navigator Viewport 附近的
`ScrollMarkerItem`，而不是仍然创建全部 Marker 容器。

这里所说的“测试业务逻辑”是测试用例的验证场景和验收逻辑，不是 ScrollMarker 对外提供的
运行时业务 API。Marker 虚拟化的正式布局与容器契约见
[导航条 UI 与交互设计](navigator-design.md#marker-容器实现范围)。

标准对话数据的计数单位是 `ConversationTurn`（Question + Answer 问答对）。一个 Turn 产生一个
Descriptor 和一个逻辑 Marker；`LogicalCount=N` 表示 N 个问答对，不表示 2N 条独立消息。

## 验证目标

Marker 总数为 N 时，设计允许并要求：

```text
MarkerDescriptor 数量        = N
ScrollMarkerItem 实际容器数量 ≈ Viewport + CacheLength + 框架特殊保留项
```

`MarkerDescriptor` 是轻量逻辑数据，按 N 存在是预期行为。验证对象是昂贵的
`ScrollMarkerItem` 控件、视觉树节点和容器生命周期；测试不能把“Descriptor 仍然全部存在”
误报为虚拟化失败。

当 Marker 总量从 100 增加到 1,000、10,000，而 Navigator Viewport、槽位尺寸和缓存策略不变
时，实际 Marker 容器数量必须保持在同一量级，不能随 N 线性增长。

## 验证边界

本文只验证两个 Host 共享的 Navigator Marker 虚拟化：

```text
ScrollMarkerNavigator : SelectingItemsControl
└─ ScrollMarkerTrackPanel : VirtualizingStackPanel
   └─ ScrollMarkerItem × K
```

不在本文验证：

- Direct Host 中开发者创建的全部 `ScrollMarkerSection`。Direct 模式不虚拟化业务 Section。
- `ScrollMarkerItemsView` 的 Section 内容虚拟化、框架平均尺寸估算和内容 Panel
  `CacheLength` 验证；这些由
  [Virtual Items 验证与极限性能门禁](virtual-items-verification.md)单独覆盖。
- 主内容滚动的平滑动画、平台渲染器或 GPU 性能。
- 未经实现和实测支持的最大 Marker 数量或毫秒级性能承诺。

大规模 Marker 测试优先直接承载内部 `ScrollMarkerNavigator` 和 MarkerDescriptor 序列，避免
Direct Content 的 N 个实体 Section 干扰 Marker 容器数据。另设少量公共 Host 模板集成测试，
证明真实 `ScrollMarkerView` 模板确实连接到相同的虚拟化 Panel。

## 术语与观测值

| 名称 | 含义 | 是否作为确定性门禁 |
|---|---|---|
| `LogicalCount` | MarkerDescriptor 总数 N；标准对话中等于 ConversationTurn 数量 | 是 |
| `ExpectedRealizedRange` | Viewport 与 `CacheLength` 推导出的预期连续索引范围 | 是 |
| `ActualRealizedContainers` | `GetRealizedContainers()` 返回的当前真实容器 | 是 |
| `PreparedIndices` | 本次操作触发 `ContainerPrepared` 的 Marker 索引 | 是 |
| `UniqueContainerInstances` | 按引用身份统计的真实容器实例 | 是 |
| `FrameworkRetainedContainers` | 焦点或进行中的 `ScrollIntoView` 暂时保留的额外容器 | 是，需显式分类 |
| Elapsed Time | 指定操作的耗时 | 只记录 Benchmark 基线 |
| Allocated Bytes | 指定操作期间的托管分配 | 只记录 Benchmark 基线 |

测试通过 `InternalsVisibleTo` 访问内部 Navigator、Descriptor 和固定槽位状态，沿用现有 LED
测试项目的友元程序集模式。容器观测优先使用 Avalonia 已有的 `ContainerPrepared`、
`ContainerClearing`、`ContainerIndexChanged` 事件和 `GetRealizedContainers()`；正式运行时
程序集不增加公共计数器、诊断属性或测试专用开关。

## 固定基线场景

首轮结构验证使用以下固定输入：

```text
Orientation                  = Vertical
MarkerSlotExtent             = 24 DIP
NavigatorViewportExtent      = 240 DIP
CacheLength                  = 0.5
Viewport 可见容量            = 10 个 Marker
前置缓存                     ≈ 5 个 Marker
后置缓存                     ≈ 5 个 Marker
ExpectedRealizedCount        ≈ 20 个 Marker
MarkerCount                  = 100 / 1,000 / 10,000
```

Horizontal 使用等价的 240 DIP 宽度和 24 DIP 槽位，验证同一逻辑公式映射到 `Offset.X`。

测试不能把实际数量硬编码为恰好 20。允许：

- 槽位与 Viewport 边界取整造成至多 2 个边界容器差异。
- 至多 1 个当前键盘焦点容器。
- 至多 1 个当前串行 `ScrollIntoView` 目标容器。

稳定布局且没有焦点和进行中的 `ScrollIntoView` 时：

```text
ActualRealizedCount
    <= ExpectedRealizedCount + BoundaryAllowance

BoundaryAllowance = 2
```

存在特殊保留项时：

```text
ActualRealizedCount
    <= ExpectedRealizedCount
       + BoundaryAllowance
       + ExplicitFrameworkRetainedCount
```

每个超出 `ExpectedRealizedRange` 的容器都必须能被分类为明确的焦点或
`ScrollIntoView` 保留项。不能用“框架可能保留容器”掩盖无法解释的大量额外实例；若 Avalonia
版本升级改变边界行为，必须先形成验证记录再调整测试容差。

## 测试层级

### 纯算法测试

不创建视觉树，验证确定性计算：

- `EffectiveMarkerSlotExtent`、TrackExtent 和 MarkerSlotStart。
- Viewport Start、中间和 End 位置的 `ExpectedRealizedRange`。
- Vertical 与 LTR Horizontal 使用相同逻辑槽位结果。
- 0、1、2 个 Marker、刚好容纳、刚好溢出和非整槽 Offset。
- `MarkerSlotExtent` 为负数、`NaN` 或 Infinity 时的规整。
- 大数量乘法只产生有限非负 Track 坐标。

算法测试不能替代真实布局测试。公式正确不代表模板向
`VirtualizingStackPanel` 提供了有效的有限 Viewport。

### Avalonia Headless 布局测试

计划测试项目：

```text
tests/AtomUI.Labs.Controls.ScrollMarker.Tests
```

测试沿用仓库现有 Avalonia Headless、xUnit 和 Shouldly 初始化模式，创建具有确定宽高的真实
Host、ScrollViewer、ItemsPresenter 和 `ScrollMarkerTrackPanel`，执行完整 Measure、Arrange
与 Dispatcher 布局稳定过程。

每个测试遵循：

```text
Given：构造逻辑 Marker、视口、槽位和焦点状态
When：执行布局、滚动、跳转、集合变化或键盘操作
Then：检查实现范围、容器生命周期和视觉状态
```

禁止只实例化 Panel 后传入无限尺寸完成验证；这无法证明真实模板中的 EffectiveViewport 和
ScrollViewer 接线正确。

### Release Benchmark

计划性能项目：

```text
tools/performances/AtomUI.Labs.Controls.ScrollMarker.Performance
```

Runner 直接测试正式内部 Navigator，不用性能原型替代正式容器逻辑。运行报告必须记录：

- Git commit、配置和 Avalonia/AtomUI 版本。
- 操作系统、CPU、运行时和进程架构。
- MarkerCount、Viewport、槽位、CacheLength、方向和 Theme 复杂度。
- 预热次数、测量次数和是否使用真实窗口或 Headless。
- Elapsed Time、Allocated Bytes、Prepare 次数、不同容器实例数和峰值实现数量。

Elapsed Time 和 Allocated Bytes 是带环境条件的性能证据。首版没有基线前不得把某个毫秒数、
吞吐量或分配值设置为跨机器 CI 阈值。

## 结构性测试矩阵

### 初始实现范围与规模稳定性

分别使用 100、1,000 和 10,000 个 Descriptor，在相同 Viewport 下完成初始布局：

1. `LogicalCount` 必须等于输入 N。
2. 当前可见 Marker 必须全部存在于 `ActualRealizedContainers`。
3. 实际容器必须满足预期范围、边界容差和特殊保留项上限。
4. 10,000 项场景不能生成接近 10,000 个容器。
5. 三种规模的稳定布局峰值容器数量必须保持同一量级；N 增加 100 倍不能使容器数量增加
   100 倍。

如果 `ActualRealizedCount == LogicalCount` 且 Track 已经溢出 Viewport，直接判定虚拟化失败。

### Viewport 滚动

从 Start 滚动到中间、End，再返回 Start：

- 每个稳定位置的可见项都已实现。
- 离开缓存区且没有焦点/ScrollIntoView 原因的容器可以被回收。
- `PreparedIndices` 只覆盖新进入扩展 Viewport 的附近项，不出现整表 Prepare。
- 多次往返后，`UniqueContainerInstances` 应进入平台相关但有界的稳定区间，不能每轮继续按
  经过的 Marker 数量线性增长。

### 远距离 Follow

固定场景：

```text
MarkerCount = 10,000
OldIndex    = 5
TargetIndex = 5,000
```

清空本次操作的 `PreparedIndices` 记录后，使活动 Marker 从 5 直接变为 5,000：

1. SelectionModel 可以在目标容器尚未实现时先保存逻辑 selected。
2. Navigator 根据 `TargetIndex * EffectiveMarkerSlotExtent` 一次设置最终 Offset。
3. 稳定布局后目标 Marker 位于 Follow 安全区并呈现 selected。
4. 新 Prepare 的索引必须位于目标预期范围或明确的框架保留集合。
5. 不得依次 Prepare 6～4,999，也不得生成跨越路径上的全部容器。
6. 自动 Follow 不改变当前键盘焦点，不产生 Navigator 平滑滚动动画队列。

### 键盘与框架保留项

- 方向键在首尾停止，不循环。
- `Home`/`End` 定位未实现目标时使用 `ScrollIntoView`，目标实现后再取得焦点。
- 被滚出预期范围但仍持有焦点的 Marker 可以作为一个额外容器保留。
- 自动活动变化只改变 selected，不抢夺焦点。
- 释放或移动焦点并完成稳定布局后，无其它原因的旧焦点容器应可以回收。

### 容器复用与状态隔离

使用明显不同的 Descriptor：

```text
Marker A：Label="A"，Red Theme，selected
Marker K：Label="K"，Blue Theme，not selected
```

滚动使容器离开实现范围并在远处复用后验证：

- 每个当前容器的 AnchorKey、Label、Theme、selected、Tooltip 和自动化名称与当前 Descriptor
  完全一致。
- A 的 pressed/pointer 临时状态、事件或命令关联不能泄漏给 K。
- 测试按引用记录容器；一旦观察到同一实例换绑，必须专门验证其旧状态已清理。
- 即使某次平台调度没有复用指定实例，所有当前容器仍必须通过状态一致性检查，测试不能依赖
  某个固定回收顺序。

### 动态集合与布局变化

本小节直接验证共享 Navigator 的 Descriptor 输入和 Direct Host 集成，不扩大 Virtual Items 的
业务集合契约。`ScrollMarkerItemsView` 首版只接受 End Add；Remove、Move、Replace、Reset 和运行期
ItemsSource 替换必须按 Virtual Faulted 协议终止。

- Add、Remove、Move 分别只更新受影响 Descriptor 和当前实现范围，不一律 Reset 整表。
- AnchorKey 相同的 Descriptor 尽可能保持身份，selected 不因数组索引移动指向其它 Marker。
- Viewport、`MarkerSlotExtent` 或 Orientation 变化后重新计算范围，但不实现全部 Marker。
- 远距离请求执行期间集合版本变化时，旧 `AnchorKey + 集合版本` 请求失效。
- 公共 `ScrollMarkerView` 模板必须实际使用内部 `ScrollMarkerTrackPanel`；开发者 Theme 不能把
  ItemsPanel 替换为非虚拟化 Panel。

## Benchmark 场景

Release Runner 至少覆盖：

| 场景 | 操作 | 重点指标 |
|---|---|---|
| Initial Layout | 100/1,000/10,000 项首次布局 | 时间、分配、Prepare、峰值容器 |
| Sequential Scroll | 连续滚动 100 个槽位 | 每步时间、Prepare/Clear 趋势、实例复用 |
| Far Follow | 5 → 5,000，再返回 5 | 时间、Prepare 索引、峰值容器 |
| Full Sweep | Start → End → Start，重复多轮 | 实例是否持续增长、长稳分配 |
| Resize | 在两种固定 Viewport 间重复切换 | 重测量成本、范围收缩与扩张 |
| Theme Cost | 默认 Theme 与代表性自定义 Theme | Theme 准备成本，不改变槽位数量 |

每个场景先完成布局和运行时预热，再进行多轮测量；报告必须同时输出确定性结构指标和机器相关
指标。若结构门禁失败，即使毫秒数据看起来很快，也不能宣称虚拟化通过。

## 失败判定

出现任一情况即属于实现缺陷：

- Track 溢出时实际创建全部或接近全部 Marker 容器。
- MarkerCount 增长导致稳定视口容器数量近似线性增长。
- 远距离 Follow 逐个实现中间 Marker。
- 重复滚动后不同容器实例持续无界增长。
- 容器复用后遗留旧 Label、Theme、选择、自动化或事件状态。
- 实际模板没有使用 `ScrollMarkerTrackPanel : VirtualizingStackPanel`，或 Panel 得不到有限
  EffectiveViewport。
- 测试为了通过而不断扩大无法解释的容器数量容差。

单次机器上的毫秒波动不自动构成语义失败。性能是否回归必须在相同场景、相同口径和足够重复
测量下判断，并与带条件的历史基线比较。

## 首版交付物

实现 ScrollMarker 时应同时交付：

1. 纯算法测试和 Avalonia Headless 结构测试。
2. 独立 Release Performance Runner。
3. 一份带日期、机器、框架版本和 Git commit 的首轮性能报告。
4. Gallery 中可人工观察 Marker 大量数据、远距离 Follow、键盘焦点和容器复用的 Workbench。

正式文档只能在相应自动化或 Runner 实际产生后，把状态从“目标验证契约”更新为“当前测试
契约”或“历史性能快照”。在此之前不得写成已经通过，也不得引用假设数据作为发布证据。
