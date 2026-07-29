using Unity.Netcode;
using UnityEngine;

// 몽타주 공유를 담당한다.
public class MontageShareManager : MontageSyncBase {

	[Header("=== 공유 시에 바로 그 당시 몽타주 상태 가져오기 위함 ===")] 
	[SerializeField] private MontageSyncManager _syncManager; 

	public NetworkVariable<float> LastSharedTime = new NetworkVariable<float>(-100f);
	
	// 이번 라운드에서 한번이라도 몽타주가 전송된 적이 있는가?
	public NetworkVariable<bool> IsMontageShared = new(false);

	[Rpc(SendTo.Server)]
	public void ShareMontageRpc(RpcParams rpcParams = default) {
		if (!IsHeadquarter(rpcParams.Receive.SenderClientId)) {
			Debug.LogError($"[MontageShareManager] 본부 요원이 아닌 사람이 몽타주 공유를 시도했습니다.");
			return;
		}
		
		if (LastSharedTime.Value + RoundManager.Instance.MontageShareCooldown > NetworkManager.Singleton.ServerTime.Time) {
			Debug.LogError($"[MontageShareManager] 쿨타임이 완료되지 않았는데 몽타주 공유를 시도했습니다.");
			return;
		}
		
		// 주의: IsMontageShared는 항상 _montageState와 "같은 시점"에 true가 된다는 걸 전제로,
		// MontageShareUI는 이 값 변경을 따로 구독하지 않고 OnMontageStateChanged(=_montageState 변경) 구독만으로 함께 갱신한다.
		// 이 둘을 서로 다른 시점에 바뀌게 고치면 MontageShareUI가 갱신을 놓치니, 그럴 땐 구독을 분리해야 한다.
		IsMontageShared.Value = true;
		
		// 몽타주의 실시간 상태를 스냅샷에 복사한다
		_montageState.Value = _syncManager.State;

		// 최종 전송 시간값도 수정한다.
		LastSharedTime.Value = (float)NetworkManager.Singleton.ServerTime.Time;
	}

	protected override void HandleRoundStateChanged(RoundState state) {
		base.HandleRoundStateChanged(state);

		if (!IsServer) { return; }
		if (state != RoundState.InRound) { return; }
		
		// 이번 라운드 몽타주 전송 여부 초기화
		IsMontageShared.Value = false;
		
		// 전송 시간도 초기화. 라운드 넘어가면 다시 전송 가능하게 해야 함.
		LastSharedTime.Value = -100f;
	}
}
