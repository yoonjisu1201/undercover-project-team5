using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

// NavMeshAgent에 목적지와 속도를 적용하고 도착 여부를 판정합니다.
//
// NavMeshAgent는 NavMesh 위에 올라가 있지 않으면 목적지도 정지 상태도 받지 않는다.
// 구역 전환이나 지하 맵 생성으로 NavMesh를 다시 구우면 그런 순간이 실제로 생긴다.
// 그래서 이 클래스는 요청을 바로 넘기지 않고 '원하는 상태'로 보관한 뒤,
// 에이전트가 받을 수 있는 상태가 될 때마다 다시 맞춘다. 요청이 조용히 사라지지 않게 하는 것이 목적이다.
[RequireComponent(typeof(NavMeshAgent))]
public class NpcMovement : MonoBehaviour
{
        [SerializeField] private NavMeshAgent _agent;
        [FormerlySerializedAs("_arrivalThreshold")]
        [SerializeField, Min(0f)] private float _arrivalExtraDistance = 0.1f;

        [Header("Avoidance Settings")]
        [SerializeField] private ObstacleAvoidanceType _obstacleAvoidanceType =
            ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        [SerializeField, Range(0, 99)] private int _minimumAvoidancePriority = 30;
        [SerializeField, Range(0, 99)] private int _maximumAvoidancePriority = 70;

        // 에이전트는 서버에서만 구동한다. 클라이언트는 NetworkTransform이 동기화한 값을 따른다.
        private bool _hasServerAuthority;

        // 지금 향하려는 목적지. 에이전트에 넣지 못했다면 넣을 수 있을 때까지 여기 남는다.
        private Vector3 _desiredDestination;
        private bool _hasDesiredDestination;
        private bool _isDestinationApplied;

        // 멈춤 이유를 둘로 나눈다. NPC 내부 로직(장거리 이동 중 휴식, Phone 종료 대기)과
        // 외부 시스템(검거 확인 패널 등)이 같은 스위치를 공유하면 한쪽이 다른 쪽을 풀어 버린다.
        private bool _isPausedInternally;
        private int _externalHoldCount;

        // 직전 프레임에 에이전트가 요청을 받을 수 있었는지. NavMesh에 다시 올라온 순간을 알아내는 데 쓴다.
        private bool _wasAgentUsable;

        // 같은 경고를 매 프레임 쏟지 않도록, 한 번 밀린 뒤에는 다시 적용될 때까지 침묵한다.
        private bool _hasWarnedAboutPendingRequest;

        // 경로 계산이 끝났고 정지 거리 안에서 이동이 끝났는지 여부를 가져옵니다.
        public bool HasArrived
        {
            get
            {
                if (_agent == null || _agent.pathPending)
                {
                    return false;
                }

                float arrivalDistance = _agent.stoppingDistance + _arrivalExtraDistance;

                if (_agent.remainingDistance > arrivalDistance)
                {
                    return false;
                }

                return !_agent.hasPath || _agent.velocity.sqrMagnitude <= 0.01f;
            }
        }

        // 검거 확인 패널/투표처럼 외부 시스템이 NPC를 붙잡아 둔 상태인지 여부입니다.
        // 배회 로직이 이 값을 확인해서 이동을 재개하지 않습니다.
        public bool IsHeldExternally => _externalHoldCount > 0;

        // 멈출 이유가 하나도 없을 때만 움직인다.
        private bool ShouldMove => !_isPausedInternally && _externalHoldCount <= 0;

        // 에이전트가 목적지와 정지 상태를 받을 수 있는지 여부입니다.
        private bool IsAgentUsable => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        private void Reset()
        {
            TryGetComponent(out _agent);
        }

        private void Awake()
        {
            if (_agent == null)
            {
                _agent = GetComponent<NavMeshAgent>();
            }

            _hasServerAuthority = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

            // 서버가 아닌 인스턴스는 NavMeshAgent가 스스로 Transform을 갱신하지 않게 해서
            // NetworkTransform이 동기화한 값과 충돌하지 않게 한다.
            if (!_hasServerAuthority)
            {
                _agent.updatePosition = false;
                _agent.updateRotation = false;
                return;
            }

            _agent.obstacleAvoidanceType = _obstacleAvoidanceType;

            int minimumPriority = Mathf.Min(_minimumAvoidancePriority, _maximumAvoidancePriority);
            int maximumPriority = Mathf.Max(_minimumAvoidancePriority, _maximumAvoidancePriority);
            _agent.avoidancePriority = Random.Range(minimumPriority, maximumPriority + 1);
            _wasAgentUsable = IsAgentUsable;
        }

        // NavMesh를 다시 구우면 에이전트가 잠시 NavMesh를 벗어난다.
        // 돌아온 순간 밀린 요청을 다시 넣어야 NPC가 그대로 굳어 있지 않는다.
        private void Update()
        {
            if (!_hasServerAuthority)
            {
                return;
            }

            bool isUsable = IsAgentUsable;

            // NavMesh를 벗어난 동안 경로는 사라진다. 돌아왔으면 목적지를 다시 넣어야 한다.
            if (isUsable && !_wasAgentUsable)
            {
                _isDestinationApplied = false;
            }

            _wasAgentUsable = isUsable;
            ApplyDesiredState();
        }

        // 향할 목적지를 등록합니다. 지금 넣을 수 없으면 넣을 수 있게 될 때 적용됩니다.
        public void MoveTo(Vector3 worldPos)
        {
            _desiredDestination = worldPos;
            _hasDesiredDestination = true;
            _isDestinationApplied = false;

            ApplyDesiredState();
        }

        // 목적지에 완전히 멈추기 전에 다음 목적지를 잡아야 추격이 끊기지 않습니다.
        // 경로가 아예 없으면 remainingDistance가 무한이라 이 값이 영원히 false가 되고,
        // 호출부가 다음 목적지를 고르지 못해 제자리에 멈춘다. HasArrived와 같은 기준으로 맞춘다.
        public bool IsNearDestination(float distance)
        {
            if (_agent == null || _agent.pathPending)
            {
                return false;
            }

            if (!_agent.hasPath)
            {
                return true;
            }

            return _agent.remainingDistance <= distance;
        }

        // Agent의 최대 이동 속도를 0 이상의 값으로 설정합니다.
        public void SetSpeed(float speed)
        {
            if (_agent != null)
            {
                _agent.speed = Mathf.Max(0f, speed);
            }
        }

        // 현재 경로를 제거하고 이동 속도를 0으로 설정합니다.
        // 경로가 사라졌으므로 내부 일시정지도 함께 푼다. 남겨 두면 다음 이동 요청이 막힌다.
        public void Stop()
        {
            _hasDesiredDestination = false;
            _isDestinationApplied = true;
            _isPausedInternally = false;

            if (IsAgentUsable)
            {
                _agent.ResetPath();
            }

            SetSpeed(0f);
            ApplyDesiredState();
        }

        // NPC 이동을 일시 정지합니다. 현재 경로는 유지되어 Resume() 호출 시 이어서 이동합니다.
        public void Pause()
        {
            _isPausedInternally = true;
            ApplyDesiredState();
        }

        // 일시 정지된 NPC 이동을 재개합니다.
        public void Resume()
        {
            _isPausedInternally = false;
            ApplyDesiredState();
        }

        // 검거 확인 패널/투표처럼 외부 시스템이 NPC를 붙잡아 둘 때 사용합니다.
        // 붙잡는 주체가 여럿일 수 있어(다른 플레이어가 같은 NPC의 패널을 여는 경우) 수를 센다.
        public void HoldExternally()
        {
            _externalHoldCount++;
            ApplyDesiredState();
        }

        public void ReleaseExternalHold()
        {
            _externalHoldCount = Mathf.Max(0, _externalHoldCount - 1);
            ApplyDesiredState();
        }

        // 붙잡은 쪽이 Release를 빠뜨리면 NPC가 영구히 멈춘다.
        // 추격 시작처럼 "지금부터는 무조건 움직여야 한다"가 확실한 지점에서 호출한다.
        public void ReleaseAllExternalHolds()
        {
            if (_externalHoldCount > 0)
            {
                Debug.Log($"[NpcMovement] '{name}' NPC의 남은 외부 붙잡기 {_externalHoldCount}건을 모두 해제합니다.", this);
            }

            _externalHoldCount = 0;
            ApplyDesiredState();
        }

        // 보관해 둔 목적지와 정지 여부를 에이전트에 맞춘다.
        // 넣지 못했으면 상태를 그대로 남겨 두고 Update가 다시 시도한다.
        private void ApplyDesiredState()
        {
            if (!IsAgentUsable)
            {
                WarnOncePendingRequest();
                return;
            }

            _agent.isStopped = !ShouldMove;

            if (_hasDesiredDestination && !_isDestinationApplied)
            {
                _agent.SetDestination(_desiredDestination);
                _isDestinationApplied = true;
            }

            _hasWarnedAboutPendingRequest = false;
        }

        // 조용히 사라지던 실패를 한 번은 남긴다. 매 프레임 반복되지 않도록 플래그로 막는다.
        private void WarnOncePendingRequest()
        {
            if (_hasWarnedAboutPendingRequest || !_hasServerAuthority)
            {
                return;
            }

            _hasWarnedAboutPendingRequest = true;

            bool isAgentEnabled = _agent != null && _agent.enabled;
            bool isOnNavMesh = _agent != null && _agent.isOnNavMesh;
            Debug.LogWarning(
                $"[NpcMovement] '{name}' NPC가 NavMesh 위에 없어 이동 요청을 적용하지 못했습니다. " +
                $"NavMesh에 돌아오면 다시 적용합니다. (agent 활성={isAgentEnabled}, isOnNavMesh={isOnNavMesh})",
                this);
        }
}
