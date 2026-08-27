using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI
{
    /// <summary>
    /// Owns the level-wide HUD values and all time-scale controls.
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

        private bool _isPause;
        private bool _is2X;
        private bool _isSlow;

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
                ApplyTimeScale();
            });
            _pause.onClick.AddListener(() =>
            {
                _isPause = !_isPause;
                _pause.image.sprite = _isPause ? _pauseSprite : _continue;
                _pauseMask.SetActive(_isPause);
                ApplyTimeScale();
            });
            LevelMessageViewLookup.Get<Button>(root, "exit").onClick.AddListener(() =>
            {
                Time.timeScale = 0;
                NoticeManager.NM.LaunchMessageBox(
                    "ȷ���˳��ؿ���",
                    () => LevelActionManager.Manager.MissionEnd(false),
                    ApplyTimeScale);
            });
        }

        public void OnEnter()
        {
            _isPause = false;
            _is2X = false;
            _isSlow = false;
            _pause.image.sprite = _continue;
            _pauseMask.SetActive(false);
            _timeMultiple.image.sprite = _x1;
            ApplyTimeScale();

        }

        public void OnExit()
        {
            _isPause = false;
            _is2X = false;
            _isSlow = false;
            Time.timeScale = 1;
        }

        public void OnPause()
        {
            // Panel stack pause is temporary. Preserve the user's flags so OnResume can restore them.
            Time.timeScale = 1;
        }

        public void OnResume()
        {
            ApplyTimeScale();
        }

        public void SetSlow(bool slow)
        {
            _isSlow = slow;
            ApplyTimeScale();
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

        private void ApplyTimeScale()
        {
            if (_isPause)
                Time.timeScale = 0;
            else if (_isSlow)
                Time.timeScale = 0.1f;
            else if (_is2X)
                Time.timeScale = 2;
            else
                Time.timeScale = 1;
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
