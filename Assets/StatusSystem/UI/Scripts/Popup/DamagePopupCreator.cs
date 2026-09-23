using UnityEngine;

public class DamagePopupCreator : MonoBehaviour
{
    [SerializeField]
    private DamagePopupUI prefab;

    public void CreatePopup(in DamageContext damage)
    {
        GameObject popup = Instantiate(prefab.gameObject, damage.hitPoint, Quaternion.identity);
        DamagePopupUI script = popup.GetComponent<DamagePopupUI>();
        if (script == null)
        {
            Debug.LogError("DamagePopupUI component not found on the prefab.");
            return;
        }
        if (damage.hitPoint == Vector3.zero)
        {
            script.SetValue(new DamageContext
                (
                damage.Damage, 
                damage.Source, 
                transform.position, 
                Vector3.up
                ));
            return;
        }
        script.SetValue(damage);
    }
}
