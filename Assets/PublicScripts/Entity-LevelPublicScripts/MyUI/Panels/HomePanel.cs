using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI
{
    public class HomePanel : BasePanel
    {
        private static HomePanel _instance;
        public static HomePanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HomePanel();
                }
                return (HomePanel)_instance;
            }
        }
        private CancellationTokenSource _escListenCts;

        private HomePanel() : base(new UIType("Prefabs/UI/MyUIs/HomePanel"))
        {
            Button terminalb = GetComponentInChildrenByPath<Button>("RightNaveBar/terminal");
            terminalb.onClick.AddListener(() => PanelManager.Push(TerminalPanel.Panel));
            Button handBook = GetComponentInChildrenByPath<Button>("RightNaveBar/handbook");
            handBook.onClick.AddListener(() => PanelManager.Push(MonsterHandbookPanel.Panel));
            Button editor = GetComponentInChildrenByPath<Button>("RightNaveBar/editor");
            editor.onClick.AddListener(() => PanelManager.Push(MapEditorPanel.Panel));
            Button team = GetComponentInChildrenByPath<Button>("RightNaveBar/team");
            team.onClick.AddListener(() =>
            {
                TeamPanel teamPanel = TeamPanel.Panel;
                teamPanel.SetTeamManagementMode();
                PanelManager.Push(teamPanel);
            });
            Button closeGame = GetComponentInChildrenByPath<Button>("CloseGame");
            closeGame.onClick.AddListener(LaunchQuitConfirm);
        }
        public override void OnEnter()
        {
            base.OnEnter();
            StartEscListen();
        }
        public override void OnResume()
        {
            base.OnResume();
            StartEscListen();
        }
        public override void OnPause()
        {
            base.OnPause();
            StopEscListen();
        }
        public override void OnExit()
        {
            base.OnExit();
            StopEscListen();
        }
        /// <summary>
        /// HomePanel 位于面板栈顶期间逐帧监听 Esc，唤起退出确认弹窗（HomePanel 入栈/恢复时启动，被覆盖/退出时停止）
        /// </summary>
        private async UniTaskVoid EscListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    LaunchQuitConfirm();
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        private void StartEscListen()
        {
            if (_escListenCts == null)
            {
                _escListenCts = new CancellationTokenSource();
                EscListenLoop(_escListenCts.Token).Forget();
            }
        }
        private void StopEscListen()
        {
            if (_escListenCts != null)
            {
                _escListenCts.Cancel();
                _escListenCts.Dispose();
                _escListenCts = null;
            }
        }
        private void LaunchQuitConfirm()
        {
            NoticeManager.NM.LaunchMessageBox("确认退出游戏？", QuitGame, () => { });
        }
        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

