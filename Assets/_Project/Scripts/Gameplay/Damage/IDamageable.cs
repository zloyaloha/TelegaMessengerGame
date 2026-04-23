using UnityEngine;

public interface IDamageable
{
    bool IsDestroyed { get; }
    float CurrentDurability { get; }
    float MaxDurability { get; }
    void ApplyDamage(float amount, Vector3 hitPoint, Vector3 impulse, Object source);
}
