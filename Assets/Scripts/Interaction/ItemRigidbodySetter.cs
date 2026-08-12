using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
// 네트워크 아이템의 Rigidbody를 서버에서만 시뮬레이션하고,
// 바닥에 안정적으로 안착한 뒤 Kinematic 상태로 고정한다.
public sealed class ItemRigidbodySetter : NetworkBehaviour
{
    // 스폰 후 이 시간이 지나고 바닥에 닿아 있으면 안착한 것으로 판정한다.
    private const float SettleDuration = 1f;

    [SerializeField] private bool _alignToGround = true;

    private Rigidbody _rigidbody;
    private bool _hasGroundContact;
    private Vector3 _groundNormal = Vector3.up;
    private Vector3 _groundPoint;
    private float _spawnElapsed;

    // 네트워크 스폰 시 서버와 클라이언트의 Rigidbody 역할을 구분한다.
    // 서버는 물리를 계산하고, 클라이언트는 Kinematic 상태로 서버 위치만 따른다.
    public override void OnNetworkSpawn()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
        {
            Debug.LogError($"[ItemRigidbodySetter] '{name}'에 Rigidbody가 없습니다.", this);
            return;
        }

        if (!IsServer)
        {
            _rigidbody.isKinematic = true;
            return;
        }

        _rigidbody.isKinematic = false;
        _spawnElapsed = 0f;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
    }

    // 스폰 후 1초가 지났고 현재 바닥에 닿아 있으면 즉시 Kinematic으로 전환한다.
    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer || _rigidbody == null || _rigidbody.isKinematic)
        {
            return;
        }

        _spawnElapsed += Time.fixedDeltaTime;
        bool canSettle = _spawnElapsed >= SettleDuration && _hasGroundContact;
        _hasGroundContact = false;

        if (!canSettle)
        {
            return;
        }

        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;

        // 건전지처럼 고정된 눕힘 회전이 필요한 아이템은 현재 회전을 그대로 유지한다.
        if (_alignToGround)
        {
            Quaternion groundAlignment = Quaternion.FromToRotation(transform.up, _groundNormal);
            _rigidbody.position =
                _groundPoint + groundAlignment * (_rigidbody.position - _groundPoint);
            _rigidbody.rotation = groundAlignment * _rigidbody.rotation;
        }

        _rigidbody.isKinematic = true;
    }

    // 인벤토리에서 다시 꺼내는 등, 이미 스폰된 오브젝트를 재사용할 때 물리를 스폰 직후 상태로 되돌린다.
    // OnNetworkSpawn은 스폰 시 한 번만 불리므로, 재사용 시점엔 이걸 직접 호출해줘야 한다.
    public void Rearm(Vector3 position, Quaternion rotation, Vector3 initialVelocity)
    {
        if (!IsServer || _rigidbody == null)
        {
            return;
        }

        _spawnElapsed = 0f;
        _hasGroundContact = false;
        _rigidbody.position = position;
        _rigidbody.rotation = rotation;
        _rigidbody.isKinematic = false;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
        _rigidbody.WakeUp();
        _rigidbody.linearVelocity = initialVelocity;
        _rigidbody.angularVelocity = Vector3.zero;
    }
    // 인벤토리에 넣는 등, 물리 시뮬레이션을 완전히 멈춰야 할 때 사용한다.
    public void Freeze()
    {
        if (_rigidbody == null)
        {
            return;
        }

        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.isKinematic = true;
    }

    // 위쪽을 향하는 충돌 법선을 감지해 아이템이 바닥 위에 있는지 기록한다.
    // 실제 안착 판정은 FixedUpdate에서 스폰 경과 시간과 함께 검사한다.
    private void OnCollisionStay(Collision collision)
    {
        if (!IsSpawned || !IsServer || _rigidbody == null || _rigidbody.isKinematic)
        {
            return;
        }

        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y > 0.5f)
            {
                _hasGroundContact = true;
                _groundNormal = contact.normal;
                _groundPoint = contact.point;
            }
        }
    }
}
