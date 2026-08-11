# BloomPot AR：Hydrangea WiltRig v3 Unity 交接

本文件对应 `Hydrangea_AR_Interactive_WiltRig_v3.fbx`。不要用 v3 覆盖或删除 DroopRig v2；请作为新资源导入和集成，以便随时回退。

## 给 Unity 项目对话的直接执行指令

你现在负责 `E:\Download\unity\project\BloomPot AR` 中 Hydrangea WiltRig v3 的实际集成。请先完整读取本交接文档，再读取当前 Unity 项目的场景、Prefab、生长阶段控制脚本、材质和 `Assets/Flower1` 资源结构。不要只复述方案；在确认现有引用后直接实施，并保留 v2 作为回退。

必须理解的核心变化：v3 不是一个 `Droop` Blend Shape，也不能继续使用 v2 的固定本地 X 轴旋转脚本。Blender 已保留五个成长 Blend Shape，并新增 110 根固定 bind skeleton 骨骼；每个成长阶段的真实下垂枢轴、旋转轴和系数保存在 `Hydrangea_WiltRig_v3_Pivots.json`。Unity 必须根据 JSON，以 bindpose 为基准重建模型空间变形矩阵，才能同时保证 Leafing、Bud、HalfBloom 和 Bloom 的连接点不脱离。

请依次执行：

1. 检查现有 Unity 项目和 v2 集成逻辑，记录将要修改的 Prefab、脚本和材质引用。
2. 将 v3 FBX 和 Pivot JSON 作为新资源放入 `Assets/Flower1`，不要覆盖或删除旧 FBX、v2 Prefab、材质和贴图。
3. 按本文第 6 节设置 FBX Importer，确认 5 个可控 Blend Shape、110 根骨骼、4 个材质槽。
4. 复用现有外部材质，确认叶片 Alpha Clipping、花瓣贴图和 `PetalRandom` 顶点色正常。
5. 新建 `HydrangeaWiltRigV3Controller.cs`，严格按本文第 7 节读取 JSON、计算 `Dlocal / Dtotal`、从 bindpose 重建骨骼目标矩阵。禁止累计 Transform。
6. 接入当前生长阶段控制器：先设置 Blend Shape，再应用对应阶段的 Wilt 矩阵；相邻成长阶段过渡时同步插值阶段数据。
7. 创建新的 v3 Prefab 或 Prefab Variant，不直接破坏旧 Prefab。
8. 执行五阶段 × `wilt 0 / 0.3 / 0.6 / 1.0` 共 20 组测试，并检查恢复、Bounds、材质和 AR 真机性能。
9. 最后报告修改文件、Importer 实际识别结果、材质映射、20 组结果、Bounds 状态和剩余风险。

如果 FBX 导入结果不是 679,081 顶点、5 个可控 Blend Shape、110 根骨骼和 4 个材质槽，请停止 Prefab 替换并先诊断，不要通过修改拓扑、删除骨骼或重建 Shape Key 绕过。

## 1. 为什么 v3 与原计划不同

原计划是用同一套固定骨骼直接旋转叶片。检查 v2 后确认，五个成长 Shape Key 会让叶根、枝条分叉和花头连接点在模型空间中移动很远；固定在 Bloom 的骨骼枢轴无法同时落在 Leafing、Bud、HalfBloom 和 Bloom 的真实连接点上。这正是 v2 在 Leafing 仍可能轻微脱离的原因。

v3 因此采用：

```text
5 个成长 Blend Shape
+ 一套固定 bind skeleton
+ 每阶段枢轴/轴向 JSON
+ Unity 运行时模型空间矩阵控制
```

它不是五套额外网格，也没有增加五套 Wilt Shape Key。JSON 只有约 365 KiB；FBX 仍约 63.30 MiB。运行时只需计算 110 个小矩阵，不会复制 679,081 顶点。

## 2. Blender 输出文件

- FBX：`E:\Download\blender\AR项目盆栽建模\绣球花\Unity_AR_Export\Hydrangea_AR_Interactive_WiltRig_v3.fbx`
- 源文件：`E:\Download\blender\AR项目盆栽建模\绣球花\Unity_AR_Export\Hydrangea_AR_Interactive_WiltRig_v3_Source.blend`
- 阶段枢轴：`E:\Download\blender\AR项目盆栽建模\绣球花\Unity_AR_Export\Hydrangea_WiltRig_v3_Pivots.json`
- 源验证：`E:\Download\blender\AR项目盆栽建模\绣球花\Unity_AR_Export\Hydrangea_WiltRig_v3_Verification.json`
- FBX 回读验证：`E:\Download\blender\AR项目盆栽建模\绣球花\Unity_AR_Export\Hydrangea_WiltRig_v3_FBX_Roundtrip.json`
- 20 组检查图：`E:\Download\blender\AR项目盆栽建模\绣球花\Unity_AR_Export\Hydrangea_WiltRig_v3_20Case_Review.png`

文件大小：

| 文件 | 字节 |
|---|---:|
| v3 Source `.blend` | 47,465,930 |
| v3 `.fbx` | 66,373,084 |
| Pivots `.json` | 374,084 |

## 3. 不变的数据

- 顶点：679,081
- 边：1,293,110
- 面：619,904
- UV0：`UVMap`
- 顶点色：`PetalRandom`，`CORNER / BYTE_COLOR`
- 每顶点最多 2 个骨骼影响，权重归一化
- 不使用 Bone Envelope

Shape Key 顺序：

1. `Basis`
2. `Sprout`
3. `Leafing`
4. `Bud`
5. `HalfBloom`
6. `Bloom`

材质槽顺序：

1. `M_Hydrangea_Stems`
2. `M_Hydrangea_Leaves`
3. `M_Hydrangea_Petals`
4. `M_Hydrangea_Centers`

## 4. 骨骼结构

总数 110：

| 前缀/名称 | 数量 | 用途 |
|---|---:|---|
| `Root` | 1 | 全局根 |
| `WiltStem_01` | 1 | 主茎轻微弯曲 |
| `WiltBranch_Main_01..05` | 5 | 五条主侧枝 |
| `WiltBranch_Hub_01..05` | 5 | 花头分叉层级枢纽，不自行旋转 |
| `WiltBranch_Sub_01_01..05_06` | 30 | 每个花头 6 条末端 Sub-branch |
| `WiltLeaf_001..063` | 63 | 叶片下垂与缩小 |
| `WiltHead_01..05` | 5 | 花序轻微倾斜 |

层级：

```text
Root
└─ WiltStem_01
   └─ WiltBranch_Main_XX
      ├─ WiltLeaf_XXX
      └─ WiltBranch_Hub_XX
         ├─ WiltBranch_Sub_XX_YY
         └─ WiltHead_XX
```

叶片、Sub-branch 和花序会继承父枝弯曲。所有 Rest 骨统一为平行方向，以降低 FBX Pre/Post Rotation 回读误差；实际旋转轴不能从骨骼可视方向猜测，必须读取 JSON 中的 `axisUnity`。

## 5. 最大效果与阶段系数

| 部位 | 最大角度/缩放 |
|---|---:|
| 主茎 `WiltStem` | 2.5° |
| 主枝 `WiltBranch_Main` | 8° |
| Hub | 0° |
| Sub-branch | 6° |
| 叶片 | 18° |
| 花序 | 4° |
| 叶片最小均匀缩放 | 0.94 |

阶段系数：

| 阶段 | stem | branch | subBranch | leaf | head | leafScale |
|---|---:|---:|---:|---:|---:|---:|
| Sprout | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 |
| Leafing | 0.15 | 0.20 | 0.00 | 0.35 | 0.00 | 0.25 |
| Bud | 0.35 | 0.45 | 0.45 | 0.65 | 0.45 | 0.55 |
| HalfBloom | 0.65 | 0.75 | 0.75 | 0.85 | 0.80 | 0.80 |
| Bloom | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |

输入响应不是线性角度：

```csharp
float response = Mathf.Pow(Mathf.Clamp01(wilt), 1.35f);
```

因此 `wilt = 0.3` 很轻，`0.6` 明显，`1.0` 为作品集演示强度。

## 6. Unity 导入设置

### Model

- Import BlendShapes：开启
- Import Cameras：关闭
- Import Lights：关闭
- Preserve Hierarchy：开启
- Mesh Compression：Off
- Read/Write：控制器不读写 Mesh 时关闭
- 法线/切线保持当前已验证设置，不要擅自重算

### Rig

- Animation Type：Generic
- Avatar Definition：Create From This Model
- Optimize Game Objects：关闭

必须关闭 Optimize Game Objects，否则 110 个骨骼 Transform 可能无法被脚本访问。

### Animation

- Import Animation：关闭
- 不添加 Animator，不播放 baked 时间轴动画

### Materials

- 使用 Importer 的 Remapped Materials 连接现有外部 `.mat`
- 不把 FBX sub-asset 材质直接拖成外部材质
- 保持四个材质槽顺序不变
- 重新确认叶片 Alpha Clipping 和花瓣 `PetalRandom` 顶点色 Shader

## 7. Unity 控制算法（必须按 JSON 矩阵执行）

不要只修改骨骼 `localRotation.x`，也不要把骨骼 Transform 自身位置当作阶段枢轴。每个阶段的真实 `pivotUnity` 和 `axisUnity` 都在 JSON 中。

每根骨骼的局部变形矩阵：

```text
Dlocal = T(pivotUnity)
       * R(axisUnity, maxAngleDegrees * angleCoefficient * response)
       * S(uniformScale)
       * T(-pivotUnity)

uniformScale = 1 - (1 - minimumScale) * scaleCoefficient * response
```

按 JSON 的父子顺序累计：

```text
Dtotal[Root] = Identity
Dtotal[bone] = Dtotal[parent] * Dlocal[bone]
```

然后使用 `SkinnedMeshRenderer.sharedMesh.bindposes` 求目标骨骼世界矩阵：

```text
targetBoneWorld = renderer.localToWorldMatrix
                * Dtotal[bone]
                * inverse(bindpose[boneIndex])
```

再按父级顺序转成局部矩阵并分解为 `localPosition / localRotation / localScale`。叶片缩放必须围绕 JSON 枢轴进行，不能直接写 `leaf.localScale = 0.94`。

关键约束：

- 每次从 bindpose/JSON 重新计算，禁止在上一帧 Transform 上累计
- 生长 Blend Shape 权重先设置，Wilt 骨骼在 `LateUpdate` 或同一控制流程的后半段应用
- 离散阶段直接选对应 JSON stage
- 若在相邻阶段间交叉过渡，同时插值 pivot、归一化后的 axis 和各系数
- JSON 已转换为 Unity 坐标：`Unity(x,y,z) = Blender(x,z,-y)`
- 旋转使用 JSON 的正角度；`axisUnity` 已包含朝下方向，不要再人工反号
- Animator 必须关闭或不得写这些骨骼

建议新建独立组件：

```text
Assets/Scripts/HydrangeaWiltRigV3Controller.cs
```

建议接口：

```csharp
void SetGrowthStage(string stageName);
void SetGrowthTransition(string from, string to, float t);
void SetWilt(float value01);
void SetWiltTarget(float value01);
void RestoreHealthy();
```

## 8. 已完成验证

源文件 5 阶段 × wilt `0 / 0.3 / 0.6 / 1.0` 共 20 组：

- 所有坐标有限，无飞点/爆点
- 叶尖向下比例：100%
- 最坏连接间隙增量：`4.185736e-5 m`（约 0.0419 mm）
- Leafing 最坏连接间隙增量：`4.951133e-7 m`
- 最大枢轴继承误差：`9.848304e-7 m`
- wilt 1 恢复到 0 的最大误差：`5.364418e-7 m`

FBX 导回空 Blender 场景后：

- 拓扑完全一致
- 6 个 Shape Key 坐标逐顶点最大误差：0
- 权重哈希完全一致
- 骨骼数量与父子层级完全一致
- 4 材质槽、UV0、`PetalRandom` 完全一致
- 20 组最大位移与源文件逐组差值：0
- FBX 静止蒙皮五阶段最大逐顶点误差：`1.862109e-6 m`，满足 `< 1e-5 m`

## 9. Unity 内仍需执行的工作

1. 将 v3 FBX 作为新资源复制到 `Assets/Flower1/`，不要覆盖 v2。
2. 将 Pivot JSON 放入 `Assets/Flower1/Data/`，建议作为 `TextAsset` 加载。
3. 按第 6 节设置 Importer，并重新映射四个外部材质。
4. 编写 v3 矩阵控制器；不能继续使用 v2 的固定 X 轴骨骼旋转脚本。
5. 通过名称回读 5 个 Blend Shape，不要硬编码索引。
6. 测试 20 组组合，并重点观察 Leafing 叶根、Bloom 满枯萎、Sub-branch 与花头连接。
7. 从 wilt 1 恢复到 0，确认 Transform 不累计。
8. 检查 `SkinnedMeshRenderer.localBounds`。如相机边缘裁切，先用 Update When Offscreen 诊断，再扩大 Bounds 后关闭该选项。
9. 在目标 AR 设备测帧率。679k 顶点仍是主要性能风险；本次没有减面，优化必须作为独立任务。

如果 Unity 导入后不是 110 根骨骼、5 个可控 Blend Shape 或 4 个材质槽，请停止 Prefab 替换并检查导入设置，不要修改 FBX 拓扑来绕过。
