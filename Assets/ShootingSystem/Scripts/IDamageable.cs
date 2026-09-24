using UnityEngine;

public interface IDamageable
{
    public void TakeDamage(in DamageContext context);
}