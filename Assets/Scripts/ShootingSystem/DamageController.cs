using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DamageController : MonoBehaviour, IDamageable
{
    public void TakeDamage(DamageContext damage)
    {
        Debug.Log($"Получен урон: {damage.Damage.Total.ToString()} от объекта {damage.Source.name}, критический урон {damage.Damage.CriticalDamage.ToString()} единиц статуса: {damage.Damage.StatusBuildup.ToString()}");
    }
}