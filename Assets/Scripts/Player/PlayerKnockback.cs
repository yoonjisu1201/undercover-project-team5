using System;
using UnityEngine;

// 맞았을 때 뒤로 밀리는 것만 담당한다.
//
// 물리에 맡기지 않는다. 콜라이더가 겹쳐서 밀려나는 것(겹침 해소)은 이동이 아니라 위치를
// 직접 보정하는 것이라, 연속 충돌 판정을 켜 두어도 벽을 그대로 지나간다. 그래서 속도로
// 밀되, 밀려갈 자리에 벽이 없고 발 디딜 곳이 있는지 직접 확인한다.
//
// MonoBehaviour 가 아니다. 물리 스텝마다 이동 처리와 순서가 맞아야 하는데, 컴포넌트로 두면
// FixedUpdate 순서가 정해져 있지 않아 어느 쪽이 속도를 마지막에 쓰는지 알 수 없다.
[Serializable]
public class PlayerKnockback
{
    [Tooltip("맞은 순간의 속도(m/s). 곧바로 줄어들므로 실제로 밀리는 거리는 이보다 짧다.")]
    [SerializeField, Min(0f)] private float _speed = 8f;

    [Tooltip("밀리는 동안 조작이 속도를 덮지 않는 시간(초). 길면 조작을 뺏긴 느낌이 난다.")]
    [SerializeField, Min(0f)] private float _seconds = 0.25f;

    [Tooltip("초당 감쇠율. 클수록 처음만 세게 밀리고 금방 멎는다. 0이면 등속으로 미끄러진다.")]
    [SerializeField, Min(0f)] private float _damping = 12f;

    // 밀려갈 자리를 볼 때 한 스텝 거리에 더하는 여유(m).
    private const float Skin = 0.05f;

    // 그 자리에 몸을 놓아 볼 때 반경을 줄이는 비율. 스치는 접촉까지 막힘으로 치지 않는다.
    private const float CheckShrink = 0.9f;

    // 밀려갈 자리 아래로 바닥을 찾아보는 거리(m). 계단·비탈은 넘어가고 낭떠러지만 걸러낸다.
    private const float GroundProbe = 1.5f;

    private readonly Collider[] _overlaps = new Collider[8];

    private Transform _owner;
    private Rigidbody _rigidbody;
    private CapsuleCollider _body;

    private Vector3 _velocity;
    private float _until;

    // 밀리는 중인지. 이 동안에는 조작이 속도를 덮으면 안 된다.
    public bool IsActive => Time.time < _until;

    public void Initialize(Transform owner, Rigidbody rigidbody, CapsuleCollider body)
    {
        _owner = owner;
        _rigidbody = rigidbody;
        _body = body;
    }

    // 맞은 자리의 반대쪽으로 민다.
    public void Push(Vector3 sourcePosition)
    {
        if (_speed <= 0f || _owner == null)
        {
            return;
        }

        Vector3 away = _owner.position - sourcePosition;
        away.y = 0f;

        if (away.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        _velocity = away.normalized * _speed;
        _until = Time.time + _seconds;
    }

    // 물리 스텝마다 부른다.
    //
    // 속도를 한 번 주고 놔두면 넉백 중에는 마찰이 0이라 끝까지 같은 빠르기로 미끄러진다.
    // 맞아서 튕긴 것이 아니라 밀려나는 것으로 보인다. 처음이 세고 빨리 죽어야 타격으로 읽힌다.
    public void Tick()
    {
        _velocity *= Mathf.Exp(-_damping * Time.fixedDeltaTime);

        float step = _velocity.magnitude * Time.fixedDeltaTime;
        if (step > 0f && !CanPushTo(_velocity.normalized, step + Skin))
        {
            _velocity = Vector3.zero;
        }

        // 세로 속도는 건드리지 않는다. 여기서 덮으면 공중에서 맞았을 때 낙하가 끊긴다.
        _rigidbody.linearVelocity = new Vector3(
            _velocity.x, _rigidbody.linearVelocity.y, _velocity.z);
    }

    // 그 방향으로 그만큼 밀어도 되는 자리인지. 벽이 없고 발 디딜 곳이 있어야 한다.
    private bool CanPushTo(Vector3 direction, float distance)
    {
        if (_body == null)
        {
            return true;
        }

        // 옮겨간 자리에 놓일 캡슐. 축은 y 로 서 있다고 본다.
        Vector3 scale = _owner.lossyScale;
        float radius = _body.radius * Mathf.Max(scale.x, scale.z) * CheckShrink;
        float half = Mathf.Max(0f, _body.height * 0.5f * scale.y - radius);

        Vector3 center = _owner.TransformPoint(_body.center) + direction * distance;
        Vector3 bottom = center - Vector3.up * half;
        Vector3 top = center + Vector3.up * half;

        return HasGroundUnder(bottom, radius) && !HasWallAt(bottom, top, radius);
    }

    // 벽을 뚫는 것만 막아서는 소용이 없다. 난간이나 통로 끝에서 맞으면 앞이 비어 있어서
    // 검사를 그냥 통과하고, 그대로 밀려 떨어진다. 그쪽이 벽을 뚫는 것보다 자주 죽는다.
    private static bool HasGroundUnder(Vector3 bottom, float radius)
    {
        return Physics.Raycast(
            bottom + Vector3.up * radius, Vector3.down,
            radius + GroundProbe, ~0, QueryTriggerInteraction.Ignore);
    }

    // 도착할 자리에 몸을 놓아 보고 겹치는지 센다.
    //
    // 쓸기 검사(SweepTest, CapsuleCast)로는 안 된다. 물리 엔진의 쓸기는 출발 시점에 이미
    // 겹쳐 있는 것을 무시한다. 벽에 등을 붙이고 맞는 상황이 정확히 그 경우라, 바로 앞의
    // 벽을 못 보고 통과했다.
    private bool HasWallAt(Vector3 bottom, Vector3 top, float radius)
    {
        int count = Physics.OverlapCapsuleNonAlloc(
            bottom, top, radius, _overlaps, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider other = _overlaps[i];

            if (other == null || other.transform.IsChildOf(_owner))
            {
                continue;
            }

            // 밀리는 물체는 막힘이 아니다. 벽·문처럼 꿈쩍 않는 것만 센다.
            Rigidbody body = other.attachedRigidbody;
            if (body != null && !body.isKinematic)
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
