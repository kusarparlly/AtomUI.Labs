# ScrollMarkerItemsView Virtual Items 验证与极限性能门禁

> 文档状态：验证契约部分施工，更新于 2026-08-02。本文固定方案 A 的测试实现、极限
> 数据矩阵、Benchmark 口径、发布门禁和方案 B 强制重审条件。仓库已有 Headless 测试、Gallery
> 和初版 Performance Runner，但正式多进程 Benchmark、长稳与真实桌面验收尚未完成。

本文验证 `ScrollMarkerItemsView` 的内容虚拟化，而不是共享 Navigator Marker 虚拟化。共享
`ScrollMarkerTrackPanel` 的独立门禁见
[Marker 虚拟化验证设计](marker-virtualization-verification.md)；Virtual Items 的正式功能契约见
[Virtual Items 设计](virtual-items-design.md)。两个验证套件都必须通过，不能用其中一个替代另一个。

本文所有标准对话规模中的 N 都表示 `ConversationTurn` 问答对数量。每项同时包含 Question 与
Answer，只生成一个 Descriptor、一个逻辑 Marker 和一个可回收内容容器身份；测试不得用
Question、Answer 各占一个 ItemsSource 项的错误数据模型把逻辑规模扩大为 2N。

## 发布判定原则

当前接受验证的唯一内容方案是方案 A：

```text
ScrollMarkerItemsPanel : VirtualizingStackPanel
├─ Avalonia EffectiveViewport
├─ 连续实现范围与 CacheLength=0.5
├─ 框架平均尺寸估算与 Extent 修正
├─ 框架容器生成、回收和复用
└─ Framework ScrollIntoView(index)
```

方案 A 不得拥有逐 Item 精确尺寸表、第二对象池、离散屏幕外保留集合或第二套 Extent 真相。
验证结果只有三种：

```text
Passed
    -> 全部确定性、性能和人工门禁通过，形成首版发布基线

ImplementationFailed
    -> 设计允许实现，但当前代码存在缺陷；修复后重新跑完整门禁

SchemeARejected
    -> 最小复现证明失败来自 VirtualizingStackPanel 的固有限制
    -> 阻止发布并重新评审方案 B
```

正确性门禁零容忍。性能门禁必须在规定环境和重复口径下通过，不允许用平均分抵消任一确定性
失败，也不允许在同一次实现评审中为了通过而放宽阈值。

## 计划测试与性能项目

测试和 Runner 使用正式产品类型，不用行为不同的性能原型替代：

```text
tests/AtomUI.Labs.Controls.ScrollMarker.Tests
tools/performances/AtomUI.Labs.Controls.ScrollMarker.Performance
samples/AtomUI.Labs.Gallery/Controls/ScrollMarker
```

测试项目使用 xUnit、Shouldly、Avalonia Headless 和 `InternalsVisibleTo`。正式程序集不为测试增加
公共计数器、虚拟化开关或对象池入口；内部诊断探针只允许从测试友元访问。

Release 验证至少覆盖仓库规定的 `net8.0` 和 `net10.0`。Debug 可以用于诊断，但不能替代 Release
结构测试、Benchmark 和真实桌面运行。

## 统一观测模型

内部测试探针至少记录：

| 观测值 | 语义 | 门禁性质 |
|---|---|---|
| `LogicalItemCount` | 当前业务 Item 数量 N | 确定性 |
| `CommittedDescriptorCount` | 已原子提交的 Descriptor 数量 | 确定性 |
| `ExpectedRealizedRange` | Viewport、Cache 和布局事实推导的正常连续实现范围 | 确定性 |
| `ActualRealizedContainers` | 当前真实 Section Container 集合 | 确定性 |
| `PreparedIndices` / `ClearedIndices` | 每次操作发生的 Prepare/Clear 索引 | 确定性 |
| `UniqueContainerInstances` | 按引用身份累计的不同 Section Container | 确定性 |
| `FrameworkRetainedContainers` | 当前 ScrollIntoView 等框架原因保留的额外容器 | 确定性，必须逐项分类 |
| `LayoutEpoch` | 有效主内容布局的单调版本 | 确定性 |
| `NavigationGeneration` / `HostGeneration` | 导航与 Host 生命周期身份 | 确定性 |
| `OffsetWritesByIntent` | 各 ScrollIntent 产生的主 Offset 写入 | 确定性 |
| `SnapshotEntryCount` | 每份 VirtualLayoutSnapshot 的 K | 确定性 |
| `ItemsSourceEnumerationCount` | 初始化后普通滚动是否扫描全集 | 确定性 |
| `ElapsedTime` / 分位数 | 操作耗时 | 条件性能门禁 |
| `AllocatedBytes` / `RetainedBytes` | 托管分配与稳定保留内存 | 条件性能门禁 |
| `FrameTime` / DroppedFrames | 真实桌面帧时间 | 发布参考机器门禁 |

每个超出正常实现范围的 Container 都必须关联具体 `SourceIndex`、ContainerGeneration 和保留
原因。“框架可能保留”不是合法分类。

## 四层验证实现

### 纯算法与状态机测试

不创建视觉树，完整验证：

- Descriptor 投影、稳定顺序、Key/Index 映射和原子批次。
- Automatic/Explicit、PositionGroup、0.5 DIP 布局容差和 4 DIP 空间滞回。
- MarkerNavigation 五阶段、最新请求优先、次数预算和全部取消出口。
- Content FollowingEnd/BrowsingHistory、追加前快照和 ScrollIntent 仲裁。
- Running/Faulted 单向转换、HostGeneration 和迟到回调失效。
- Vertical 与 Horizontal LTR 主轴换算、Start/End 和 Offset clamp。

除固定边界样本外，使用至少 5 个固定随机种子执行属性化序列。随机失败必须输出种子、完整输入
和最小化后的操作序列，不能只输出最终断言。

### Avalonia Headless 集成测试

必须创建真实的：

```text
ScrollMarkerItemsView
└─ PART_ContentScrollViewer
   └─ ItemsPresenter
      └─ ScrollMarkerItemsPanel : VirtualizingStackPanel
         └─ ScrollMarkerSectionContainer × K
```

测试执行真实 Measure、Arrange、EffectiveViewport、Dispatcher 和容器生命周期。推进状态必须等待
明确 LayoutEpoch 或 Dispatcher 队列条件，禁止使用固定毫秒 `Sleep`、无限重试或扩大超时掩盖
未收敛。

### Release Benchmark

Performance Runner 同时运行：

```text
Baseline：ItemsControl + VirtualizingStackPanel
Candidate：ScrollMarkerItemsView + ScrollMarkerItemsPanel
```

两者使用相同 ItemsSource、ItemTemplate、Viewport、方向、缩放和操作序列。Baseline 不创建
Descriptor/Navigator；报告必须单独列出 ScrollMarker 提供导航语义所增加的成本。

每项 Benchmark 使用至少 3 个干净进程；每个进程充分预热后至少测量 15 轮。变异系数大于 5%
的运行视为无效环境，必须诊断并重跑，不能选择性保留较快轮次。批量操作应使 Baseline 单轮中位
耗时至少 10 ms，避免用接近计时器分辨率的结果计算比例。

### 真实桌面 Workbench

真实桌面测试使用正式 Theme、真实窗口、平台渲染器和固定脚本输入，至少能够动态切换数据规模、
高度分布、Viewport、方向、Follow 状态并触发远距离导航。必须显示当前 N、K、Offset、Extent、
LayoutEpoch、导航阶段、帧时间和 Container 计数，但这些诊断只属于 Gallery/Debug 工具。

Headless 不能证明平台合成、文字排版、ScrollBar Thumb 视觉修正或输入到呈现的帧时间；真实桌面
Workbench 是发布门禁，不是可选演示。

## 固定极限数据矩阵

### 规模

```text
N = 0 / 1 / 2 / 20 / 100 / 1,000 / 10,000 / 50,000
```

这里的 N 是 ConversationTurn 数量，不是独立消息数量。

### Section 主轴尺寸分布

```text
Fixed        每项 48 DIP
Alternating  24 DIP / 2,400 DIP 交替
Random       32～12,000 DIP，固定随机种子
Extreme      普通项中包含一个 100,000 DIP Section
Streaming    最后一个 Section 连续增长 10,000 个有效 LayoutEpoch
```

固定高度基准模板直接使用布局尺寸，避免业务文本分配污染容器算法结果。真实问答模板另外覆盖标题、
多段正文、代码块和操作区，不把业务内容字符串内存算入控件保留成本。

### Viewport、缩放和方向

```text
MainViewportLength = 320 / 720 / 1,440 DIP
RenderScaling      = 1.0 / 1.5 / 2.0
Orientation        = Vertical / Horizontal LTR
```

纯算法和固定尺寸 Headless 结构测试覆盖上述完整笛卡尔积。真实问答模板至少覆盖 10,000 和 50,000
项、四种尺寸分布、720 DIP Viewport、1.0/1.5 缩放和两个方向。Release Benchmark 使用固定模板与
真实模板各自报告，不能混合为一组数字。

## 正确性硬门禁

任意样本出现以下行为即失败：

- 永久空白、重复 Section、错误 SourceIndex、错误 Descriptor 或错误 AnchorKey。
- Prepare/Clear 后旧 Content、Theme、selected、自动化、事件、Binding 或异步回调泄漏。
- `ItemsSource.Count` 与成功提交后的 Descriptor Count 不相等。
- 有效 End 追加重建任意已有 Descriptor 或对已有元数据重新求值。
- 同一个 LayoutEpoch 发布多份部分 VirtualLayoutSnapshot，或 Snapshot 混用不同 Epoch。
- Automatic 普通滚动扫描全部 ItemsSource、使用平均尺寸猜测 SourceIndex 或逐项播放中间选择。
- MarkerNavigation 完成到错误项、接受旧 Generation 或超过既定次数预算。
- ContentEndFollow 冒充用户输入、抢占 MarkerNavigation 或直接写 selected。
- Faulted 在异常前未锁存、异常后继续提交任何逻辑或视觉状态。

正确性失败不能被耗时、分配或人眼结果抵消。

## 内容容器虚拟化硬门禁

固定 48 DIP 场景按真实 Viewport 与前后 `CacheLength=0.5` 计算正常范围：

```text
ActualRealizedCount
    <= ExpectedRealizedCount
       + BoundaryAllowance
       + ExplicitFrameworkRetainedCount

BoundaryAllowance = 2
```

无焦点、无进行中 ScrollIntoView 的稳定布局中，`ExplicitFrameworkRetainedCount=0`。远距离导航
期间至多允许 1 个能够按身份证明的 ScrollIntoView 特殊保留项。每个超范围容器都必须有记录；
不能增加笼统的“平台容差”。

在相同 48 DIP、Viewport、Cache 和稳定位置下比较 N=100、1,000、10,000、50,000：

```text
max(StablePeakRealizedCount) - min(StablePeakRealizedCount) <= 2
```

如果 Track 已溢出且 `ActualRealizedCount == LogicalItemCount`，立即认定内容虚拟化失败。

先滚过最密集的短 Section，使回收池经历最大正常容量，再执行 100,000 次主视口移动和 10,000 次
随机远距离导航。稳定期间要求：

```text
LiveContainerCount 始终满足实现范围公式
UniqueContainerInstances <= PeakExpectedRealizedCount + 4
```

额外 4 个实例是严格的生命周期裕量，不是长期实现范围；每个新增实例仍必须关联创建时的范围或
当前特殊请求。累计实例若随已访问历史持续增长，直接失败。

## 导航与 Automatic 门禁

对 50,000 项、全部尺寸分布和至少 5 个固定随机种子执行 10,000 次非相邻目标跳转：

- 目标成功率必须为 100%。
- `ScrollIntoView` 实现请求最多 2 次。
- 精确 Offset 写入最多 2 次。
- 最终 `abs(ActualOffset - EffectiveTargetOffset) <= 0.5 DIP`。
- 不实现目标路径上的全部中间 Item，不依次提交中间 selected。
- 旧 NavigationGeneration、TemplateGeneration 或 ContainerGeneration 回调提交次数为 0。
- 连续请求只完成最新目标；用户输入在下一次 Offset 写入前终止当前请求。

Automatic 每个有效 LayoutEpoch 只允许构造一份 O(K) 快照。测试 ItemsSource 使用带计数枚举器；
初始投影完成后，普通滚动和 Automatic 判定的全量枚举次数必须为 0。

## End 追加与流式增长门禁

从 10,000 项开始按顺序执行：

```text
单项 End Add       × 1,000
10 项批量 End Add  × 100
100 项批量 End Add × 10
尾部流式增长       × 10,000 个有效 LayoutEpoch
```

每个有效新增 Item 的三个元数据 Binding 各求值一次。已有 Descriptor 引用、AnchorKey 和快照结果
必须 100% 保持；追加期间不得扫描或重新投影旧 N 项。

`BrowsingHistory` 和 `ContentEndFollowMode=Disabled` 下：

- ContentEndFollow Offset 写入次数为 0。
- `ScrollIntoView` 调用次数为 0。
- 不注册第二套滚动锚点。
- End 追加后当前视口锚点漂移不超过 0.5 DIP。

`FollowingEnd` 下：

- 批量追加只请求最终 SourceIndex，单批最多一次 ScrollIntoView。
- 稳定布局后距离新 MaxScrollOffset 不超过 0.5 DIP。
- 每个真实 Extent 变化、每个 LayoutEpoch 最多一次 ContentEndFollow 写入。
- Extent 未变化时写入次数为 0，不产生 Layout/Offset 反馈循环。
- Start 方向用户输入到达后，下一次尾部写入前必须进入 BrowsingHistory。

## Faulted 终止门禁

Debug 和 Release、`net8.0` 和 `net10.0` 分别覆盖：

- 非 End Add、Remove、Move、Replace、Reset。
- 重复、空白、带首尾空白或错误类型的 AnchorKey。
- Binding 求值失败以及 Label/MarkerTheme 类型错误。
- Control Item 和非空 ItemsSource 缺失 ItemTemplate。
- 有效初始提交后的 ItemsSource 替换。
- Count、Index、Descriptor 与 Key 映射不一致故障注入。

每例必须证明 Faulted 在 `InvalidOperationException` 抛出前已经锁存。捕获异常后继续修改集合、
替换 ItemsSource、重套模板、修改配置、激活 Marker 并投递旧 Dispatcher/Layout 回调，原实例的
Descriptor、selected、Offset 和 Navigator 提交次数都必须保持 0 增量。只有创建新 Host 才能
获得 Running 状态。

## Release 性能门禁

### 相对 Avalonia 基线

在同一进程启动、同一硬件和相同工作负载下，Candidate 必须同时满足：

```text
SteadyScroll Median       <= Baseline × 1.20
SteadyScroll P95          <= Baseline × 1.25
FarNavigation P95         <= Baseline × 1.25
SteadyScroll Allocations  <= Baseline × 1.25
```

Baseline 与 Candidate 各自独立启动并交错测量，避免固定执行顺序造成热状态偏差。三个干净进程
必须全部满足门禁，不能使用三次平均掩盖某次稳定失败。

### 初始化线性与 Descriptor 内存

初始投影测量不包含业务数据和测试字符串构造。方案 A 允许 Descriptor O(N)，但不允许超线性：

```text
T(50,000) / T(10,000)                       <= 5.5
AllocatedBytes(50,000) / AllocatedBytes(10,000) <= 5.5
```

完整 GC 后，以相同业务源和裸 Baseline 为参照，ScrollMarker 控件侧稳定保留内存必须满足：

```text
CandidateRetained - BaselineRetained
    <= 256 bytes × N + 1 MiB
```

完整业务 Item、字符串、ItemTemplate 数据和平台共享 Theme 不计入 Descriptor 预算；测试报告必须
给出排除方法，不能在结果不利时临时更改统计边界。

### 60 Hz 发布参考机器

仓库必须指定一台固定发布参考机器，并在报告中记录 CPU、内存、GPU、操作系统、缩放、运行时、
Avalonia/AtomUI 版本和 Git commit。以下是该机器上的发布门禁，不是跨所有用户硬件的公共承诺：

```text
真实问答模板稳态滚动 FrameTime P95 <= 16.67 ms
真实问答模板稳态滚动 FrameTime P99 <= 33.34 ms
普通稳态滚动中 FrameTime > 100 ms 的次数 = 0
随机远距离导航从请求到最终稳定位置 P95 <= 100 ms
```

窗口遮挡、调试器、系统更新、热降频或其它环境干扰必须使本轮测量无效并重跑，不能被归入控件
失败或从结果中只删除个别慢样本。

## 长稳与重复性测试

至少使用 5 个固定随机种子，每个种子执行：

```text
100,000 次主视口移动
10,000 次随机远距离导航
3,000 个累计 End 追加 Item
10,000 个尾部增长 LayoutEpoch
重复 Viewport resize 与 FollowingEnd/BrowsingHistory 切换
```

结束后等待稳定布局并执行完整 GC。要求：

- 无错误身份、空白、重复容器、未终止事务或迟到回调提交。
- Live Container 回到当前实现范围门禁。
- Unique Container 不随操作次数继续增长。
- 排除新增 Descriptor 合法成本后，稳定保留内存相对预热基线增长不超过 2 MiB。
- 相同种子、相同环境的逻辑事件序列和最终状态一致。

## 方案 B 强制重审条件

确定性失败先最小化并归因。若错误属于 ScrollMarker 普通实现缺陷，状态为
`ImplementationFailed`，修复后必须重跑完整门禁；不能把普通 Bug 包装成架构问题。

出现以下任一情况，并能用最小方案 A 原型证明根因来自 `VirtualizingStackPanel` 的平均尺寸、
连续实现范围、回收或 ScrollIntoView 固有限制时，状态必须改为 `SchemeARejected`：

- 实现容器数量随 N 或已访问历史持续增长。
- 可变高度造成永久空白、重复 Container、错误 Index 或无法接受的持续视口跳动。
- 远距离导航不能在 2 次实现请求和 2 次精确写入内 100% 收敛。
- BrowsingHistory 的 End 追加无法保持 0.5 DIP 视口锚点门禁。
- 普通滚动或 Automatic 无法消除 O(N) 全集工作。
- 容器复用无法满足状态隔离或稳定内存平台期。
- 三个干净进程持续无法通过相对性能门禁，且瓶颈来自方案 A 固有布局路径。
- 为通过测试必须增加逐 Item 尺寸索引、第二对象池、离散保留集合或第二套 Extent/Offset 真相。

`SchemeARejected` 立即阻止发布并重新打开“自定义 `VirtualizingPanel`”方案 B 架构评审。方案 A
源码中不得先堆入半套方案 B 再补写文档。任何门禁阈值修改都必须单独提交证据和架构评审，不能
与使测试转绿的实现改动混在同一次评审中。

## 首版交付物与发布资格

首版必须同时交付：

1. 纯算法与状态机测试。
2. Avalonia Headless 真实布局测试。
3. Debug/Release、net8.0/net10.0 Faulted 契约测试。
4. 独立 Release Performance Runner 和裸 Avalonia Baseline。
5. 带环境、版本、Git commit、原始数据和统计摘要的首轮性能报告。
6. 可执行固定脚本并显示诊断事实的真实桌面 Gallery Workbench。
7. 方案 A `Passed`、`ImplementationFailed` 或 `SchemeARejected` 的书面结论。

只有全部确定性门禁、相对性能门禁、60 Hz 参考机器门禁和人工 Workbench 验收均通过，方案 A 才
具备首版发布基线。缺失 Runner、报告或真实桌面验证与测试失败具有相同效果：不得宣称具备发布
能力。
