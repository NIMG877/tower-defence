using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// LevelData 的自定义 Inspector (UI Toolkit)。
/// 触发条件: 在 Project 窗口选中 LevelData 资产。
/// </summary>
[CustomEditor(typeof(LevelData))]
public class LevelDataEditor : Editor
{
    (int waveIdx, int trackIdx, int actionIdx) _selectedAction = (-1, -1, -1);
    VisualElement _detailContainer;
    BlockMapCache _mapCache;
    PathEditingState _pathState;
    IDisposable _sectionDisposable;

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Column;

        // Header
        var header = new VisualElement();
        header.AddToClassList("level-editor-header");
        var title = new Label(((LevelData)target).LevelName);
        title.AddToClassList("level-editor-title");
        header.Add(title);
        root.Add(header);

        // Body placeholder
        var body = new VisualElement();
        body.style.paddingLeft = 12;
        body.style.paddingRight = 12;
        body.style.paddingTop = 12;
        body.style.paddingBottom = 12;
        body.Add(MetadataSection.Build(serializedObject));
        body.Add(ReferencesSection.Build(serializedObject));
        body.Add(EconomySection.Build(serializedObject));
        body.Add(WaveTimelineSection.Build(serializedObject, OnActionSelected, GetCurrentSelection));
        _detailContainer = new VisualElement();
        _detailContainer.style.paddingTop = 6;
        _detailContainer.style.paddingBottom = 6;
        body.Add(_detailContainer);
        RenderDetail();
       

        // Map editor + path editor section (uses LevelData.MapData, prefab is optional)
        var ld = (LevelData)target;
        try
        {
            _mapCache = BlockMapCache.Load(ld);
            _pathState = new PathEditingState { Cache = _mapCache };
            body.Add(MapEditorSection.Build(serializedObject, ld, out _sectionDisposable));
        }
        catch (System.Exception e)
        {
            var err = new Label($"⚠ MapEditorSection 构建失败: {e.Message}");
            err.style.color = new Color(0.95f, 0.4f, 0.4f);
            body.Add(err);
            UnityEngine.Debug.LogError($"[LevelDataEditor] MapEditorSection 构建失败: {e}");
        }

        root.Add(body);

        // 底部状态条 + Playtest 按钮
        var footer = new VisualElement();
        footer.style.backgroundColor = new Color(0.16f, 0.16f, 0.2f);
        footer.style.paddingTop = 8;
        footer.style.paddingBottom = 8;
        footer.style.paddingLeft = 12;
        footer.style.paddingRight = 12;
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.alignItems = Align.Center;
        root.Add(footer);

        Label statusLabel;
        footer.Add(ValidationBar.Build(serializedObject, out statusLabel));

        var spacer = new VisualElement();
        spacer.style.flexGrow = 1;
        footer.Add(spacer);

        var playtestBtn = new Button(() => PlaytestLauncher.Playtest((LevelData)target)) { text = "▶ Playtest" };
        playtestBtn.style.backgroundColor = new Color(0.86f, 0.86f, 0.66f);
        playtestBtn.style.color = new Color(0, 0, 0);
        playtestBtn.style.fontSize = 12;
        playtestBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
        playtestBtn.style.paddingTop = 4;
        playtestBtn.style.paddingBottom = 4;
        playtestBtn.style.paddingLeft = 12;
        playtestBtn.style.paddingRight = 12;
        playtestBtn.style.borderTopLeftRadius = 3;
        playtestBtn.style.borderTopRightRadius = 3;
        playtestBtn.style.borderBottomLeftRadius = 3;
        playtestBtn.style.borderBottomRightRadius = 3;
        footer.Add(playtestBtn);

        return root;
    }

    void OnActionSelected(int waveIdx, int trackIdx, int actionIdx)
    {
        _selectedAction = (waveIdx, trackIdx, actionIdx);
        WaveTimelineSection.RefreshDelActionBtnStates(waveIdx, trackIdx, actionIdx);
        RenderDetail();
    }

    // 给 WaveTimelineSection 用的 live 读取入口 (删除按钮要判断"删的是不是当前显示的 action")
    (int, int, int) GetCurrentSelection() => _selectedAction;

    void RenderDetail()
    {
        if (_detailContainer == null) return;
        _detailContainer.Clear();
        if (_selectedAction.waveIdx < 0) return;
        _detailContainer.Add(ActionDetailSection.Build(
            serializedObject,
            _selectedAction.waveIdx,
            _selectedAction.trackIdx,
            _selectedAction.actionIdx));
    }

    void OnEnable()
    {
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        _sectionDisposable?.Dispose();
        _sectionDisposable = null;
        _mapCache = null;
        _pathState = null;
    }

    void OnUndoRedo()
    {
        serializedObject.Update();
        RenderDetail();
    }
}