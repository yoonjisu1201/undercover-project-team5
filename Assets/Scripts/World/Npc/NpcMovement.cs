using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

/// <summary>
/// NavMeshAgent에 목적지와 속도를 적용하고 도착 여부를 판정합니다.
/// OffMeshLink는 자동 순회하지 않고 Agent 속도로 수동 이동합니다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NpcMovement : MonoBehaviour
{
        [SerializeField] private NavMeshAgent _agent;
        [FormerlySerializedAs("_arrivalThreshold")]
        [SerializeField, Min(0f)] private float _arrivalExtraDistance = 0.1f;

        private bool _isTraversingLink;
        private Vector3 _linkEndPosition;

        /// <summary>
        /// 경로 계산이 끝났고 정지 거리 안에서 이동이 끝났는지 여부를 가져옵니다.
        /// </summary>
        public bool HasArrived
        {
            get
            {
                if (_agent == null || _agent.pathPending)
                {
                    return false;
                }

                if (_isTraversingLink || _agent.isOnOffMeshLink)
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

        /// <summary>
        /// Agent를 연결하고 Link 구간의 자동 순회를 끕니다.
        /// </summary>
        private void Awake()
        {
            if (_agent == null)
            {
                _agent = GetComponent<NavMeshAgent>();
            }

            if (_agent != null)
            {
                _agent.autoTraverseOffMeshLink = false;
            }
        }

        /// <summary>
        /// Link 진입부터 끝점 완료까지 Transform과 Agent 위치를 동기화합니다.
        /// </summary>
        private void Update()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            {
                _isTraversingLink = false;
                return;
            }

            if (!_isTraversingLink)
            {
                if (!_agent.isOnOffMeshLink)
                {
                    return;
                }

                OffMeshLinkData linkData = _agent.currentOffMeshLinkData;
                _linkEndPosition =
                    linkData.endPos + Vector3.up * _agent.baseOffset;
                _agent.updatePosition = false;
                _isTraversingLink = true;
            }

            transform.position = Vector3.MoveTowards(
                transform.position,
                _linkEndPosition,
                _agent.speed * Time.deltaTime);
            _agent.nextPosition = transform.position;

            if (transform.position != _linkEndPosition)
            {
                return;
            }

            _agent.CompleteOffMeshLink();
            _agent.updatePosition = true;
            _isTraversingLink = false;
        }

        /// <summary>
        /// 활성화되어 NavMesh에 배치된 Agent에 목적지를 설정합니다.
        /// </summary>
        /// <param name="worldPos">이동할 월드 좌표입니다.</param>
        public void MoveTo(Vector3 worldPos)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
                return;

            _agent.SetDestination(worldPos);
        }

        /// <summary>
        /// Agent의 최대 이동 속도를 0 이상의 값으로 설정합니다.
        /// </summary>
        /// <param name="speed">적용할 이동 속도입니다.</param>
        public void SetSpeed(float speed)
        {
            if (_agent != null)
            {
                _agent.speed = Mathf.Max(0f, speed);
            }
        }

        /// <summary>
        /// 현재 경로를 제거하고 이동 속도를 0으로 설정합니다.
        /// </summary>
        public void Stop()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                if (_agent.isOnOffMeshLink)
                {
                    _agent.nextPosition = transform.position;
                    _agent.CompleteOffMeshLink();
                }

                _agent.updatePosition = true;
                _agent.ResetPath();
            }

            _isTraversingLink = false;
            SetSpeed(0f);
        }

        /// <summary>
        /// NPC 이동을 일시 정지합니다. 후속 이슈에서 동작이 구현됩니다.
        /// </summary>
        public void Pause()
        {
        }

        /// <summary>
        /// 일시 정지된 NPC 이동을 재개합니다. 후속 이슈에서 동작이 구현됩니다.
        /// </summary>
        public void Resume()
        {
        }
}
