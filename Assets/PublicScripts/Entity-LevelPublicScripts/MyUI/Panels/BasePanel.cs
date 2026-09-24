using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace MyUI
{
    public class BasePanel
    {
        public UIType UIType { get; private set; }
        protected BasePanel(UIType uIType)
        {
            UIType = uIType;
            UIObject = Object.Instantiate(Resources.Load<GameObject>(UIType.Path), PanelManager.Panel);
            UIObject.SetActive(false);
            UIGroup = UIObject.GetComponent<CanvasGroup>();
            Button back = GetComponentInChildrenByPath<Button>("backButton");
            if (back)
            {
                back.onClick.AddListener(() =>
                {
                    PanelManager.Pop(1);
                });
            }
        }
        public GameObject UIObject;
        protected CanvasGroup UIGroup;

        protected T GetComponentInChildrenByPath<T>(string path) where T : Component
        {
            Transform child = UIObject.transform.Find(path);
            if (child)
            {
                T t = UIObject.transform.Find(path).GetComponent<T>();
                if (t != null)
                {
                    return t;
                }
                Debug.LogWarning($"δ�������� {child.name} �з������{typeof(T)}");
                return null;
            }
            Debug.LogWarning($"δ�ҵ�·�� {path} ��Ӧ��������");
            return null;

        }

        /// <summary>
        /// UI����ʱ�Ĳ�������ʼ���ȣ�
        /// </summary>
        public virtual void OnEnter()
        {
            // 进入/退出淡入淡出互斥：先杀对方在飞的 tween。OnExit 的延迟
            // SetActive(false) 挂在其 tween 的 OnComplete 上，必须随杀取消，
            // 否则同帧重入（如 restart）会被旧退出的 OnComplete 停用
            DOTween.Kill(UIGroup);
            UIGroup.alpha = 0;
            UIObject.transform.SetAsLastSibling();
            UIObject.SetActive(true);
            DOTween.To((value) =>
            {
                UIGroup.alpha = value;
            }, 0, 1, 0.2f).SetTarget(UIGroup).SetUpdate(true);
        }
        /// <summary>
        /// UI��ͣʱ�Ĳ���
        /// </summary>
        public virtual void OnPause()
        {

        }
        /// <summary>
        /// UI����ִ�еĲ���
        /// </summary>
        public virtual void OnResume()
        {
            UIObject.transform.SetAsLastSibling();
            UIObject.SetActive(true);
        }
        /// <summary>
        /// UI�˳�ʱ�Ĳ���
        /// </summary>
        public virtual void OnExit()
        {
            DOTween.Kill(UIGroup);
            DOTween.To((value) =>
            {
                UIGroup.alpha = value;
            }, 1, 0, 0.2f).SetTarget(UIGroup).SetUpdate(true).OnComplete(() =>
            {
                UIObject.SetActive(false);
            });
        }
    }
}
