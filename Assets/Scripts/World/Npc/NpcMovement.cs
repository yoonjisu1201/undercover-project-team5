using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

// NavMeshAgent에 목적지와 속도를 적용하고 도착 여부를 판정합니다.
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

            // 서버가 아닌 인스턴스는 NavMeshAgent가 스스로 Transform을 갱신하지 않게 해서
            // NetworkTransform이 동기화한 값과 충돌하지 않게 한다.
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                _agent.updatePosition = false;
                _agent.updateRotation = false;
                return;
            }

            _agent.obstacleAvoidanceType = _obstacleAvoidanceType;

            int minimumPriority = Mathf.Min(_minimumAvoidancePriority, _maximumAvoidancePriority);
            int maximumPriority = Mathf.Max(_minimumAvoidancePriority, _maximumAvoidancePriority);
            _agent.avoidancePriority = Random.Range(minimumPriority, maximumPriority + 1);
        }

        // 활성화되어 NavMesh에 배치된 Agent에 목적지를 설정합니다.
        public void MoveTo(Vector3 worldPos)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
                return;

            _agent.SetDestination(worldPos);
        }

        // 목적지에 완전히 멈추기 전에 다음 목적지를 잡아야 추격이 끊기지 않습니다.
        public bool IsNearDestination(float distance)
        {
            if (_agent == null || _agent.pathPending)
            {
                return false;
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
        public void Stop()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.ResetPath();
            }
            SetSpeed(0f);
        }

        // NPC 이동을 일시 정지합니다. 현재 경로는 유지되어 Resume() 호출 시 이어서 이동합니다.
        public void Pause()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
            }
        }

        // 일시 정지된 NPC 이동을 재개합니다.
        public void Resume()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
            }
        }

        // 검거 확인 패널/투표처럼 외부 시스템이 NPC를 붙잡아 둘 때 사용한다.
        // 랜덤 배회 로직이 이 상태를 확인해서 이동을 재개하지 않도록 한다.
        // (Pause()/Resume()은 NPC 내부 로직(장거리 이동 중 휴식)도 함께 쓰기 때문에 구분해서 관리한다)
        public bool IsHeldExternally { get; private set; }

        public void HoldExternally()
        {
            IsHeldExternally = true;
            Pause();
        }

        public void ReleaseExternalHold()
        {
            IsHeldExternally = false;
            Resume();
        }
}
