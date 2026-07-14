using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// #27 수동 검증용 씬을 조립하며 제품 Stage/Round 호출을 대체하지 않습니다.
/// </summary>
public sealed class NpcBehaviorTestBootstrap : MonoBehaviour
{
    [SerializeField] private NpcSpawner _spawner;
    [SerializeField] private MapBlockController _blockController;
    [SerializeField] private MapBlock _blockToUnlock;
    [SerializeField, Min(0f)] private float _unlockDelaySeconds = 5f;

    /// <summary>
    /// NPC를 한 라운드 배치하고 일정 시간 뒤 테스트용 Block을 해금합니다.
    /// </summary>
    private void Start()
    {
        if (_spawner != null)
        {
            _spawner.SpawnRound();
        }

        UnlockBlockAfterDelayAsync(destroyCancellationToken).Forget();
    }

    /// <summary>
    /// 씬 수명에 연결된 취소 토큰으로 지연 해금 작업을 실행합니다.
    /// </summary>
    private async UniTaskVoid UnlockBlockAfterDelayAsync(
        CancellationToken cancellationToken)
    {
        if (_blockController == null || _blockToUnlock == null)
        {
            return;
        }

        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(Mathf.Max(0f, _unlockDelaySeconds)),
                cancellationToken: cancellationToken);

            _blockController.UnlockBlock(_blockToUnlock);
            Debug.Log($"[NPC Test] {_blockToUnlock.name} unlocked.", this);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
