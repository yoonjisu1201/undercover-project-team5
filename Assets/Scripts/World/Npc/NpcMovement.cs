using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

/// <summary>
/// NavMeshAgent에 목적지와 속도를 적용하고 도착 여부를 판정합니다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NpcMovement : MonoBehaviour
{
        [SerializeField] private NavMeshAgent _agent;
        [FormerlySerializedAs("_arrivalThreshold")]
        [SerializeField, Min(0f)] private float _arrivalExtraDistance = 0.1f;

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
            if (!NetworkManager.Singleton.IsServer)
            {
                _agent.updatePosition = false;
                _agent.updateRotation = false;
            }
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
                _agent.ResetPath();
            }
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
