using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
// 네트워크 아이템의 Rigidbody를 서버에서만 시뮬레이션하고,
// 바닥에 안정적으로 안착한 뒤 Kinematic 상태로 고정한다.
public sealed class ItemRigidbodySettler : NetworkBehaviour
{
    // 바닥 접촉이 이 시간 동안 유지되면 안착한 것으로 판정한다.
    private const float SettleDuration = 1f;

    private Rigidbody _rigidbody;
    private bool _hasGroundContact;
    private Vector3 _groundNormal = Vector3.up;
    private Vector3 _groundPoint;
    private float _stableDuration;

    // 네트워크 스폰 시 서버와 클라이언트의 Rigidbody 역할을 구분한다.
    // 서버는 물리를 계산하고, 클라이언트는 Kinematic 상태로 서버 위치만 따른다.
    public override void OnNetworkSpawn()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
        {
            Debug.LogError($"[ItemRigidbodySettler] '{name}'에 Rigidbody가 없습니다.", this);
            return;
        }

        if (!IsServer)
        {
            _rigidbody.isKinematic = true;
            return;
        }

        _rigidbody.isKinematic = false;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
    }

    // 매 물리 프레임마다 바닥 접촉이 유지된 시간을 확인한다.
    // 안정 상태가 일정 시간 유지되면 Rigidbody를 Kinematic으로 전환한다.
    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer || _rigidbody == null || _rigidbody.isKinematic)
        {
            return;
        }

        _stableDuration = _hasGroundContact
            ? _stableDuration + Time.fixedDeltaTime
            : 0f;
        _hasGroundContact = false;

        if (_stableDuration < SettleDuration)
        {
            return;
        }

        Quaternion groundAlignment = Quaternion.FromToRotation(transform.up, _groundNormal);

        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.position =
            _groundPoint + groundAlignment * (_rigidbody.position - _groundPoint);
        _rigidbody.rotation = groundAlignment * _rigidbody.rotation;
        _rigidbody.isKinematic = true;
    }

    // 위쪽을 향하는 충돌 법선을 감지해 아이템이 바닥 위에 있는지 기록한다.
    // 실제 안착 판정은 FixedUpdate에서 접촉이 유지된 시간을 검사한다.
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
