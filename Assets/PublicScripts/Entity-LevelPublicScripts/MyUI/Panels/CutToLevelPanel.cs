using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace MyUI
{
    public class CutToLevelPanel : BasePanel
    {
        private static CutToLevelPanel _instance;
        public static CutToLevelPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CutToLevelPanel();
                }
                return _instance;
            }
        }
        private Image _bk, _bbk;
        private CanvasGroup _group;
        private TextMeshProUGUI _code, _name;

        private CutToLevelPanel() : base(new UIType("Prefabs/UI/MyUIs/CutToLevelPanel"))
        {
            _bbk = UIObject.GetComponent<Image>();
            _bk = GetComponentInChildrenByPath<Image>("bk");
            _group = GetComponentInChildrenByPath<CanvasGroup>("group");
            _code = GetComponentInChildrenByPath<TextMeshProUGUI>("group/code");
            _name = GetComponentInChildrenByPath<TextMeshProUGUI>("group/name");
        }
        public override void OnEnter()
        {
            base.OnEnter();
            LevelData levelData = LevelResourceSharing.LD;
            Texture2D texture2D = levelData.CutToLevelTexture;
            _bk.sprite = Sprite.Create(texture2D, new Rect(0, 0, texture2D.width, texture2D.height), Vector2.one * 0.5f);
            _code.text = levelData.LevelCode;
            _name.text = levelData.LevelName;
            _bbk.color = new Color(0.196f, 0.204f, 0.216f);
            _bbk.raycastTarget = true;
            DOTween.To((value) =>
            {
                _group.alpha = value;
                _bk.color = new Color(1, 1, 1, value);
            }, 0, 1, 0.5f).onComplete += () =>
            {
                PanelManager.HidePauseUI();
                LevelResourceSharing.LevelInitialize();
                DOTween.To((value) =>
                {
                    _bk.color = new Color(1, 1, 1, value);
                    _bbk.color = new Color(0.196f, 0.204f, 0.216f, value);
                }, 6, 0, 3).onComplete += () =>
                {
                    PanelManager.Push(LevelMessagePanel.Panel);
                    UIObject.transform.SetAsLastSibling();
                    DOTween.To((value) =>
                    {
                        _group.alpha = value;
                    }, 4, 0, 1).SetUpdate(true).onComplete += () =>
                    {
                        _bbk.raycastTarget = false;
                        LevelResourceSharing.LevelStart();
                    };
                };
            };
        }

    }
}

