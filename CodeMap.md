# 功能代码导航

路径相对于项目根目录；游戏代码位于 `Assets/BeetleArena/`。先按下表找到功能，再区分规则、事务、界面和表现，不需要在整个工程里猜文件名。

## 分层与数据归属

```text
Runtime/Campaign/UI（输入、排版、提示）
  → CampaignApp（命令、克隆候选状态、保存成功后发布）
    → Domain/Campaign（规则、价格、身份、库存与日历）
    → CampaignSaveRepository（JSON与原子文件提交）
  → Runtime/Campaign/Presentation（镜头、模型、预览、音画反馈）
```

`CampaignState` 拥有长期数据；显示组件不直接扣金币、发物品或改所有权。交易失败保留原状态，成功保存后才发布余额变化与反馈。构建预览是临时状态，只有确认放置才走正式命令。领域程序集不引用 Unity，运行时代码不依赖 Editor。

`CampaignSession` 和 `CampaignView` 使用按功能拆分的 partial 文件：它们分别共享一份领域状态和一个页面上下文，不是每个文件各建一个会话或 Canvas。

## 按功能查找

新成虫模型/翅膀接入：`Runtime/Rig/BeetleModelAdapter.cs`（六足语义绑定与静止姿态）、`BeetleAppendageRig.cs`（可变关节与动作通道）；`Editor/Model/BeetleModelAdapterEditor.cs`、`BeetleAppendagePreview.cs`（配置与可还原预览）；`Editor/Validation/BeetleModelAdapterValidation.cs`。操作流程见 [成虫模型与动画适配](BeetleModelAdaptation.md)。

窗外树模型替换：模型先保存为 Prefab，在根节点添加 `HusbandryTreeModelAdapter`，将**只有叶片的 Renderer**放入 `foliage`，可选积雪/春芽放入 `snowCaps`/`buds`；树干保留为普通部件，三个数组都不要求固定数量或命名，支持 MeshRenderer 和 SkinnedMeshRenderer。通过菜单“Tools → 甲壳虫斗 → 画面与资源 → 养殖表现 → 配置窗外树模型”打开 `Generated/Husbandry/Room/Tree/TreeModelProfile.asset`，指定 Prefab 和相对窗外根节点的位置/旋转/缩放，再点“重建窗外树（应用配置）”。配置留空使用现有程序巨树。模型保留原材质和骨骼；带 `_LeafCoverage` 的叶片材质可逐叶落叶，普通 `_BaseColor`/`_Color` 材质按 Renderer 分组变色和脱叶，因此应将树干与树叶拆成独立 Renderer。`HusbandryTreeModelValidation` 检查绑定、多个材质槽和状态恢复。

下表中的领域文件均位于 `Domain/Campaign/`，界面文件位于 `Runtime/Campaign/UI/`，经营表现文件按职责放在 `Runtime/Campaign/Presentation/` 的子目录；具体分类见下一节。

| 功能 | 规则 / 数据入口 | 界面 / 表现入口 |
| --- | --- | --- |
| Inspector 共享布局参数 | `Resources/CampaignUiLayout.asset`（仅表现参数，不进入经营存档） | `CampaignUiLayoutProfile.cs`（分组与尺寸约束）、`CampaignLayoutConfiguration.cs`（加载、刷新、弹窗定位）；`Editor/Presentation/CampaignUiLayoutTools.cs`；`CampaignUiLayoutValidation`（编辑器内布局逻辑检查） |
| 玩家名字、旧档补名、署名 | `CampaignPlayerIdentity.cs`、`CampaignNaming.cs` | `CampaignPlayerNamingView.cs`（共用主题的独立命名页，`Window` 背景）；`CampaignApp.NewGame` / `RevealAfterPlayerNaming`（保存成功后BlackFade） |
| 甲虫名字 | `CampaignNaming.cs` 的 `RenameBeetle` | `CampaignNamingView.OpenBeetleNaming` |
| 时间、阶段、寿命、用品 | `CampaignLifecycle.cs` | `CampaignCalendarView.cs`、`CampaignCalendarReadout.cs`（虫箱/市场单行）、`CampaignTimeSettingsView.cs`（仅0.5–64倍速）、`CampaignLifeStageDisplay.cs`；`CampaignApp.LoadSlot` 清除旧手动暂停并保留日长与周期 |
| 甲虫信息、翻倒与阶段照料 | `CampaignSession.cs`、`CampaignLifecycle.cs` | `CampaignGreenhouseView.cs`（信息/管理/建造浮窗）、`CampaignCareController.cs`、`CampaignSpecimen.cs`、`CampaignRescuePrompt.cs` |
| 食盆投喂、残渣与丢弃 | `CampaignSession.Feed` / `CompleteMeal` / `ClearFoodDish`；`CampaignBeetle.foodResidue` | `CampaignFeedingDishView.cs`（非模态操作窗、丢弃确认）；`CampaignSpecimen`（食物/残渣显示）；`CampaignFoodDishValidation` |
| 长按背部提起、放下与取消 | 不增加经营存档字段；`CampaignApp.HandlingBeetle` 临时暂停日历 | `Presentation/Husbandry/CampaignBeetleGrabController.cs`（锁定初始背部命中，0.7秒白色空心进度圈，绑定返回键取消握持；落地不因UI命中撤回）；`CampaignCareController.HeldId`；`CampaignSpecimen` / `CarePosePresenter.PresentHeld` / `CareLocomotion.PlaceAt`；`CampaignBeetleGrabValidation` |
| 一次性必要操作提示 | `CampaignPreferences.learnedControls`，保存于 `settings.json` | `CampaignControlHints.cs`；甲虫/食盆/抓取操作、`CampaignObservationCamera.UserInputRevision` 与 `CampaignConstructionController.PlacementRevision` 记录已学；提示无emoji |
| 建造库存、占格、移动、回售 | `CampaignConstruction.cs` | `CampaignConstructionView.cs`、`CampaignConstructionController.cs`；`CampaignConstructionOccupancy.cs` 将虫体覆盖格绘为红X，并在预览/提交时拒绝重叠 |
| 建造剩余空间与安全返回 | `CampaignConstructionSpace.cs` | `CampaignConstructionHabitat.cs`、`CareEnclosureLayout.cs` |
| 景观造型、建造动画 | `CampaignConstruction.cs` 内的 `CampaignDecorations` | `CampaignDecorationVisuals.cs`、`CampaignDecorationDragSource.cs` |
| 捕虫队 | `CampaignSession.Dispatch` 与跨周结算 | `CampaignCatchingView.cs` |
| 邮件、仓库、附件领取与提示音 | `CampaignMailbox.cs`；`CampaignApp.SessionCommitted` | `CampaignMailboxView.cs`、`CampaignWarehouseView.cs`（二级分类与 DOTween）；`Presentation/Audio/CampaignMailAudio.cs` |
| 中心母虫配种服务 | `CampaignBreeding.cs`（稳定报价、随机失败、扣费与母虫所有权）；`CampaignSession.CreateOffspring` | `CampaignBreedingView.cs`；独立 `CampaignBreeding.unity`；`CampaignInterfaceValidation`、`CampaignInterfacePlayValidation` |
| 商店、挂牌、报价、直接回售 | `CampaignMarket.cs`、`CampaignTrading.cs` | `CampaignMarketView.cs`、`CampaignMarketTradeView.cs`、`CampaignMarketDialogs.cs` |
| 公开竞拍、托管、落槌 | `CampaignTrading.cs` | `CampaignMarketAuctionView.cs` |
| 市场共用选项卡、卡片外框及冠军拍卖 | — | `CampaignMarketCards.cs`（`MarketChoice`、`MarketCardSurface`、`MarketCardGrid`）；`CampaignMarketChampionView.cs`；`CampaignUiLayoutValidation.RunMarketChecks()` |
| 冠军拍卖、联赛、血统分红 | `CampaignLeague.cs`、`CampaignGenetics.cs` | `CampaignLeagueView.cs`、`Integration/CampaignLeagueBridge.cs` |
| 遗体、装框、标本交易 | `CampaignTrading.cs` | `CampaignSpecimenRoomView.cs`、`CampaignSpecimenRoom.cs` |
| 金币数字、浮字、音效 | `CampaignApp` 的提交事件 | `CampaignCurrencyHud.cs`、`CampaignCurrencyAudio.cs` |
| 页面调度、刷新、输入 | `CampaignApp.cs`、`CampaignInputPolicy.cs` | `CampaignView.cs`、`CampaignUiTree.cs` |
| 共用排版、按钮、滚动卡片 | — | `CampaignUiLayout.cs`（普通页居中、浅背景与按钮区分）；`CampaignView`（市场铺满实际画布） |
| 市场、邮箱、仓库的经典窗口控件 | — | `CampaignClassicControls.cs`（`MText` / `MStatus` / `MarketDialog`） |
| 共用确认框、背景模糊、输入与焦点 | — | `CampaignDialogView.cs`；`Presentation/Display/CampaignDialogMotion.cs`（DOTween弹出/缩回）；`CampaignModalBackdrop.cs`（一次抓取世界+HUD，Gaussian模糊，前景清晰） |
| 下拉菜单、联赛与独立格斗面板动画 | — | `Runtime/Presentation/UI/BeetlePanelMotion.cs`；`CampaignMarketDialogs.cs`、`CampaignLeagueView.cs`、`BeetleFeedbackSettings.cs`、`BeetleArenaHud.cs` |
| 转场、上下帘软边、全黑停留 | `CampaignTransitionProfile`、`Resources/CampaignTransition.asset` | `Presentation/Transitions/CampaignSceneTransition.cs` 的 `Go` / `Play`；`Resources/CampaignTransitionShutter.shader`；`Editor/Setup/CampaignTransitionSetup.cs` |
| 温室观察与重置视角 | — | `CampaignGreenhouseView.cs` 的“重置视角”；`Presentation/Cameras/CampaignObservationCamera.cs`：室内侧140°–220°旋转，缩远上限为完整取景距离的1.1倍，随缩放收窄侧墙处转角，近裁剪面不穿窗框/侧墙。`HusbandryCameraBoundaryValidation` 覆盖观察、建造、窗口比例变化与极端输入，并输出最远距离的正面/左右截图。 |
| 窗边长桌、大树与昼夜 | 只读取 `CampaignClock.elapsedDays`，不改日历或存档 | `Editor/Setup/HusbandryRoomSetup.cs` 布置两个养殖场景：28×13长桌贴窗、窗帘/书架/饲育工具；`ApplyDesktopWallFit` 将虫箱、桌子和窗户整组左移至固定左墙，保持局部导航坐标，重复应用不累积偏移；`HusbandryWindowTreeSetup.cs` 用曲面枝干和带纹理的独立叶片生成窗外半棵大树；`Presentation/Husbandry/HusbandryDaylight.cs` 同步天空、室内灯光与玻璃反射；`Shaders/HusbandryWindowSky.shader` 使用视线角度采样远山、横向镜像续接山景，云图边缘过渡程序云，保留日月星云，`_GardenEnabled=0` 关闭旧庭院合成；`HusbandryAtmosphere.cs` 管理叶影与微尘。默认镜头正对窗户，环境视角关闭景深；养殖世界帧上限1920。`HusbandryPolishValidation` 检查动作、场景与隔离Play昼夜画面。 |
| 窗外四季与天气 | 360天一年、90天一季，与经营日历同步；晨雾由完整日期确定，不新增存档字段 | `Presentation/Husbandry/HusbandrySeasons.cs` 控制叶色、叶量、春芽和枝头积雪；`FogAtDay` 约六天选一个雾天，晨间浓、11:30散尽，合并天空属性块的 `_OutdoorFog`。风与天气漂动使用表现时间。`Shaders/HusbandrySeasonalFoliage.shader` 以逐叶种子裁剪落叶并同步阴影/深度；`HusbandryWeather.shader` 绘制几何落叶、花瓣和雪花，已生成粒子同样响应夜色。`Generated/Husbandry/Room/Tree` 保存共享网格/材质，所有天气局限窗外。`HusbandrySeasonalTreeValidation` 在隔离存档中采样四季、冬夜和晨雾。 |
| 动漫远山、手绘云层 | 只随昼夜和雾量改变表现 | `Art/Husbandry/Textures/AnimeMountainVista.png`（sRGB，山峰外黑底）、`AnimeCloudMask.png`（Linear密度）由 `HusbandryWindowTreeSetup` 以Clamp导入；`HusbandryWindowSky.shader` 分开合成山体、天空、云、日月星星和低处流雾，`_SkyUVTransform` 保持窗口构图与大背景覆盖独立。 |
| 养殖微卡通美术 | 仅场景材质参数，不进入经营存档 | `Shaders/HusbandrySoftToon.shader`（柔和光照分层、真实昼夜能量、双面窗帘、低强度高光）；`HusbandryRoomSetup` 统一配色与贴图；`Art/Husbandry/Textures/*-SoftToon.png` 是庭院、木纹和基质素材，原图保留；`Generated/Husbandry/Room` 保存室内材质与独立基质材质；`CampaignSpecimen.Tint` 只对养殖个体放柔高光与轮廓。 |
| 玻璃养殖箱、照片土层 | — | `Editor/Setup/HusbandryTerrariumSetup.cs`（土层1.6、顶面亮度0.36、侧面0.48，保持土面高度并降低背景底板）；`Generated/Husbandry/GlassEnclosure.prefab`（四面薄 Cube）；`Runtime/Presentation/Husbandry/HusbandryGlassEnclosure.cs`（高度）；`Generated/Husbandry/Materials/`（玻璃与土壤材质） |
| 养殖聚光与入场闪灯 | — | `Presentation/Husbandry/HusbandrySpotlight.cs` 同步固定聚光、圆形地面光池和入场 DOTween 双闪，黑幕退去后播放、离场关闭；`Presentation/Display/BeetleHusbandryLightingPreferences.cs` 保存画面设置开关。`Editor/Setup/HusbandryLightingSetup.cs` 配置两个养殖场景。场景 `EnclosureSpot/Light` 调光强及内外锥角（光池大小与柔边同步）；`Generated/Husbandry/Materials/SpotlightPool.mat` 调光池亮度/颜色，`Backdrop.mat` 移除随视角游走的高光。`SoilTop.mat` 独立控制土壤亮度。 |
| 玻璃效果：拟真 / 低配 | `Runtime/Presentation/Display/BeetleGlassPreferences.cs`（全局偏好，默认拟真） | `CampaignSettingsView.cs`、`BeetleFeedbackSettings.cs`、`RetroFilterDropdown.cs`；`Runtime/Presentation/Husbandry/HusbandryGlassSurface.cs`；`Shaders/HusbandryGlass.shader` / `HusbandryGlassLow.shader` |
| 格斗个体跟随镜头 | `BeetleCameraFrame` 的水平 `Forward` | `Runtime/Presentation/Cameras/ArenaCamera.cs`（后上方，与地面60°，随个体转向） |
| 菜单与存档槽 | `CampaignSaveRepository.cs`、`CampaignSnapshots.cs` | `CampaignMenuView.cs`（主菜单设置入口；四卡按内宽排版；缺失/解码失败/纯色缩略图隐藏）；`CampaignBackdrop.ConfigureMenuFraming` / `SetMenuTransitionProgress`（完整边界取景、宽高比适配与离场缩小）；存档入口仅保留主菜单 |
| 设置、滑块、显示样式 | `CampaignPreferences`、`BeetleDisplayPreferences` | `CampaignSettingsView.cs`、`Runtime/Presentation/UI/RetroSettingsSlider.cs` |
| 纸板/经典风格、季节与明暗配色 | `CampaignClock.Month`；风格和明暗模式独立保存 | `CampaignRetroTheme.cs`：四季×明暗共享 `Window` / `Panel` / `Button` / `Selection`、对应文字与交互颜色；`SetStyle` / `SetMode` / `NotifyCurrentPalette` 发出 `Changed`；`RetroBevel` 绘制原尺寸内的纸面、墨线、厚边及硬阴影，`CampaignSettingsView` / `BeetleFeedbackSettings` 提供风格切换，`CampaignView.Rebuild` 同步季节，`BeetleArenaHud` / `BeetleHusbandryHud` 订阅更新 |
| 游戏滤镜：原画 / CRT / 两套原色 Dither | `BeetleDisplayPreferences`、`WorldDisplayProfile` | `CampaignSettingsView.DrawDisplaySettings`、`BeetleFeedbackSettings`、`Runtime/Presentation/UI/RetroFilterDropdown.cs`；`Runtime/Presentation/Display/BeetleCombatCrt.cs` 和对应显示着色器 |
| 全场景卡通叠加（在滤镜之前） | `WorldDisplayProfile.toon`、`WorldToonLayer` | `Resources/BeetleWorldToon.shader`；`CampaignBackdrop` 与 `BeetleCombatDisplay` 共用，UI 不进入处理纹理；`WorldToonValidation.CheckLogic()` |
| 分场景画面编辑器、Shader 扫描与应用 | `WorldDisplayProfile`、`Resources/WorldDisplay.asset` | `Editor/Presentation/WorldDisplayWindow.cs`（配套 UXML/USS）、`WorldDisplayEditorService.cs`；Shader 参数在 `Resources/WorldDisplayMaterials/` 各场景材质的 Inspector 编辑 |
| 本地化与玩家原样文字 | `CampaignDisplayText.cs` | `CampaignLocalization.cs`；`Assets/Localization/Source/GameText.json` |

`CareEnclosureLayout.cs` 位于 `Runtime/Presentation/Husbandry/`，`BeetleCombatCrt.cs` 位于 `Runtime/Presentation/Display/`；共用表现与经营页的表现组件分开存放。

游戏内入口为「设置 → 画面 → 玻璃效果」，默认「拟真」，选择全局保存，与各场景游戏滤镜独立。`Generated/Husbandry/Materials/Glass.mat` 与 `GlassLow.mat` 分别调整两档材质：透射颜色、吸收密度、折射率、反射光滑度、环境反光和柔和窗光；拟真档还提供折射位移、最大像素偏移和深度边缘排除参数。两套 Shader 共用 `Shaders/HusbandryGlassCommon.hlsl`；拟真档采样屏幕颜色与深度作轻微折射，低配档仅保留简化反光，不采样屏幕颜色、深度或阴影。

`HusbandryGlassSurface` 将所选材质应用到四面玻璃，编辑器使用独立的 `editorPreview`。同文件中的 `GlassCameraTextures` 仅在能看到活动玻璃的当前世界相机渲染期间临时修改 URP 相机选项：拟真请求颜色和深度，低配关闭颜色副本并保留原深度选项；渲染结束恢复原值。它不修改共享管线资源，也不接管 UI 相机。

土壤照片位于 `Art/Husbandry/Textures/SoilTop.png` 与 `SoilSide.png`，`Generation.json` 记录图片来源与生成提示词；`SoilCube.asset` 从 Unity Cube 拆分顶面/侧面材质槽，保持单位边界。`Terrarium/SoilBed` 厚度由0.8增至1.6，增量向下延伸，土面仍是脚底、建造与相机共用的原锚点-0.02，不在上方另盖地面。`HusbandryTerrariumSetup.SoilSize` 控制尺寸，`SoilTopBrightness=.36` 与 `SoilSideBrightness=.48` 控制顶面/侧面材质乘色。`BackdropClearance=.1` 让背景底板至少低于土层底面0.10，光池跟随底板，防止底板截掉土层下部与玻璃底框；灯光配置保持原值。`Apply()` 更新独立养殖与经营温室；`HusbandryTerrariumValidation` 验证完整土层、底框与光池之间的净空，`TerrariumPlayValidation` 验证真实运行画面。

自然时间的玩家入口只修改倍率；日期、基准日长、生命周期编辑和独立 `NextWeek` 快捷入口已移除。“帮工一周”仍走领域跨周规则。建造、模态弹窗及抓取/落下的暂停由 `CampaignApp` 的页面/操作门禁控制，退出后恢复，不写入长期暂停标记；`HandlingBeetle` 还阻止抓取期间的跨日进食结算。甲虫信息与食盆浮窗本身不暂停日历。领域配置接口仍供旧档兼容和验证使用。

玻璃高度在 `Generated/Husbandry/GlassEnclosure.prefab` 根物体的 `HusbandryGlassEnclosure` 组件中调整 `Height`：应用1.6厚土层后总高为6.8，下沿世界高度-1.62，上沿保持5.18。参数表示玻璃下沿至上沿的总高度；`SetHeight` 同步四面板、上边框和接缝，`SetBottom` 在土层增厚时只向下延长玻璃与底框，保留上沿位置。组件在编辑模式和运行模式均生效，全景观察镜头通过 `Revision` 重算取景；甲虫近观不被重置。更改场景实例可单独设高，更改预设则所有未覆盖的实例一起更新。

## 表现代码目录

`Presentation` 只作为分类入口，根目录不再平铺脚本。新增文件按功能归入下面的目录；目录调整不改变程序集和类型命名。

| 共用表现目录（`Runtime/Presentation/`） | 职责与入口 |
| --- | --- |
| `Display/` | dither、CRT、自定义画面材质及分场景配置：`BeetleCombatCrt`、`BeetleCombatDisplay`、`BeetleCrtProfile`、`BeetleDisplayPreferences`、`WorldDisplayProfile`；全局玻璃品质：`BeetleGlassPreferences` |
| `Cameras/` | 战斗取景、镜头姿态快照：`ArenaCamera`、`BeetleCameraFrame` |
| `UI/` | 独立格斗/养殖 HUD、设置、滑块和养殖导航：`BeetleArenaHud`、`BeetleHusbandryHud`（启用订阅主题、禁用解绑；刷新保留精简卡片与选中态）、`BeetleFeedbackSettings`、`RetroSettingsSlider`、`BeetleNurseryNavigation`；共用滤镜与玻璃效果下拉：`RetroFilterDropdown`；常驻窗口/下拉动画：`BeetlePanelMotion` |
| `Audio/` | 战斗声音、按钮合成声及滑块点击声：`ArenaAudio`、`RetroUiAudio`、`RetroUiAudioProfile`、`MouseClickSynthesizer`、`RetroUiSliderClick` |
| `Husbandry/` | 养殖模型姿态、游走、足部 IK 和容器空间：`BeetleHusbandryView`、`CarePosePresenter`（`PresentAmbient` 静止/暂停感知微动，`PresentHeld` 悬空姿态）、`CareLocomotion`（`PlaceAt` 安全落地）、`CareFootPlant`、`CareEnclosureLayout`、`CareWalkArea`、`CareGroundSurface`、`CareShellContact`；玻璃高度与品质：`HusbandryGlassEnclosure`、`HusbandryGlassSurface` |
| `CombatFeedback/` | 战斗接触反馈、火星：`BeetleContactFeedback`、`HornClashSparks` |

| 经营表现目录（`Runtime/Campaign/Presentation/`） | 职责与入口 |
| --- | --- |
| `Display/` | 世界背景纹理显示：`CampaignBackdrop`；弹窗一次页面截图与柔和模糊：`CampaignModalBackdrop`；DOTween窗口动画：`CampaignDialogMotion` |
| `Cameras/` | 观察镜头、景深：`CampaignObservationCamera`、`CampaignDepthOfFieldToggle` |
| `Audio/` | 经营背景声、金币声音、邮件反馈：`CampaignAudio`、`CampaignCurrencyAudio`、`CampaignMailAudio` |
| `Husbandry/` | 照料与阶段外观：`CampaignCareController`、`CampaignLifeStageDisplay`、`CampaignLifeStageVisualProfile`、`CampaignSpecimen`（活虫、食盆及残渣）；`CampaignBeetleGrabController`（锁定初始背部命中、0.7秒白色空心进度圈、手形光标、合法移动、落下与取消） |
| `Construction/` | 建造交互、净空检查、景观造型：`CampaignConstructionController`、`CampaignConstructionHabitat`、`CampaignDecorationVisuals`；`CampaignConstructionOccupancy`（身体投影、逐格红X禁放） |
| `Specimens/` | 标本墙与相框展示：`CampaignSpecimenRoom` |
| `Transitions/` | 场景 `Go` 与同场景 `Play`：`CampaignSceneTransition`、`CampaignTransitionProfile`；BlackFade时长默认×3，全黑2.06秒，上下帘双向柔化 |

画面配置编辑器及其 UXML/USS 保持在 `Editor/Presentation/`，游戏内画面逻辑在 `Runtime/Presentation/Display/`。

转场 Inspector 资源为 `Resources/CampaignTransition.asset`：基础关闭/打开时长与曲线、`blackFadeDurationMultiplier`、`blackHoldDuration`、`shutterSoftness`。`CampaignTransitionSetup.Apply` 关联软边Shader并升级旧默认停留；运行时预览只改配置副本。详细默认数值与页面策略见 `Campaign.md`。

## 场景与对象命名

正式场景位于 `Assets/Scenes/Campaign/`。场景 UI 根对象按页面命名，如 `GreenhouseUI`、`MarketUI`、`SpecimenRoomUI`。场景中的 `ObservedBeetle`、`Terrarium`、`SoilBed` 分别表示观察个体、养殖箱和底床；保留这些有含义且被表现组件使用的名字。

运行时界面使用 `CampaignCanvas / Page`、`DialogPanel`、`DialogTitleBar`、`GreenhousePanel`、`GreenhouseTitle`、`Card`、`Viewport / Content` 等结构名称。养殖主导航 `GreenhouseNavigation` 占左侧1/6，标题为“虫箱”，不再有“虫箱”“照料”重复选项；仓库 `WarehouseCategories / WarehouseDetails` 按选择展开至1/3。`GreenhouseInfo` 是点击甲虫后打开的浮窗，其 `CareScroll`、`ManagementScroll`、`ConstructionScroll` 分别属于信息、管理、建造页签；保留 `CareScroll` 内部名以维持引用，育种仍为独立页面。`FeedingDishWindow` 是点击食盆打开的非模态操作窗，与甲虫信息互斥；`ControlHint` 显示必要的一次性操作提示。`GreenhouseFooter` 内整合金币与邮箱，`UnreadMailBadge / RetroRoundBadge` 绘制白边红色圆圈，并置于金币之上。建造世界对象 `CareConstructionAnimalCells` 绘制每个身体占格的红X。交互按钮沿用其可见操作名称，便于在 Hierarchy 查找。业务归属使用稳定 ID 或明确标记，不用显示名判断玩家、甲虫或拍卖所有权。

导入模型的部件名、骨骼名和已有物理接触标记有绑定用途，不能仅为统一字形随意重命名。新增场景对象应描述职责，避免 `GameObject`、`Cube` 或冗长的解释句。生成器在 `Editor/Setup/`，修改命名时同步生成器，避免重新安装后退回旧名。

## 编辑器工具菜单

统一入口为 `Tools > 甲壳虫斗`，按用途分类，常用工具优先排列。

| 分类 | 用途 |
| --- | --- |
| 常用工具 | 调试工作台、场景画面设置、战斗 CRT、界面点击音效 |
| 场景与安装 | 生成场景、安装战役/标本室、更新界面、整理层级与安装战斗模块 |
| 画面与资源 | 材质与灯光、养殖表现、界面与字体 |
| 模型与绑定 | 拆分触角口器、修正和查看角部绑定 |
| 验证 | 战斗、养殖、战役流程、界面与画面、本地化、存档与数据、模型与动作、架构与性能 |
| 维护与清理 | 预览或清理无资产引用、源码引用及文件名 GUID 引用的限定生成资源 |

整流程验收只保留一个菜单入口：`验证 > 战役流程 > 完整战役流程`。旧的 `CampaignPlayModeValidation.Run()` 仍可供脚本调用。

## 修改后验证

所有入口位于 `Editor/Validation/`，Unity 菜单前缀为 `Tools > 甲壳虫斗 > 验证`。

| 修改范围 | 定向验证类 |
| --- | --- |
| 玩家身份、姓名迁移、同名竞拍 | `CampaignPlayerIdentityValidation`、`CampaignPlayerIdentityPlayValidation` |
| 建造规则、空间、库存、虫体占格与实际放置 | `CampaignConstructionValidation`（含旋转身体逐格投影）、`CampaignConstructionPlayValidation`；`CampaignInterfacePlayValidation` 检查红X显示与放置拒绝 |
| 食盆清理、丢弃、残渣持久化与旧档 | `CampaignFoodDishValidation`（`Artifacts/Campaign/Care/FoodDish.txt`；隔离存档自动清理） |
| 提起/落点约束、悬空姿态、落地足部 | `CampaignBeetleGrabValidation`（`Artifacts/Campaign/Care/Grab.txt`）；`CampaignInterfacePlayValidation` 检查真实抓取与取消流程 |
| 支撑脚驱动胸部、预判落脚、起停转向、探查先后与一轮挣扎收尾 | `CareMotionValidation`（74项隔离编辑器逻辑检查）；`CareSupportPose` 拟合支撑面，`CareFootPlant` 管理慢三足迈步、六足承重停顿、世界坐标落脚和骨长约束；`CareLocomotion` 养殖速度乘0.75、转速上限34°/秒，旧存档同样生效；`CarePosePresenter` 用落脚承重的小幅下沉替代走路正弦跳动。`CareWalkPreviewValidation` 在隔离Play中渲染6秒起步/转身/停止/续步序列，输出 `Artifacts/HusbandryPolish/Walk`。 |
| 虫箱/仓库/市场/存档排版、食盆浮窗、提示与静止微动 | `CampaignInterfacePlayValidation`（`Artifacts/InterfaceRework/`；检查真实输入、模态阻挡、提示记忆与界面边界） |
| 土层增厚、顶面亮度与地面锚点 | `HusbandryTerrariumValidation`、`TerrariumPlayValidation` |
| 翻身按钮、侧栏与视口 | `CampaignGreenhouseUiValidation` |
| 市场与标本交易、竞价托管 | `CampaignTradingValidation`、`SpecimenTradingPlayValidation` |
| 标本室封闭外壳、极限镜头与双语界面 | `SpecimenLocalePlayValidation`、`SpecimenRoomGeometryChecks`（`Artifacts/SpecimenLocale/`） |
| 铭牌来源、玩家文字和键位翻译 | `CampaignSpecimenLabelValidation`、`CampaignNamingLocalizationValidation`、`CampaignBindingLocalizationValidation` |
| 生命周期、日历、用品与阶段表现 | `CampaignLifecycleValidation`、`CampaignLifecyclePlayValidation` |
| 转场时长、软边、全黑遮挡与同场景回调 | `CampaignTransitionValidation`（14次切场景+2次同场景，`Artifacts/Campaign/Transitions/`） |
| 连续金币变化、静音、失败保存 | `CampaignCurrencyValidation` |
| 新旅程到自然战斗结算、拍卖、标本与读档 | `CampaignEndToEndValidation`（旧 `CampaignPlayModeValidation.Run` 转发至此） |
| 异步读档失败、缩略图与旅程一致性 | `CampaignSaveCommitValidation` |
| 存档图真实渲染、自动更新、覆盖显示与阶段场景 | `CampaignThumbnailPlayValidation`（隔离存档，图像对照报告在 `Artifacts/SaveThumbnails/`） |
| 分场景dither/CRT/原画/自定义材质与清晰UI | `WorldDisplayPlayValidation`（12个场景、隔离存档，`Artifacts/SceneDisplay/`） |
| 游戏滤镜下拉、两套Dither、RGB与UI隔离、偏好保存 | `GameFilterValidation`（真实鼠标/键盘、养殖与独立格斗，`Artifacts/GameFilter/`） |
| 画面编辑器、Shader 扫描、应用与撤销 | `SceneDisplayValidation`；`DisplayPreferencesSnapshot` 负责还原测试偏好 |
| 战斗结构与运动学 | `BeetleArchitectureValidation` 及 `MotionModules.md` 对应专项 |
| 四场地自然对局、接触与显示停滞 | `BeetleSeedSweepValidation.Start()`（12种子×4场地，临时场景） |

只运行受改动影响的验证，再检查实际画面；不把“进入Play”当成验收通过。实际通过项、报告路径和局限记录在 `CHANGELOG.md`。

## 本轮整理原则

- 保留现有分层，拆出混放的页面与共用控件，不增加服务定位器、通用事件总线或新的仓储抽象。
- 删除已被前置分支排除的重复判断；圆角血条纹理只在需要它的格斗页面创建。
- 存档、外部JSON、价格/库存边界和异步提交失败检查属于真实边界，保留；内部程序错误继续记录异常，不伪装成成功。
- 模型、价格和界面分别修改，不让屏幕像素决定领域规则。建造格子空间与实际身体净空分别由专门模块计算。
