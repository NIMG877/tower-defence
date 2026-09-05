using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MyUI;
using Cysharp.Threading.Tasks;
using UnityEngine.UI;
using UnityEditorInternal;
using static UnityEngine.EventSystems.EventTrigger;

public class MCEnvironmentalDevice : MonoBehaviour, IManagerStartEnd
{
    /// <summary>
    /// 񣩱HungryRate>100ٶ빥ֵٻָе㱥80<HungryRate<=100ֵָ󣩳̬30<HungryRate<=80Чո0<HungryRate<=30ٶ빥½HungryRate=0ٶ빥½һ۳ֵ
    /// </summary>
    private class EntityAndHungryMessage
    {

        public Entity Entity;
        /// <summary>
        /// ״̬0-1-2-
        /// </summary>
        public int HungryState;
        private float _hungryValue;
        public float HungryValue
        {
            get
            {
                return _hungryValue;
            }
            set
            {
                _hungryValue = value;
                if (_hungryValue < 0)
                {
                    _hungryValue = 0;
                }
                else if (_hungryValue > 130)
                {
                    _hungryValue = 130;
                }
            }
        }
        private float _operationTimer;
        private Buff _hungryBuff;

        // 0=Attack(AddPercent), 1=AttackSpeed(AddFlat)
        private Modifier[] _fullBuffValue = new Modifier[2] { new Modifier("Attack", ModifierOp.AddPercent, 0.15f), new Modifier("AttackSpeed", ModifierOp.AddFlat, 30f) };
        private Modifier[] _hungryBuffValue = new Modifier[2] { new Modifier("Attack", ModifierOp.AddPercent, -0.15f), new Modifier("AttackSpeed", ModifierOp.AddFlat, -30f) };
        private Modifier[] _normalBuffValue = new Modifier[2] { new Modifier("Attack", ModifierOp.AddPercent, 0f), new Modifier("AttackSpeed", ModifierOp.AddFlat, 0f) };

        public EntityAndHungryMessage(Entity entity, float hungryValue, int type)
        {
            Entity = entity;
            HungryValue = hungryValue;
            HungryState = type;
            _operationTimer = 1;
            _hungryBuff = entity.buffController.CreateBuff(new Modifier[2] { new Modifier("Attack", ModifierOp.AddPercent, 0.15f), new Modifier("AttackSpeed", ModifierOp.AddFlat, 30f) }, null, "hungryBuff", -5, BuffScope.WhiteList);
        }
        public void Update()
        {
            _operationTimer -= Time.fixedDeltaTime;
            HungryValue -= Time.fixedDeltaTime * 0.15f;
            if (_operationTimer <= 0)
            {
                _operationTimer += 1;
                if (HungryValue > 100)
                {
                    if (Entity.Stats.CurrentHpRate < 1)
                    {
                        Entity.Stats.ApplyDamage(null, 200, 1, 0, 0, 0, 0, 3, 2);
                        HungryValue -= 2;
                    }
                }
                else if (HungryValue > 80)
                {
                    if (Entity.Stats.CurrentHpRate < 1)
                    {
                        Entity.Stats.ApplyDamage(null, 40, 1, 0, 0, 0, 0, 3, 2);
                        HungryValue -= 0.4f;
                    }
                }
                else if (HungryValue == 0)
                {
                    Entity.Stats.ApplyDamage(null, 50, 1, 0, 0, 0, 0, 2, 2);
                }
            }
            if (HungryValue > 100)
            {
                if (_hungryBuff.modifiers[0].magnitude != _fullBuffValue[0].magnitude)
                {
                    Entity.buffController.SetBuffValues(_fullBuffValue, _hungryBuff);
                }
            }
            else if (HungryValue > 30)
            {
                if (_hungryBuff.modifiers[0].magnitude != _normalBuffValue[0].magnitude)
                {
                    Entity.buffController.SetBuffValues(_normalBuffValue, _hungryBuff);
                }
            }
            else
            {
                if (_hungryBuff.modifiers[0].magnitude != _hungryBuffValue[0].magnitude)
                {
                    Entity.buffController.SetBuffValues(_hungryBuffValue, _hungryBuff);
                }
            }
        }
    }
    private List<EntityAndHungryMessage> _entityAndHungryMessages;
    private static MCEnvironmentalDevice _instance;
    public static MCEnvironmentalDevice MCED { get { return _instance; } }
    public Sprite[][] StatImages;
    private int _hungrySliderType;
    public void Initialize()
    {
        _entityAndHungryMessages = new List<EntityAndHungryMessage>();
        _instance = this;
        _hungrySliderType = SlidersManager.Manager.AddSliderType<HungrySliderController>(Resources.Load<GameObject>("Prefabs/Levels/Main/AdventureOfMinecraft/MCEnvironmentalDevice/HungrySlider_Static"), 10);
        StatImages = new Sprite[3][];
        Sprite[] sprites = Resources.LoadAll<Sprite>("Prefabs/Levels/Main/AdventureOfMinecraft/imgs/hungryicon");
        StatImages[0] = new Sprite[2] { sprites[2], sprites[3] };
        StatImages[1] = new Sprite[2] { sprites[4], sprites[5] };
        StatImages[2] = new Sprite[2] { sprites[0], sprites[1] };
    }
    public void ToEnd()
    {
        _entityAndHungryMessages.Clear();
    }
    public async UniTaskVoid EntityAndHungryMessageUpdate()
    {
        while (_entityAndHungryMessages.Count > 0)
        {
            for (int i = _entityAndHungryMessages.Count - 1; i >= 0; i--)
            {
                if (_entityAndHungryMessages[i].Entity.Stats.IsActive == false)
                {
                    _entityAndHungryMessages.RemoveAt(i);
                }
                else
                {
                    _entityAndHungryMessages[i].Update();
                }
            }
            await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
        }
    }
    public void ToStart()
    {
        GameObject beef = Resources.Load<GameObject>("Prefabs/Levels/Main/AdventureOfMinecraft/Devices/Meats/Beef");
        EntityManager.Manager.OnAfterSetEntity += (Entity setEntity) =>
        {
            if (setEntity.Camp == 1 && setEntity.TryGetComponent(out InteractableStatic staticEntity) && setEntity.EntityData.CharacterJob != 8)
            {
                EntityAndHungryMessage thisMessage = new EntityAndHungryMessage(setEntity, 130, 0);
                SlidersManager.Manager.SetSlider<HungrySliderController>(setEntity, 4, _hungrySliderType, 2, false, false);
                setEntity.AttackBase.OnAttackSuccessfully += () =>
                {
                    if (thisMessage.HungryState != 1)
                    {
                        thisMessage.HungryValue -= 1;
                    }
                    else
                    {
                        thisMessage.HungryValue -= 5;
                    }
                };
                _entityAndHungryMessages.Add(thisMessage);
                if (_entityAndHungryMessages.Count == 1)
                {
                    EntityAndHungryMessageUpdate().Forget();
                }
            }
        };
    }
    public (float hungryValue, int hungryType) GetHungryValueAndHungryType(Entity entity)
    {
        for (int i = 0; i < _entityAndHungryMessages.Count; i++)
        {
            if (_entityAndHungryMessages[i].Entity == entity)
            {
                return (_entityAndHungryMessages[i].HungryValue, _entityAndHungryMessages[i].HungryState);
            }
        }
        return (0, 0);
    }
    public void EntityEatSomething(Entity entityToEat, float hungryValue)
    {
        for (int i = 0; i < _entityAndHungryMessages.Count; i++)
        {
            if (_entityAndHungryMessages[i].Entity == entityToEat && _entityAndHungryMessages[i].HungryValue < 100)
            {
                _entityAndHungryMessages[i].HungryValue += hungryValue;
            }
        }
    }
}
public class HungrySliderController : SliderControllerBasic
{
    private RectTransform _fullSlider;
    private Image _image;
    public override void SliderInitialize(GameObject sliderObject)
    {
        base.SliderInitialize(sliderObject);
        _fullSlider = _slider.transform.Find("FULLFill").GetComponent<RectTransform>();
        _image = _slider.transform.Find("Image").GetComponent<Image>();
    }
    protected override void SetRateOperations()
    {
        (float value, int type) = MCEnvironmentalDevice.MCED.GetHungryValueAndHungryType(_hostEntity);
        value = value / 100;
        if (value > 0.3)
        {
            _image.sprite = MCEnvironmentalDevice.MCED.StatImages[type][0];
        }
        else
        {
            _image.sprite = MCEnvironmentalDevice.MCED.StatImages[type][1];
        }
        if (value > 1)
        {
            SetRate(1);
            if (!_fullSlider.gameObject.activeSelf)
            {
                _fullSlider.gameObject.SetActive(true);
            }
            _fullSlider.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, (value - 1) * _backGround.rectTransform.rect.width);
        }
        else
        {
            if (_fullSlider.gameObject.activeSelf)
            {
                _fullSlider.gameObject.SetActive(false);
            }
            SetRate(value);
        }
    }
}