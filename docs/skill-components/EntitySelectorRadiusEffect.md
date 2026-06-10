# EntitySelectorRadiusEffect

Selects entities in a radius and forwards a sub-component to each.
For every selected entity, a fresh instance of `subComponentType` is
created via `ComponentFactory.Create`, initialized with the SAME
`ParamList` as the parent, and then triggered.

**⚠ Dispatch-bypass warning:** the sub-component is constructed
ad-hoc and receives `ctx.currentEvent` directly from the parent.
That bypasses the dispatcher's trigger-bucket guarantee — the
sub-component sees whatever event type the parent received. The
sub-component declared in `subComponentType` **must** be able to
handle every event type declared in the parent's `triggers[]`.
Otherwise the sub-component's direct cast on `ctx.currentEvent`
will throw a `NullReferenceException`.

**Registered as:** `EntitySelectorRadiusEffect`
**Class:** `SkillSystem.Components.EntitySelectorRadiusEffectComponent`
**File:** `Assets/PublicScripts/Entity-LevelPublicScripts/SkillSystem/Components/EntitySelectorRadiusEffectComponent.cs`

## Parameters

| Key | Type | Default | Description |
|---|---|---|---|
| `radius` | Float | `1` | Selection radius in world units. |
| `sameCamp` | Bool | `True` | `true` = select entities with the same camp; `false` = opposite camp. |
| `camp` | Int | `0` | Camp id to match. `0` = use `ctx.entity.Camp`. |
| `subComponentType` | String | `""` | Component type to instantiate and forward to each selected entity. Must be a registered component name (e.g. `ApplyBuff`). The sub-component reads the SAME `ParamList` as the parent. Empty = no-op. |
