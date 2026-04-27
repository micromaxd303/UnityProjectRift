using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ShopController : MonoBehaviour
{
    [Header("Настройки списков")]
    [SerializeField, Tooltip("Вкладка оружия")]
    private UITab WeaponTab;

    [SerializeField, Tooltip("Вкладка снаряжения")]
    private UITab EquipmentTab;

    [Space(5), Header("Цвета")]
    [SerializeField, Tooltip("Цвет рамки по умолчанию")]
    private Color BorderDefault;

    [SerializeField, Tooltip("Цвет выделения")]
    private Color BorderSelected;

    [Space(5), Header("Префабы")]
    [SerializeField, Tooltip("Префаб карты товара")]
    private GameObject ItemCardPrefab;

    [Space(5), Header("Окно информации")]
    [SerializeField, Tooltip("Текст для название товара")]
    private TMPro.TextMeshProUGUI InfoNameText;

    [SerializeField, Tooltip("Текст стоимости товара")]
    private TMPro.TextMeshProUGUI InfoPriceText;

    [Space(3)]
    [SerializeField, Tooltip("Объект вкладки информации об оружии (для скрывания)")]
    private GameObject WeaponInfoObject;

    [SerializeField, Tooltip("Скрипт динамического создания сегментов звезды свойств")]
    private UIPropertyStarCreator WeaponInfoStarCreator;

    [Space(3)]
    [SerializeField, Tooltip("Объект вкладки информации о снаряжении (для скрывания)")]
    private GameObject EquipmentInfoObject;

    [Space(5), Header("Товары")]
    [SerializeField, Tooltip("Список items для магазина")]
    public ItemListSO Items;

    private int CurrentTab = 0;

    private void Awake()
    {
        UpdateList(WeaponTab, Items.weaponsArray, WeaponCardOnClick);
        UpdateList(EquipmentTab, Items.equipmentArray, EquipmentCardOnClick);
        SwitchTab(CurrentTab);
    }

    private void UpdateList(UITab Tab, IEnumerable<ItemUI> ItemArray, UnityAction<int> onClick)
    {
        float cardHeight = ItemCardPrefab.GetComponent<RectTransform>().sizeDelta.y;
        float offset = 4f;

        if (!Tab.AnyIsNull())
        {
            for (int i = 0; i < ItemArray.Count(); i++)
            {
                ItemUI item = ItemArray.ElementAtOrDefault(i);
                Vector3 position = (Tab.ContentObject.transform.position - Vector3.up * (cardHeight + offset) * i) - Vector3.up * offset;
                GameObject Card = Instantiate(ItemCardPrefab,
                    position,
                    Quaternion.identity,
                    Tab.ContentObject.transform);
                ItemCardUI itemCard = Card.GetComponent<ItemCardUI>();
                if (itemCard != null)
                {
                    itemCard.PriceText.text = item.price.ToString();
                    itemCard.Icon.sprite = item.icon;
                    itemCard.index = i;
                    itemCard.buttonOnClick.AddListener(onClick);
                }
                Tab.ItemCards.Add(itemCard);
            }
            Tab.ContentObject.GetComponent<RectTransform>().sizeDelta =
                new Vector2(
                    Tab.ContentObject.GetComponent<RectTransform>().sizeDelta.x,
                    (cardHeight + offset) * ItemArray.Count() + offset
                    );
        }
    }

    public void SwitchTab(int index)
    {
        CurrentTab = index;
        WeaponTab.SetActive(index == 0);
        EquipmentTab.SetActive(index == 1);
        if (index == 0) WeaponCardOnClick(WeaponTab.CurrentObject);
        else if (index == 1) EquipmentCardOnClick(EquipmentTab.CurrentObject);
    }

    private void WeaponCardOnClick(int index)
    {
        WeaponTab.CurrentObject = index;
        WeaponTab.UpdateSelected(BorderSelected, BorderDefault);
        UpdateInfo();
    }
    private void EquipmentCardOnClick(int index)
    {
        EquipmentTab.CurrentObject = index;
        EquipmentTab.UpdateSelected(BorderSelected, BorderDefault);
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        ItemUI currentObject = null;
        if (CurrentTab == 0 && WeaponTab.CurrentObject < Items.weaponsArray.Count) currentObject = Items.weaponsArray[WeaponTab.CurrentObject];
        else if (CurrentTab == 1 && EquipmentTab.CurrentObject < Items.equipmentArray.Count) currentObject = Items.equipmentArray[EquipmentTab.CurrentObject];
        else
        {
            if (InfoNameText) InfoNameText.text = "Empty";
            if (WeaponInfoObject) WeaponInfoObject.SetActive(false);
            if (EquipmentInfoObject) EquipmentInfoObject.SetActive(false);
            return;
        }

        if (WeaponInfoObject) WeaponInfoObject.SetActive(CurrentTab == 0);
        if (EquipmentInfoObject) EquipmentInfoObject.SetActive(CurrentTab == 1);

        if (InfoNameText) InfoNameText.text = currentObject.name;
        if (InfoPriceText) InfoPriceText.text = currentObject.price.ToString();

        if (CurrentTab == 0)
        {
            if (WeaponInfoStarCreator) WeaponInfoStarCreator.CreateStar(Items.weaponsArray[WeaponTab.CurrentObject].properties);
        }
    }


    [System.Serializable]
    public class UITab
    {
        public GameObject TabObject;
        public GameObject ContentObject;
        public GameObject ButtonObject;

        [HideInInspector] public int CurrentObject = 0;
        [HideInInspector] public List<ItemCardUI> ItemCards;

        [SerializeField, Tooltip("Цвет выбраной вкладки для кнопки")]
        private Color BorderSelectedColor;

        [SerializeField, Tooltip("Цвет неактивной вкладки для кнопки")]
        private Color BorderDefaultColor;

        public bool AnyIsNull()
        {
            if (TabObject == null) return true;
            if (ContentObject == null) return true;
            if (ButtonObject == null) return true;
            return false;
        }

        public void UpdateSelected(Color Selected, Color Default)
        {
            for (int i = 0; i <  ItemCards.Count; i++)
            {
                ItemCards[i].SetBorderColor(i == CurrentObject ?  Selected : Default);
            }
        }

        public void SetActive(bool value)
        {
            if (TabObject) TabObject.SetActive(value);
            if (ButtonObject && ButtonObject.GetComponent<Image>()) ButtonObject.GetComponent<Image>().color = value ? BorderSelectedColor : BorderDefaultColor;
        }
    }
}
