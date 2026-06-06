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
                Debug.LogWarning($"未在子物体 {child.name} 中发现组件{typeof(T)}");
                return null;
            }
            Debug.LogWarning($"未找到路径 {path} 对应的子物体");
            return null;

        }

        /// <summary>
        /// UI进入时的操作（初始化等）
        /// </summary>
        public virtual void OnEnter()
        {
            UIGroup.alpha = 0;
            UIObject.SetActive(true);
            DOTween.To((value) =>
            {
                UIGroup.alpha = value;
                //UIObject.transform.localScale = (float)(1.2 - 0.2 * value) * Vector3.one;
            }, 0, 1, 0.2f).SetUpdate(true);
        }
        /// <summary>
        /// UI暂停时的操作
        /// </summary>
        public virtual void OnPause()
        {

        }
        /// <summary>
        /// UI继续执行的操作
        /// </summary>
        public virtual void OnResume()
        {
            UIObject.SetActive(true);
        }
        /// <summary>
        /// UI退出时的操作
        /// </summary>
        public virtual void OnExit()
        {
            DOTween.To((value) =>
            {
                UIGroup.alpha = value;
                //UIObject.transform.localScale = (float)(1.2 - 0.2 * value) * Vector3.one;
            }, 1, 0, 0.2f).SetUpdate(true).OnComplete(() =>
            {
                //UIObject.transform.localScale = Vector3.one;
                UIObject.SetActive(false);
            });
        }
    }
}

