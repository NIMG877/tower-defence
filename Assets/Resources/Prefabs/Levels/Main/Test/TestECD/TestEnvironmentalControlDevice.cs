using System.Collections.Generic;
using UnityEngine;
using MyUI;
using UnityEngine.UI;

public class TestEnvironmentalControlDevice : MonoBehaviour, IManagerStartEnd
{
    private bool _isStart;
    private EntityManager _entityManager;
    [SerializeField] private GameObject _range;
    private (int x, int y)[] _attackRange;
    private (int x, int y) _attackCenter;
    private float _totalSp;
    public float _currentSp;
    private Image _smooth;
    private Image _fill;
    public void ToStart()
    {
        _isStart = true;
        _entityManager = EntityManager.Manager;
        _range.SetActive(false);
        _attackCenter.x = -1;
        _totalSp = 2;
        _attackRange = new (int x, int y)[21] {(0,-2),(0,-1),(0,0), (0,1),(0,2),
                                                (1, -2), (1, -1), (1, 0), (1, 1), (1, 2),
                                                (-1, -2), (-1, -1), (-1, 0), (-1, 1), (-1, 2),
                                                (2,-1),(2,0),(2,1),
                                                (-2,-1),(-2,0),(-2,1)};
        CFPPanel cfp = CFPPanel.Panel;
        _smooth = cfp.Smooth;
        _fill = cfp.Fill;
    }
    public void ToEnd()
    {

    }
    private void FixedUpdate()
    {
        if (!_isStart)
            return;
        List<Entity> entities = _entityManager.EntitySelector_Radius((0, 0), 1, true, -1, false);
        if (entities.Count == 0)
        {
            if (_attackCenter.x != -1)
            {
                _range.SetActive(false);
                _attackCenter.x = -1;
            }
        }
        else
        {
            Entity maxOccupy = entities[0];
            int maxPccupyNum = entities[0].Stats.BlockOccupationS;
            for (int i = 1; i < entities.Count; i++)
            {
                if (entities[i].Stats.BlockOccupationS > maxPccupyNum)
                {
                    maxOccupy = entities[i];
                    maxPccupyNum = entities[i].Stats.BlockOccupationS;
                }
                else if (entities[i].Stats.BlockOccupationS == maxPccupyNum && entities[i].Movement.Priority > maxOccupy.Movement.Priority)
                {
                    maxOccupy = entities[i];
                }
            }
            if (_attackCenter.x == -1)
            {
                _range.SetActive(true);
            }
            _attackCenter = ((int)(maxOccupy.transform.position.x + 0.5), (int)(maxOccupy.transform.position.y + 0.5));
            _range.transform.position = maxOccupy.transform.position;
        }
        if (_currentSp < _totalSp)
        {
            _currentSp += Time.fixedDeltaTime;
            _fill.fillAmount = _currentSp / _totalSp;
        }
        else
        {
            TryToAttack();
        }
        if (_fill.fillAmount > _smooth.fillAmount)
        {
            _smooth.fillAmount = _fill.fillAmount;
        }
        else if (_fill.fillAmount < _smooth.fillAmount)
        {
            if (_smooth.fillAmount - _fill.fillAmount >= 0.005f)
            {
                _smooth.fillAmount -= 4 * (_smooth.fillAmount - _fill.fillAmount) * Time.fixedDeltaTime;
            }
            else
            {
                _smooth.fillAmount = _fill.fillAmount;
            }
        }
    }
    private void TryToAttack()
    {
        if (_attackCenter.x != -1)
        {
            (int x, int y)[] range = MapDataManager.Manager.RangeCaculator(_attackRange, _attackCenter, 0);
            List<Entity> entities = _entityManager.EntitySelector_Range(range, 1, false, 0.5f, false);
            entities.AddRange(_entityManager.EntitySelector_Range(range, 2, false, 0.5f, false));
            for (int i = 0; i < entities.Count; i++)
            {
                entities[i].Stats.ApplyDamage(null, 3000, 1, 0, 0, 0, 0, 2, 0);
            }
            _currentSp = 0;
        }
    }

    public void Initialize()
    {

    }
}




namespace MyUI
{
    using UnityEngine.UI;
    public class CFPPanel : BasePanel
    {
        public Image Smooth;
        public Image Fill;
        private static CFPPanel _instance;

        public static CFPPanel Panel
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CFPPanel();
                }
                return (CFPPanel)_instance;
            }
        }

        private CFPPanel() : base(new UIType("Prefabs/Levels/Main/Test/TestECD/CFP"))
        {
            Smooth = GetComponentInChildrenByPath<Image>("CFPSlider/Smooth");
            Fill = GetComponentInChildrenByPath<Image>("CFPSlider/Fill");
        }
    }
}


