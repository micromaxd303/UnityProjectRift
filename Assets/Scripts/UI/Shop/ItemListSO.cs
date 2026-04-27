using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ItemListSO", menuName = "Scriptable Objects/ItemListSO")]
public class ItemListSO : ScriptableObject
{
    public List<ItemWeaponUI> weaponsArray;
    public List<ItemEquipmentUI> equipmentArray;
}
