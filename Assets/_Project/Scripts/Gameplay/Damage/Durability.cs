using System;
using UnityEngine;

[DisallowMultipleComponent]
public class Durability : MonoBehaviour, IDamageable
{
    [SerializeField, Min(1f)] private float maxDurability = 100f;
    [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.3f;

    private bool _isDestroyed;
    private bool _criticalStateRaised;
    private float _currentDurability;

    public event Action<Durability> DurabilityChanged;
    public event Action<Durability> CriticalStateEntered;
    public event Action<Durability> Destroyed;

    public bool IsDestroyed => _isDestroyed;
    public float CurrentDurability => _currentDurability;
    public float MaxDurability => maxDurability;
    public float NormalizedDurability => maxDurability <= 0f ? 0f : Mathf.Clamp01(_currentDurability / maxDurability);
    public bool IsCritical => !_isDestroyed && _currentDurability <= maxDurability * criticalThreshold;

    private void Awake()
    {
        ResetDurability();
    }

    public void ResetDurability()
    {
        _isDestroyed = false;
        _criticalStateRaised = false;
        _currentDurability = Mathf.Max(1f, maxDurability);
        DurabilityChanged?.Invoke(this);
    }

    public void ApplyDamage(float amount, Vector3 hitPoint, Vector3 impulse, UnityEngine.Object source)
    {
        if (_isDestroyed || amount <= 0f)
        {
            return;
        }

        _currentDurability = Mathf.Max(0f, _currentDurability - amount);
        DurabilityChanged?.Invoke(this);

        if (!_criticalStateRaised && IsCritical)
        {
            _criticalStateRaised = true;
            CriticalStateEntered?.Invoke(this);
        }

        if (_currentDurability <= 0f)
        {
            _isDestroyed = true;
            Destroyed?.Invoke(this);
        }
    }
}
