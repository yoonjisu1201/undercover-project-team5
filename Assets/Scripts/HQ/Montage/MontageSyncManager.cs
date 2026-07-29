using Unity.Netcode;
using UnityEngine;

/// 본부에서 조합한 몽타주 상태를 전원에게 동기화하고, 각 클라이언트에서 조립을 담당합니다.
///
/// 몽타주는 옷 프리팹을 조립한 3D 모델을 카메라가 RenderTexture로 뽑아내는 구조라,
/// 결과물 자체를 네트워크로 보낼 수 없습니다. 그래서 파츠별 옷 id만 서버 권위로 동기화하고
/// 조립은 각 클라이언트가 로컬에서 똑같이 수행합니다.
public class MontageSyncManager : MontageSyncBase {

	/// 본부 요원이 옷을 갈아입히거나 벗을 때 호출합니다. 실제 반영은 서버 검증을 거친 뒤 전원에게 일어납니다.
	/// 벗길 때는 clothId에 MontageState.None을 넘깁니다.
	public void RequestSetCloth(MontageParts part, int clothId) {
		if (!IsSpawned) {
			Debug.LogError("[MontageSyncManager] 스폰되기 전에 몽타주 변경을 요청했습니다.", this);
			return;
		}

		RequestSetClothRpc(part, clothId);
	}

	[Rpc(SendTo.Server)]
	private void RequestSetClothRpc(MontageParts part, int clothId, RpcParams rpcParams = default) {
		ulong senderClientId = rpcParams.Receive.SenderClientId;

		// 몽타주는 본부 요원만 조합할 수 있다
		if (!IsHeadquarter(senderClientId)) {
			Debug.LogWarning($"[MontageSyncManager] 본부 요원이 아닌 클라이언트({senderClientId})의 몽타주 변경 요청을 무시했습니다.", this);
			return;
		}

		// 존재하지 않는 옷 id는 각 클라이언트에서 조립에 실패하므로 서버에서 걸러낸다
		// (Find는 캐시에 없으면 Lazy Loading을 시도하므로, 서버가 본부 역할이 아니라 전체 로딩을 안 했어도 검증할 수 있다)
		if (clothId != MontageState.None && _catalog.Find(part, clothId) == null) {
			Debug.LogError($"[MontageSyncManager] {part} 파츠에 존재하지 않는 옷 id({clothId}) 요청입니다.", this);
			return;
		}

		_montageState.Value = _montageState.Value.WithCloth(part, clothId);
	}
}
