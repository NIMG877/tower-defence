# 统一"被查看实体"重构设计（LevelMessage 选中逻辑）

日期：2026-09-05
状态：已确认（方案 A）

## 背景与问题

`LevelMessageSelectionContext` 把两个正交维度塞进两个平行槽位：

1. **被查看的实体是谁**（EntityModule 的数据来源）——拆成 `SelectedEntity`（部署后）与 `SelectedPlaceData` 里的碎片（部署前：`EntityStats`+`EntityVision`+`EntityData` 三件散装）；
2. **当前在做什么操作**（放卡预览/拖拽朝向/场上查看）——`Orientation`/`PreviewPosition` 等（这部分无恙）。

后果：EntityModule 每个显示特性都要自己决定"从哪个槽取数"——`GetCurrentEntityData`/`GetCurrentEntityStats` 手写双源 fallback，`GetCurrentWorldRange` 三路分支，技能/天赋页有部署前回退，操作区/技能范围仅认场上实体，**buff 页漏写回退**（待部署干员 buff 不显示的 bug，结构性必然）。

关键事实：部署前的"被查看实体"本来就存在——`LevelMessagePlaceData.Initialize` 取的池样本实体（`pool.GetEntity()`）就是之后 `CallOut` 派出的同一个 GameObject，buff 列表与部署后完全一致；现在只摘了它的 `Stats`/`Vision` 碎片，Entity 本体被丢弃。

## 设计

### 1. LevelMessageSelectionContext（数据模型）

```csharp
public LevelMessagePlaceData SelectedPlaceData { get; private set; } // 保留：卡片归属（数量/费用/技能索引）
public Entity Inspected { get; private set; }      // 唯一被查看实体：部署前=样本实体，部署后=场上实体
public bool IsDeployed { get; private set; }       // Inspected 是否在场
public bool HasSelection => Inspected != null;     // 替代 SelectedStaticEntityID.HasValue 门控
public EntityID? SelectedStaticEntityID => Inspected?.EntityData.ID;  // 派生属性，不再独立维护
// Orientation / PreviewPosition / HasPreviewPosition 原样保留（部署预览专用态）
```

- `SelectPlace(placeData)`：`Inspected = placeData.Sample`，`IsDeployed = false`，预览态复位
- `SelectEntity(entity)`：签名去掉冗余 entityId 参数（调用方传的即 `entity.EntityData.ID`），`Inspected = entity`，`IsDeployed = true`
- **`SelectedEntity` 属性消失**——"仅场上"特性改用 `IsDeployed` 门控 + 读 `Inspected`
- `Clear()`：全部复位

### 2. LevelMessagePlaceData

- 新增 `public Entity Sample { get; private set; }`，在 `Initialize` 现有取池样本处一并赋值
- 删除 `EntityStats` / `EntityVision` 公开碎片属性（消费方改读 `Inspected.Stats` / `Inspected.Vision`，同一对象）
- `CalculateCost` 内部改读 `Sample.Stats.CostS`

### 3. LevelMessageEntityModule

- 删除 `GetCurrentEntityData` / `GetCurrentEntityStats`，调用点直接读 `_context.Inspected`
- `GetCurrentWorldRange` 三分支保留结构，数据源统一为 Inspected：
  1. `IsDeployed` → `Inspected.Vision.Range`（实时）
  2. 放置预览（Orientation/Preview 就位）→ `Inspected.Vision.BaseRange` 经 `RangeCaculator` 旋转
  3. 卡片无预览 → `Inspected.EntityData.VisionRange` 原样（行为不变）
- 操作区 `ShowOperator`、技能范围按钮、Skill 范围分支：门控从 `SelectedEntity == null` 改为 `!IsDeployed`
- `ShowBuffDetails` 直接读 `Inspected.buffController.Buffs`——待部署 buff 显示由此自然成立（样本实体在池内 Dormancy 态，列表恰好只含跨回收的 level buff）
- 技能/天赋页部署前预览**保留走卡片配置**（`PlaceData.SkillIndex` / `EntityData.Talents`），不改读样本实体的 runner：样本实体 `SelectedSkillIndex` 恒为默认 0，不一定是编队选中技能。语义边界：**部署前从样本实体只读与部署无关的状态（Stats 终值 / Vision / level buff），技能运行时态仍走卡片**

### 4. LevelMessagePanel

- `SwitchToViewAfterSet(entityId, entity)` → `SwitchToViewAfterSet(Entity entity)`
- `OnResume` / `UpdateCurrentState` 的 ViewAfterSet 检查简化为 `!_selection.Inspected.Stats.IsActive`（ViewAfterSet 态下 IsDeployed 恒真，原 null 检查为不可达防御代码，按项目"禁用兜底掩盖问题"原则移除）

### 5. 行为变化（唯一一处，即本次目标）

部署前点开待部署干员详情，Buff 页显示 level buff（如 `catap_t1_cost / Cost: -1.000`），与部署后一致。其余一切行为不变。

## 错误处理

- `Inspected == null` 仅当无选中；所有消费点沿用"未选中即 return/隐藏"门，统一为 `HasSelection`
- 样本实体契约：池化实体必经 `CreateNewEntity → PreWarm`，`buffController`/`Stats`/`Vision` 必非空——不加防御性判空，契约破坏就让它崩

## 测试

- `LevelMessageSelectionContext` 为纯 POCO：新增 EditMode 测试（SelectPlace 后 `Inspected==Sample && !IsDeployed`；SelectEntity 后反之；Clear 复原；`SelectedStaticEntityID` 派生正确）
- PlaceData/EntityModule 依赖 Unity 视图对象，走 PlayMode 手动清单：进关→点卡片看 buff 页→拖放部署→场上选中看操作区/技能范围→回收→再部署→各页无回归
