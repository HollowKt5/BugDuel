# BugDuel 动作模块维护指南

## 1. 启用、禁用、移除

打开 `Assets/BeetleArena/Generated/MatchSettings.asset`，在 Inspector 的 **Motion Modules** 列表管理动作。资产位于 `Assets/BeetleArena/Generated/Motions/`。

- 暂时禁用：在对应动作资产取消 **Enabled In Match**。
- 从本局配置移除：删除列表中对应引用。运行中移除会中断动作并归还腿控制权。
- 恢复：把动作资产拖回列表并启用。
- 关闭全部扩展：清空列表；原有战斗状态机继续工作，静置换足也随之关闭。
- 调低出现率：调整 **Module Action Chance**；单个 Combat 动作用 **Weight** 调整相对概率。**Duration / Cooldown** 控制时长与冷却。
- SupportOverlay 不经过加权抽取，其启停靠列表/Enabled；不要用 Weight=0 来关闭静置模块。
- 开局不会偷偷安装被删模块。菜单 `Tools > Beetle Arena > Install Motion Modules` 是显式重装操作，会重新发现所有具体类型并重建列表；自定义顺序/列表会被替换，已有资产参数保留。

## 2. 当前七个模块

| 文件（Runtime/Motion/Modules） | 通道 | 效果 |
| --- | --- | --- |
| `DrivingPushMotion.cs` | Combat | 后足蹬地、压低重心、角部前推 |
| `HornBindMotion.cs` | Combat | 偏头与侧向角力 |
| `ForelegGrappleMotion.cs` | Combat | 单前足探抵、后撤扒拉；真实前足接触才有响应 |
| `HornFeintMotion.cs` | Combat | 探角前压后收角退步；没有模块攻击冲量 |
| `SupportAdjustmentMotion.cs` | SupportOverlay | 静置、赛后、败者恢复后的非等周期换足与小幅重心调整 |
| `AntennalSamplingMotion.cs` | SupportOverlay | 左右错相、有停顿的触角寻触；不参与攻击判定 |
| `ImpactRecoveryMotion.cs` | SupportOverlay | 依据受击强度与时间的身体让步、回稳姿态 |

这七种动作可分别删除，不互相引用。原有冲顶、托举、六档撬飞仍由原状态机维护，未全量迁入这个接口。

### 力度调节

虚晃资产增加 `Head Dip / Foot Lift / Foot Reach`；侧顶增加 `Head Drive / Contact Impulse / Brace Travel`；前推增加 `Head Drive / Contact Impulse / Hind Leg Drive`。角度与脚端偏移会经过模型标定、IK 与碰撞约束，实际幅度可能小于输入。节奏曲线位于各自模块，说明见技术文档 17.7。修改资产后观察虚晃垫步、碰角瞬间、卸力回位，最终观感由用户验收。

## 3. 增加一个动作

1. 在 `Runtime/Motion/Modules` 新建继承 `BeetleMotionModule` 的类，并添加 `CreateAssetMenu`。
2. 实现 `DisplayName`、`Channel`、`CanStart(in context)`、`Sample(in context, phase)`；需要脚动作时实现 `SampleLeg`，触角实现可选的 `SampleAntenna`，需要特殊退出条件时覆盖 `CanContinue`。`PoseResponse` 表示姿态响应速度建议，由宿主统一限幅和平滑。
3. `phase` 为进入至退出的 0..1；`SampleLeg` 返回模型局部偏移。腿编号 0/1/2=左前/中/后，3/4/5=右前/中/后。
4. `SetDefaults()` 设置默认参数和证据说明。新资产没有 evidence 时由 OnEnable 初始化；不要将 Evidence 文本清空当作普通备注编辑，否则下次加载可能恢复默认值。
5. 通过 Create 菜单建资产，手动拖入配置列表即可。无须改核心类型判断或动作枚举。
6. 不能在资产里保存“上次命中/本次时间/某只虫引用”，不能在 Sample 内抽随机数、直接移动 Transform 或修改比赛结果。可变状态由每虫的 Runtime 管理；额外复杂状态需要先扩展统一实例契约。
7. 需要接触响应时指定 Horn 或 Foreleg 与窗口/冲量意图。新增其它部位必须扩展统一物理能力与验证，不能用距离近冒充实际接触。
8. 运行模块验证，再检查动作中断、空中抢占、起伏地图和真正模型的脚端/碰撞。只验证曲线数值不足以确认模型表现。

删除动作时先移除配置引用和对应资产，再删脚本；连同 .meta 一起由 Unity 处理。漏掉的 Missing 引用由宿主跳过，但应清理 Inspector 中的失效项。

## 4. 数据流与边界

```text
MatchSettings 列表 → 每虫 MotionRuntime（选择、计时、冷却、一次命中锁）
  → Module（只读快照 → 姿态/足端/移动/接触意图）
  → Simulation 消费移动；Animator 消费姿态与脚端 → 原有 IK
  → ContactSolver 接受最终姿态 → IBeetleMotionContacts 确认部件接触
  → Simulation 修改速度、平衡与事件
  → PoseBuffer / Render 仅显示
```

活动 Combat 对声明控制的腿优先；SupportOverlay 只填未被占用的腿。脚端退出以 0.18 秒从实际位置衔接。受击/托举/结算切状态立即取消模块。没有 Unity 接触接口的纯数据模拟不伪造模块攻击命中。

研究资料、物种边界、公式和时钟说明见 [TechnicalGuide.md 第 17 节](TechnicalGuide.md#17-可插拔动作接触响应与活体时钟)。虚晃是依据试探/退出构造的游戏表现，不声称已证明有意欺骗；前足扒拉参考 Golofa，是跨物种改编。

## 5. 注册表与采样成本

`BeetleMotionRuntime.Refresh()` 每个固定步从配置复制有序引用，去除 null 与重复项，复用 List 的容量。一次姿态/六足/触角采样使用同一注册表，不在每次采样内重新做重复项扫描。保持配置顺序，所以足端/触角覆盖的优先级不变。

- 修改列表、替换数组、插入/删除条目：下个固定步 Refresh 生效。
- `enabledInMatch` 仍在采样/资格检查时读取；活动动作在 Tick 发现禁用或移除时取消。
- `readyAt`、Elapsed、命中锁和 Context 仍逐虫保存；共享资产没有运行计时。
- 本轮优化没有缓存模块 Sample 的输出：求解器回退重试需要用当时姿态/上下文重新求值。
- 不在 Sample 中修改配置列表或资产；此类副作用会破坏一次固定步内的一致性。

正式游戏及快进使用 `MatchController.AdvanceFixedStep()`，再用 `Present()` 显示。只有专门验证渲染/反馈隔离的低层测试才拆开调用 Simulation、Actor、ContactFeedback。

## 6. 短恢复期间的换足（2026-10-13）

SupportAdjustmentMotion 使用“重心转移 → 一足低提/落回 → 回正”的错相窗口，稳定/贴身时缩小动作。休息的开始/退出、体力与威胁响应由 BeetleRecoveryPolicy + Simulation 管理，删除换足模块不会改变恢复规则。触角寻触保持独立。详见 TechnicalGuide 第 22 节；具体位移和时间是游戏参数，资料没有给出独角仙的精确对应测量值。

## 7. 局势约束与快慢招募曲线（2026-10-13）

BeetleMotionContext 增加 Intent / BurstReserve 只读快照。DrivingPush / HornBind 可在 Clash 或 Pressure 情境开始，实际抽选仍由 Simulation 的战术资格约束。防守、恢复、换位或抢收招时，不让普通随机模块覆盖已有目的。

InsectMuscleAnimation 提供 Recruit / Strike / Swing / Locomotion 等纯采样曲线，见 TechnicalGuide §23。模块可复用加载后快速招募曲线，但不能在资产中存储某只虫的爆发余量；资源结算仍由模拟调用 EnergyPolicy。强接触窗口与发力段对齐，接触接口仍负责是否真正命中。

中足规划使用 ModelRig.PlanSupport / SupportReach。模块输出的脚端偏移仍由宿主处理 IK 和碰撞，删除模块不会删除步态可达检查。胸部 BodyHeight 叠加到独立动作高度缓存，不能把坡面抬胸反复读回。中足与高度修复详见 TechnicalGuide §24。
