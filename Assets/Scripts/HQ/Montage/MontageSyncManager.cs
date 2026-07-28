using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

/// 본부에서 조합한 몽타주 상태를 전원에게 동기화하고, 각 클라이언트에서 조립을 담당합니다.
/// 
/// 몽타주는 옷 프리팹을 조립한 3D 모델을 카메라가 RenderTexture로 뽑아내는 구조라,
/// 결과물 자체를 네트워크로 보낼 수 없습니다. 그래서 파츠별 옷 id만 서버 권위로 동기화하고
/// 조립은 각 클라이언트가 로컬에서 똑같이 수행합니다.
public class MontageSyncManager : NetworkBehaviour {

	[Header("=== 조립 대상 몽타주 오브젝트 ===")]
	[SerializeField] private Montage _montage;

	private readonly NetworkVariable<MontageState> _montageState = new NetworkVariable<MontageState>(
		MontageState.Empty,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server
	);

	private readonly MontageClothCatalog _catalog = new MontageClothCatalog();

	// 모든 파츠를 순회할 때 매번 GetValues를 부르지 않도록 한 번만 만들어둔다
	private static readonly MontageParts[] AllParts =
		Enum.GetValues(typeof(MontageParts)).Cast<MontageParts>().ToArray();

	private UniTask _initializeTask;
	private bool _initializeStarted;
	private bool _montageInitialized;

	// 옷 데이터 로딩과 Montage 초기화가 끝나야 조립을 시작할 수 있다
	private bool _isReady;

	public MontageState State => _montageState.Value;
	public MontageClothCatalog Catalog => _catalog;

	/// 몽타주 상태가 바뀔 때마다 발동합니다. UI가 선택 표시를 갱신하는 데 사용합니다
	public event Action<MontageState> OnMontageStateChanged;

	public override void OnNetworkSpawn() {
		_montageState.OnValueChanged += HandleStateChanged;

		// 라운드가 새로 시작될 때 서버가 몽타주를 비운다
		if (IsServer && RoundManager.Instance != null) {
			RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
		}

		// 스폰보다 준비가 먼저 끝났다면, 이 시점에 도착한 최신 상태를 다시 적용해준다
		// (초기화는 RoundManager.WaitForLocalSpawnReadyAsync가 책임지고 호출한다)
		if (_isReady) {
			ApplyFull(_montageState.Value);
		}
	}

	public override void OnNetworkDespawn() {
		_montageState.OnValueChanged -= HandleStateChanged;

		if (IsServer && RoundManager.Instance != null) {
			RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
		}
	}
	
	// 옷 데이터를 로드합니다.
	public UniTask InitializeAsync() {
		if (!_initializeStarted) {
			_initializeStarted = true;
			_initializeTask = InitializeInternalAsync().Preserve();
		}

		return _initializeTask;
	}

	private async UniTask InitializeInternalAsync() {
		
		// 전체 로딩은 Server만. 나머지는 확인용 로그들
		if (!NetworkManager.LocalClient.PlayerObject.TryGetComponent<Player>(out Player value)) {
			Debug.LogError($"[MontageSyncManager] Player Component를 찾지 못했습니다.");
		}
		else if (value.PlayerRole ==  Role.Headquarter) {
			Debug.Log($"[MontageSyncManager] 본부이기에 몽타주 데이터 전체 로딩하였습니다.");
			await _catalog.EnsureLoadedAsync();
		} else {
			Debug.Log($"[MontageSyncManager] 현장이기에 몽타주 로딩하지 않았습니다.");
		}
		
		if (_montage == null) {
			Debug.LogError("[MontageSyncManager] Montage 참조가 비어 있어 몽타주를 조립할 수 없습니다.", this);
			return;
		}

		// Initialize는 내부 딕셔너리를 새로 만들기 때문에 두 번 부르면 이미 입은 옷을 놓친다
		if (!_montageInitialized) {
			_montage.Initialize();
			_montageInitialized = true;
		}

		_isReady = true;

		// OnValueChanged는 최초 동기화값에는 발동하지 않으므로 현재 값을 직접 한 번 적용한다.
		// 준비되기 전에 도착해 무시된 변경도 여기서 최신값으로 함께 반영된다.
		ApplyFull(_montageState.Value);
	}
	
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

	private bool IsHeadquarter(ulong clientId) {
		if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) { return false; }
		if (client.PlayerObject == null) { return false; }
		if (!client.PlayerObject.TryGetComponent(out Player player)) { return false; }

		return player.PlayerRole == Role.Headquarter;
	}

	private void HandleStateChanged(MontageState previous, MontageState current) {
		// 준비 전에 도착한 변경은 버린다. InitializeInternalAsync가 최신값으로 대신 적용한다
		if (_isReady) {
			foreach (MontageParts part in AllParts) {
				int previousId = previous.Get(part);
				int currentId = current.Get(part);

				if (previousId == currentId) { continue; }

				ApplyPart(part, currentId);
			}
		}

		OnMontageStateChanged?.Invoke(current);
	}

	// 상태 전체를 입힌다. 미착용 파츠는 아직 입은 적이 없으므로 건드리지 않는다
	// OnNetworkSpawn, Initialize시에 사용한다.
	private void ApplyFull(MontageState state) {
		foreach (MontageParts part in AllParts) {
			int clothId = state.Get(part);

			if (clothId == MontageState.None) { continue; }

			ApplyPart(part, clothId);
		}
	}

	private void ApplyPart(MontageParts part, int clothId) {
		if (clothId == MontageState.None) {
			_montage.RemoveCloth(part);
			return;
		}

		MontageClothData data = _catalog.Find(part, clothId);
		if (data == null) {
			Debug.LogError($"[MontageSyncManager] {part} 파츠의 옷 id({clothId})를 찾을 수 없습니다.", this);
			return;
		}

		_montage.WearCloth(part, data.ClothPrefabs);
	}

	private void HandleRoundStateChanged(RoundState state) {
		if (!IsServer) { return; }
		if (state != RoundState.InRound) { return; }

		// 새 라운드는 새 범인이므로 이전 라운드에 조합한 몽타주를 비운다
		_montageState.Value = MontageState.Empty;
	}
}
