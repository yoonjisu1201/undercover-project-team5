using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// NpcBehaviorTest 씬에서 라운드 NPC 배치를 한 번 시작합니다.
/// </summary>
public sealed class NpcBehaviorTestBootstrap : MonoBehaviour
{
    [SerializeField] private NpcSpawner _spawner;
    [SerializeField] private MapBlockController _blockController;
    [SerializeField] private MapBlock _blockToUnlock;
    [SerializeField, Min(0f)] private float _unlockDelaySeconds = 5f;

    private void Start()
    {
        if (_spawner != null)
        {
            _spawner.SpawnRound();
        }

        UnlockBlockAfterDelayAsync(destroyCancellationToken).Forget();
    }

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
