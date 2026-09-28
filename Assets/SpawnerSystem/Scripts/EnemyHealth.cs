using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    [Header("Здоровье")]
    public float maxHealth = 100f;

    private float currentHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(in DamageContext context)
    {
        // Получаем урон из твоей системы Damage
        float damage = context.Damage.Total;

        currentHealth -= damage;

        Debug.Log(
            gameObject.name +
            " получил " +
            damage +
            " урона. Осталось HP: " +
            currentHealth
        );

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log(
            gameObject.name +
            " уничтожен!"
        );

        Destroy(gameObject);
    }
}