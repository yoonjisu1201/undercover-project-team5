using Unity.Netcode;
using UnityEngine;

// 서버 권위로 실제 이동하는 에일리언샷건의 탄알. 경로상 처음 맞은 것에서 사라지며,
// 아무것도 맞지 않으면 사거리 끝에서 스스로 디스폰한다.
//
// 표적 판정은 비어 있다. 외계인 복제체가 유일한 표적이었고 그 시스템을 제거했다.
public class AlienBulletProjectile : NetworkBehaviour
{
    [SerializeField, Min(0.1f)] private float _speed = 80f;
    [SerializeField] private LayerMask _hitMask = ~0;

    private Vector3 _direction;
    private float _remainingRange;
    private float _damage;
    private bool _isDespawning;

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
            SafeDespawn();
        }
    }

    // 무엇에 맞았든 탄알은 사라진다.
    //
    // 원래 유일한 표적이 외계인 복제체였는데 그 시스템을 제거했다. 지금은 맞아도 피해가 없다.
    // 샷건을 어디에 쓸지 정해지면 여기에 그 표적 판정을 넣는다.
    private void HandleHit(Collider hitCollider)
    {
        SafeDespawn();
    }

    private void SafeDespawn()
    {
        if (_isDespawning)
        {
            return;
        }

        _isDespawning = true;

        if (TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned)
        {
            networkObject.Despawn(destroy: true);
            return;
        }

        Destroy(gameObject);
    }
}
