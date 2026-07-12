using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

public class SlidersManager : IManagerStartEnd
{
    private static SlidersManager _instance;
    public static SlidersManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new SlidersManager();
            return _instance;
        }
    }
    private SlidersManager()
    {
        _allTypeSlider = new List<GameObject>();
        _allTypeSliderPool = new List<List<SliderControllerBasic>>();
        _allTypeSliderShow = new List<List<SliderControllerBasic>>();
        int poolNum = 10;
        UISlider = GameObject.Find("GameUI/Sliders").transform;
        string[] sliderName = new string[] { "HPSlider_Enemy", "HPSlider_Turret", "HPSlider_Enemy", "SPSlider_normal", "SPSlider_normal" };
        AddSliderType<HpSliderController>(Resources.Load<GameObject>($"Prefabs/UI/Sliders/{sliderName[0]}"), poolNum);
        AddSliderType<HpSliderController>(Resources.Load<GameObject>($"Prefabs/UI/Sliders/{sliderName[1]}"), poolNum);
        AddSliderType<HpSliderController>(Resources.Load<GameObject>($"Prefabs/UI/Sliders/{sliderName[2]}"), poolNum);
        AddSliderType<SpSliderController>(Resources.Load<GameObject>($"Prefabs/UI/Sliders/{sliderName[3]}"), poolNum);
        AddSliderType<SpSliderController>(Resources.Load<GameObject>($"Prefabs/UI/Sliders/{sliderName[4]}"), poolNum);
    }
    private Transform UISlider;
    private List<GameObject> _allTypeSlider;
    private List<List<SliderControllerBasic>> _allTypeSliderPool;
    private List<List<SliderControllerBasic>> _allTypeSliderShow;

    private int _sliderCountShow;
    public void Initialize()
    {

    }
    public void ToStart()
    {
        _sliderCountShow = 0;
    }
    public void ToEnd()
    {
        _sliderCountShow = 0;
        for (int i = 0; i < _allTypeSliderShow.Count; i++)
        {
            for (int j = _allTypeSliderShow[i].Count - 1; j >= 0; j--)
            {
                _allTypeSliderShow[i][j].ReturnSlider();
            }
        }
    }
    private async UniTaskVoid SliderUpdate()
    {
        while (_sliderCountShow > 0)
        {
            for (int i = 0; i < _allTypeSliderShow.Count; i++)
            {
                for (int j = 0; j < _allTypeSliderShow[i].Count; j++)
                {
                    _allTypeSliderShow[i][j].FixedUpdate();
                }
            }
            await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
        }
    }
    /// <summary>
    /// add sometype slider
    /// </summary>
    /// <param name="sliderType"></param>
    /// <param name="poolNum"></param>
    /// <returns>slider type index</returns>
    public int AddSliderType<T>(GameObject sliderType, int poolNum) where T : SliderControllerBasic, new()
    {
        sliderType.SetActive(false);
        _allTypeSlider.Add(sliderType);
        List<SliderControllerBasic> thisTypePool = new List<SliderControllerBasic>();
        List<SliderControllerBasic> thisTypeShow = new List<SliderControllerBasic>();
        for (int i = 0; i < poolNum; i++)
        {
            GameObject slider = Object.Instantiate(sliderType, UISlider);
            T sc = new T();
            sc.SliderInitialize(slider);
            thisTypePool.Add(sc);
        }
        _allTypeSliderPool.Add(thisTypePool);
        _allTypeSliderShow.Add(thisTypeShow);
        return _allTypeSlider.Count - 1;
    }
    /// <summary>
    /// ����UI��
    /// </summary>
    /// <param name="hostEntity">Ŀ��ʵ��</param>
    /// <param name="type">�����ͣ�0-����HP��1-��ɫHP��2-BOSSHP��3-����SP��4-��ɫSP��5-MOVEELEMENT��6-STATICELEMENT</param>
    public void SetSlider<T>(Entity hostEntity, float smoothSpeed, int type, int positionLayer, bool hideWhenFull, bool moveSlider) where T : SliderControllerBasic, new()
    {
        if (_allTypeSliderPool[type].Count > 0)
        {
            _allTypeSliderPool[type][0].SetHostEntity(hostEntity, smoothSpeed, type, positionLayer, hideWhenFull, moveSlider);
            _allTypeSliderShow[type].Add(_allTypeSliderPool[type][0]);
            _allTypeSliderPool[type].RemoveAt(0);
        }
        else
        {
            GameObject slider = Object.Instantiate(_allTypeSlider[type], UISlider);
            T sc = new T();
            sc.SliderInitialize(slider);
            sc.SetHostEntity(hostEntity, smoothSpeed, type, positionLayer, hideWhenFull, moveSlider);
            _allTypeSliderShow[type].Add(sc);
        }
        _sliderCountShow++;
        if (_sliderCountShow == 1)
        {
            SliderUpdate().Forget();
        }
    }
    public void ReturnSlider(SliderControllerBasic sliderControllerBasic, int type)
    {
        _allTypeSliderShow[type].Remove(sliderControllerBasic);
        _allTypeSliderPool[type].Add(sliderControllerBasic);
        _sliderCountShow--;
    }
    public void TakeOverSliderMove()
    {
        for (int i = 0; i < _allTypeSliderShow.Count; i++)
        {
            for (int j = 0; j < _allTypeSliderShow[i].Count; j++)
            {
                _allTypeSliderShow[i][j].Move();
            }
        }
    }

}

