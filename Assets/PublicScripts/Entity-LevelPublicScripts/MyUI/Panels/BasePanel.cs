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
                Debug.LogWarning($"在 {child.name} 上未找到组件 {typeof(T)}");
                return null;
            }
            Debug.LogWarning($"未找到路径 {path} 对应的子物体");
            return null;

        }

        /// <summary>
        /// 面板入栈进入（Push 对新栈顶调用）：激活 UI 并置于最前，alpha 从 0 淡入到 1（0.2 秒，不受 TimeScale 影响）。
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
        /// 面板被覆盖暂停（Push 对原栈顶调用）。基类无操作，子类可覆盖。
        /// </summary>
        public virtual void OnPause()
        {

        }
        /// <summary>
        /// 面板恢复栈顶（Pop/PopTo 对新栈顶调用）：重新激活并置于最前。
        /// </summary>
        public virtual void OnResume()
        {
            UIObject.transform.SetAsLastSibling();
            UIObject.SetActive(true);
        }
        /// <summary>
        /// 面板出栈退出（Pop/PopTo 对被移除面板调用）：alpha 从 1 淡出到 0（0.2 秒），淡出完成后在 OnComplete 中 SetActive(false) 收尾。
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
