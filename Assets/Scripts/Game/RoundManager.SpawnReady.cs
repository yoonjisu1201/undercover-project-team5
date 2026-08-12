using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

// 게임씬 진입 직후 전원의 스폰 준비가 끝날 때까지 기다렸다가 라운드를 시작하는 흐름.
// 특정 클라이언트가 준비되지 않아 전원이 무한 로딩에 갇히지 않도록 제한 시간을 두고,
// 라운드 진행 중에도 최소 인원이 유지되는지 계속 감시한다.
public partial class RoundManager
{
    [Header("게임 시작하면서 몽타주 의류 데이터 로딩하기 위함")]
    [SerializeField] private MontageClothCatalog _catalog;
    [SerializeField] private MontageSyncManager _syncManager;
    [SerializeField] private MontageShareManager _shareManager;

    [Header("스폰 대기 제한 시간 (초) — 초과 시 클라이언트가 스스로 로비로 돌아간다")]
    [SerializeField] private float _spawnReadyTimeoutSeconds = 20f;

    // 스스로 나가지도 못할 만큼 멈춘 클라이언트를 서버가 대신 정리하기까지 더 기다리는 시간.
    private const float ServerKickGraceSeconds = 3f;

    // 유저에게는 누가/왜 실패했는지가 아니라 로비로 돌아간다는 사실만 필요하므로,
    // 원인 구분은 각 발생 지점의 로그에만 남기고 표시 문구는 두 가지로 통일한다.
    private const string SpawnReadyTimeoutReason = "게임 입장 시간이 초과되어 로비로 이동합니다";
    private static readonly string PlayerLeftReason =
        $"다른 플레이어의 접속이 끊어졌습니다 (최소 {WaitingRoomReadyManager.MinPlayersToStart}명 필요)";

    // 스폰 완료 확인 응답을 보낸 클라이언트 목록 (서버만 사용, 네트워크 동기화 불필요)
    private readonly HashSet<ulong> _spawnReadyConfirmedClients = new();

    // 각자 로컬 확인을 시작하고, 서버는 추가로 제한 시간 감시와 이탈 감시를 건다.
    private void BeginSpawnReadyFlow()
    {
        WaitForLocalSpawnReadyAsync(this.GetCancellationTokenOnDestroy()).Forget();

        if (IsServer)
        {
            WatchSpawnReadyTimeoutAsync(this.GetCancellationTokenOnDestroy()).Forget();
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    private void EndSpawnReadyFlow()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    // 로컬 씬에 NPC/단서가 기대한 수만큼 존재할 때까지 기다린 뒤 서버에 준비됐다고 보고한다.
    private async UniTaskVoid WaitForLocalSpawnReadyAsync(CancellationToken cancellationToken)
    {
        if (_npcSpawner == null || _clueSpawner == null)
        {
            Debug.LogError("[RoundManager] NpcSpawner/ClueSpawner 참조가 비어 있어 스폰 완료를 확인할 수 없습니다.", this);
            return;
        }

        // 내 연결이 이미 끊긴 상태라면 서버의 킥 사유가 도달할 수 없고, 전송 계층이 끊김을 알아챌 때까지
        // 로딩 화면에 갇힌다. 그래서 로컬에서도 같은 제한 시간을 재고 스스로 나간다.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // 타이머는 만료되면 timeoutCts.Cancel()을 호출하므로, 정상 완료 시 타이머부터 정리해야 한다.
        // (해제하지 않으면 이미 Dispose된 CTS를 건드려 ObjectDisposedException이 난다)
        using var timeoutTimer = timeoutCts.CancelAfterSlim(
            TimeSpan.FromSeconds(_spawnReadyTimeoutSeconds), DelayType.Realtime);

        try
        {
            // PlayerObject가 아직 안 온 상태로 넘어가면 LoadClothData()에서 NRE로 조용히 죽는다.
            await UniTask.WaitUntil(
                () => NetworkManager.Singleton.LocalClient.PlayerObject != null
                    && FindObjectsByType<NpcStateMachine>(FindObjectsSortMode.None).Length >= _npcSpawner.SpawnCount
                    && FindObjectsByType<ItemBase>(FindObjectsSortMode.None).Length >= _clueSpawner.SpawnCount,
                cancellationToken: timeoutCts.Token);

            // 나를 적절한 위치로 스폰시킨다
            _playerSpawner.SpawnPlayer(NetworkManager.Singleton.LocalClient.PlayerObject);

            // 몽타주 옷 데이터를 미리 로딩하고, 지금까지 조합된 몽타주를 내 화면에도 조립해둔다.
            // 이 셋은 취소 토큰을 받지 않아, 대기만 중단하도록 외부에서 취소를 붙인다.
            await _catalog.LoadClothData().AttachExternalCancellation(timeoutCts.Token);
            await _syncManager.InitializeAsync().AttachExternalCancellation(timeoutCts.Token);
            await _shareManager.InitializeAsync().AttachExternalCancellation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return; // 씬 전환/오브젝트 파괴로 인한 취소는 실패가 아니다
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            Debug.LogWarning("[RoundManager] 로컬 스폰 준비가 제한 시간을 초과했습니다.", this);

            // 호스트가 나가면 방이 사라지므로, 호스트는 서버 감시 타이머가 전원을 정리하도록 넘긴다.
            if (IsServer) return;

            GameSessionManager.Instance.LeaveSessionWithReason(SpawnReadyTimeoutReason);
            return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RoundManager] 로컬 스폰 준비 중 오류가 발생했습니다.\n{e}", this);

            // 호스트는 곧 서버라, 호스트가 방을 나가면 방이 사라져 나머지 인원까지 전부 로비로 밀린다.
            // 그래서 호스트는 여기서 방을 나가지 않고, 서버 감시 타이머가 전원을 정리하도록 넘긴다.
            if (IsServer) return;

            GameSessionManager.Instance.LeaveSessionWithReason("게임 준비 중 오류가 발생했습니다");
            return;
        }

        ReportSpawnReadyServerRpc();
    }

    // 접속자 전원의 준비 보고가 모이면 게임을 시작한다.
    [Rpc(SendTo.Server)]
    private void ReportSpawnReadyServerRpc(RpcParams rpcParams = default)
    {
        _spawnReadyConfirmedClients.Add(rpcParams.Receive.SenderClientId);
        TryStartWhenEveryoneReady();
    }

    private void TryStartWhenEveryoneReady()
    {
        // 인원이 모자란 상태로는 시작하지 않는다. (감시 타이머의 킥 루프가 이 검사를 다시 타므로,
        //  이 조건이 없으면 내보내는 도중에 최소 인원 미만으로 라운드가 시작될 수 있다)
        if (NetworkManager.ConnectedClientsIds.Count < WaitingRoomReadyManager.MinPlayersToStart) return;

        if (_spawnReadyConfirmedClients.Count >= NetworkManager.ConnectedClientsIds.Count)
        {
            StartGame();
        }
    }

    // 로딩 대기 중이든 라운드 진행 중이든, 접속자가 빠지면 계속 진행할 수 있는지 다시 판단한다.
    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        if (clientId == NetworkManager.LocalClientId) return; // 호스트 종료는 세션 자체가 끝나는 상황

        // 호스트가 방을 나가면 종료 과정에서 클라이언트가 하나씩 끊긴다. 이건 이탈이 아니라
        // 세션이 끝나는 것이므로, 남은 인원 판정으로 퇴장 사유를 덮어쓰지 않는다.
        if (NetworkManager.ShutdownInProgress) return;

        // 최소 인원을 못 채우면 로딩 중이든 라운드 중이든 게임을 이어갈 수 없다.
        if (NetworkManager.ConnectedClientsIds.Count < WaitingRoomReadyManager.MinPlayersToStart)
        {
            SendEveryoneToLobby(PlayerLeftReason);
            return;
        }

        if (_currentState.Value != RoundState.Waiting) return;

        // 준비 보고를 보낼 클라이언트가 이탈하면 전원 도달 검사를 촉발할 계기가 사라지므로,
        // 남은 인원이 이미 전부 보고했는지 여기서 다시 확인해 제한 시간까지 기다리지 않게 한다.
        // 이미 보고한 클라이언트가 나간 경우를 지우지 않으면, 남은 미보고 인원이 있는데도
        // 개수 비교가 통과해 라운드가 먼저 시작될 수 있다.
        _spawnReadyConfirmedClients.Remove(clientId);
        TryStartWhenEveryoneReady();
    }

    // 각 클라이언트는 제한 시간이 지나면 스스로 나가지만, 그조차 못 할 만큼 멈춘 경우가 있다.
    // 여유 시간을 더 준 뒤에도 남아 있는 미보고 클라이언트를 내보내고 남은 인원으로 진행 여부를 정한다.
    private async UniTaskVoid WatchSpawnReadyTimeoutAsync(CancellationToken cancellationToken)
    {
        // timeScale이 0으로 내려가도 감시는 계속돼야 하므로 Realtime을 쓴다.
        await UniTask.Delay(TimeSpan.FromSeconds(_spawnReadyTimeoutSeconds + ServerKickGraceSeconds),
            DelayType.Realtime, cancellationToken: cancellationToken);

        if (_currentState.Value != RoundState.Waiting) return; // 제한 시간 안에 정상 시작됨

        // 호스트는 내보낼 수 없으므로, 호스트가 준비되지 않았다면 게임을 진행할 방법이 없다.
        if (!_spawnReadyConfirmedClients.Contains(NetworkManager.LocalClientId))
        {
            Debug.LogError("[RoundManager] 호스트의 스폰 준비가 제한 시간 안에 끝나지 않았습니다.", this);
            SendEveryoneToLobby(SpawnReadyTimeoutReason);
            return;
        }

        // DisconnectClient가 순회 중인 목록을 바꾸므로, 대상을 먼저 모아두고 나서 내보낸다.
        List<ulong> pendingClients = new();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!_spawnReadyConfirmedClients.Contains(clientId))
            {
                pendingClients.Add(clientId);
            }
        }

        foreach (ulong clientId in pendingClients)
        {
            Debug.LogWarning($"[RoundManager] 클라이언트 {clientId}의 스폰 준비가 제한 시간을 초과해 내보냅니다.", this);
            DisconnectWithReason(clientId, SpawnReadyTimeoutReason);
        }

        // DisconnectClient는 사유가 붙으면 실제 끊기를 다음 프레임으로 미루므로 ConnectedClientsIds가
        // 아직 줄지 않는다. 방금 내보낸 인원을 직접 빼야 라운드가 한 프레임 잘못 시작되지 않는다.
        if (NetworkManager.ConnectedClientsIds.Count - pendingClients.Count
            < WaitingRoomReadyManager.MinPlayersToStart)
        {
            SendEveryoneToLobby(PlayerLeftReason);
            return;
        }

        StartGame();
    }

    // 남은 인원으로는 게임을 시작할 수 없으므로 방을 정리하고 전원을 로비로 돌려보낸다.
    private void SendEveryoneToLobby(string reason)
    {
        List<ulong> clientsToDisconnect = new();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.LocalClientId)
            {
                clientsToDisconnect.Add(clientId);
            }
        }

        foreach (ulong clientId in clientsToDisconnect)
        {
            DisconnectWithReason(clientId, reason);
        }

        // 호스트가 나가는 순간 방이 사라지므로, 클라이언트에게 사유를 보낸 뒤 마지막에 나간다.
        GameSessionManager.Instance.LeaveSessionWithReason(reason);
    }

    // NGO가 자동으로 채우는 영문 사유와 구분되도록 표식을 붙여 내보낸다.
    private void DisconnectWithReason(ulong clientId, string reason)
        => NetworkManager.DisconnectClient(clientId, GameSessionManager.ServerReasonPrefix + reason);
}
