using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Idle 상태의 NPC가 다음 Checkpoint 주변 목적지를 선택해 걷도록 요청합니다.
/// </summary>
[RequireComponent(typeof(NpcStateMachine))]
public sealed class NpcWanderController : MonoBehaviour
{
    [Header("Wander Settings")]
    [SerializeField, Min(0f)] private float _idleDelay = 1f;
    [SerializeField, Min(0f)] private float _checkpointRadius = 2f;
    [SerializeField, Min(0f)] private float _navMeshSampleDistance = 1f;

    private Transform[] _checkpoints = Array.Empty<Transform>();
    private NpcStateMachine _stateMachine;
    private float _idleElapsedSeconds;

    /// <summary>
    /// 배회 요청에 사용할 NpcStateMachine 참조를 가져옵니다.
    /// </summary>
    private void Awake()
    {
        _stateMachine = GetComponent<NpcStateMachine>();
    }

    /// <summary>
    /// 서버의 Idle NPC만 배회 대기 시간을 갱신합니다.
    /// </summary>
    private void Update()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            return;
        }

        UpdateWander();
    }

    /// <summary>
    /// NPC가 배회할 Scene Checkpoint 목록을 전달받습니다.
    /// </summary>
    /// <param name="checkpoints">목적지 기준으로 사용할 Transform 목록입니다.</param>
    public void Configure(Transform[] checkpoints)
    {
        _checkpoints = checkpoints ?? Array.Empty<Transform>();
    }

    /// <summary>
    /// Idle 대기가 끝나면 산개된 목적지를 구해 걷기를 요청합니다.
    /// </summary>
    private void UpdateWander()
    {
        if (_stateMachine.CurrentStateId != NpcStateId.Idle)
        {
            _idleElapsedSeconds = 0f;
            return;
        }

        _idleElapsedSeconds += Time.deltaTime;

        if (_idleElapsedSeconds < _idleDelay)
        {
            return;
        }

        _idleElapsedSeconds = 0f;

        if (!TryGetRandomCheckpoint(out Transform checkpoint))
        {
            Debug.LogError("[NPC Wander] 사용할 Checkpoint를 선택하지 못했습니다.", this);
            return;
        }

        if (!TryGetDestination(checkpoint, out Vector3 destination))
        {
            Debug.LogWarning( $"[NPC Wander] {checkpoint.name} 주변에서 NavMesh 목적지를 찾지 못했습니다.", this);
            return;
        }

        _stateMachine.RequestWalk(destination);
    }

    /// <summary>
    /// 설정된 Checkpoint 중 하나를 무작위로 선택합니다.
    /// </summary>
    /// <param name="checkpoint">선택에 성공한 Checkpoint입니다.</param>
    /// <returns>사용할 수 있는 Checkpoint를 선택했으면 true입니다.</returns>
    private bool TryGetRandomCheckpoint(out Transform checkpoint)
    {
        checkpoint = null;

        if (_checkpoints == null || _checkpoints.Length == 0)
        {
            return false;
        }

        int checkpointIndex = UnityEngine.Random.Range(0, _checkpoints.Length);

        checkpoint = _checkpoints[checkpointIndex];

        return checkpoint != null;
    }

    /// <summary>
    /// Checkpoint 중심 주변에서 실제로 걸을 수 있는 목적지를 찾습니다.
    /// </summary>
    /// <param name="checkpoint">산개 위치의 중심이 되는 Checkpoint입니다.</param>
    /// <param name="destination">NavMesh 위에서 보정된 최종 목적지입니다.</param>
    /// <returns>걸을 수 있는 목적지를 찾았으면 true입니다.</returns>
    private bool TryGetDestination(Transform checkpoint, out Vector3 destination)
    {
        destination = default;

        Vector2 destinationOffset = UnityEngine.Random.insideUnitCircle * _checkpointRadius;

        Vector3 samplePosition = checkpoint.position + new Vector3( destinationOffset.x, 0f, destinationOffset.y);

        if (!NavMesh.SamplePosition(samplePosition, out NavMeshHit navMeshHit, _navMeshSampleDistance, NavMesh.AllAreas))
        {
            return false;
        }

        destination = navMeshHit.position;

        return true;
    }
}
