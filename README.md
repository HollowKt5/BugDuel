# BugDuel

Unity 6000.3.16f1 / URP。正式经营流程从 `Assets/Scenes/Campaign/CampaignMainMenu.unity` 进入，运行后选择存档。养殖、市场、拍卖、联赛和标本室使用同一份经营存档。

## 开发入口

| 要做什么 | 从这里开始 |
| --- | --- |
| 找功能对应代码、修改入口、验证入口 | [CodeMap.md](CodeMap.md) |
| 查游戏规则、价格、生命周期、操作 | [Campaign.md](Campaign.md) |
| 查最近实现与实际验证结果 | [CHANGELOG.md](CHANGELOG.md) |
| 替换阶段模型或景观模型 | [ModelingChecklist.md](ModelingChecklist.md) |
| 查战斗模拟、骨架、接触求解 | [TechnicalGuide.md](TechnicalGuide.md)、[MotionModules.md](MotionModules.md) |
| 查独立战斗与早期养殖原型 | [Assets/BeetleArena/README.md](Assets/BeetleArena/README.md)、[Framework.md](Framework.md) |

`BeetleArena.unity` 是独立战斗入口，`BeetleNursery.unity` 是早期养殖原型；它们不替代 `Scenes/Campaign/` 的正式经营流程。第三方代码在 `Assets/Plugins/`，不要与游戏功能代码混改。

Unity 的 `Tools > 甲壳虫斗 > 验证` 菜单提供安装和专项验证入口。验证报告、截图与隔离存档输出到被 Git 忽略的 `Artifacts/`；正式玩家存档不用于自动验收。
