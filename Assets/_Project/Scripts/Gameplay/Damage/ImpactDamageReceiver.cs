using UnityEngine;

[DisallowMultipleComponent]
public class ImpactDamageReceiver : MonoBehaviour
{
    [SerializeField] private Durability durability;
    [SerializeField] private float minimumImpactVelocity = 2.5f;
    [SerializeField] private float minimumImpulseMagnitude = 0f;
    [SerializeField] private float damagePerVelocityUnit = 6f;
    [SerializeField] private float damagePerImpulseUnit = 0.1f;
    [SerializeField] private float maximumDamagePerHit = 40f;
    [SerializeField] private bool ignoreWhileCarried = true;
    [SerializeField] private bool ignoreGroundLikeContacts = true;
    [SerializeField, Range(0f, 1f)] private float groundNormalThreshold = 0.6f;
    [SerializeField, Min(0f)] private float minimumGroundImpactVelocity = 7f;
    [SerializeField, Min(0f)] private float minimumGroundImpulseMagnitude = 25f;

    private CargoInstance _cargoItem;
    private float _ignoreImpactsUntilTime;

    private void Awake()
    {
        if (durability == null)
        {
            durability = GetComponent<Durability>();
        }

        _cargoItem = GetComponent<CargoInstance>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (durability == null || durability.IsDestroyed)
        {
            return;
        }

        if (Time.time < _ignoreImpactsUntilTime)
        {
            return;
        }

        if (ignoreWhileCarried && _cargoItem != null && _cargoItem.State != CargoState.Free)
        {
            return;
        }

        ContactPoint primaryContact = collision.contactCount > 0
            ? collision.GetContact(0)
            : default;

        Vector3 contactNormal = collision.contactCount > 0
            ? primaryContact.normal.normalized
            : Vector3.up;

        // Only count the component of motion that is actually closing into the contact.
        float impactVelocity = Mathf.Max(0f, -Vector3.Dot(collision.relativeVelocity, contactNormal));
        float impulseMagnitude = Mathf.Max(0f, Vector3.Dot(collision.impulse, contactNormal));

        bool isGroundLikeContact = ignoreGroundLikeContacts
            && collision.rigidbody == null
            && contactNormal.y >= groundNormalThreshold
            && impactVelocity < minimumGroundImpactVelocity
            && impulseMagnitude < minimumGroundImpulseMagnitude;

        if (isGroundLikeContact)
        {
            return;
        }

        if (impactVelocity < minimumImpactVelocity && impulseMagnitude < minimumImpulseMagnitude)
        {
            return;
        }

        float velocityDamage = Mathf.Max(0f, impactVelocity - minimumImpactVelocity) * damagePerVelocityUnit;
        float impulseDamage = Mathf.Max(0f, impulseMagnitude - minimumImpulseMagnitude) * damagePerImpulseUnit;
        float damage = Mathf.Min(maximumDamagePerHit, velocityDamage + impulseDamage);

        if (damage <= 0f)
        {
            return;
        }

        Vector3 hitPoint = collision.contactCount > 0
            ? primaryContact.point
            : transform.position;

        durability.ApplyDamage(damage, hitPoint, collision.impulse, this);
    }

    public void IgnoreImpactsForSeconds(float seconds)
    {
        _ignoreImpactsUntilTime = Mathf.Max(_ignoreImpactsUntilTime, Time.time + Mathf.Max(0f, seconds));
    }
}
