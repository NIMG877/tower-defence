using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

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
        _sliderPools = new List<ObjectPool<SliderControllerBasic>>();
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
    // 每个血条类型一个 ObjectPool：GameObject 与控制器一起由 createFunc 创建
    private List<ObjectPool<SliderControllerBasic>> _sliderPools;
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
        List<SliderControllerBasic> thisTypeShow = new List<SliderControllerBasic>();
        GameObject template = sliderType;
        Transform parent = UISlider;
        // 池跨关存活、从不 Clear（不设 actionOnDestroy），与原行为一致
        ObjectPool<SliderControllerBasic> pool = new ObjectPool<SliderControllerBasic>(
            () =>
            {
                GameObject slider = Object.Instantiate(template, parent);
                T sc = new T();
                sc.SliderInitialize(slider);
                return sc;
            },
            collectionCheck: true);
        for (int i = 0; i < poolNum; i++)
        {
            pool.Release(pool.Get()); // 预热：实例化后直接入空闲栈（模板已失活，克隆同态）
        }
        _sliderPools.Add(pool);
        _allTypeSliderShow.Add(thisTypeShow);
        return _allTypeSlider.Count - 1;
    }
    /// <summary>
    /// 从 type 对应的滑条池取一个滑条绑定到 hostEntity 开始显示;
    /// 显示中的首个滑条会启动统一刷新循环。
    /// </summary>
    /// <param name="hostEntity">目标实体(滑条宿主)</param>
    /// <param name="type">滑条池槽位。0-4 为构造函数注册的固定池:0=HPSlider_Enemy、
    /// 1=HPSlider_Turret、2=HPSlider_Enemy(与槽位 0 同一 prefab,并非 BOSS 血条)、
    /// 3/4=SPSlider_normal;type≥5 无固定池,须先经 AddSliderType 动态注册后方可使用,
    /// 传入未注册的值会越界。</param>
    public void SetSlider(Entity hostEntity, float smoothSpeed, int type, int positionLayer, bool hideWhenFull, bool moveSlider)
    {
        SliderControllerBasic sc = _sliderPools[type].Get();
        sc.SetHostEntity(hostEntity, smoothSpeed, type, positionLayer, hideWhenFull, moveSlider);
        _allTypeSliderShow[type].Add(sc);
        _sliderCountShow++;
        if (_sliderCountShow == 1)
        {
            SliderUpdate().Forget();
        }
    }
    public void ReturnSlider(SliderControllerBasic sliderControllerBasic, int type)
    {
        _allTypeSliderShow[type].Remove(sliderControllerBasic);
        _sliderPools[type].Release(sliderControllerBasic);
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

