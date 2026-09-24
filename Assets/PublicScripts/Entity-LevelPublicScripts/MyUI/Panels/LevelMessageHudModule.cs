using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI
{
    /// <summary>
    /// Owns the level-wide HUD values and the speed/pause button visuals.
    /// 时间倍速本体在 <see cref="TimeScaleManager"/>，这里的按钮只是它的
    /// 一个申请来源（按自己的开关状态翻 true/false）。
    /// </summary>
    internal sealed class LevelMessageHudModule
    {
        private readonly Button _timeMultiple;
        private readonly Button _pause;
        private readonly GameObject _pauseMask;
        private readonly Sprite _x1;
        private readonly Sprite _x2;
        private readonly Sprite _continue;
        private readonly Sprite _pauseSprite;

        private readonly TextMeshProUGUI _cost;
        private readonly Image _costSlider;
        private readonly TextMeshProUGUI _canSetNum;
        private readonly TextMeshProUGUI _currentNumAndTotalNum;
        private readonly TextMeshProUGUI _levelHpLeft;

        // 按钮开关的 UI 表现状态（sprite/遮罩）；时间倍速状态在 TimeScaleManager
        private bool _isPause;
        private bool _is2X;
        // 退出/重开确认框非模态且 timeScale=0 时 UI 仍可点：防连点重复申请暂停
        private bool _exitConfirming;
        private bool _restartConfirming;

        public LevelMessageHudModule(GameObject root)
        {
            _x1 = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/1X");
            _x2 = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/2X");
            _continue = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/continue_black");
            _pauseSprite = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/LevelMessagePanel/pause_black");

            _timeMultiple = LevelMessageViewLookup.Get<Button>(root, "timeMultiple");
            _pause = LevelMessageViewLookup.Get<Button>(root, "pause");
            _pauseMask = LevelMessageViewLookup.Get<Transform>(root, "pauseMask").gameObject;
            _cost = LevelMessageViewLookup.Get<TextMeshProUGUI>(root, "staticEntityArea/resource/cost");
            _costSlider = LevelMessageViewLookup.Get<Image>(root, "staticEntityArea/resource/costSlider");
            _canSetNum = LevelMessageViewLookup.Get<TextMeshProUGUI>(root, "staticEntityArea/numLeft/canSetNum");
            _currentNumAndTotalNum = LevelMessageViewLookup.Get<TextMeshProUGUI>(root, "count_total_healthleft/c_t");
            _levelHpLeft = LevelMessageViewLookup.Get<TextMeshProUGUI>(root, "count_total_healthleft/t_hp");

            _timeMultiple.onClick.AddListener(() =>
            {
                _is2X = !_is2X;
                _timeMultiple.image.sprite = _is2X ? _x2 : _x1;
                TimeScaleManager.Manager.SetFast(_is2X);
            });
            _pause.onClick.AddListener(() =>
            {
                _isPause = !_isPause;
                _pause.image.sprite = _isPause ? _pauseSprite : _continue;
                _pauseMask.SetActive(_isPause);
                TimeScaleManager.Manager.SetPause(_isPause);
            });
            LevelMessageViewLookup.Get<Button>(root, "exit").onClick.AddListener(() =>
            {
                if (_exitConfirming) return;
                _exitConfirming = true;
                TimeScaleManager.Manager.SetPause(true);
                NoticeManager.NM.LaunchMessageBox(
                    "确认退出当前关卡？",
                    () => LevelActionManager.Manager.MissionEnd(false),
                    () =>
                    {
                        _exitConfirming = false;
                        TimeScaleManager.Manager.SetPause(false);
                    });
            });
            LevelMessageViewLookup.Get<Button>(root, "restart").onClick.AddListener(() =>
            {
                if (_restartConfirming) return;
                _restartConfirming = true;
                TimeScaleManager.Manager.SetPause(true);
                NoticeManager.NM.LaunchMessageBox(
                    "确认重新开始本关？",
                    () =>
                    {
                        // 与 MissionEnd 同序：先 LevelEnd 让实体还池、Dormancy 回填
                        // 部署列表，面板退栈的 OnExit 清列表必须在其后；之后复走
                        // TeamPanel→CutToLevel 的标准入场流程重开本关
                        LevelResourceSharing.LevelEnd();
                        PanelManager.PopTo(TeamPanel.Panel);
                        PanelManager.Push(CutToLevelPanel.Panel);
                    },
                    () =>
                    {
                        _restartConfirming = false;
                        TimeScaleManager.Manager.SetPause(false);
                    });
            });
        }

        public void OnEnter()
        {
            _isPause = false;
            _is2X = false;
            _exitConfirming = false;
            _restartConfirming = false;
            _pause.image.sprite = _continue;
            _pauseMask.SetActive(false);
            _timeMultiple.image.sprite = _x1;
            TimeScaleManager.Manager.Reset();

        }

        public void OnExit()
        {
            _isPause = false;
            _is2X = false;
            TimeScaleManager.Manager.Reset();
        }

        public void OnPause()
        {
            // Panel stack pause is temporary. TimeScaleManager keeps the tier
            // requests so OnResume re-synthesizes them.
            TimeScaleManager.Manager.Suspend();
        }

        public void OnResume()
        {
            TimeScaleManager.Manager.Resume();
        }

        public void UpdateCost()
        {
            _cost.text = LevelResourceManager.Manager.CostMessage.currentCost.ToString();
        }

        public void UpdateCanSetNum()
        {
            _canSetNum.text = LevelResourceManager.Manager.CanSetNumLeft.ToString();
        }

        public void UpdateLevelHp()
        {
            _levelHpLeft.text = LevelResourceManager.Manager.LevelHpLeft.ToString();
        }

        public void UpdateOperateProgress()
        {
            _currentNumAndTotalNum.text =
                LevelResourceManager.Manager.CurrentOperateCount + "/" +
                LevelResourceManager.Manager.NeedOperateCount;
        }

        public void Tick()
        {
            var costMessage = LevelResourceManager.Manager.CostMessage;
            _cost.text = costMessage.currentCost.ToString();
            _costSlider.fillAmount = costMessage.costTimer;
            _canSetNum.text = LevelResourceManager.Manager.CanSetNumLeft.ToString();
        }
    }
}
