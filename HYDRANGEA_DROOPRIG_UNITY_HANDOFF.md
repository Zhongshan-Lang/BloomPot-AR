# BloomPot AR：绣球花 DroopRig Unity 集成交接任务

请完整读取当前 Unity 项目后，执行本文档中的集成和验证工作。不要只给建议；在确认现有场景、Prefab、脚本和材质引用后直接实施，并在最后报告改动文件与测试结果。

## 重要：本次最终方案与原计划不同

### 原计划

最初计划是在现有网格上新增一个名为 `Droop` 的相对 Shape Key，并让它与以下五个生长 Shape Key 同时叠加：

- `Sprout`
- `Leafing`
- `Bud`
- `HalfBloom`
- `Bloom`

Unity 原本预计同时使用：

```text
生长阶段 Blend Shape + Droop Blend Shape
```

### 为什么没有继续制作单一 Droop Shape Key

对 Blender 母版逐组件检查后发现，五个生长阶段并不是一套已经成形的网格只做轻微位置变化，而是通过 Shape Key 把大量几何体从接近零尺寸逐渐展开：

- 63 个叶片组件在早期阶段全部压缩到接近一个点
- 3,780 个花瓣组件在早期阶段全部压缩
- 945 个花蕊组件在早期阶段全部压缩
- 71 个枝条组件中有 65 个在早期阶段处于压缩状态

相对 Shape Key 的位移始终以 `Basis` 为参照。如果把 Bloom 状态下的叶片下垂位移直接写入一个 `Droop` Shape Key，再与 Sprout、Leafing 或 Bud 叠加，Droop 位移仍会施加到那些已经被生长 Shape Key 压缩的顶点上。结果会是：

- 隐藏或零尺寸叶片被拉离生长原点
- 早期阶段出现飞散顶点、双重位移或爆点
- 同一个 Droop 无法同时正确适配五个阶段

这不是 Unity Import Settings 可以修复的问题，而是相对 Shape Key 的数学叠加方式与当前生长拓扑表达方式冲突。

### 曾评估但未采用的替代方案

可以为每个阶段分别制作：

```text
Sprout_Droop
Leafing_Droop
Bud_Droop
HalfBloom_Droop
Bloom_Droop
```

但这样会额外保存五套完整的 679,081 顶点形态数据，显著增加 Blender 和 FBX 文件体积，也会让 Unity 必须根据当前阶段切换不同的 Droop Shape Key，控制和过渡更复杂。因此没有采用这个方案。

### 最终采用的方案

在确认不适合安全制作单一 Droop Shape Key 后，最终改为：

```text
五个原生长 Blend Shape + 独立下垂骨骼 DroopRig
```

- 原有 `Basis、Sprout、Leafing、Bud、HalfBloom、Bloom` 完全保留
- 没有新增名为 `Droop` 的 Shape Key
- 没有修改原 Shape Key 的名称或顶点位置
- 新增 63 根叶片下垂骨骼和 5 根花序轻微倾斜骨骼
- 生长 Shape Key 先形成当前阶段，骨骼再在最终结果上做轻微旋转
- 因此同一套骨骼下垂能够安全叠加所有阶段

该调整已经在 Blender 中测试了五个阶段分别叠加 Droop 25%、30% 和 100%，共 15 组。全部通过拓扑、有限坐标、刚性误差、包围盒和下垂方向检查，并进行了五个阶段 100% 下垂的可视化抽查。

### 对 Unity 原计划的具体影响

Unity 中的生长控制方式不变，仍然使用：

```csharp
SkinnedMeshRenderer.SetBlendShapeWeight(...)
```

只有下垂控制方式发生变化：

- 原计划：调用 `SetBlendShapeWeight()` 控制名为 `Droop` 的 Blend Shape
- 最终方案：旋转名字以 `DroopLeaf_` 和 `DroopHead_` 开头的骨骼

因此 Unity 项目不应该查找 `Droop` Blend Shape。需要新增一个独立的骨骼控制组件，同时必须关闭 FBX Importer 中的 `Optimize Game Objects`，确保这些骨骼 Transform 可以被脚本访问。

### 文件体积变化

- 原交互 FBX：62.14 MiB
- 新 DroopRig FBX：63.20 MiB
- FBX 只增加约 1.06 MiB，即约 1.7%
- 原交互 Blender 源文件：160.70 MiB
- 新 DroopRig Blender 源文件：186.79 MiB

所以最终骨骼方案没有造成文件体积失控，同时保留了五个阶段安全叠加下垂的能力。

## 重要更新：请使用 DroopRig v2

Unity 对第一版 DroopRig 的静止蒙皮实测发现叶柄根部可能与枝条脱离。Blender 复查确认第一版存在两个结构性问题：63 片叶子的连接处全部为 `DroopLeaf=1 / Root=0`，而叶骨旋转中心仍停在 Sprout 生长原点；该原点距离 Bloom 阶段真实连接处最高约 3.995 m。

v2 已作以下修复：

- 每根 `DroopLeaf_*` 的 Rest Head 移到 Bloom 阶段叶柄与枝条表面的真实连接位置
- 每片叶子至少保留 6 个 `Root=1 / DroopLeaf=0` 的固定根部顶点
- 每片叶子至少使用 20 个顶点完成 Root→DroopLeaf 平滑过渡
- 权重严格归一化，不使用 Bone Envelope
- Mesh、Armature 和 Parent Inverse 均为单位矩阵
- 源文件保存为 Rest Position，Pose Transform 全部为零
- Blender 驱动和 Action 已移除；Unity 负责运行时旋转
- FBX 空场景回读后，静止 Armature 最大逐顶点误差为 `9.78×10⁻⁷ m`
- 五阶段所有测试的叶根锚点位移为 `0`

必须使用文件名带 `_v2` 的资源，不再导入第一版 DroopRig。

## 一、项目与资源位置

- Unity 项目：`E:\Download\unity\project\BloomPot AR`
- 当前绣球花资源目录：`Assets/Flower1`
- 当前旧模型：`Assets/Flower1/Hydrangea_Growth_BlendShape_Unity.fbx`
- 新的 Blender 导出 FBX：`E:\Download\blender\AR项目盆栽建模\绣球花\Unity_AR_Export\Hydrangea_AR_Interactive_DroopRig_v2.fbx`
- 新的 Blender 源文件：`E:\Download\blender\AR项目盆栽建模\绣球花\Unity_AR_Export\Hydrangea_AR_Interactive_DroopRig_v2_Source.blend`

本次必须把新 FBX 作为新资源导入，例如：

`Assets/Flower1/Hydrangea_AR_Interactive_DroopRig_v2.fbx`

不要覆盖、删除或移动旧 FBX、现有材质、贴图、Prefab、场景和控制脚本。修改现有脚本前先读取其逻辑并保持现有交互行为。

## 二、新 FBX 已验证的数据

- 网格对象：`Hydrangea_AR`
- 骨架对象：`Hydrangea_DroopRig`
- 顶点：679,081
- 边：1,293,110
- 面：619,904
- UV0：`UVMap`
- 顶点色：`PetalRandom`，`CORNER / BYTE_COLOR`
- 骨骼总数：69
  - `Root`：1
  - `DroopLeaf_001` 至 `DroopLeaf_063`：63
  - `DroopHead_01` 至 `DroopHead_05`：5
- 所有顶点均具有蒙皮权重
- FBX 不包含 Action、Animator Clip 或自动播放时间轴动画

Shape Key 顺序：

1. `Basis`
2. `Sprout`
3. `Leafing`
4. `Bud`
5. `HalfBloom`
6. `Bloom`

Unity 中 Basis 通常不作为可控制 Blend Shape 显示，因此预计 `SkinnedMeshRenderer.sharedMesh.blendShapeCount == 5`，可控制名称为：

`Sprout、Leafing、Bud、HalfBloom、Bloom`

导入后必须通过 `GetBlendShapeName()` 实际回读确认，不能依赖固定索引。

材质槽顺序：

1. `M_Hydrangea_Stems`
2. `M_Hydrangea_Leaves`
3. `M_Hydrangea_Petals`
4. `M_Hydrangea_Centers`

当前项目已有外部材质：

- `Assets/Flower1/Material/M_Unity_Hydrangea_Stems.mat`
- `Assets/Flower1/Material/M_Unity_Hydrangea_Leaves.mat`
- `Assets/Flower1/Material/M_Unity_Hydrangea_Flowers.mat`

优先复用现有材质映射：Stems 对应 Stems、Leaves 对应 Leaves、Petals 对应 Flowers。Centers 暂时也可对应 Flowers，但必须先检查旧模型当前的第四材质槽或中心花蕊显示方式；如果已有更准确的中心材质，应复用已有设置，不要擅自重做贴图或 Shader。

## 三、Unity FBX Import Settings

导入新 FBX 后设置：

### Model

- Import BlendShapes：开启
- Import Cameras：关闭
- Import Lights：关闭
- Preserve Hierarchy：开启（如果当前 Unity 版本提供）
- Read/Write：默认关闭；只有代码确实需要读写 Mesh 时才开启
- 不要开启网格压缩，避免本阶段引入 Shape Key 精度变化
- 不要重新计算会破坏现有显示的材质、顶点色或切线设置

### Rig

- Animation Type：`Generic`
- Avatar Definition：`Create From This Model`
- Optimize Game Objects：关闭

`Optimize Game Objects` 必须关闭，否则 `DroopLeaf_*` 和 `DroopHead_*` 可能不会作为可访问 Transform 保留，运行时控制会失效。

### Animation

- Import Animation：关闭
- 不创建默认 Animator Controller
- 不让模型在进入 Play Mode 后自动播放整段生长动画

### Materials

- 使用 External Materials 或 Importer 的 Remapped Materials 指向现有 `.mat`
- 不要把 FBX 内部 sub-asset 材质直接当作外部材质保存；如果 Unity 报“is a sub-asset of ... and cannot be used as an external material”，应在 Importer 中进行 Remap，或先 Extract Materials 后再引用
- 保持四个材质槽顺序不变

## 四、实现方式

生长阶段继续使用 Blend Shape；下垂使用骨骼。不要创建名为 `Droop` 的 Blend Shape，也不要修改现有五个生长 Blend Shape。

原因：早期生长阶段把叶片和花朵压缩到接近零尺寸。单一相对 Droop Shape Key 与这些阶段叠加会把隐藏顶点拉离生长原点，出现爆点或双重变形。骨骼在 Blend Shape 之后作用，能够安全叠加。

请优先新增独立组件，例如：

`Assets/Scripts/HydrangeaDroopRigController.cs`

不要把下垂逻辑硬塞进已有生长脚本，除非读取现有代码后确认统一控制器更符合项目结构。独立组件至少需要：

- 自动查找并缓存所有 `DroopLeaf_` 骨骼
- 自动查找并缓存所有 `DroopHead_` 骨骼
- 缓存每根骨骼的初始 `localRotation`
- 提供 `SetDroop(float value01)`，输入范围为 0 到 1
- 每次都从初始旋转计算，不能在当前旋转上重复累加
- 支持平滑过渡到目标值
- 与任意 Blend Shape 权重同时工作

核心旋转逻辑应等价于：

```csharp
leaf.localRotation = leafRestRotation
    * Quaternion.AngleAxis(-7.0f * droop01 * leafStageScale, Vector3.right);

head.localRotation = headRestRotation
    * Quaternion.AngleAxis(-1.25f * droop01 * headStageScale, Vector3.right);
```

其中：

- 叶片最大下垂角：7°
- 花序最大下沉/倾斜角：1.25°
- 推荐常态交互不足值：0.25 至 0.30
- 0 表示完全恢复
- 1 表示测试用最强效果，不建议作为日常默认值

由于 Sprout 阶段的叶片顶点全部压缩到接近一个点，而 v2 使用叶柄平滑权重，Unity 必须同时应用以下阶段系数，避免零尺寸网格被不同权重拉开：

| 阶段 | leafStageScale | headStageScale |
|---|---:|---:|
| Sprout | 0.00 | 0.00 |
| Leafing | 0.40 | 0.00 |
| Bud | 0.70 | 0.55 |
| HalfBloom | 0.90 | 0.85 |
| Bloom | 1.00 | 1.00 |

如果在相邻生长阶段之间做权重插值，阶段系数也应在相邻两行之间线性插值。不要在 Sprout 阶段直接施加完整 7° 叶骨旋转。

FBX 使用 Unity 轴向导出参数 `-Z Forward / Y Up`。理论上负 X 旋转应与 Blender 验证方向一致；仍需在 Unity 中实机确认。如果所有叶片统一向上，只允许把角度符号从负改为正，不要改变骨骼权重、Shape Key 或网格拓扑。

## 五、生长阶段控制

继续通过名称查找 Blend Shape：

```csharp
int index = renderer.sharedMesh.GetBlendShapeIndex("Bloom");
renderer.SetBlendShapeWeight(index, 100f);
```

不要硬编码索引。切换阶段时，先明确当前项目采用哪一种逻辑：

- 离散阶段：目标阶段 100，其余阶段 0
- 相邻阶段插值：只在相邻两个阶段间交叉过渡

保持项目现有逻辑，不要在这次集成中重写整个生长系统。Droop 必须能在以下任意状态上独立叠加：

- Sprout
- Leafing
- Bud
- HalfBloom
- Bloom

## 六、建议的交互接口

至少暴露：

```csharp
public void SetDroop(float value01);
public void SetDroopTarget(float value01);
public void RestoreFromDroop();
```

如果项目已有“互动值、浇水值、活力值”系统，应把数值映射放在上层状态脚本中，例如：

```csharp
float droop = Mathf.InverseLerp(healthyThreshold, ignoredThreshold, lackOfInteraction);
droopController.SetDroopTarget(Mathf.Clamp01(droop) * 0.30f);
```

不要让控制器自行猜测游戏状态，也不要使用 Animator 播放 Droop。

## 七、Prefab 与场景集成

1. 先找到当前场景中旧绣球花实例对应的 Prefab 或模型引用。
2. 不直接破坏旧 Prefab；创建一个新的 DroopRig Prefab 或 Prefab Variant。
3. 将新 FBX 的模型层级放入该 Prefab。
4. 重新连接现有三套材质，并确认第四槽映射。
5. 添加 `HydrangeaDroopRigController`。
6. 将现有生长阶段控制器引用改为新对象，但保留旧 Prefab 作为回退。
7. 检查 AR 放置、缩放、旋转、碰撞体和交互按钮引用是否仍然正确。

## 八、必须执行的验证

### 导入验证

- 场景中只有一个目标 `SkinnedMeshRenderer`
- Blend Shape 数量为 5，并打印实际名称
- 找到 63 个 `DroopLeaf_*` 和 5 个 `DroopHead_*`
- 材质槽数量为 4，顺序正确
- 花瓣随机颜色仍然存在
- 叶片透明裁切/透明度贴图仍然正常
- Play Mode 中没有自动播放 Animator 动画

### 组合验证

分别测试五个阶段在下列 Droop 值下的显示：

- 0
- 0.25
- 0.30
- 1.0

检查：

- 无爆点、飞散顶点、异常拉伸
- 叶片整体向下而不是向上
- 花序只有极轻微下沉
- 主茎、花盆和整体比例不变
- 切换阶段时 Droop 不被重置，除非上层逻辑明确要求
- 从 Droop 1 恢复到 0 后，骨骼旋转与初始值一致
- 连续多次设置 Droop 不产生旋转累积

### Bounds 与 AR 验证

骨骼变形可能超出原始 SkinnedMeshRenderer Bounds。先开启 `Update When Offscreen` 做诊断；如果这样能解决摄像机边缘消失，生产版本应扩大 `localBounds` 约 8% 至 10%，再关闭 Update When Offscreen，避免长期性能开销。

在目标 AR 设备上检查帧率。当前模型为 679,081 顶点，明显偏重；本次不允许减面或改拓扑，性能优化必须作为后续独立任务处理。

## 九、完成后报告

请明确报告：

- 新 FBX 在 Unity Assets 中的完整路径
- 新建或修改了哪些 `.cs`、Prefab、Scene、`.meta` 文件
- Unity 实际识别到的 Blend Shape 名称和数量
- 实际识别到的叶片骨骼、花序骨骼数量
- 四个材质槽最终映射
- 五阶段与 Droop 0/25/30/100 的测试结果
- AR 场景是否正常显示、是否出现 Bounds 裁切
- 是否保留旧模型和旧 Prefab 作为回退
- 未解决风险，尤其是移动端性能风险

如果导入后 Shape Key、骨骼或材质槽数量不符合本文记录，请先停止场景替换，诊断导入设置或资源损坏，不要通过修改原 FBX、删减骨骼或重建拓扑强行绕过。
