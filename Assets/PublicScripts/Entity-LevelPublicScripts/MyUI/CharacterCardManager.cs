using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;


public class CharacterCardManager
{
    private struct CharacterCardElements
    {
        public Button CharacterButton;
        public Image Bk, Photo, Light, Lh, Uh, ClassImg, SkillImg;
        public TextMeshProUGUI Name;
        public CharacterCardElements(Button characterButton)
        {
            CharacterButton = characterButton;
            Bk = characterButton.transform.Find("bk").GetComponent<Image>();
            Photo = characterButton.transform.Find("photo").GetComponent<Image>();
            Light = characterButton.transform.Find("light").GetComponent<Image>();
            Lh = characterButton.transform.Find("lh").GetComponent<Image>();
            Uh = characterButton.transform.Find("uh").GetComponent<Image>();
            ClassImg = characterButton.transform.Find("classImg").GetComponent<Image>();
            SkillImg = characterButton.transform.Find("skillImg").GetComponent<Image>();
            Name = characterButton.transform.Find("name").GetComponent<TextMeshProUGUI>();
        }

    }
    private Sprite[] _lh, _uh, _light, _bk, _class;
    private Sprite _noneSkill;
    private Dictionary<EntityID, EntityData> _characterOwn;
    private Dictionary<EntityID, CharacterCardElements> _cardsForbidNull;
    private Button _cardForbidNullPrefab;
    private static CharacterCardManager _instance;
    public static CharacterCardManager cardManager
    {
        get
        {
            if (_instance == null)
            {
                _instance = new CharacterCardManager();
            }
            return _instance;
        }
    }
    private CharacterCardManager()
    {
        _lh = new Sprite[6];
        _uh = new Sprite[6];
        _light = new Sprite[6];
        _bk = new Sprite[6];
        _class = new Sprite[8];
        for (int i = 0; i < 6; i++)
        {
            _lh[i] = Resources.Load<Sprite>($"Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/lh_{i}");
            _uh[i] = Resources.Load<Sprite>($"Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/uh_{i}");
            _light[i] = Resources.Load<Sprite>($"Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/light_{i}");
            _bk[i] = Resources.Load<Sprite>($"Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/bk_{i}");
        }
        for (int i = 0; i < 8; i++)
        {
            _class[i] = Resources.Load<Sprite>($"Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/class_{i}");
        }
        _noneSkill = Resources.Load<Sprite>("Prefabs/UI/MyUIs/UISprites/CharacterHandSprites/none_black");
        EntityID[] characterOwn_id = SaveSystem.Current.charactersOwn.ToArray();
        _characterOwn = new Dictionary<EntityID, EntityData>();
        for (int i = 0; i < characterOwn_id.Length; i++)
        {
            _characterOwn.Add(characterOwn_id[i], GameDataService.EntityRepository.Get(characterOwn_id[i]));
        }
        _cardsForbidNull = new Dictionary<EntityID, CharacterCardElements>();
        _cardForbidNullPrefab = Resources.Load<Button>("Prefabs/UI/MyUIs/Components/characterCard");
    }
    public void SetCharacterCardAllowNull(Button card, EntityID characterID)
    {
        GameObject nullshow = card.transform.Find("nullShow").GetComponent<Transform>().gameObject;
        Image bkNull = card.transform.Find("nullShow/bknull").GetComponent<Image>();
        Image bk = card.transform.Find("bk").GetComponent<Image>();
        Image photo = card.transform.Find("photo").GetComponent<Image>();
        Image light = card.transform.Find("light").GetComponent<Image>();
        Image lh = card.transform.Find("lh").GetComponent<Image>();
        Image uh = card.transform.Find("uh").GetComponent<Image>();
        Image classImg = card.transform.Find("classImg").GetComponent<Image>();
        Image skillImg = card.transform.Find("skillImg").GetComponent<Image>();
        TextMeshProUGUI name = card.transform.Find("name").GetComponent<TextMeshProUGUI>();
        if (characterID.ID_C == null)
        {
            nullshow.SetActive(true);
            bk.enabled = false;
            photo.enabled = false;
            light.enabled = false;
            lh.enabled = false;
            uh.enabled = false;
            classImg.enabled = false;
            skillImg.enabled = false;
            name.enabled = false;
            card.targetGraphic = bkNull;
        }
        else
        {
            nullshow.SetActive(false);
            bk.enabled = true;
            photo.enabled = true;
            light.enabled = true;
            lh.enabled = true;
            uh.enabled = true;
            classImg.enabled = true;
            skillImg.enabled = true;
            name.enabled = true;
            EntityData characterData = _characterOwn[characterID];
            int index = characterData.CharacterRarity - 1;
            bk.sprite = _bk[index];
            photo.sprite = characterData.HalfBodyImage;
            light.sprite = _light[index];
            lh.sprite = _lh[index];
            uh.sprite = _uh[index];
            classImg.sprite = _class[characterData.CharacterJob];
            var abilities = characterData.Skills;
            if (abilities != null && abilities.Count > 0 && abilities[0].icon != null)
            {
                skillImg.sprite = abilities[0].icon;
            }
            else
            {
                skillImg.sprite = _noneSkill;
            }
            name.text = _characterOwn[characterID].ChineseName;
            card.targetGraphic = photo;
        }

    }
    public Button InstantiateCardForbidNull(Transform toTransform, EntityID characterId)
    {
        if (!_cardsForbidNull.ContainsKey(characterId))
        {
            if (_characterOwn.ContainsKey(characterId))
            {
                CharacterCardElements card = new CharacterCardElements(Object.Instantiate(_cardForbidNullPrefab, toTransform));
                int index = _characterOwn[characterId].CharacterRarity - 1;
                card.Bk.sprite = _bk[index];
                card.Photo.sprite = _characterOwn[characterId].HalfBodyImage;
                card.Light.sprite = _light[index];
                card.Lh.sprite = _lh[index];
                card.Uh.sprite = _uh[index];
                card.ClassImg.sprite = _class[_characterOwn[characterId].CharacterJob];
                card.Name.text = _characterOwn[characterId].ChineseName;
                _cardsForbidNull.Add(characterId, card);
                return card.CharacterButton;
            }
            return null;
        }
        else
        {
            Button card = _cardsForbidNull[characterId].CharacterButton;
            card.transform.SetParent(toTransform);
            card.onClick.RemoveAllListeners();
            return card;
        }
    }
    public Button GetCardForbidNull(EntityID characterId)
    {
        return _cardsForbidNull[characterId].CharacterButton;
    }
    public void ResetCardForbidNullSkill(EntityID characterId)
    {
    }
    public EntityData GetCharacterAttribute(EntityID characterId)
    {
        return _characterOwn[characterId];
    }

}
