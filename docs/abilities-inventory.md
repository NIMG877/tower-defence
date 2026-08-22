# Abilities 全量盘点

> **文档状态：**2026-07-14 架构分析快照。当前 Ability 资产结构、执行
> 语义和 op 注册规范见 [`Ability Steps`](ability-steps.md)；component-backed
> op 参数见 [`Skill Components`](skill-components/README.md)。本文保留行为
> 盘点和耦合分析，不作为当前资产编写或 operation 扩展指南。
>
> 相关：[[ability-migration-trigger-limit]] [[project-subsystem-map]] [[coupling-audit-2026-07-14]]

---

## 第一部分：17 个旧 Skill/Talent 子类

### 1. Eyjafjalla/Skill1（火山爆发·攻速强化）
- **机制**：SkillBegin 给自己 AttackSpeed+120(AddFlat) buff + 调 Talent1.Skill1Open()；SkillEnd 销毁 buff + 调 Skill1End()。
- **旧 API**：buffController.CreateBuff/DestroyBuff、Talent1 跨技能直调。
- **跨时间**：无。**跨技能联动**：强（调 Talent1）。
- **可迁性**：纯事件响应、一次性。但"通知 Talent1 开关"撞**能力间编排缺口**（ability 无法互调，得靠 Blackboard 信号桥接）。

### 2. Eyjafjalla/Skill2（范围攻击+强制生成气泡）
- **机制**：SkillBegin→Talent1.Skill2Open()释放气泡目标 + TryToAttackWithAnimation(空目标+override动画) + 订阅 OnAttackSuccessfully + 设 4 个固定目标点 (1,1)(1,2)(2,2)(2,1)。攻击成功时往 4 点各发一颗 Bullet→子弹销毁回调 OnBulletDestroy(pos)→Talent1.Skill2SetBubble(pos) 在那生成气泡。SkillEnd→Talent1.Skill2End()。
- **旧 API**：AttackBase.TryToAttackWithAnimation/OnAttackSuccessfully、new Bullet(...)、Talent1 直调。
- **跨时间**：子弹飞行→销毁回调，跨子弹生命周期。**跨技能联动**：强。
- **可迁性**：撞缺口——无 SpawnEntity、无"发子弹到固定坐标"、无子弹销毁事件驱动。

### 3. Eyjafjalla/Talent1（岩浆气泡·核心载体）
- **机制**（被动，永久挂载）：
  - 维护 `_bubbles` 列表 + `_lavaBubbleATKBuff`（自身 Attack% 随气泡数 *0.2 增长）。
  - 每次 OnAfterTakeDamage：RandomP(p) 概率在**目标位置**生成气泡(敌营)→刷新自身 Attack buff→给气泡加两个伤害率 buff(PhysicalDamageRate/MagicDamageRate *0.005 MulFinal)。
  - 气泡死亡：移出列表 + Explode(pos)（**延迟 0.367s** → 半径1.5 内敌人 siege 伤害 AttackS*3.7）→刷新 Attack buff→气泡清空则 ReleaseBubbleTarget。
  - SetBubbleTarget（Skill2/Skill1 关闭时）：override 攻击动画 + 订阅 OnBeforeTargetSelect（**把气泡插到目标列表最前**）+ OnAfterTakeDamage（打到气泡秒杀气泡）。
  - Skill1Open/End：技能1期间 p*=3 + 气泡不再成为目标。
- **旧 API**：EntityManager.SetMovableEntity、buffController.CreateBuff/SetBuffValues、am.AddOverride/RemoveOverride、EntitySelector_Radius、Stats.ApplyDamage、RandomHelper、attack/animation 事件订阅。
- **跨时间**：Explode 的 await 0.367s。**跨技能联动**：被 Skill1/Skill2 驱动。
- **可迁性**：全撞缺口——SpawnEntity+delay+目标列表注入+能力间编排，是 SlimeTalent1 缺口的升级版。

### 4. SlimeTalent1（死亡分裂召唤）
- **机制**：OnBeforeDieAnimation→在本体格子中心±0.24 随机位置、间隔 spawnGap 秒、循环生成 spawnNum 个 spawnEntityID 怪、继承路径(SetMoveParameters)。
- **跨时间**：async 循环 + await WaitForSeconds(spawnGap)，靠 LevelCtk 活。**跨技能联动**：无。
- **可迁性**：缺口清单首项——SpawnEntity+delay+trigger 驱动撑不起间隔生成（详见 ability-migration-trigger-limit 记忆）。

### 5. HeadSeterSkill1（头颅闪现）
- **机制**：SkillBegin（仅在非攻击/无可阻挡目标时）→按 IsDie 选 begin/begin_d 动画覆盖+切 Die 态+上异常状态0/3。SkillEnd→选 end/end_d 动画+切 Die 态+移除异常状态+**FlashMove**：沿当前路径段累计距离，闪现 moveDis 后落位+SetMoveParameters 更新路径指针。
- **旧 API**：am.AddOverride/TrySetState、buffController.AddAbnormalState/TryRemoveAbnormalState、MoveBase.CurrentSection/CurrentPathSerial/CurrentSectionSerial/CurrentPointSerial/SetMoveParameters、Movement.Position。
- **跨时间**：无。**跨技能联动**：被 HeadSeterTalent1 设 IsDie=true。
- **可迁性**：动画/异常状态可迁；**FlashMove 沿路径闪现**撞缺口——无"沿路径位移到指定距离"组件（位移/传送类缺口）。

### 6. HeadSeterTalent1（头颅投掷·凋灵召唤链一环）
- **机制**（被动）：Initialize 上异常状态2(定身)+订阅 OnAfterHurt：致命伤时自伤1点治疗(damageType3)+标记 _isDie+AddHurtable/AddSelectable+换"无头"动画套装+减速35%+设 Skill1.IsDie=true。FixedUpdate：半径1内找"凋灵基座"实体→移除定身+换"有头"动画+TryToAttack(基座)+订阅 OnBeforeTakeDamage：multiplyer=0(免伤)+_haveHead=false+**WitherPedestalTalent1.AddHead()**（跨实体调外部 Talent）+按 _isDie 决定 Die 或换攻击动画。
- **旧 API**：buffController.AddAbnormalState/TryRemoveAbnormalState/CreateBuff、am.AddOverride/RemoveOverrides/TrySetState、Stats.ApplyDamage/AddHurtable/AddSelectable、AttackBase.TryToAttack/OnBeforeTakeDamage、EntityManager.EntitySelector_Radius、Movement.Camp、跨实体 GetComponent<WitherPedestalTalent1>().AddHead()。
- **跨时间**：无（FixedUpdate 轮询）。**跨技能联动**：极强——驱动 WitherPedestalTalent1、被 HeadSeterSkill1 读 IsDie。
- **可迁性**：撞缺口——致命伤触发条件、动画形态切换可迁，但"找到特定命名实体并跨实体调方法""致命伤免伤+投头"是高度特化逻辑。

### 7. WitchSkill（喝药自疗）
- **机制**：SkillBegin（仅 CurrentHpRate<0.7 时）→override 攻击动画为 _drink + TryToAttack(自己) + 播放药水粒子 + 按医学效果设色。SkillEnd→停粒子 + MedicalEffect.TakeEffect_Single(自己,1,1,20)（治疗20）+移除动画覆盖。
- **旧 API**：am.AddOverride/RemoveOverride、AttackBase.TryToAttack、MedicalEffect.MedicalEffectLauncher（外部系统）、ParticleSystem。
- **跨时间**：无。**跨技能联动**：无。
- **可迁性**：动画/异常可迁；**治疗走 MedicalEffect 硬编码路径**（绕过 AttributeStore，见 coupling-audit P0 提及的同类问题）——治疗缺口。

### 8. WitchTalent（毒/伤二选一溅射）
- **机制**（被动）：Initialize 克隆攻击子弹/拖尾粒子到 TempContainer 并替换 AttackBase 的 BulletData。订阅 OnAfterTargetSelect：取首目标位置+半径1.414内敌+按 CaculateValue 算 poison/damage 权重（HP>425 计毒、<425 计伤、按优先级加权）。订阅 OnAttackAnimationBegin：按 poison>=damage 染毒色或伤色。订阅 OnAttackSuccessfully/OnAttackInterrupt：停药水粒子。订阅 OnBeforeTakeDamage：multiplyer=0(免伤)+按毒/伤调 MedicalEffect.TakeEffect_Radius(范围毒2/范围伤3)。
- **旧 API**：AttackBase._attackEffectData.BulletData、OnAfterTargetSelect/OnAttackAnimationBegin/OnAttackSuccessfully/OnAttackInterrupt/OnBeforeTakeDamage、EntityManager.EntitySelector_Radius、Combat.PriorityOrder、MedicalEffect、ParticleSystem/TrailRenderer。
- **跨时间**：无。**跨技能联动**：无（与 WitchSkill 同体但不互调）。
- **可迁性**：撞缺口——子弹粒子替换、毒/伤加权计算、范围 MedicalEffect，全是硬编码外部系统调用，最难迁。

### 9. BeefSkill（牛肉被吃）
- **机制**：SkillBegin 切 Idle+override Idle 动画。SkillEnd→本格范围内找 InteractableStatic 且 CharacterJob!=8→MCED.EntityEatSomething(它,40)→自身 Die()。
- **旧 API**：am.TrySetState、EntityManager.EntitySelector_Range、EntityData.CharacterJob、InteractableStatic、MCEnvironmentalDevice.MCED、Die。
- **跨时间**：无。**跨技能联动**：无。
- **可迁性**：撞缺口——"与静态实体交互/食物消耗"是关卡特定逻辑，无对应组件。

### 10. BeefTalent（牛肉定身）
- **机制**：Initialize 给自身上异常状态3(定身类)。
- **旧 API**：buffController.AddAbnormalState。
- **跨时间/联动**：均无。
- **可迁性**：✅ 可直接迁——单一 ApplyAbnormalState(trigger OnInitialize/PreWarm, abnormalType=3)。最简单的迁移候选。

### 11. WitherTalent1（凋灵·易伤+盾机制）
- **机制**（被动）：订阅 OnBeforeTakeDamage：若攻击方 Camp==2(玩家)→multiplyer=10（玩家攻击高倍易伤）。订阅 OnAfterTakeDamage：致命伤时按 HaveShield 标志——无盾自伤5000、有盾且血量低于半血-2500 阈值则自伤2500。
- **旧 API**：AttackBase.OnBeforeTakeDamage/OnAfterTakeDamage、Entity.Camp、Stats.ApplyDamage/CurrentHp/MaxHpS。
- **跨时间**：无。**跨技能联动**：HaveShield 被 WitherTalent3 设。
- **可迁性**：撞缺口——OnBeforeTakeDamage 改 multiplyer 可用 AttackEventValueModifier+条件；自伤可 ApplyDamage(self)；但"玩家阵营易伤""盾保护自伤阈值"需条件门控+跨 ability 状态(HaveShield)。

### 12. WitherTalent2（凋灵·恢复+爆炸）
- **机制**：Initialize 压血到0.001+上异常状态3/0/2+恢复动画+恢复特效。EnterRecoverMode(async)：多层 await 动画时长→血量 DOTween 渐变回满→切死亡爆炸动画+爆炸特效→按 EntitySelector_Radius + EntityR 距离分 2/3/4/5 倍档位 AOE+冲量→移除异常状态+调 WitherTalent3.HpCheck()。
- **旧 API**：am.AddOverride/RemoveOverrides/TrySetState/ResolveAnimationDuration/ResolveNamedAnimationDuration、Stats.CurrentHpRate、buffController.AddAbnormalState/TryRemoveAbnormalState、EntityManager.EntitySelector_Radius/EntityR、Stats.ApplyDamage、MoveBase.TryToAddImpulse、DOTween、Talent3 直调。
- **跨时间**：强——多层 await + DOTween OnComplete。**跨技能联动**：调 Talent3.HpCheck。
- **可迁性**：撞缺口——多段延时序列(类似 creeper 但更碎)、血量渐变、距离分档 AOE。最难迁之一。

### 13. WitherTalent3（凋灵·半血护盾）
- **机制**：HpCheck(async while 循环)：每帧 WaitForFixedUpdate 轮询 CurrentHpRate>0.5 直到降到半血→设 WitherTalent1.HaveShield=true+创建多属性 buff(防御/魔抗/攻击/攻速升、移速降)+激活护盾特效。
- **旧 API**：Stats.CurrentHpRate、buffController.CreateBuff、GetComponent<WitherTalent1>。
- **跨时间**：强——async while 轮询。**跨技能联动**：写 Talent1.HaveShield、被 Talent2 调。
- **可迁性**：撞缺口——血量阈值轮询守护(OnTick 能做但本体死后风险)、跨 ability 公共字段共享。

### 14. WitherPedestalTalent1（凋灵基座·集齐三头召唤）
- **机制**：Initialize 关闭所有头颅 Sprite+上异常状态3。外部调 AddHead() 逐个点亮头颅，集齐3个→Die()（凋灵召唤条件）。
- **旧 API**：buffController.AddAbnormalState、Die、SpriteRenderer。
- **跨时间**：无。**跨技能联动**：被 HeadSeterTalent1 跨实体调 AddHead。
- **可迁性**：撞缺口——计数器式状态推进+跨实体方法调用。

### 15. MachineTalent1（飞行机器·投射体跟随+寻路）
- **机制**：Initialize 创建移速 buff+分配全图 _bestOrientation 数组。ProjectEntity(被外部 WdslmSkill3 调)：接管目标移动跟随 _projectionRoot + EntityPassiveUpdate(async while：轮询投射位置+重算最佳朝向/视野)+MoveSpeedAD(async while：基于距离的 sqrt(dcc·dis·2) 加减速物理移速调节)+FindBestPoint/FindBestOrientation(全图双层 for 寻找覆盖最多敌人的朝向)。目标死亡→调传入 skill.SkillEnd()。
- **旧 API**：MoveBase(CurrentSection/CurrentPointSerial/CurrentPathSerial/AddTempTarget/SetMoveParameters/MoveMethod)、BuffController.CreateBuff/SetBuffValues、MapDataManager(MapSize/Tiles/RangeCaculator)、EntityManager.EntitySelector_Range、SlidersManager、LevelCtk、Stats.IsActive、Movement.Position、Vision.Range/BaseRange、SetOrientation、RandomHelper。
- **跨时间**：强——两个 async while 持续循环。**跨技能联动**：被 WdslmSkill3 调+反向调 skill.SkillEnd。
- **可迁性**：撞缺口——持续物理循环+全图遍历寻点+实体投射体跟随，全是硬编码 AI 逻辑，几乎无法数据驱动化。

### 16. WdslmSkill2（煽动叛变）
- **机制**：SkillBegin 全图(半径-1)选敌方单位+countPriority(atkp/surp/medp 三类权重)选最优目标+触发 OnBeforeSkillAttack：改目标阵营为己方+施 incite buff(攻击-45%、最大血量+650%)+异常状态2/3+移出静态列表+销毁 facing 影子+交 WdslmSkill3 处理。Initialize 即禁止 Skill3 恢复。
- **旧 API**：EntityManager.EntitySelector_Radius/RemoveEntityFromStaticList、buffController.CreateBuff/AddAbnormalState、Entity.Camp、AttackBase.OnBeforeAttack/TryToAttack/DamageType、am.AddOverride/RemoveOverride、Stats(AttackS/BaseAttackTimeS/DefS/MagicResistanceS/MaxHpS)、Vision.Range.Length、TempContainer.Find、SkilllRecoverForbid、GetComponent<WdslmSkill3>。
- **跨时间**：无。**跨技能联动**：强——与 Skill3 双向 SkilllRecoverForbid 互锁。
- **可迁性**：撞缺口——阵营叛变、优先级加权目标选择、跨 skill 恢复禁令互锁、销毁 facing 影子，全是特化逻辑。

### 17. WdslmSkill3（召唤飞行机器投送叛变单位）
- **机制**：SkillBegin→上异常状态3/2/0+Start/Idle 动画覆盖+切 Die 态+SummonMachine(async：await Start 动画时长→SetMovableEntity 召唤机器+SetMoveParameters+眩晕+MachineTalent1.ProjectEntity(TargetEntity,this) 投送)。SkillEnd→机器与目标死亡(若目标存活)+移除动画覆盖+切死亡动画+移除异常状态+释放 Skill2 恢复禁令+禁止自身恢复。
- **旧 API**：buffController.AddAbnormalState/TryRemoveAbnormalState、am.AddOverride/RemoveOverrides/TrySetState/ResolveNamedAnimationDuration、EntityManager.SetMovableEntity、MoveBase.SetMoveParameters、MachineTalent1.ProjectEntity、Stats.IsActive、Entity.Die、LevelCtk、Movement.Position/MoveBase.Current*Serial、GetComponent、SkilllRecoverForbid。
- **跨时间**：async await 动画时长。**跨技能联动**：极强——与 Skill2 双向+驱动 MachineTalent1。
- **可迁性**：撞缺口——召唤+动画时长延时+跨 skill 编排链(Skill2→Skill3→MachineTalent1)。

---

## 第二部分：24 个新 AbilitySystem 组件

> 全部 POCO，继承 `AbilityComponentBase`，`[RegisterComponent("Name")]` 反射注册，`OnInit` 取 `Func<T>` 懒参数，`OnTrigger` 执行，`OnTick`/`OnTeardown` 按需。组件间只通过 `ctx.sharedBlackboard` 通信。trigger 事件见 `AbilityEvents.cs`（1=PreWarm,2=AbilityBegin,3=AbilityEnd,6=BeforeAttack,9=AfterTakeDamage,10=AttackSuccessfully,11=AttackInterrupt,16=Tick 等）。

### 伤害类
| 组件 | 职责 | 关键参数 | BB读写 | Tick/Teardown |
|---|---|---|---|---|
| **ApplyDamage** | 对目标 Stats.ApplyDamage，attack/fixed 基础值+倍率+穿透+伤害类型+施加类型 | targetMode,baseValueMode,baseValue,multiplier,defPenetrate,mgrPenetrate,damageType,applyType | 读 blackboardKey(List<Entity>) | 无 |
| **AttackEventValueModifier** | 通用 DamageEventBase 字段改写器(fields/values/methods 三 CSV 平行，mult/add/set/div) | fields,values,methods | 改 currentEvent 字段 | 无 |
| **ChargeAttackDamageModifier** | 订阅 ChargeAttack.OnBeforeChargeTakeDamage 乘倍率 | multiplier | 读 blackboardKey | Teardown 取消订阅 |

### Buff 类
| 组件 | 职责 | 关键参数 | BB读写 | Tick/Teardown |
|---|---|---|---|---|
| **ApplyBuff** | 创建 buff(normal一次性/aura持续同步)，attributes/ops/magnitudes 三 CSV 构造 Modifier[] | attributes,ops,magnitudes,buffId,buffTime,mode(normal/aura),isWhiteList | 读 blackboardKey；写 outputTarget/outputBuff 平行累积 | Teardown(aura) 销毁记录 |
| **DestroyBuff** | 消费 ApplyBuff 写的 (target,buff) 对，销毁 buff，清空 key | inputTarget,inputBuff | 读 inputTarget/inputBuff | 无 |

### 异常状态类
| 组件 | 职责 | 关键参数 | BB读写 | Tick/Teardown |
|---|---|---|---|---|
| **ApplyAbnormalState** | 施加异常状态0-3(normal/aura同步) | mode,abnormalTypes(CSV),abnormalTimes(CSV),toSelf | 读 blackboardKey；写 outputTarget/outputState | Teardown(aura) 移除 |
| **DestroyAbnormalState** | 消费 (target,state) 对，移除异常状态，清空 key | inputTarget,inputState | 读 inputTarget/inputState | 无 |

### 动画类
| 组件 | 职责 | 关键参数 | BB读写 | Tick/Teardown |
|---|---|---|---|---|
| **ApplyAnimationOverride** | once 一次性状态切换 / override 持久注册(带优先级) | mode(once/override),slots,resources,priority,state,forceChange,moveBranch,attackBranch | 读 blackboardKey；写 outputKey(List<AnimationOverrideRecord> 仅override) | 无 |
| **RemoveAnimationOverride** | 消费 AnimationOverrideRecord 列表撤销持久覆盖 | inputKey,blackboardKey | 读 inputKey/blackboardKey | 无 |

### 目标选择类
| 组件 | 职责 | 关键参数 | BB读写 | Tick/Teardown |
|---|---|---|---|---|
| **EntitySelector** | self/blackboard/eventTarget 为中心，radius/ring/range/vision/subject/eventTarget 模式选实体，same/opposing/both 阵营过滤 | subjectMode,selectionMode,campRelation,radius,minRadius,squareLength,force,excludeSubjects | 读 subjectBlackboardKey；写 outputEntitiesKey/outputCountKey | 无 |
| **EntityFilter** | 订阅 OnBeforeTargetSelect，OR组AND条件过滤候选(monsterStatus/camp/currentHp/currentHpRate/maxHp 字段) | fields,ops,values,groups | 读 blackboardKey | Teardown/AbilityEnd 取消订阅 |

### 攻击行为覆盖类
| 组件 | 职责 | 关键参数 | BB读写 | Tick/Teardown |
|---|---|---|---|---|
| **AttackBehaviorOverride** | 覆盖 damageType/targetPriority(快照原值) | damageType,targetPriority | 读 blackboardKey；写 outputKey(List<AttackBehaviorSnapshot>) | 无 |
| **AttackBehaviorRestore** | 恢复快照 | inputKey,blackboardKey | 读 inputKey/blackboardKey | 无 |
| **AttackRangeOverride** | 替换 Vision.Range 格点数组 | range,targetEntity | 读 targetEntity | 无 |
| **AttackRangeRestore** | 恢复 Vision.BaseRange | targetEntity | 读 targetEntity | 无 |
| **ForceResetAttack** | 调 AttackBase.ForceResetAttack 立即重选目标+攻击 | toSelf,blackboardKey | 读 blackboardKey | 无 |

### 充能类
| 组件 | 职责 | 关键参数 | BB读写 | Tick/Teardown |
|---|---|---|---|---|
| **ChargeStateController** | Idle→Charging→Backout/Attack→Detonate→Finished 状态机，每帧推进，发布 phase 到 BB | targetCountKey,minimumTargetCount,phaseKey,phaseEnteredKey,chargeDuration,backoutAnimation,attackAnimation | 读 targetCountKey；写 phaseKey/phaseEnteredKey | **OnTick(必须)**+Teardown 移除 key |
| **ChargeAttackReservePool** | 给 ChargeAttack 加额外能量池+怪物状态过滤 | capacity,minMonsterStatus,maxMonsterStatus | 读 blackboardKey | Teardown/AbilityEnd RemoveAll |

### 实体生命周期类
| 组件 | 职责 | 关键参数 | BB读写 | Tick/Teardown |
|---|---|---|---|---|
| **DestroyEntity** | 调 Entity.Die()(仅 IsActive，去重) | toSelf,blackboardKey | 读 blackboardKey | 无 |

### 黑板/随机/通信类
| 组件 | 职责 | 关键参数 | BB读写 | Tick/Teardown |
|---|---|---|---|---|
| **WriteBlackboard** | 通用 BB 写 set/add/mult/div，值源 value/event/entity | key,value,method,source,path,asString | 读 key/value(add/mult/div 先读旧值)；写 key | 无 |
| **RandomRoll** | probability 概率门 / value 区间随机 / list 列表随机 | input,mode,outputKey | 写 outputKey | 无 |
| **ShareAttackTarget** | BeforeAttack 时把目标广播给友方(可选通信子弹延迟投递) | blackboardKey,receiverAbilityId,queueKey,activeSourceKey,bulletPrefabResource,bulletTrailResource,bulletSpeed | 读 blackboardKey/activeSourceKey；写接收方 queueKey/activeSourceKey | 无 |
| **SharedTargetExtraAttack** | 消费共享请求队列，Tick 驱动精确目标额外攻击(带异常状态) | queueKey,activeSourceKey,attackAnimation,abnormalType,abnormalTime | 读 queueKey；写 activeSourceKey/queueKey | Teardown 释放异常+清队列 |

---

## 第三部分：18 个 AbilityConfig .asset 实际用法

> 已迁到新系统的配置。trigger 枚举：1=PreWarm,2=AbilityBegin,3=AbilityEnd,6=BeforeAttack,9=AfterTakeDamage,16=Tick。

### 角色（Characters/3 与 6）
| asset | 名 | Kind | SP要点 | 组件组合 | 效果 |
|---|---|---|---|---|---|
| hibisc_s1 | 治疗强化α | Skill | 30SP手动持续 | ApplyBuff(2,Atk+50%) / DestroyBuff(3) | 手动持续攻+50% |
| hibisc_t1 | 治疗力提升 | Skill* | 被动 | ApplyBuff(2,Atk+8%) / DestroyBuff(3) | 常驻攻+8% |
| kross_s1 | 二连射 | Skill | 4SP每攻击 | AttackEventValueModifier(6,cumbo=2,mult1.4) | 每攻连射2发140% |
| test_s1 | 测试技能 | Skill | 3SP自动 | WriteBB(1,set mult=1)/AttackEventValMod(6,mult 读BB)/WriteBB(6,mult×1.1递增)/AttackRangeOverride(2)/Restore(3) | 黑板驱动递增倍率+范围扩 |
| kroos_t1 | 要害瞄准初级 | Talent | 被动 | RandomRoll(6,0.2概率)/AttackEventValMod(6,mult1.5 条件=t1_trigger=true) | 20%概率150%伤害 |
| test_talent | 圣光审判 | Talent | 被动 | EntityFilter(2,残血或精英)/AttackEventValMod(6,mult3) | 只打残血/精英且×3 |
| melan_s1 | 攻击力强化α | Skill | 40SP手动 | ApplyBuff(2,Atk+50%) / DestroyBuff(3) | 同 hibisc_s1 |
| melan_t1 | 攻击提升 | Skill* | 被动 | ApplyBuff(2,Atk+8%) | 常驻攻+8%(无Destroy) |
| spot_s1 | 次级治疗模式 | Skill | 40SP手动 | ApplyBuff(2,Atk+45%/攻间隔+1.3)/AttackRangeOverride(2,3×3)/ApplyAnimOverride(2)/AttackBehaviorOverride(2,治疗/血量最低)/ForceReset(2) + 结束5个Restore | 完整形态切换为群疗者 |
| spot_t1 | 烟雾加装 | Skill* | 被动 | WriteBB(9,提伤害类型/目标)/ApplyBuff(9,物闪75% 3秒,条件=物伤) | 治疗友方后给物闪 |
| stward_s1 | 强力击α | Skill | 4SP每攻击 | AttackEventValMod(6,mult1.9) | 190%伤害 |
| stward_t1 | 铠甲突破 | Skill* | 被动 | ApplyBuff(2,Atk+6%)/AttackBehaviorOverride(2,防御最高,无Restore) | 常驻攻+6%+永久优先高防 |
| ebnhlz_s3 | 寂静之声 | Skill | 20SP手动可关 | ApplyBuff(2,攻速+80/攻+65%)/ApplyAnimOverride(2)/WriteBB(2,mult×1.4)/EntityFilter(2&3,仅精英1-2)/ForceReset(2) + 结束Restore+WriteBB(div还原) | 形态切换+跨天赋联动 |
| ebnhlz_t1 | 强弱法 | Skill* | 被动 | WriteBB(2,set倍率1.43)/ChargeAttackDamageModifier(2,读BB)/ChargeAttackReservePool(2,1份仅精英) | 蓄力伤×1.43+额外精英蓄力 |
| ebnhlz_t2 | 倚音 | Talent | 被动 | EntitySelector(9,半径1.1同阵营计数)/ApplyDamage(9,15%法术,条件=计数0) | 孤立目标额外15%法伤 |

### 怪物（MC）
| asset | 名 | Kind | SP要点 | 组件组合 | 效果 |
|---|---|---|---|---|---|
| creeper_t1 | 爆炸 | Talent | 被动 | 20组件：ApplyAbnormalState(1,定身)/EntitySelector(16,视野计数)/ChargeStateController(16)/各阶段ApplyAbnormalState+ApplyAnimOverride/DestroyAbnormalState/4组(环形分桶EntitySelector+ApplyDamage+ApplyImpulse按0.25/0.5/1.664/2.915衰减)/ApplyAnimOverride(Die→Die2)/DestroyEntity | 苦力怕蓄力1秒→爆炸4层衰减+冲量→死亡。最复杂配置 |
| skeleton_t1 | 目标共享 | Skill* | 被动 | EntitySelector(6,半径2同阵营)/ShareAttackTarget(6,通信子弹)/SharedTargetExtraAttack(16/10/11,队列额外攻击) | 骷髅协同共享目标 |
| zombie_t1 | 屹立不倒 | Skill* | 特殊触发8s | ApplyDamage(2,自伤1触发致命)/ApplyAbnormalState(2,锁血3)/ApplyBuff(2,强化)/ApplyAnimOverride×2(2,形态切换)/EntitySelector(16,半径1友方)/ApplyBuff(16,aura光环)/Destroy×2/DestroyAbnormalState/RemoveAnimOverride/DestroyEntity | 濒死狂暴8秒后死亡 |

### 高频组合模式
- **模式A ApplyBuff+DestroyBuff**（手动持续 buff）：hibisc_s1/t1、melan_s1、ebnhlz_s3、zombie_t1。最基础骨架。
- **模式B Override+Restore 成对**（形态切换）：spot_s1、ebnhlz_s3、zombie_t1。4-5 对。
- **模式C AttackEventValueModifier 单组件**（自动攻击强化）：kross_s1、stward_s1。
- **模式D WriteBlackboard+条件触发**（数据驱动天赋）：kroos_t1、spot_t1、ebnhlz_t1、test_s1。
- **模式E EntitySelector 分桶+ApplyDamage**（AOE 爆炸）：creeper_t1。

### 别扭/特化配置
1. **zombie_t1 用 ApplyDamage 自伤1点触发致命伤害判定**——靠伤害系统副作用触发状态切换，取巧。
2. **talents 目录下 Kind 标 0(Skill)**：hibisc_t1/melan_t1/stward_t1/spot_t1/ebnhlz_t1/zombie_t1/skeleton_t1——Kind 语义与目录划分不一致。
3. **ebnhlz_s3↔t1 跨 ability 共享黑板变量** `ebnhlz_t1_charge_damage_multiplier`（t1 set=1.43，s3 mult×1.4/div÷1.4）——唯一跨 ability 变量联动，div 还原有浮点精度风险。
4. **stward_t1 的 AttackBehaviorOverride 无 Restore**——永久覆盖，与成对模式不一致。
5. **creeper_t1 异常状态类型**数字含义需对照枚举（2=定身?0=根定?3=锁血?）。

---

## 第四部分：原子功能总表与缺口分析

### 旧系统（17 子类）出现的原子功能类型
1. 加 buff / 销毁 buff（多属性 Modifier 数组）
2. 异常状态施加/移除（眩晕/定身/锁血/免伤，含负数ID特殊语义）
3. 动画覆盖/状态切换（Start/Idle/Move/Attack/Die 多段，含 once/override）
4. 攻击事件改写（multiplyer、cumbo、damageType）
5. 范围选择+分级 AOE（EntitySelector_Radius + 按距离分档倍率+冲量）
6. 冲量/击退（MoveBase.TryToAddImpulse 按方向）
7. **生成实体/召唤**（SetMovableEntity + SetMoveParameters 继承路径）
8. **延迟/多段延时序列**（await WaitForSeconds + 动画时长解析编排）
9. **血量百分比轮询守护循环**（while + WaitForFixedUpdate 等阈值）
10. **持续物理循环**（async while 加减速移速调节、投射体跟随）
11. **全图遍历寻点/寻朝向**（双层 for 覆盖最多敌人）
12. **阵营叛变/策反**（改 Entity.Camp + 移出静态列表）
13. **优先级加权目标选择**（atkp/surp/medp 三类权重公式）
14. **Skill 间恢复禁令互锁**（SkilllRecoverForbid 双向）
15. **跨技能/跨实体显式方法调用**（GetComponent<T>().Method()，如 Wither2→3、Wdslm3→MachineTalent1、HeadSeter→WitherPedestal）
16. **公共标志位跨技能状态共享**（HaveShield）
17. **计数器式状态推进**（AddHead 集齐3触发）
18. **静态实体交互/食物消耗**（InteractableStatic + MCED.EntityEatSomething）
19. **子弹/飞行物**（new Bullet + 销毁回调驱动后续）
20. **目标列表注入**（把指定实体插到攻击目标列表最前）
21. **沿路径闪现位移**（FlashMove 累计距离落位+更新路径指针）
22. **血量渐变动画**（DOTween CurrentHpRate）
23. **粒子/拖尾替换**（克隆子弹粒子到 TempContainer 替换 BulletData）
24. **医学效果硬编码**（MedicalEffect.TakeEffect_Single/Radius，绕过 AttributeStore）

### 新系统（24 组件）覆盖的原子功能类别
- **伤害**：直接伤害(ApplyDamage)、事件改写(AttackEventValueModifier)、充能伤倍(ChargeAttackDamageModifier)
- **Buff**：施加(ApplyBuff normal/aura)、销毁(DestroyBuff)
- **异常状态**：施加(ApplyAbnormalState normal/aura)、销毁(DestroyAbnormalState)、额外攻击期自带(SharedTargetExtraAttack)
- **动画**：覆盖(ApplyAnimationOverride once/override)、撤销(RemoveAnimationOverride)、充能阶段(ChargeStateController)
- **目标选择**：实体选择(EntitySelector)、目标过滤(EntityFilter)
- **攻击行为覆盖**：行为(AttackBehaviorOverride/Restore)、范围(AttackRangeOverride/Restore)、强制重置(ForceResetAttack)
- **充能**：状态机(ChargeStateController)、储备池(ChargeAttackReservePool)、伤修(ChargeAttackDamageModifier)
- **实体生命周期**：销毁(DestroyEntity)
- **黑板**：通用写(WriteBlackboard set/add/mult/div)
- **随机**：摇骰(RandomRoll probability/value/list)
- **通信**：目标共享(ShareAttackTarget)、共享额外攻击(SharedTargetExtraAttack)

### 缺口对照（旧系统能做、新系统缺组件的）

| 缺口 | 旧系统代表 | 现有组件最近候选 | 严重度 |
|---|---|---|---|
| **生成实体/召唤+继承属性** | SlimeTalent1、EyjafjallaTalent1、WdslmSkill3、MachineTalent1 | 无（DestroyEntity 只有反向） | 🔴 最高 |
| **延迟/多段延时序列** | SlimeTalent1、EyjafjallaTalent1(Explode)、WitherTalent2、WdslmSkill3、creeper(已用ChargeStateController绕) | ChargeStateController(专用，不可复用通用) | 🔴 最高 |
| **流程型编排(step/delay 原语)** | 所有跨时间多段逻辑 | 无（trigger 驱动撑不起，见 ability-migration-trigger-limit） | 🔴 最高 |
| **子弹/飞行物(通用可配置)** | EyjafjallaSkill2、WitchTalent | 无（ShareAttackTarget 内部硬编码 new Bullet） | 🟠 高 |
| **治疗(走伤害管道外)** | WitchSkill、WitchTalent、spot_s1(用damageType3绕) | ApplyDamage(damageType=3 取巧) | 🟠 高 |
| **位移/传送/闪现/沿路径位移** | HeadSeterSkill1(FlashMove) | ApplyImpulse(只有击退方向) | 🟠 高 |
| **范围瞬时AOE一体化** | WitherTalent2、Eyjafjalla(Explode) | 需 EntitySelector+ApplyDamage 三段链 | 🟡 中 |
| **护盾/屏障/减伤** | WitherTalent1(盾保护自伤阈值) | 无 | 🟡 中 |
| **复活** | 无旧实现但常见需求 | 无（DestroyEntity 只有反向） | 🟡 中 |
| **能力间编排(ability互调/信号)** | Eyjafjalla(Skill1→Talent1)、Wdslm(2↔3)、Wither(2→3) | 仅 Blackboard 变量(无显式信号通道) | 🟠 高 |
| **跨实体方法调用/公共字段共享** | HeadSeter→WitherPedestal.AddHead、Wither1↔3(HaveShield) | 无（违架构铁律） | 🟠 高 |
| **目标列表注入(插到最前)** | EyjafjallaTalent1(气泡优先) | EntityFilter(只能剔除不能插入) | 🟡 中 |
| **计数器式状态推进** | WitherPedestal(集齐3) | ChargeStateController(部分覆盖) | 🟡 中 |
| **持续物理循环(加减速/跟随)** | MachineTalent1 | 无（硬编码AI） | 🟡 中(难数据驱动化) |
| **阵营叛变/策反** | WdslmSkill2 | 无 | 🟡 中(特化) |
| **粒子/拖尾动态替换** | WitchTalent | 无 | 🟢 低(表现层) |
| **血量渐变动画** | WitherTalent2 | 无(DOTween散落) | 🟢 低 |

### 关键判断

1. **新系统已能干净覆盖的旧子类**：仅 **BeefTalent**（单一 ApplyAbnormalState）。其余 16 个全部不同程度撞缺口。
2. **新系统最成熟的模式**：buff技能(ApplyBuff/DestroyBuff)、形态切换(Override/Restore成对)、攻击事件改写(AttackEventValueModifier)、数据驱动天赋(WriteBlackboard+条件)、充能状态机(creeper证明可做复杂流程)。这五类**新系统已超过旧系统**。
3. **新系统最薄弱的领域**：生成实体、延迟/流程编排、子弹、治疗、位移——这五项几乎挡住了所有"召唤型/爆炸型/范围型/位移型"旧子类的迁移，是架构升级的核心动机。
4. **creeper_t1 是关键参照**：它用 ChargeStateController+环形分桶 EntitySelector+ApplyDamage 在新系统里**复刻了苦力怕爆炸**——证明"状态机+条件分桶"能在 trigger 驱动下表达复杂流程。但代价是 20 个组件、靠 OnTick 推进、且依赖本体存活(死亡即停)。这条路能走多远，取决于要迁的 ability 是否在本体存活期内完成。
5. **架构升级的真实信号已充分**：SlimeTalent1(间隔生成)、Eyjafjalla三件套(气泡+子弹+跨ability)、Wither三件套(延时+血量轮询+跨ability字段)、Wdslm三件套(召唤+延时+跨skill编排)、MachineTalent1(持续循环+全图寻路)——5 组流程型/联动型 ability 全撞同一类缺口(延迟+编排+生成)。**这正是上 step/delay 原语的真实依据**，不再是推测。
