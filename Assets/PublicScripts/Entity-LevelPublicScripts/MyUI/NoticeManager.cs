using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine.Events;

public class NoticeManager
{
    private Transform _notice;
    private static NoticeManager _instance;
    public static NoticeManager NM
    {
        get
        {
            if (_instance == null)
            {
                _instance = new NoticeManager();
            }
            return _instance;
        }
    }
    private NoticeManager()
    {
        _notice = GameObject.Find("GameUI/Notice").transform;
        MessageBoxInitialize();
        _notificationsShow = new List<NotificationData>();
        _notificationPrefab = Resources.Load<GameObject>("Prefabs/UI/MyUIs/Components/notification");
        _notificationsPool = new ObjectPool<NotificationData>(
            () => new NotificationData(Object.Instantiate(_notificationPrefab, _notice)),
            actionOnRelease: data => data.Notification.SetActive(false),
            collectionCheck: true);
        _notificationsPool.Release(_notificationsPool.Get()); // 预热 1 个
    }
    #region//Notification
    private class NotificationData
    {
        public GameObject Notification;
        public GameObject PhotoObject;
        public Image Photo;
        public GameObject ClosePauseObject;
        public Button Pause;
        public Button Close;
        public Image TimeSlider;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Content;
        public RectTransform RTransform;
        public bool IsPause;
        /// <summary>关闭流程唯一化标志：置位后到期计时循环退出、重复关闭直接跳过。</summary>
        public bool Closed;
        public NotificationData(GameObject notification)
        {
            Notification = notification;
            PhotoObject = notification.transform.Find("image").gameObject;
            Photo = notification.transform.Find("image/photo").GetComponent<Image>();
            ClosePauseObject = notification.transform.Find("closeAndPause").gameObject;
            Pause = ClosePauseObject.transform.Find("pause").GetComponent<Button>();
            Pause.onClick.AddListener(() =>
            {
                TimeSlider.gameObject.SetActive(false);
                Pause.gameObject.SetActive(false);
                Close.gameObject.SetActive(true);
                IsPause = true;
            });
            Close = ClosePauseObject.transform.Find("close").GetComponent<Button>();
            Close.onClick.AddListener(() => NM.RemoveNotificationDataFromShow(this));
            TimeSlider = ClosePauseObject.transform.Find("timeSlider").GetComponent<Image>();
            Title = notification.transform.Find("main/title").GetComponent<TextMeshProUGUI>();
            Content = notification.transform.Find("main/content").GetComponent<TextMeshProUGUI>();
            RTransform = notification.GetComponent<RectTransform>();
        }
    }
    private List<NotificationData> _notificationsShow;
    private ObjectPool<NotificationData> _notificationsPool;
    private GameObject _notificationPrefab;

    public void LaunchNotification(Image photo, string title, string content, float duration, bool allowPause, bool allowClose)
    {
        NotificationData notificationData = _notificationsPool.Get();
        _notificationsShow.Add(notificationData);
        notificationData.Title.text = title;
        notificationData.Content.text = content;
        notificationData.IsPause = false;
        notificationData.Closed = false;
        if (photo)
        {
            notificationData.PhotoObject.SetActive(true);
            notificationData.Photo = photo;
        }
        else
        {
            notificationData.PhotoObject.SetActive(false);
        }
        if (allowPause)
        {
            notificationData.ClosePauseObject.SetActive(true);
            notificationData.TimeSlider.gameObject.SetActive(true);
            notificationData.Pause.gameObject.SetActive(true);
            notificationData.Close.gameObject.SetActive(false);
        }
        else if (allowClose)
        {
            notificationData.ClosePauseObject.SetActive(true);
            notificationData.TimeSlider.gameObject.SetActive(false);
            notificationData.Pause.gameObject.SetActive(false);
            notificationData.Close.gameObject.SetActive(true);
        }
        else
        {
            notificationData.ClosePauseObject.SetActive(false);
        }
        RectTransform notificationRT = notificationData.RTransform;
        notificationData.Notification.SetActive(true);
        notificationRT.anchoredPosition = new Vector2(0, -80 - 70 * (_notificationsShow.Count - 1));
        DOTween.To((value) => { notificationRT.anchoredPosition = new Vector2(value, notificationRT.anchoredPosition.y); }, notificationRT.sizeDelta.x, 0, 0.2f);
        NotificationTimeUpdate(notificationData, duration);
    }
    private void RemoveNotificationDataFromShow(NotificationData notificationData)
    {
        // 关闭唯一入口：点击关闭、到期计时、退出动画期间的再次点击都会走到这里。
        // 已关闭（退出动画进行中/已回池）直接跳过——这是用户可触发的真实时序，非逻辑错误。
        if (notificationData.Closed)
            return;
        notificationData.Closed = true;
        RectTransform notificationRT0 = notificationData.RTransform;
        DOTween.To((value) => { notificationRT0.anchoredPosition = new Vector2(value, notificationRT0.anchoredPosition.y); }, notificationRT0.anchoredPosition.x, notificationRT0.sizeDelta.x, 0.2f).OnComplete(() =>
        {
            notificationData.Notification.SetActive(false);
            _notificationsPool.Release(notificationData);
        });
        int index = _notificationsShow.IndexOf(notificationData);
        _notificationsShow.RemoveAt(index);
        for (int i = index; i < _notificationsShow.Count; i++)
        {
            RectTransform notificationRT = _notificationsShow[i].RTransform;
            DOTween.To((value) => { notificationRT.anchoredPosition = new Vector2(notificationRT.anchoredPosition.x, value); }, notificationRT.anchoredPosition.y, -80 - 70 * i, 0.2f);
        }
    }
    private async void NotificationTimeUpdate(NotificationData notificationData, float duration)
    {
        float timeleft = duration;
        while (timeleft > 0 && !notificationData.IsPause && !notificationData.Closed)
        {
            timeleft -= Time.fixedDeltaTime;
            notificationData.TimeSlider.fillAmount = timeleft / duration;
            await UniTask.WaitForFixedUpdate();
        }
        if (timeleft <= 0)
        {
            RemoveNotificationDataFromShow(notificationData);
        }
    }
    #endregion
    #region//MessageBox
    private GameObject _messageBox;
    private UnityAction _messageBoxDoneAction;
    private UnityAction _messageBoxCancelAction;
    private TextMeshProUGUI _messageBoxContent;
    public void LaunchMessageBox(string content, UnityAction doneAction, UnityAction cancelAction)
    {
        _messageBox.SetActive(true);
        _messageBoxContent.text = content;
        _messageBoxDoneAction = doneAction;
        _messageBoxCancelAction = cancelAction;
    }
    private void MessageBoxInitialize()
    {
        _messageBox = Object.Instantiate(Resources.Load<GameObject>("Prefabs/UI/MyUIs/Components/messageBox"), _notice);
        _messageBox.transform.Find("content/done").GetComponent<Button>().onClick.AddListener(() =>
        {
            _messageBox.SetActive(false);
            _messageBoxDoneAction();
        });
        _messageBox.transform.Find("content/cancel").GetComponent<Button>().onClick.AddListener(() =>
        {
            _messageBox.SetActive(false);
            _messageBoxCancelAction();
        });
        _messageBoxContent = _messageBox.transform.Find("content/content").GetComponent<TextMeshProUGUI>();
    }
    #endregion
}
