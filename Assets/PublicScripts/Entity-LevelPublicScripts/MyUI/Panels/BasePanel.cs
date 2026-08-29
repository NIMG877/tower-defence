using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Unity.Plastic.Newtonsoft.Json.Linq;

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
            UIGroup.alpha = 0;
            UIObject.transform.SetAsLastSibling();
            UIObject.SetActive(true);
            DOTween.To((value) =>
            {
                UIGroup.alpha = value;
            }, 0, 1, 0.2f).SetUpdate(true);
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
            DOTween.To((value) =>
            {
                UIGroup.alpha = value;
            }, 1, 0, 0.2f).SetUpdate(true).OnComplete(() =>
            {
                UIObject.SetActive(false);
            });
        }
    }
}
