# BugDuel · 真实模型甲虫对战

Unity 6000.3.16f1 / URP 17.3。打开 `Assets/Scenes/BeetleArena.unity` 后 Play。
Q/E 加油，空格暂停，T 在平地木桩与起伏岩台之间切换并重开。默认平地。
双方自动行动，玩家不能控制移动或招式，落台判负；保留原来的体型配对、交战节奏、抬举和小概率挑飞。

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
| Runtime/Simulation | 战斗状态、固定步模拟与事件 |
| Runtime/Configuration | MatchSettings 配置定义 |
| Runtime/Actors | Actor 绑定与 MatchController 调度 |
| Runtime/Animation | 程序步态、身体头角姿态、完整姿态插值 |
| Runtime/Rig | 真实模型骨骼 IK、触角避地 |
| Runtime/Physics | 碰撞代理缓存与接触求解 |
| Runtime/Environment | 地形支撑和贴地标记 |
| Runtime/Presentation | 镜头、音效和原有 HUD |
| Editor/Model | FBX 部件、关节、蒙皮、材质与碰撞代理生成 |
| Editor/Setup | 场景、双擂台与材质安装 |
| Editor/Validation | 规则、碰撞、蒙皮和地形验证 |
| Editor/Automation | 编辑器测试入口 |
| Generated/ImportedModel | 由 FBX 派生的网格、材质和碰撞网格 |
| Generated/ArticulatedBeetle.prefab | 已绑定的真实模型甲虫 |
| Generated/MatchSettings.asset | 比赛参数及模型脚位 |

`Tools > Beetle Arena > Build Game Scene` 会重建生成场景和预制体；手工定制请另存。
模型绑定坐标在 `BeetleImportedModelBuilder`；骨骼求解在 `BeetleModelRig`；步态在 `BeetleProceduralAnimator`。

## 验证与备份

`Artifacts/Migration/` 保存本次真实模型的对局报告、最终网格穿地采样和截图。
`Artifacts/BeetleArena/` 保存专项碰撞、挑飞和运行检查报告。
报告中代理重叠深度与可见网格穿地深度是不同指标，不能混为一谈。
有限样本通过不保证任意姿态都完全无穿模；细刺和足爪使用近似约束。
目标对局时长约1–3分钟，胜负由真实落台触发，没有定时判负。
种子复现还依赖输入时序、配置、执行顺序和物理环境，不能承诺跨平台逐帧一致。

迁移前资源与设置：`Artifacts/Migration/BeforeMigration.zip`。
原未命名场景已保存在 `Assets/Scenes/BeforeMigration.unity`。
旧项目 `C:/UnityBuild/My project (3)` 的游戏代码与资产未修改。
