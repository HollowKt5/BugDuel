# BugDuel 养殖、物种与内容扩展框架

本文记录早期 `BeetleNursery` 独立养殖原型及可复用底层接口。正式经营流程已经另在 `Scenes/Campaign/` 接入市场、库存、联赛、拍卖、日历与建造；当前入口和功能文件索引见 [README.md](README.md)、[CodeMap.md](CodeMap.md)、[Campaign.md](Campaign.md)。下文的“尚未接入”不代表正式经营流程的现状。

> 根据当前代码核对：2026-10-15。本文说明已经提供的接口、职责与接入约束；示例参数属于游戏调校。
> 路径均相对于项目根目录。场景、物理与动作细节继续参见 [Intro.md](Intro.md)、[TechnicalGuide.md](TechnicalGuide.md) 和 [MotionModules.md](MotionModules.md)。

## 1. 当前提供到哪一层

框架已提供长期个体、养殖推进、食物目录、翻倒救助、繁殖、环境选择、参赛快照、战后结算、存档和内容 JSON 更新接口。独立养殖场景为 `Assets/Scenes/BeetleNursery.unity`；对战场景通过“养殖 [B]”入口或 B 键进入，养殖界面可以返回擂台。
养殖场景可以选中甲虫、喂红薯/香蕉/苹果/甲虫果冻、迁移环境、扶正、暂停、保存和读取内容更新。第一次建立名册时提供琥珀和墨甲，琥珀已翻倒，作为扶正教学；已有存档不会被这组示例覆盖。
当前养殖名册还没有自动接入具体比赛，返回擂台仍使用现有展示对局；参赛与结算适配 API 已提供，后续入口需要明确绑定会话。联网下载、资源包更新、经济与库存系统尚未接入。多种物种的数据可以共存，但现有战斗模型适配仅支持 `HornWrestling`；`MandibleGrappling` 会被明确拒绝。

## 2. 依赖方向与状态归属

```text
BeetleHusbandryHud / BeetleHusbandryController / 游戏入口
  → Application/BeetleRosterSession → Domain（纯 C#）
  → Application/BeetleContentService → ContentCatalog → RosterSession
  → Persistence/IBeetleRosterStore → JSON 文件

RosterSession → Integration/BeetleMatchEntrant → Simulation
Simulation → Integration/BeetleMatchResult → RosterSession.Complete
Actors → Simulation + Animation + Physics → Presentation
Configuration 作者资产 → 不可变领域定义 / 显式战斗标定
```

| 层 | 主要文件（均在 `Assets/BeetleArena/`） | 拥有的数据与职责 |
| --- | --- | --- |
| 领域 | `Domain/SpeciesDefinition.cs`、`BeetleIndividual.cs` | 物种规则、稳定身份、潜力、条件、年龄和亲缘。 |
| 领域服务 | `Domain/HusbandryService.cs`、`BreedingService.cs`、`ContestRecoveryService.cs` | 养殖、繁殖、明确的战后消耗；不读取场景、系统时间或文件。 |
| 内容定义 | `Domain/ContentCatalog.cs` | 一个完整内容版本，包含物种目录和环境模板。 |
| 食物定义 | `Domain/FoodCatalog.cs` | 稳定食物 ID、显示名称和不可变单份营养/水分定义。 |
| 应用会话 | `Runtime/Application/BeetleRosterSession.cs` | 个体档案、参赛占用、环境选择、结算账本及事务提交。 |
| 养殖场景入口 | `Runtime/Application/BeetleHusbandryController.cs` | 场景时间、当前选择、事故命令、读档与保存策略。 |
| 养殖表现 | `Runtime/Presentation/UI/BeetleHusbandryHud.cs`、`Runtime/Presentation/Husbandry/BeetleHusbandryView.cs` | 按钮和状态显示；只读快照播放进食、挣扎、扶正和死亡姿态。 |
| 内容适配 | `Runtime/Application/BeetleContentService.cs` | 内容 DTO、JSON 解析、显式本地文件读取、版本发布。 |
| 战斗边界 | `Runtime/Integration/BeetleMatchEntrant.cs`、`BeetleMatchResult.cs` | 局前输入快照、胜负确定时的结果快照。 |
| 持久化 | `Domain/BeetleSaveData.cs`、`Runtime/Persistence/BeetleRosterStore.cs` | 个体 DTO 映射、整个名册 JSON 的校验与原子替换。 |
| 作者配置 | `Runtime/Configuration/BeetleSpeciesAsset.cs`、`BeetleSpeciesCatalog.cs`、`BeetleCombatProfile.cs` | Inspector 编写、目录检查、模型与战斗参数标定。 |

`Domain/BugDuel.Domain.asmdef` 设置 `noEngineReferences=true`，领域代码不能引用 `UnityEngine`。`Runtime/BeetleArena.Runtime.asmdef` 显式依赖领域与输入/UI包，`Editor/BeetleArena.Editor.asmdef` 仅在编辑器编译；运行时不能反向依赖编辑器工具。Runtime 内部的应用、配置、适配与表现目录边界也需要代码审查维护。
禁止领域服务访问 Actor、Controller、HUD、ScriptableObject、存档路径或网络；禁止战斗模拟直接修改养殖档案；禁止表现层反写长期个体条件。
`BeetleSimulation` 使用 Unity 数学类型和接触接口，不应称为无 Unity 依赖的领域核心。既有固定步、动画和接触求解的职责保持独立。

## 3. 长期个体与养殖 API

`BugDuel.Domain.BeetleIndividual` 的 `Id` 是长期身份，`SpeciesId` 是物种外键；显示名称、选手槽位和资源路径都不能替代它们。
个体、物种、食物和环境的 `DisplayName` 在构造/恢复时去除首尾空白，保留内部空格并拒绝空白名称；ID、亲缘引用和比赛编号继续精确校验，不自动 Trim 后改变引用关系。
`BugDuel.Domain.BeetleTraits` 保存 0～1 的力量、防御、速度与耐力潜力；`BeetleArena.BeetleTraits` 则是单局游戏数值。调用处同时使用两个命名空间时应明确类型，不能把两种属性混用。
`BeetleCondition` 的健康、营养、水分、压力为 0～100；定义和值对象不可变，动态条件只经领域服务替换。`RosterSession` 读取返回副本，外部修改副本不会更新名册。

| API | 输入与结果 |
| --- | --- |
| `BeetleIndividual.Create(species, id, name, sex, stage, traits)` | 创建活体；默认成虫，年龄从对应阶段起点开始。 |
| `HusbandryService.Feed(individual, species, food)` | 食物按物种和阶段校验，返回 `DomainResult` 与事件。 |
| `HusbandryService.MarkOverturned(individual, species)` | 标记活体成虫需要救助；重复调用失败，不重新开始宽限期。 |
| `HusbandryService.HelpRight(individual, species)` | 扶正活体成虫并清除翻倒计时，不退还已经损失的健康。 |
| `HusbandryService.Advance(individual, species, habitat, elapsedDays)` | 明确传入游戏天数；不读取真实时钟，不自行推断离线时长。 |
| `BreedingService.Breed(species, mother, father, offspringIds, random)` | 显式双亲、子代 ID 和随机源，成功返回子代与事件。 |
| `BeetleSaveMapper.Export / Restore` | 个体与版本化 DTO 互转；还原时检查当前物种、年龄和亲缘数据。 |

生命周期为卵 → 幼虫 → 蛹 → 成虫 → 死亡。推进在阶段结束、营养/水分阈值、救助宽限、恢复与死亡边界分段，支持一次推进跨越多个阶段。
总龄和阶段龄记录已经经历的时间，不再要求等于当前配置的阶段起点之和。阶段时长热更后，读档和喂食、扶正保留已有阶段与年龄；若当前阶段已经到期，下次显式推进（含 0 天边界结转）才结束该阶段，时间不倒退，也不会在读档时直接死亡。
翻倒计时在构造/恢复时限制为不大于阶段龄，不再用 `+ 0.000001` 扩张合法上限；负数、NaN/Infinity，以及站立状态带翻倒计时等矛盾记录仍拒绝。
`BeetleCondition` 统一把有限的健康、营养、水分、压力截断到 0–100；喂食、时间推进、繁殖消耗和赛后恢复直接传计算结果，不再各自 Clamp。NaN/Infinity 仍是错误；内容参数、身份、阶段与亲缘等结构约束继续严格校验，`DomainGuard.Range` 不承担游戏状态饱和运算。
营养与水分会减少；环境压力、缺乏营养/水分会影响健康；满足条件时可以恢复。死亡为终止状态，不会继续增长年龄或自动复活。
幼虫和成虫有独立食谱；单份 `FoodPortion` 只能指定一种 `FoodKind`。卵、蛹不能通过成虫食物绕过阶段限制。
`FoodKind.SweetPotato=32` 扩展原有位标记，旧食物数值保持不变；默认成虫允许水果、果冻和红薯。`FoodCatalog.CreateDefaults()` 提供下表定义，`session.Foods.Definitions` 用于界面列出选项，`session.Feed(individualId, foodId)` 按 ID 饲喂。每份数值是游戏调校，不是现实养殖建议；目前没有食物库存或购买消耗。

| 食物 ID | 名称 | 营养 | 水分 |
| --- | --- | --- | --- |
| `food.sweet-potato` | 红薯 | +30 | +8 |
| `food.banana` | 香蕉 | +25 | +15 |
| `food.apple` | 苹果 | +15 | +25 |
| `food.beetle-jelly` | 甲虫果冻 | +20 | +20 |

翻倒状态保存在个体的 `IsOverturned` 和累计游戏天数 `OverturnedDays`；`IsStruggling` 仅在活体成虫仍翻倒时为真。领域层不操纵动画，不会自动扶正。翻倒期间不能喂养、繁殖或参赛，也不会因环境舒适而回血；换环境不能清除翻倒状态。
`CareProfile.OverturnedGraceDays` 默认 `.05`，`OverturnedHealthLossPerDay` 默认 `500`；超过宽限后持续扣健康，环境、缺营养/水分造成的伤害仍可叠加。健康归零后正常进入死亡，保留最后的翻倒姿态和计时，但停止挣扎和计时。死亡不能通过扶正或喂食复活；只有 `HelpRight` 成功才清除活体翻倒状态和计时。
应用接口为 `session.MarkOverturned(id)` 和 `session.HelpRight(id)`，两者都拒绝参赛占用个体。养殖场景控制器会根据清洁度与拥挤程度产生游戏事故，再发送 `MarkOverturned` 命令；这与战斗模拟的局内倒地状态相互独立。
繁殖要求同物种、不同的雌雄成虫、正常姿态、合格条件与冷却；拒绝记录中的祖孙和共同祖先组合。`IBreedingRule` 可添加规则，输入为副本；`IRandomSource.NextUnit()` 必须在 `[0,1)`。
规则评估只建立两份亲本快照，各条规则后恢复其可变状态以防互相污染，规则不得保留快照引用。主动返回拒绝理由属于业务失败；只有随机值本身违反 `[0,1)` 契约才返回 `InvalidRandomSource`，随机实现或规则中的程序异常原样传播，不改写成业务拒绝。
子代潜力从双亲继承并带配置中的变异，出生阶段为卵。父母消耗和冷却只在全部验证、随机生成成功后提交；ID 全局唯一由应用会话保证。
未来库存扣除、付费繁殖、离线时间来源应在应用事务层处理，不向领域服务添加 UI、钱包或磁盘访问。

## 4. 环境选择与最小养殖例子

`HabitatDefinition` 包含稳定 ID、名称、`HabitatConditions`、可用阶段和标签。标签目前是内容元数据，不自动实现饲养箱容量、库存或装修效果。
`HabitatDefinition.CreateDefaults()` 提供成虫饲养箱、幼虫发酵基质箱、隔离休养箱三个游戏模板；新增环境不需要修改物种类。
应用通过 `AvailableHabitats(id)` 列出当前阶段可选环境，`SelectHabitat(id, habitatId)` 保存选择，再由 `AdvanceInSelectedHabitat(id, days)` 推进。
`AdvanceInHabitat(id, habitatId, days)` 同时校验并记录本次环境；未选择环境或阶段不适合时返回失败。换环境不会立即改变健康、清除救助计时或自行消耗时间。
推进前计算到首次不兼容阶段的最大天数；超出时整体返回 `RelocationRequired`，不部分消耗时间。可一次跨越连续允许的阶段，精确推进到蜕变边界后保留原选择，下一次推进前迁居。
阶段热更缩短后，若专用箱中的当前阶段已到期，第一次合法推进仅结转这个零时间阶段边界、返回阶段事件，不消耗请求的照料时间；之后可选择下一阶段的箱子。这样不会卡在“旧箱不能推进、新箱不能入住”的状态。

下面代码可放在已导入 `System`、`System.Collections.Generic`、`BugDuel.Domain` 和 `BeetleArena` 的应用方法中；它只创建内存档案：

```csharp
var species = new SpeciesDefinition("example.horn", "示例甲虫", 55, 8);
var content = new ContentCatalog(1, new[] { species }, HabitatDefinition.CreateDefaults());
var session = new BeetleRosterSession(content, new Dictionary<string, BeetleCombatProfile>());
session.Add(BeetleIndividual.Create(species, "beetle.001", "一号", BeetleSex.Female));
var selected = session.SelectHabitat("beetle.001", "habitat.adult");
if (!selected.Success) throw new InvalidOperationException(selected.ErrorMessage);
var advanced = session.AdvanceInSelectedHabitat("beetle.001", .25);
if (!advanced.Success) throw new InvalidOperationException(advanced.ErrorMessage);
var fed = session.Feed("beetle.001", "food.sweet-potato");
if (!fed.Success) throw new InvalidOperationException(fed.ErrorMessage);
```

空战斗配置目录允许只做养殖；它不会自动把新物种加入现有比赛。成功调用后可通过 `TryGet` 或 `Snapshot` 读取状态。

养殖场景的 `BeetleHusbandryController.secondsPerGameDay` 默认 600，即运行中的 600 秒对应 1 游戏天。默认救助配置因此是 30 秒宽限，之后满健康在仅有翻倒伤害时约 120 秒耗尽；实际剩余时间还受原有健康和其他伤害影响。这些是可调游戏参数。
场景约每 `.2` 秒推进一次全部活体；暂停按钮暂停照料时间和模型表现，关闭或离开养殖场景不会补算离线时间。普通营养、水分消耗和缺乏导致的死亡仍按物种规则计算，并非只有翻倒才会死亡。表现层只观察快照，死亡后冻结姿态，不再播放挣扎。

## 5. 物种作者数据与战斗标定分开

`BeetleSpeciesAsset.ToDefinition()` 返回领域副本，负责生命周期、养殖范围、食谱、繁殖及展示体长/质量。`BeetleSpeciesCatalog.BuildDefinitions()` 拒绝缺失和重复物种 ID。
`BeetleCombatProfile` 另行记录模型 `rigId`、视觉缩放、归一化质量/力量/抓地/耐力、角长、六足支撑坐标与动作模块列表。毫米和克只用于展示，不能直接代入归一化受力公式。
`ValidateForMatch()` 要求恰好六个有限足位及合法数值范围；校验通过表示结构有效，不代替模型接触、IK 与战斗行为的实际验收。
`actorPrefab` 是作者配置引用和 rig 一致性检查入口，当前 Controller 不会根据它自动生成或替换场景 Actor。应用必须准备好已标定的两只 Actor。
`overrideMotionModules=false` 继承比赛模块；设为 `true` 时只使用物种列表，空列表表示无可插拔模块，基础角斗动作仍属于模拟规则。
`BeetleMatchEntrant.FromIndividual` 校验成虫、正常姿态、健康、压力、营养、水分和物种，然后复制个体与形态数据；每场重新创建战斗属性，不在开局再次随机抽取长期潜力。
参赛快照不持有活体档案，但动作模块仍是无实例状态的共享资产；`BeetleMatchSettings` 也保留现有调参引用。不要将该边界误认为所有战斗配置都已深冻结。

### 新增一种甲虫

1. 建立稳定物种 ID、领域规则或 `BeetleSpeciesAsset`，加入物种/内容目录，先验证养殖、繁殖和存档往返。
2. 若暂时没有战斗模型，可只提供养殖定义；不能用缺省战斗配置冒充已适配。
3. 准备模型与骨架，重新标定静止姿态、腿长、足位、头部活动范围、碰撞代理与支点。
4. 建立 `BeetleCombatProfile`，按模型填写缩放、归一化战斗值、六足位置与动作集合；Actor 的 `rigId` 必须匹配。
5. 为新碰撞体配置 `BeetleContactRig.ShapeBinding` 的 `shape/role/legIndex/weapon`，足位依次为左前中后、右前中后。
6. 通过物种接入、接触元数据和真实模型专项验证，再将该配置注册到会话的战斗目录。

现有 `HornWrestling` 动画会使用头部/角长缩放和角下托举。**仅改 `rigId`、物种名或枚举，不能支持锹甲大颚夹持。** 新攻击器官需要独立的接触语义、命中资格、动作、支点与验证。
旧模型按既有名称回退分类；未知部件安全归为 Body，不会默认为右后足。显式绑定优先，数组或绑定修改会刷新缓存；旧名称重命名需调用 `InvalidateShapeMetadata()`。

## 6. 名册、参赛占用和战后结算

`BeetleRosterSession` 只在游戏主线程调用；并发控制不是多线程锁，也不是联网服务器权威系统。添加、读取、繁殖与导入导出使用副本隔离外部引用。
`BeginMatch(firstId, secondId, settings, seed)` 重新检查双方资格，生成新快照和唯一 MatchId，并占用两只个体。占用期间拒绝该个体的喂食、养殖推进、翻倒/扶正、迁居、繁殖或再次参赛。
它返回的 `BeetleSimulation` 应直接交给 `BeetleMatchController.StartMatch(simulation)`，不能另建一份具有相同 ID 的模拟冒充会话登记的比赛。
完成时 `Finish` 冻结 `BeetleMatchResult` 并发出 `Completed`；Controller 转发 `MatchCompleted`。结束后的落地与镜头展示不会修改已冻结结果。
`session.Complete(result)` 只接受登记模拟自己的结果对象，校验双方身份后在副本上应用战后策略，再共同提交双方、写入 MatchId 账本并释放占用。重复结算返回 `false`。
当前战后消耗由剩余耐力计算努力程度，再交给 `RecoveryPolicy`；落台本身不会自动变成受伤。默认策略扣营养/水分、增加压力，健康消耗默认为零。
`session.Cancel(matchId)` 只释放占用，不伪造胜负或结算收益；Controller 替换未完成比赛和销毁时发出 `MatchCancelled`。离开场景的应用代码必须处理这条路径。
持久个体的重赛/下一局走 `PersistentMatchRequested`，由应用重新选择并调用 `BeginMatch`。不会复用旧健康快照；未安装处理器时相关请求不可用。
持久个体换场请求被拒绝或没有绑定新比赛时恢复原场地。应用在 `Awake` 中提前绑定的比赛会保留到 Unity `Start`，不会被自动展示局覆盖。

下面片段假定 `session` 已登记双方及有效战斗配置，`controller` 已配置对应 Actor；事件处理器须在所属应用生命周期结束时解除：

```csharp
Action<BeetleMatchResult> completed = result => session.Complete(result);
Action<string> cancelled = id => session.Cancel(id);
controller.MatchCompleted += completed;
controller.MatchCancelled += cancelled;
var contest = session.BeginMatch("beetle.001", "beetle.002", controller.settings, 71);
try { controller.StartMatch(contest); }
catch { session.Cancel(contest.MatchId); throw; }
```

启动失败须释放本次占用。应用退出时应先完成或取消登记比赛，再解除订阅；订阅成功不意味着失败处理、自动保存和重赛选择已经自动完成。

## 7. 存档事务与版本

`session.Export()` 同时导出 `Individuals`、`AppliedMatchIds`、`HabitatSelections`。有活动比赛时禁止导出/还原，避免保存只占用了一半或尚未结算的比赛。
`session.Restore(data)` 先验证全部个体、账本与环境引用，再一次替换当前名册；错误输入不部分覆盖现有状态。未知物种、未知环境仍需显式迁移。冷却天数用当前上限截断（例如旧档 8 天、新上限 7 天，载入为 7 天），阶段时长调整不再让整个旧档失效；历史年龄与救助计时保持不变。
`BeetleRosterSaveData.Version` 是容器格式版本，`BeetleSaveData.Version` 是个体格式版本；二者当前均为 1。内容版本属于另一个概念，不能拿存档格式号代替内容修订号。
个体 DTO 增加 `IsOverturned` 与 `OverturnedDays`，旧 Version 1 档缺少这两个字段时按 `false/0` 还原。检查会拒绝非有限/负计时、正常姿态却有非零翻倒计时、幼体翻倒、超出历史阶段年龄的计时，以及死亡阶段与健康值不一致的组合。克隆、保存、读取和内容更新均保留有效的救助状态，不能借此重新获得宽限。
`BeetleRosterJsonStore` 实现 `IBeetleRosterStore`，构造时必须传入调用方明确提供的绝对文件路径；没有默认 `persistentDataPath`、全局单例或自动覆盖资源的行为。
保存先做结构校验，再写同目录唯一临时文件并刷新，最后 `File.Replace` 或 `File.Move`；替换失败保留旧档，不通过删除旧档实现降级覆盖。
加载只返回 DTO，不自动应用到游戏；缺文件、损坏结构和未知格式版本均明确报告。缺失数组、重复个体 ID、重复账本 ID、重复环境绑定或悬空个体引用会被拒绝。
养殖场景控制器选择 `Application.persistentDataPath/husbandry-roster.json` 作为默认名册路径；同目录的 `beetle-content.json` 用于内容更新。控制器每约 10 秒、成功照料操作后、应用暂停和场景销毁时尝试保存，也提供手动保存按钮。非法存档会保留原文件，并进入不可保存的临时示例会话；界面显示该状态，不能由自动保存覆盖原档。

```csharp
var store = new BeetleRosterJsonStore(userSelectedAbsolutePath);
store.Save(session.Export());        // 活动比赛必须先完成或取消
session.Restore(store.Load());       // 全量验证后提交，不自动启动场景
```

账本与个体必须作为同一个容器保存；只保存个体而丢弃 `AppliedMatchIds` 会破坏跨会话幂等。备份轮换、云同步、加密和多进程写入仲裁仍由未来应用层提供。

## 8. 内容 JSON 与数据热更新

`BeetleContentService` 接收一个会话，`ExportJson()` 导出完整物种与环境包；`TryApplyJson(payload)` 应用字符串，`TryReloadFile(absolutePath)` 显式读取本地文件。两者返回 `DomainResult`，不会自动下载或监视文件。
同文件内的 `BeetleContentData` 包含 `SchemaVersion`、递增 `Version`、完整 `Species[]` 与 `Habitats[]`。它是全量包，不是缺字段按旧值合并的补丁。
`BeetleContentExamples.Create()` 提供两种示例甲虫及三个环境的初始内容；[Content/Examples/BeetleContent.v2.json](Content/Examples/BeetleContent.v2.json) 是可直接应用到该初始内容的第2版包。它位于项目内容目录，部署时由应用提供实际路径。请从 `ExportJson()` 模板修改；允许为零的标量字段被省略时会采用 JSON 的零值默认，不继承旧版值。
`SchemaVersion` 当前导出为 2，新增物种字段 `OverturnedGraceDays` 与 `OverturnedHealthLossPerDay`。读取 Schema 1 时执行明确迁移：救助参数采用 `.05/500`，原默认成虫食谱位值 `17` 升级为 `49` 以加入红薯，其他自定义食谱保持原值。不能因为旧包缺字段就把救助伤害静默设成零；Schema 2 中显式设零则属于作者选择。
内容 `Version` 必须严格大于当前版本，与 SchemaVersion 分开递增。整个包先构造不可变 `ContentCatalog`，再由 `session.TryUpdateContent` 验证已有档案后发布。
有活动比赛、移除正在使用的物种、删除仍被选择的环境，或让所选环境不再适合现有活体阶段，都会被拒绝；需先迁居，失败保留原目录和个体。阶段时长和冷却上限调整按上述兼容策略处理。死亡记录可保留原位置。
更新养殖范围、食谱、阶段时长、救助参数或繁殖条件仍要通过已有档案兼容检查；这不是允许任意重写旧存档的迁移器。正在救助的累计天数不清零，新的宽限/伤害参数从后续推进起生效。
养殖界面的内容读取按钮调用 `ReloadContent()`，读取同存档目录的 `beetle-content.json`；它不会重新加载名册，也不会访问网络。启动场景时会先尝试读取该内容文件，再还原名册；失败更新不替换当前有效内容。

```csharp
var contentService = new BeetleContentService(session);
var package = BeetleContentData.FromCatalog(contentService.Current);
package.Version += 1;
package.Habitats[0].DisplayName = "更新后的环境名称";
var update = contentService.TryApplyJson(UnityEngine.JsonUtility.ToJson(package));
if (!update.Success) throw new InvalidOperationException(update.ErrorMessage);
```

### 新增一种环境

1. 使用独立稳定 ID 定义 `HabitatDefinition`，设置温湿度、清洁度、拥挤度、休养状态和允许阶段。
2. 把定义加入下一版完整 `ContentCatalog` 或 `BeetleContentData.Habitats`；保留仍被选中的环境 ID。
3. 增加内容 `Version`，在没有活动比赛时调用内容服务；检查失败原因，不偷偷忽略不兼容档案。
4. 通过 `AvailableHabitats` 展示候选，再调用 `SelectHabitat`；存档保存该个体与环境 ID 的绑定。

当前热更新仅替换已支持字段的数据。不能通过 JSON 下载执行代码、添加新 `BeetleMotionModule` 类型、载入新模型、换骨架、替换 Prefab 或更新 Shader；这些需要后续资源分发与适配管线。
养殖可用的新物种不自动获得可战斗的模型；现有战斗配置、场景物理与动作资产仍必须独立准备和验证。

## 9. 镜头表现数据流

显示顺序为 `Actor.Render` → `Contacts.ProjectView` → `BeetleCameraFrame.Capture(simulation, actors)` → `ArenaCamera.Tick`。镜头优先读取碰撞修正后的 `bodyPivot.position`，不直接追踪领先显示一帧的模拟根节点。
相机只接收 `BeetleCameraFrame` 的主体中心、半径与腾空/落台状态；模拟、养殖和结果判定不依赖相机。
俯视跟随使用固定 `Quaternion.Euler(90, 0, 0)`，避免竖直 `LookAt` 的世界上方向退化；位置由同一平滑旋转与距离推导，不再独立二次追赶。
构图偏移按视口比例平滑，防止模式切换瞬间跳位或缩放时过冲；默认高度 16、正交半高 4.2、主体纵向约在视口 0.56。窄窗口按实际宽高比放大取景，抛飞全景即时容纳双方；跟随主体快速移动时也会放宽取景，避免平滑焦点追赶期间主体出画。
`ResetView()` 同时恢复全景模式与姿态；零时间、负时间及非有限时间不推进镜头。摄像机高度和缩放是可调参数，真实模型预览仍是验收依据。

## 10. 验证入口与未完成边界

所有入口位于 `Assets/BeetleArena/Editor/Validation/`，可从 `Tools > Beetle Arena` 对应菜单调用。验证报告必须看本次实际结果，不能只因存在验证文件就认定通过。

| 入口 | 覆盖内容 |
| --- | --- |
| `BeetleDomainValidation.Run()` | 生命周期、饲喂、繁殖、无效输入、存档与领域隔离。 |
| `DefensiveProgrammingValidation.Run()` | 本次重构的 10 组定向检查：异常归类与日志、繁殖隔离、旧档清洗、阶段热更、环境迁移、事务与接触数据；报告见 `Artifacts/DefensiveReview/Verification.txt`。 |
| `BeetleHusbandryInteractionValidation.Run()` | 食物目录、救助宽限与死亡、时间分段一致、扶正边界、占用、存档及内容迁移。 |
| `BeetleSpeciesIntegrationValidation.Run()` | 物种作者配置、参赛快照、独立属性、模型匹配与结果冻结。 |
| `BeetleRosterSessionValidation.Run()` | 名册副本、占用、结算幂等、取消、导入导出和应用事务。 |
| `BeetleContentValidation.Run()` | 环境选择、跨阶段迁居边界、内容 JSON 与兼容性发布。 |
| `BeetleContactMetadataValidation.Run()` | 旧模型分类、未知器官、显式覆盖、缓存失效与非法绑定。 |
| `BeetlePersistenceValidation.Run()` | 文件往返、损坏/重复数据拒绝、替换失败保留和环境选择存储。 |
| `BeetleCameraValidation.Run()` / `CapturePreview()` | 跟随、过渡、暂停、窄屏和真实场景预览。 |
| `BeetleFrameworkValidation.Run()` | 汇总扩展框架与既有规则/架构验证；报告见 `Artifacts/Framework/Validation.txt`。 |

新模型仍需运行已有模型、IK、接触、撬投、连续性和多种子对局验证。养殖场景、目录、接口与数据热更新已提供；养殖名册到具体比赛的入口、繁殖界面、联网资源更新、全部物种行为和商业化系统仍需逐项实现与验收。
