using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyUI
{
    public class MapEditorPanel : BasePanel
    {
        private static MapEditorPanel _instance;
        public static MapEditorPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MapEditorPanel();
                }
                return _instance;
            }
        }

        private Image _topLeft, _bottomLeft, _topRight, _bottomRight;
        private EventTrigger _dragger;
        private RectTransform _draggerRect;
        private RectTransform _parentRect;
        private Vector2 _dragOffset;
        private bool _isDragging;
        private Vector2 _initialDragPosition;
        private bool _isHorizontalLock;  // Shift+水平拖拽时锁定
        private bool _isVerticalLock;    // Shift+垂直拖拽时锁定



        private MapEditorPanel() : base(new UIType("Prefabs/UI/MyUIs/MapEditorPanel"))
        {
            //屏幕分割器
            _topLeft = GetComponentInChildrenByPath<Image>("topLeft");
            _bottomLeft=GetComponentInChildrenByPath<Image>("bottomLeft");
            _topRight = GetComponentInChildrenByPath<Image>("topRight");
            _bottomRight=GetComponentInChildrenByPath<Image>("bottomRight");
            _dragger=GetComponentInChildrenByPath<EventTrigger>("dragger");
            _parentRect = UIObject.GetComponent<RectTransform>();
            _draggerRect = _dragger.GetComponent<RectTransform>();
            InitializeDragger();
        }
        #region 屏幕分割器逻辑
        private void InitializeDragger()
        {
            // 添加拖拽事件监听
            EventTrigger.Entry dragEntry = new EventTrigger.Entry();
            dragEntry.eventID = EventTriggerType.Drag;
            dragEntry.callback.AddListener((data) => { OnDraggerDrag((PointerEventData)data); });
            _dragger.triggers.Add(dragEntry);

            EventTrigger.Entry beginEntry = new EventTrigger.Entry();
            beginEntry.eventID = EventTriggerType.BeginDrag;
            beginEntry.callback.AddListener((data) => { OnDragStart((PointerEventData)data); });
            _dragger.triggers.Add(beginEntry);
        }
        private void OnDragStart(PointerEventData eventData)
        {
            // 记录初始位置和偏移
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRect,
                eventData.position,
                eventData.pressEventCamera,
                out _initialDragPosition
            );
            _isDragging = true;
            _isHorizontalLock = false;
            _isVerticalLock = false;
        }

        private void OnDraggerDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;

            // 获取当前鼠标位置（局部坐标）
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRect,
                
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 currentPosition
            );

            // 计算偏移量
            Vector2 delta = currentPosition - _initialDragPosition;
            
            // Shift键处理（锁定单轴）
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                // 首次按下Shift时判断锁定方向
                if (!_isHorizontalLock && !_isVerticalLock)
                {
                    if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                        _isHorizontalLock = true;
                    else
                        _isVerticalLock = true;
                }

                if (_isHorizontalLock) delta.y = 0;
                if (_isVerticalLock) delta.x = 0;
            }
            else
            {
                _isHorizontalLock = false;
                _isVerticalLock = false;
            }

            // 计算新的dragger位置（限制在父矩形范围内）
            Vector2 newPos = _draggerRect.anchoredPosition + delta;
            newPos.x = Mathf.Clamp(newPos.x, -_parentRect.rect.width/2 + 50, _parentRect.rect.width/2 - 50);
            newPos.y = Mathf.Clamp(newPos.y, -_parentRect.rect.height/2 + 50, _parentRect.rect.height/2 - 50);
            
            // 更新dragger位置
            _draggerRect.anchoredPosition = newPos;
            
            // 同步更新四个区域
            UpdatePanelsLayout(newPos);
            _initialDragPosition = currentPosition;
        }

        private void UpdatePanelsLayout(Vector2 draggerPosition)
        {
            // 将dragger的局部坐标转换为比例值（0~1）
            float widthRatio = (draggerPosition.x + _parentRect.rect.width/2) / _parentRect.rect.width;
            float heightRatio = (draggerPosition.y + _parentRect.rect.height/2) / _parentRect.rect.height;

            // 左上区域（右下角跟随dragger）
            SetPanelAnchor(_topLeft.rectTransform, 
                new Vector2(0, heightRatio), 
                new Vector2(widthRatio, 1));

            // 左下区域（右上角跟随dragger）
            SetPanelAnchor(_bottomLeft.rectTransform,
                new Vector2(0, 0),
                new Vector2(widthRatio, heightRatio));

            // 右上区域（左下角跟随dragger）
            SetPanelAnchor(_topRight.rectTransform,
                new Vector2(widthRatio, heightRatio),
                new Vector2(1, 1));

            // 右下区域（左上角跟随dragger）
            SetPanelAnchor(_bottomRight.rectTransform,
                new Vector2(widthRatio, 0),
                new Vector2(1, heightRatio));
        }

        private void SetPanelAnchor(RectTransform panel, Vector2 anchorMin, Vector2 anchorMax)
        {
            panel.anchorMin = anchorMin;
            panel.anchorMax = anchorMax;
            panel.offsetMin = Vector2.zero; // left-bottom
            panel.offsetMax = Vector2.zero; // right-top
        }
        #endregion
        
       
        
    }
}