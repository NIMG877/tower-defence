## CLI 生成自动落 asset —— 实施方案

### 目标与数据流

给生成产物开辟一条 服务端 → Unity 资产 的单向收件箱通道（现状生成物只进 `logs/gen-*.json`，出不了服务端）：

```
CLI/HTTP → GenerateService.generate()
  ├─ status=ok 且非 degraded → 原子写 ability-server/out/{abilityId}.json（纯 ability DTO）
  └─ 所有结果照旧 → logs/gen-*.json（案例回放，不动）
Unity 编辑器（InitializeOnLoad + update 轮询，须开着）
  ability-server/out/*.json → Parse 严格闸 → FromDto → 占位索引扫描(警告)
   → Assets/Resources/GeneratedAbilities/{abilityId}.asset（存在则 CopySerialized 覆盖，GUID 稳定）
   → 成功即删除 json；失败移入 out/failed/
```

### 一、服务端（ability-server）

1. **`app/config.py`**：`log_dir`（:55）后新增 `out_dir: str | None = str(Path(__file__).resolve().parent.parent / "out")`，同款锚定写法，None=关闭。
2. **`app/service.py`**：
   - `generate()`（:42-55）在缓存写入之后、`_log` 之前插入 `self._export_out(response)`；
   - **缓存命中路径同样调用 `_export_out`**——out/ 是收件箱，同 payload 命中即重灌（内容幂等），避免"文件已被导入消费、缓存命中却不复活"的困惑；
   - 新增 `_export_out`：谓词与缓存一致（`status=="ok" and not degraded`），否则直接返回；写 `{abilityId}.json.tmp` 后 `os.replace` 原子替换（Unity 轮询不会读到半截 JSON，`.tmp` 不匹配 `*.json` glob）；成功后 `response["report"]["outPath"] = str(path)`（report 加运行时字段有 `cached` 先例，案例文件也随之记录）；`except OSError: return` 沿用 `_log` 惯例。需补 `import os`。
3. **`app/cli.py`**：`print_summary`（:84-96）在 report.outPath 存在时打一行灰字提示 `已写入 out/{abilityId}.json（Unity 编辑器开启时自动导入）`。
4. **`.gitignore`**：`ability-server/logs/`（:90）旁加 `ability-server/out/`。
5. **`ability-server/README.md`**：CLI 章节补一段收件箱语义与 Unity 侧导入行为。

### 二、Unity 侧（新文件 `Assets/Editor/AbilityGeneration/GeneratedSkillImporter.cs`）

挂 Assembly-CSharp-Editor（与 GeneratedSkillProbe/SkillCorpusExporter 同目录，无 asmdef 惯例）：

- `[InitializeOnLoad]` 静态类，静态构造挂 `EditorApplication.update += Tick`；Tick 以 `timeSinceStartup` 节流 1s，`Directory.Exists` 门卫，顶层 `GetFiles("*.json")`（`out/` 根 = `Directory.GetParent(Application.dataPath) + "ability-server/out"`）。项目先例：GeneratedSkillProbe 的 update 轮询、StaticDataValidator 的 InitializeOnLoad。
- 单文件导入：`AbilityConfigBuilder.Parse`（严格反序列化，幻觉字段/枚举在闸上炸）→ `FromDto`（iconKey 缺失 → icon=null，AbilityIconPool.Resolve 已证安全）→ 占位索引扫描（静态纯函数，递归 `steps/elseSteps`：`op==spawn_entity 且 args.GetInt("spawnIndex")==0`、`op==fire_bullets 且 bulletDataIndex==0` → `Debug.LogWarning` 列出规则/步骤号，提示描述模式产物待宿主绑定）→ 落资产：
  - 目标 `Assets/Resources/GeneratedAbilities/{abilityId}.asset`；已存在 → `EditorUtility.CopySerialized(新建, 既有)` + SetDirty（**GUID 不变，EntityDataCollection/xlsx 既有引用持续有效，同 id 重生成即原地更新**）；不存在 → `Directory.CreateDirectory` 后 `AssetDatabase.CreateAsset`；`SaveAssets()` 收尾，`Debug.Log` 导入结果。
  - 成功 → `File.Delete` 消费该 json；解析/落盘失败 → `Debug.LogError`（异常+文件名）后移入 `out/failed/`（同名先删，最新者胜），不重试不刷屏。
- **位置语义**：`Resources/GeneratedAbilities/` 故意不在 `Resources.LoadAll("Abilities")` 扫描根内——不进菜单④语料导出、不进图标池，避免 LLM 产物回流污染检索库；采纳 = 手动移入角色目录（Unity 内移动保 meta）或 xlsx Talents 列配路径/EntityDataCollection 挂引用。

### 三、语义定版

- **覆盖**：同 abilityId 服务端覆盖写、Unity 覆盖导入，最新者胜（撞名已实际发生，kroos_x1 两份不同技能；旧版仍可从 logs 回溯）。
- **只收 ok**：degraded/rejected 不进 out/（结构闸已过但未走完流程的降级稿仍只从 logs 回溯）。
- **DTO 纯净**：out 文件就是 ability 对象本身（abilityId/abilityName/description/sp/rules），`Parse` 可直接吃，无协议包装，不改 AbilityConfigDto 契约与 protocolVersion。

### 四、测试与验证

- **服务端 pytest**（新增 `tests/test_out_export.py`，复刻 `test_agent.py:472-489` 的 tiered_cfg+monkeypatch 模式，`tiered_cfg(out_dir=str(tmp_path))`）：① ok → 文件存在、内容与 ability 全等、无 .tmp 残留、report.outPath 正确；② degraded → 不落；③ rejected → 不落；④ 同 id 两次（改 description 绕缓存）→ 单文件且为第二次内容；⑤ 缓存命中 → 重灌文件；⑥ out_dir=None → 不落不炸。完成后 `python -m pytest tests/ -q` 全绿（现 103 例）。
- **Unity 手册验证**（该项目 Unity 侧行为最终以实测为准）：开编辑器自动导入出资产；手工放坏 JSON 确认进 out/failed/；占位样例出警告；同 id 重生成确认 CopySerialized 原地更新。

### 五、明确不做

- 不自动绑定实体、不自动进语料库（采纳是人工动作）；
- Python 不直写 .asset YAML（GUID/meta 归 Unity，JSON 中转才自包含无断链）；
- 不做 HTTP 推送/FileSystemWatcher（文件收件箱 + InitializeOnLoad 轮询天然覆盖"生成时编辑器没开"的场景）；
- 不动菜单③/运行时 GenerateSkill 既有管线（那是战内存替换，与本持久化通道正交）。