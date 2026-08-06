using System;
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
		
		// IsMontageShared는 _montageState보다 먼저 갱신해야 한다 (UI가 _montageState 변경만 구독함)
		IsMontageShared.Value = true;

		// 몽타주의 실시간 상태를 스냅샷에 복사한다
		_montageState.Value = _syncManager.State;

		// 최종 전송 시간값도 수정한다.
		LastSharedTime.Value = (float)NetworkManager.Singleton.ServerTime.Time;
	}

	protected override void HandleRoundStateChanged(RoundState state) {
		if (IsServer && state == RoundState.InRound) {
			// 이번 라운드 몽타주 전송 여부 초기화 (같은 이유로 _montageState보다 먼저)
			IsMontageShared.Value = false;

			// 전송 시간도 초기화. 라운드 넘어가면 다시 전송 가능하게 해야 함.
			LastSharedTime.Value = -100f;
		}

		base.HandleRoundStateChanged(state);
	}

	protected override void HandleStateChanged(MontageState previous, MontageState current) {
		base.HandleStateChanged(previous, current);
		
		// 몽타주 상태 변경한 후에 1회 렌더링해서 변경사항 반영하기
		_montageCamera.Render();
	}
}
