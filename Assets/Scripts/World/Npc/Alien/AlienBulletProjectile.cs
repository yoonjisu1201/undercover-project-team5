using Unity.Netcode;
using UnityEngine;

// 서버 권위로 실제 이동하는 에일리언샷건의 탄알. 경로상 처음 맞은 대상에게 즉시 데미지를 주고
// 사라지며, 아무것도 맞지 않으면 사거리 끝에서 스스로 디스폰한다.
public class AlienBulletProjectile : NetworkBehaviour
{
    [SerializeField, Min(0.1f)] private float _speed = 80f;
    [SerializeField] private LayerMask _hitMask = ~0;

    private Vector3 _direction;
    private float _remainingRange;
    private float _damage;

    // 서버가 스폰 직후 호출해 이동 방향/사거리/데미지를 설정한다.
    public void Initialize(Vector3 direction, float range, float damage)
    {
        _direction = direction.normalized;
        _remainingRange = range;
        _damage = damage;
    }

    // 서버에서만 레이캐스트에 무언가 맞았는지 확인한다.
    private void Update()
    {
        if (!IsServer || !IsSpawned) return;

        float step = _speed * Time.deltaTime;
        Vector3 previousPosition = transform.position;

        if (Physics.Raycast(previousPosition, _direction, out RaycastHit hit, step, _hitMask, QueryTriggerInteraction.Ignore))
        {
            HandleHit(hit.collider);
            return;
        }

        transform.position = previousPosition + _direction * step;
        _remainingRange -= step;

        if (_remainingRange <= 0f)
        {
            NetworkObject.Despawn(destroy: true);
        }
    }

    // 맞은 대상이 외계인 복제체면 데미지를 주고, 무엇에 맞았든 탄알은 사라진다.
    private void HandleHit(Collider hitCollider)
    {
        if (hitCollider.GetComponentInParent<AlienCloneHealth>() is { } alienHealth)
        {
            alienHealth.TakeDamage(_damage);
        }

        NetworkObject.Despawn(destroy: true);
    }
}
