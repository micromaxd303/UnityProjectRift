using UnityEngine;

public class DamagePopupCreator : MonoBehaviour
{
    [SerializeField]
    private DamagePopupUI prefab;

    public void CreatePopup(DamageContext damage)
    {
        GameObject popup = Instantiate(prefab.gameObject, damage.hitPoint, Quaternion.identity);
        DamagePopupUI script = popup.GetComponent<DamagePopupUI>();
        if (damage.hitPoint == Vector3.zero) damage.hitPoint = transform.position;
        if (script) script.SetValue(damage);
    }
}
