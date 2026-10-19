# 甲虫外壳：金属虹彩 Shader

适用版本：Unity 6000.3.16f1 / URP 17.3。Shader 名称：`BeetleArena/IridescentShell`。

已经应用到 `CampaignMainMenu` 中两只甲虫的六个外壳 Renderer（每只三块，沿用 rig 的 `shellRenderers` 定义）。大颚、足部、腹面和背景保持原卡通材质，UI 使用标准 UI 材质。养殖与战斗场景继续使用原外壳材质。

## 文件与入口

| 内容 | 路径 |
|---|---|
| Shader 和五个渲染通道 | `Assets/BeetleArena/Shaders/BeetleIridescentShell.shader` |
| 共用参数、顶点变换和虹彩采样 | `Assets/BeetleArena/Shaders/BeetleIridescentInput.hlsl` |
| 用户提供的色带原图 | `Assets/BeetleArena/Art/Textures/ShellIridescenceRamp.jpg` |
| 三份菜单专用材质、静态反射 Cubemap | `Assets/BeetleArena/Generated/MenuIridescence/` |
| 场景安装工具 | `Assets/BeetleArena/Editor/Setup/CampaignIridescenceSetup.cs` |
| 渲染比较验收 | `Assets/BeetleArena/Editor/Validation/CampaignIridescenceValidation.cs` |

运行菜单场景即可看到效果；选中菜单甲虫外壳，在 Renderer 的 Material 中调整参数。材质名称为 `Menu rainbow metal shell`，三份材质分别保留原来对应部位的明暗纹理。

要统一调整整个菜单的甲壳，可在 Project 中打开 `Assets/BeetleArena/Generated/MenuIridescence/`，多选三份 `Shell-*.mat`，在 Inspector 的 **Color Tuning** 区域一起修改。编辑材质资源会实时更新画面；在非 Play 模式下修改更方便保留结果。

编辑器菜单 `Tools → Beetle Arena → Apply Menu Iridescent Shell` 可重新挂接材质。它只处理主菜单，不修改模型预制体、网格或骨骼；已有虹彩材质的手动参数会保留。原“Apply Campaign Opaque UI and Toon World”和经营场景安装工具已接入此规则，重复运行不会把菜单外壳刷回普通卡通材质。

## 效果如何形成

### 1. 观察角度决定色带位置

对每个像素取得归一化世界法线 `N` 和指向相机的视线方向 `V`：

```text
g = saturate(1 − dot(N, V)) ^ AnglePower
u = saturate(RampOffset + g × AngleShift + variation × FilmVariation)
rainbow = RainbowStrip(u, RampRow)
```

正对表面时 `g` 接近 0，斜看时趋近 1。默认从青绿方向向金黄、铜红方向偏移；不同朝向的壳面会同时显示不同颜色。相机移动、模型转身或外壳曲率变化都会影响颜色，没有通过时间自动滚动色带。

`variation` 是两项正弦的乘积，使用模型局部坐标形成轻微不均匀变化，模拟表面膜层厚度的视觉差异。模型走动时图案随外壳一起移动，不会固定在世界空间中滑动。

色带保留用户提供的完整 JPEG，文件内容未修改。Shader 固定读取中间彩色区域的 `v = 0.5`，不会把上下黑边和右下角图案贴到甲虫身上。导入设置为 sRGB、Clamp、Bilinear、无压缩、无 Mipmap；关闭 Mipmap 避免固定采样行与上下黑边混合。更换为其他素材时可调整采样行。

### 2. 彩色反射和金属高光

虹彩结果作为金属表面的反射颜色，接入项目安装的 URP `UniversalFragmentPBR`。金属度控制漫反射与有色镜面反射的比例，光滑度控制高光宽度。清漆层提供较中性的高光，帮助保持硬壳和抛光表面的质感。

直接光经过 URP 灯光、衰减和阴影计算；间接镜面光来自反射探针。环境采样与粗糙度对应的模糊层级沿用 URP 的实现，见 [Unity：自定义 URP Shader 的间接光照](https://docs.unity.com/en-us/engine/6000.0/manual/materials-and-shaders/shaders/writing-custom-shaders-urp/use-built-in-shader-methods/indirect-lighting)。本 Shader 不添加彩虹自发光。

菜单增加了 `Menu shell reflection studio` 静态探针：使用程序生成的 128×128、六面 RGBAHalf Cubemap，包含明亮柔光区域和较暗环境。它提供可读的反射层次，无需每帧重新捕获场景；原有卡通背景 Shader 不采样该探针，所以背景外观不变。该 Cubemap 是美术展示用近似环境，并非菜单几何的实时镜像，也不包含 UI。

### 3. 与现有卡通画面相容

保留较细的反向外壳描边。前向表面、描边、投影、深度和法线共五个 Pass，使用统一的 `UnityPerMaterial` 常量缓冲；表面使用 `UniversalForwardOnly`，附带 Forward/Forward+ 的灯光和反射探针变体。表面和投影通道不做顶点位移，描边只扩张屏幕轮廓，不改变接触、脚点或碰撞几何。

## 参数说明

| Inspector 参数 | 默认值 | 调整效果 |
|---|---:|---|
| Reflectance Tint | 白色 | 整体反射色乘色，使用深色会显著压暗金属 |
| Original Texture Luminance | 0.12 | 保留原贴图的明暗细节；不沿用原贴图的红色色相 |
| Iridescence Strength | 1 | 0 为无虹彩的基础金属，1 完全使用色带 |
| Facing Color Position | 0.72 | 正对外壳时从色带的什么位置取色 |
| Grazing Color Shift | −0.62 | 斜看时沿色带偏移的方向和距离；0 关闭角度变色 |
| Angle Response | 1 | 大于 1 将明显变色集中到更斜的角度，小于 1 提前变色 |
| Strip Sample Row | 0.5 | 原图中央彩色带；本素材建议保持 0.5 |
| Film Variation | 0.045 | 壳面轻微颜色不均匀程度；0 关闭 |
| Film Variation Scale | 1.2 | 局部变化的空间频率 |
| Rainbow Hue (Degrees) | 0 | 整套虹彩的色相旋转，范围 −180°～180°；保留各观察角度之间的色差 |
| Rainbow Saturation | 1.15 | 色带饱和度；0 变灰，1 保持原图色彩 |
| Reflectance Brightness | 1 | 反射颜色亮度倍率，范围 0.25～2；过高会截断颜色，建议小幅调整 |
| Metallic | 0.94 | 越高越依赖彩色镜面反射和环境反射 |
| Metal Smoothness | 0.78 | 越高反射和高光越锐利，最高限制为 0.95 |
| Clear Coat | 0.65 | 中性涂层高光强度，降低可减少发白的高光 |
| Coat Smoothness | 0.88 | 涂层高光的锐利程度 |
| Outline Width (Pixels) | 0.85 | 轮廓线宽，0 关闭 |

希望更偏铜金：适当降低 `Facing Color Position`；希望更多蓝青：提高它。希望颜色变化更集中、保留大块基色：减小 `Grazing Color Shift` 的绝对值。希望更柔和：降低两项光滑度。若在其他场景使用后变暗，应先检查光源和反射探针，再调整材质，不能靠金属度单独得到明亮反光。

### 绿、蓝、红调色起点

以下数值以默认色带、默认角度参数及白色 `Reflectance Tint` 为基础；仍会随角度显现金黄、紫色等邻近颜色。

| 想要的效果 | Rainbow Hue (Degrees) | Rainbow Saturation | Reflectance Brightness |
|---|---:|---:|---:|
| 绿色更鲜明 | 0 | 1.5 | 1.05 |
| 主体偏蓝 | 120 | 1.5 | 1.05 |
| 主体偏红 | −120 | 1.5 | 1.05 |

先用色相选择偏色，再用饱和度调整鲜艳程度。`Rainbow Hue` 在 HSV 空间旋转颜色：`H = frac(H + Hue / 360)`，保留 S、V；饱和度使用亮度与原色的插值，亮度倍率最后作用于反射颜色。它们都不改变金属度、粗糙度或灯光。需要大面积保持同一种颜色时，可同时将 `Grazing Color Shift` 的绝对值调小，例如从 `−0.62` 改为 `−0.2`，再微调色相。

## 验收与边界

`Tools → Beetle Arena → Validate Menu Iridescence` 在临时场景副本中渲染，结束后恢复原场景，不保存测试姿态。检查六个指定外壳、其余 Renderer 的材质、五个 Pass、Shader 编译结果，并在相同相机下分别关闭虹彩、角度偏移和金属度进行像素比较。另保存 ±40° 观察角度截图及上述绿、蓝、红三组参数效果图。调色只操作临时材质副本，不覆盖菜单的默认参数。

报告和图片位于 `Artifacts/IridescentShell/`；实际带 UI 的主菜单画面使用经营 Play 验收截图。八场景检查明确区分菜单虹彩外壳与其他卡通表面，避免效果意外扩散。

这是以参考图为目标的实时美术近似：角度色带加 URP 金属反射，并未求解真实甲壳多层纳米结构、波长干涉或偏振。静态反射环境也不会实时反射附近移动物体。更精确的薄膜光学、屏幕空间反射或动态探针需要额外成本；目前验证针对本项目桌面 URP，未打包验证移动端或 Web。
