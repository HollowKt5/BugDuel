# 甲虫外壳：金属虹彩 Shader

适用版本：Unity 6000.3.16f1 / URP 17.3。Shader 名称：`BeetleArena/IridescentShell`。

`CampaignMainMenu` 只保留一只展示甲虫。眼睛与角/颚保持原材质，其余身体使用虹彩，腿部的反射亮度默认降低为身体的65%。当前模型的眼部为 `tripo_part_15`、`tripo_part_20`，独立颚片为 `*jaw from part *`。源 `Part5` 实际包含头壳与长头角，`Part3` 包含前胸壳与短胸角，不能整块按角处理：现在由 `BeetleHornBindingUpgrade` 裁切为壳面和独立角面，`BeetleHornRig` 显式引用 `Cephalic horn surface`、`Thoracic horn surface`，只有两处角面保留原材质。腿部通过蒙皮骨骼与六足绑定识别。更换模型时需检查这些绑定。背景保持原卡通材质，UI 使用标准 UI 材质。养殖与战斗场景继续使用原材质。

## 文件与入口

| 内容 | 路径 |
|---|---|
| Shader 和五个渲染通道 | `Assets/BeetleArena/Shaders/BeetleIridescentShell.shader` |
| 共用参数、顶点变换和虹彩采样 | `Assets/BeetleArena/Shaders/BeetleIridescentInput.hlsl` |
| 用户提供的色带原图 | `Assets/BeetleArena/Art/Textures/ShellIridescenceRamp.jpg` |
| 菜单专用虹彩材质、静态反射 Cubemap | `Assets/BeetleArena/Generated/MenuIridescence/` |
| 场景安装工具 | `Assets/BeetleArena/Editor/Setup/CampaignIridescenceSetup.cs` |
| 渲染比较验收 | `Assets/BeetleArena/Editor/Validation/CampaignIridescenceValidation.cs` |

运行菜单场景即可看到效果；选中菜单甲虫身体，在 Renderer 的 Material 中调整参数。材质名称以 `Menu rainbow metal` 开头，分别保留对应部位的明暗纹理。新增身体材质从已有外壳材质复制虹彩参数，使身体与外壳的金属和调色风格一致。

材质位于 `Assets/BeetleArena/Generated/MenuIridescence/`。身体使用 `Shell-*.mat`，腿部使用独立的 `Leg-*.mat`；腿部创建时将来源材质的 `Reflectance Brightness` 乘以0.65，之后重复安装不会继续压暗，也不会覆盖手动调整。统一色相和饱和度时可多选两组材质，在 Inspector 的 **Color Tuning** 区域修改；亮度可按身体、腿部分别调整。眼睛与角不使用这些虹彩材质。编辑材质资源会实时更新画面；在非 Play 模式下修改更方便保留结果。

编辑器菜单 `Tools → Beetle Arena → Apply Menu Iridescent Shell` 可重新挂接材质，并确保菜单只保留一只甲虫，布置在画面右侧展示区域。它只处理主菜单，不修改模型预制体、网格或骨骼；已有虹彩材质的手动参数会保留。原“Apply Campaign Opaque UI and Toon World”和经营场景安装工具已接入此规则，重复运行不会恢复第二只甲虫或把虹彩身体刷回普通卡通材质。

## 效果如何形成

### 1. 观察角度决定色带位置

对每个像素取得归一化世界法线 `N` 和指向相机的视线方向 `V`：

```text
g = saturate(1 − dot(N, V)) ^ AnglePower
u = saturate(RampOffset + g × AngleShift + variation × FilmVariation)
rainbow = RainbowStrip(u, RampRow)
```

正对表面时 `g` 接近 0，斜看时趋近 1。默认从铜金向黄绿侧缘偏移，对照参考图保留成片暖色反光和鲜绿轮廓；不同朝向的壳面会同时显示不同颜色。相机移动、模型转身或外壳曲率变化都会影响颜色，没有通过时间自动滚动色带。

`variation` 是两项正弦的乘积，使用模型局部坐标形成轻微不均匀变化，模拟表面膜层厚度的视觉差异。模型走动时图案随外壳一起移动，不会固定在世界空间中滑动。

色带保留用户提供的完整 JPEG，文件内容未修改。Shader 固定读取中间彩色区域的 `v = 0.5`，不会把上下黑边和右下角图案贴到甲虫身上。导入设置为 sRGB、Clamp、Bilinear、无压缩、无 Mipmap；关闭 Mipmap 避免固定采样行与上下黑边混合。更换为其他素材时可调整采样行。

### 2. 彩色反射和金属高光

虹彩结果作为金属表面的反射颜色，接入项目安装的 URP `UniversalFragmentPBR`。金属度控制漫反射与有色镜面反射的比例，光滑度控制高光宽度。清漆层提供较中性的高光，帮助保持硬壳和抛光表面的质感。

2026-10-20 的调整以原图的“铜金反光、绿色侧缘、深色反射间隙”为目标。默认金属度为 1、光滑度为 0.88，清漆强度从 0.65 降到 0.18，减少覆盖在有色金属反射上的白雾感。保留小面积中性亮点，但不依靠加白或自发光制造金属光泽。

色带 JPEG 本身带有暗部，直接当作金属反射率会重复压暗。新增 `Ramp Reflectance Recovery`：在色带采样后、调色前，用同一个倍率恢复 RGB 的峰值，保留三通道比例：

```text
peak = max(r, g, b)
target = lerp(peak, max(peak, 0.72), ReflectanceRecovery)
rainbow *= target / max(peak, 0.001)
```

恢复量为 0 时保持旧采样，1 时把非黑色低亮度采样的峰值提高到 0.72。之后仍经过饱和度、色相、亮度、金属 BRDF 与环境光计算；无光处不会自行发亮。腿部的 0.65 亮度倍率在恢复后作用，因此仍比身体暗。

直接光经过 URP 灯光、衰减和阴影计算；间接镜面光来自反射探针。环境采样与粗糙度对应的模糊层级沿用 URP 的实现，见 [Unity：自定义 URP Shader 的间接光照](https://docs.unity.com/en-us/engine/6000.0/manual/materials-and-shaders/shaders/writing-custom-shaders-urp/use-built-in-shader-methods/indirect-lighting)。本 Shader 不添加彩虹自发光。

菜单的 `Menu shell reflection studio` 静态探针使用 `MenuShellStudioV2.asset`：程序生成的 256×256、六面 RGBAHalf Cubemap，包含宽暖色亮区、冷色补光区、细条高光以及深色间隙。亮区边界经过平滑过渡，同时保留内部清晰反射，避免原先整个环境只有模糊亮团的塑料感；探针强度为 1。无需每帧重新捕获场景；原有卡通背景 Shader 不采样该探针，所以背景外观不变。该 Cubemap 是美术展示用近似环境，并非菜单几何的实时镜像，也不包含 UI。其 Mip 使用常规纹理降采样，并非离线 GGX 卷积，粗糙表面的环境反射属于近似。

安装工具对旧默认材质执行一次升级，记录 `_MetalFinishVersion`。只替换仍等于旧预设的角度、金属度、光滑度和清漆参数；用户自调的色相、饱和度、亮度和其他非默认值保留。后续重复安装不覆盖调参。

### 3. 与现有卡通画面相容

保留较细的反向外壳描边。前向表面、描边、投影、深度和法线共五个 Pass，使用统一的 `UnityPerMaterial` 常量缓冲；表面使用 `UniversalForwardOnly`，附带 Forward/Forward+ 的灯光和反射探针变体。表面和投影通道不做顶点位移，描边只扩张屏幕轮廓，不改变接触、脚点或碰撞几何。

## 参数说明

| Inspector 参数 | 默认值 | 调整效果 |
|---|---:|---|
| Reflectance Tint | 白色 | 整体反射色乘色，使用深色会显著压暗金属 |
| Original Texture Luminance | 0.12 | 保留原贴图的明暗细节；不沿用原贴图的红色色相 |
| Iridescence Strength | 1 | 0 为无虹彩的基础金属，1 完全使用色带 |
| Facing Color Position | 0.32 | 正对外壳时从色带的什么位置取色；默认铜金 |
| Grazing Color Shift | 0.30 | 斜看时沿色带偏移的方向和距离；默认朝黄绿偏移，0 关闭角度变色 |
| Angle Response | 0.7 | 大于 1 将明显变色集中到更斜的角度，小于 1 提前变色 |
| Strip Sample Row | 0.5 | 原图中央彩色带；本素材建议保持 0.5 |
| Film Variation | 0.045 | 壳面轻微颜色不均匀程度；0 关闭 |
| Film Variation Scale | 1.2 | 局部变化的空间频率 |
| Rainbow Hue (Degrees) | 0 | 整套虹彩的色相旋转，范围 −180°～180°；保留各观察角度之间的色差 |
| Rainbow Saturation | 1.15 | 色带饱和度；0 变灰，1 保持原图色彩 |
| Reflectance Brightness | 1 | 反射颜色亮度倍率，范围 0.25～2；过高会截断颜色，建议小幅调整 |
| Ramp Reflectance Recovery | 0.65 | 恢复素材中偏暗的反射颜色，保留色相；0 使用原色带亮度 |
| Metallic | 1 | 越高越依赖彩色镜面反射和环境反射 |
| Metal Smoothness | 0.88 | 越高反射和高光越锐利，最高限制为 0.95 |
| Clear Coat | 0.18 | 中性涂层高光强度，降低可减少发白的高光 |
| Coat Smoothness | 0.88 | 涂层高光的锐利程度 |
| Outline Width (Pixels) | 0.85 | 轮廓线宽，0 关闭 |

希望更偏铜金：适当降低 `Facing Color Position`；希望更多蓝青：提高它。希望颜色变化更集中、保留大块基色：减小 `Grazing Color Shift` 的绝对值。希望更柔和：降低两项光滑度。若在其他场景使用后变暗，应先检查光源和反射探针，再调整材质，不能靠金属度单独得到明亮反光。

### 绿、蓝、红调色起点

希望主体明确偏某一种颜色时，先将 `Facing Color Position` 设为 **0.42**、`Grazing Color Shift` 设为 **0.10**，缩窄取色范围，再用以下色相参数。使用默认色带和白色 `Reflectance Tint`；仍保留一定角度色差。这是便于调色的可选起点，不会覆盖菜单默认的铜金→绿色预设。

| 想要的效果 | Rainbow Hue (Degrees) | Rainbow Saturation | Reflectance Brightness |
|---|---:|---:|---:|
| 绿色更鲜明 | 0 | 1.5 | 1.05 |
| 主体偏蓝 | 120 | 1.5 | 1.05 |
| 主体偏红 | −120 | 1.5 | 1.05 |

先用色相选择偏色，再用饱和度调整鲜艳程度。`Rainbow Hue` 在 HSV 空间旋转颜色：`H = frac(H + Hue / 360)`，保留 S、V；饱和度使用亮度与原色的插值，亮度倍率最后作用于反射颜色。它们都不改变金属度、粗糙度或灯光。`Grazing Color Shift` 的绝对值越小，颜色越集中；保持默认 0.30 则有更宽的虹彩过渡。手动使用其他角度参数的材质应按实际画面调整色相。

## 验收与边界

`Tools → Beetle Arena → Validate Menu Iridescence` 在临时场景副本中渲染，结束后恢复原场景，不保存测试姿态。检查单只甲虫、虹彩身体、独立腿部材质、眼睛与角及背景的原材质、五个 Pass、Shader 编译结果，并在相同相机下分别关闭虹彩、角度偏移和金属度进行像素比较。另保存 ±40° 观察角度截图及上述绿、蓝、红三组参数效果图。调色只操作临时材质副本，不覆盖菜单的默认参数。

报告和图片位于 `Artifacts/IridescentShell/`；实际带 UI 的主菜单画面使用经营 Play 验收截图。八场景检查明确区分菜单虹彩外壳与其他卡通表面，避免效果意外扩散。

这是以参考图为目标的实时美术近似：角度色带加 URP 金属反射，并未求解真实甲壳多层纳米结构、波长干涉或偏振。静态反射环境也不会实时反射附近移动物体。更精确的薄膜光学、屏幕空间反射或动态探针需要额外成本；目前验证针对本项目桌面 URP，未打包验证移动端或 Web。
