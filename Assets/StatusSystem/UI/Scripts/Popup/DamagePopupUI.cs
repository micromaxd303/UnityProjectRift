using TMPro;
using UnityEngine;

public class DamagePopupUI : MonoBehaviour
{
    public AnimationCurve colorCurve;
    public AnimationCurve positionYCurve;
    public AnimationCurve sizeCurve;
    public float speed = 1f;
    public float lifetime = 1f;
    public float fontSize = 10f;
    public TextMeshPro tmp;

    public Color criticalDamage;

    public Color[] ProjectileColors;

    private Vector3 position;
    private float time = 0f;

    public void SetValue(DamageContext damage)
    {
        tmp.text = damage.Damage.Total.ToString();
        tmp.fontStyle = damage.Damage.criticalDamage.isCritical ? FontStyles.Bold : FontStyles.Normal;
        if (damage.Damage.criticalDamage.isCritical)
        {
            tmp.color = criticalDamage;
        }
        else
        {
            tmp.color = ProjectileColors[(byte)damage.Damage.damageType];
        }

        position = damage.hitPoint;
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        if (tmp == null) return;
        time += Time.deltaTime;
        transform.LookAt(Camera.main.transform);

        transform.position = position + Vector3.up * positionYCurve.Evaluate(time * speed);
        tmp.fontSize = fontSize * sizeCurve.Evaluate(time * speed);
        tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, colorCurve.Evaluate(time * speed));
    }
}
