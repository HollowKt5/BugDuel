# BugDuel · 真实模型甲虫对战

正式游戏从 `Assets/Scenes/Campaign/CampaignMainMenu.unity` 进入。当前经营功能与代码导航见项目根目录 [README.md](../../README.md)、[CodeMap.md](../../CodeMap.md) 和 [Campaign.md](../../Campaign.md)。下文描述独立战斗及早期 `BeetleNursery` 原型；“未接入市场/联赛”等限制只适用于该旧入口。

Unity 6000.3.16f1 / URP 17.3。打开 `Assets/Scenes/BeetleArena.unity` 后 Play。
Q/E 加油，空格暂停，T 在平地木桩、起伏岩台、半圆槽、起伏圆槽之间切换并重开。默认平地。
C 循环切换全景、琥珀上方跟随、墨甲上方跟随；V 或右侧按钮开关击败慢镜头（落台时近景、0.28 倍速，最多 3 秒；超时平局直接结算）。
每只虫显示好斗、体长、角长、敏捷四项 0–100 分及总分；总分来自均值 240、标准差 28 的正态抽样（极端值限幅），高低档各约占种群的 10%，不是每十场强制配额。
双方自动行动，玩家不能控制移动或招式。落台或台内倒地连续 5 秒未能翻正判负；到时双方平局，体力耗尽不直接判负。保留体型配对、交战节奏、抬举和挑飞。
双方拥有独立的决策随机流、交锋时钟和稳定招式偏好；试探、追击、退让按各自状态决定。普通攻击根据本步真正撞角的时刻结算，避免动画发力却漏掉碰撞。

养殖、物种和环境扩展见项目根目录 [Framework.md](../../Framework.md)：包含纯领域服务、名册事务、参赛与结果快照、存档及数据 JSON 更新；现有战斗模型仍仅支持已标定的角斗行为。

养殖场景为 `Assets/Scenes/BeetleNursery.unity`，也可从擂台的“养殖 [B]”入口进入。选中甲虫后可喂红薯、水果或果冻、选择环境、帮翻倒的甲虫扶正；首次名册中的琥珀需要救助。翻倒会持续挣扎，宽限后损失健康，长期不救会死亡；暂停及离线不推进照料时间。名册默认保存在 `Application.persistentDataPath/husbandry-roster.json`，同目录 `beetle-content.json` 可通过界面读取并校验更新。当前养殖名册尚未自动加入比赛，返回擂台仍使用展示对局。

## 模型与骨架

源文件是 `Assets/bug.fbx`，原文件及 `bug.fbm` 贴图保持不变。
该 FBX 有 22 个静态网格部件，没有骨骼、蒙皮或动画片段。
`BeetleImportedModelBuilder` 按这个模型的实际部件建立六足骨架，将 11 个腿部网格生成蒙皮副本。
硬壳、头角等 11 个网格按刚性关节绑定。没有用可见基本几何体替换甲虫。
每个源网格的顶点数量、三角形、UV 和贴图保留，生成副本只做统一坐标变换并添加权重/绑定矩阵。

源坐标 Y 向头部、-X 向背部、Z 为左右；生成时转换为 Unity Y 向上、Z 向前。
关节和权重为这份 FBX 显式标定，替换其他模型时需要重新校准。
每条腿包含 hip → knee → ankle → contact；大部分腿段保持刚性，仅在关节附近混合相邻骨骼权重。
程序 IK 只旋转骨骼，保持骨长。支撑点锁在世界空间；迈步采用平滑轨迹、抬脚预算和交错相位。
足底厚度由网格测量，后腿站姿收回到可达范围。触角靠近坡面时局部收起。
后足在步态、进攻与受击覆盖动作间统一互斥。低头不再附加固定侧偏，双骨蒙皮颈膜封住头胸连接处；头部与下颚底部采样限制坡面上的低头角度。

## 物理与更新顺序

每只甲虫有 29 个不可见碰撞代理：18 个腿部胶囊、3 个身体凸体和8个角部胶囊。
触角使用地表避让，足爪由足底偏移和 IK 处理；细刺没有逐个独立碰撞体。
碰撞体是运动学查询代理，`Physics.ComputePenetration` 提供重叠信息，自定义求解器负责根位移和闭合速度。
这不是每条腿的动态刚体、肌肉或逐足摩擦仿真。

固定模拟：恢复上一权威姿态 → AI/运动 → 身体/头角/六足姿态 → 碰撞约束 → 捕获完整姿态。
显示：根与全部骨骼按相同 alpha 插值 → 显示碰撞修正。
实际脚位同时写入 MatchSettings.supportFootOffsets，用于地形支撑采样。
运行时不逐帧 BakeMesh；验证工具才采样最终蒙皮网格。

## 文件职责

| 目录 | 主要内容 |
| --- | --- |
| Domain | 无 Unity 依赖的个体、物种、养殖/繁殖、战后恢复、内容目录与个体存档映射 |
| Runtime/Application | 名册占用与幂等结算、环境选择、完整内容 JSON 更新、养殖场景时间与保存 |
| Runtime/Integration | 养殖到战斗的参赛快照、冻结的比赛结果 |
| Runtime/Persistence | 显式路径 JSON 存储与名册/账本/环境选择的原子保存 |
| Runtime/Simulation | 战斗状态、固定步模拟与事件 |
| Runtime/Configuration | MatchSettings、物种作者资产与目录、独立战斗模型标定 |
| Runtime/Actors | Actor 绑定与 MatchController 调度 |
| Runtime/Animation | 程序步态、身体头角姿态、完整姿态插值 |
| Runtime/Rig | 真实模型骨骼 IK、触角避地 |
| Runtime/Physics | 碰撞代理缓存与接触求解 |
| Runtime/Environment | 地形支撑和贴地标记 |
| Runtime/Presentation/Display | 原色 dither、CRT、自定义材质与分场景显示配置 |
| Runtime/Presentation/Cameras | 战斗取景与表现姿态镜头快照 |
| Runtime/Presentation/UI | 独立对战/养殖 HUD、设置、滑块与导航 |
| Runtime/Presentation/Audio | 战斗声音、按钮合成声与滑块点击声 |
| Runtime/Presentation/Husbandry | 养殖姿态、游走、足部 IK、箱内空间与地表 |
| Runtime/Presentation/CombatFeedback | 战斗接触反馈与可关闭火星 |
| Runtime/Campaign/Presentation | 经营表现按 Audio、Cameras、Construction、Display、Husbandry、Specimens、Transitions 分类；完整索引见根目录 CodeMap.md |
| Editor/Model | FBX 部件、关节、蒙皮、材质与碰撞代理生成 |
| Editor/Setup | 场景、四擂台与材质安装、格斗增量升级 |
| Editor/Validation | 规则、碰撞、蒙皮和地形验证 |
| Editor/Automation | 编辑器测试入口 |
| Generated/ImportedModel | 由 FBX 派生的网格、材质和碰撞网格 |
| Generated/ArticulatedBeetle.prefab | 已绑定的真实模型甲虫 |
| Generated/MatchSettings.asset | 比赛参数及模型脚位 |

`Tools > Beetle Arena > Build Game Scene` 会重建生成场景和预制体；手工定制请另存。
`Tools > Beetle Arena > Build Husbandry Scene` 创建独立养殖场景、安装擂台入口并加入 Build Settings；已有养殖场景时拒绝覆盖。
模型绑定坐标在 `BeetleImportedModelBuilder`；骨骼求解在 `BeetleModelRig`；步态在 `BeetleProceduralAnimator`。

## 验证与备份

`Artifacts/Migration/` 保存本次真实模型的对局报告、最终网格穿地采样和截图。
`Artifacts/BeetleArena/` 保存专项碰撞、挑飞和运行检查报告。
`Artifacts/CombatRefinement/` 保存四地图、属性分布、后足互斥及击败慢镜头验证和预览；`Artifacts/DeadlockFix/CapturedReplay.txt` 保存旧卡角现场复现。
2026-10-22 夹具字段迁移：`Editor/Validation/Fixtures/FlatHornDeadlock.json` 仅移除两个 Animator 快照中的 `arenaRadius`。该字段原本只在初始化赋值、未参与动画或几何计算，已从 `BeetleProceduralAnimator` 删除；其余状态、腿部缓存、骨骼与根旋转数据保留。`RestoreFields` 继续对未知字段报错，本次数据迁移不代表 Unity 回放验收已通过。
报告中代理重叠深度与可见网格穿地深度是不同指标，不能混为一谈。
有限样本通过不保证任意姿态都完全无穿模；细刺和足爪使用近似约束。
单局硬上限为三分钟（正常播放、暂停除外），含开场和最多三秒击败展示；默认交战时限为174秒。平均约90秒为多种子调参目标，单局长短仍由实际攻防决定。超时判平局，不移动选手来指定赢家。
种子复现还依赖输入时序、配置、执行顺序和物理环境，不能承诺跨平台逐帧一致。

迁移前资源与设置：`Artifacts/Migration/BeforeMigration.zip`。
原未命名场景已保存在 `Assets/Scenes/BeforeMigration.unity`。
旧项目 `C:/UnityBuild/My project (3)` 的游戏代码与资产未修改。
