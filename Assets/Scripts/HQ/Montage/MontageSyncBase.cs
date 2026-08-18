using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

/// 몽타주 상태(NetworkVariable<MontageState>)를 조립 대상 Montage 오브젝트에 적용하는 공통 로직
/// 실시간 편집 상태(MontageSyncManager)와 전송 스냅샷(MontageShareManager)이 조립 방식은 같고
/// "무엇을 언제 바꿀 수 있는지"만 다르므로, 그 차이만 서브클래스가 구현한다.
public abstract class MontageSyncBase : NetworkBehaviour {

	[Header("=== 조립 대상 몽타주 오브젝트 ===")]
	[SerializeField] protected Montage _montage;

	[Header("=== 몽타주 공유 시에 사용하는 카메라 ===")]
	[SerializeField] protected Camera _montageCamera;

	protected readonly NetworkVariable<MontageState> _montageState = new NetworkVariable<MontageState>(
		MontageState.Empty,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server
	);

	// 모든 파츠를 순회할 때 매번 GetValues를 부르지 않도록 한 번만 만들어둔다
	private static readonly ClothPart[] AllParts =
		Enum.GetValues(typeof(ClothPart)).Cast<ClothPart>().ToArray();
	private UniTask _initializeTask;
	private bool _initializeStarted;
	private MontageClueCapture _clueCapture;

	// 옷 데이터 로딩과 Montage 초기화가 끝나야 조립을 시작할 수 있다
	private bool _isReady;

	public MontageState State => _montageState.Value;

	/// 몽타주 상태가 바뀔 때마다 발동합니다. UI가 선택 표시를 갱신하는 데 사용합니다
	public event Action<MontageState> OnMontageStateChanged;

	public Texture2D CaptureTemporaryState(MontageState state, ClothPart focusPart) {
		if (!_isReady) {
			Debug.LogError($"[{GetType().Name}] 초기화 전에 몽타주 캡쳐를 시도했습니다.", this);
			return null;
		}

		_clueCapture ??= new MontageClueCapture(_montage, _montageCamera, ApplyPreviewState);
		return _clueCapture.Capture(state, focusPart, _montageState.Value);
	}

	public override void OnNetworkSpawn() {
		_montageState.OnValueChanged += HandleStateChanged;

		// 라운드가 새로 시작될 때 서버가 몽타주를 비운다
		if (IsServer && RoundManager.Instance != null) {
			RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
		}

		// 스폰보다 준비가 먼저 끝났다면, 이 시점에 도착한 최신 상태를 다시 적용해준다
		if (_isReady) {
			ApplyFull(_montageState.Value);
		}
	}

	public override void OnNetworkDespawn() {
		_montageState.OnValueChanged -= HandleStateChanged;

		if (RoundManager.Instance != null) {
			RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
		}
	}

	// 옷 데이터를 로드합니다.
	public UniTask InitializeAsync() {
		if (!_initializeStarted) {
			_initializeStarted = true;
			Initialize();
			_initializeTask = UniTask.CompletedTask;
		}

		return _initializeTask;
	}

	private void Initialize() {
		// 몽타주 초기화
		_montage.Initialize();

		_isReady = true;

		// OnValueChanged는 최초 동기화값에는 발동하지 않으므로 현재 값을 직접 한 번 적용한다.
		// 준비되기 전에 도착해 무시된 변경도 여기서 최신값으로 함께 반영된다.
		ApplyFull(_montageState.Value);

		// 초기화 완료 후 카메라 끄고(렉 줄이기 위해)
		// 초기 렌더 정보 만들기 위해 1회 수동 렌더링
		_montageCamera.enabled = false;
		_montageCamera.Render();
	}

	protected virtual void HandleStateChanged(MontageState previous, MontageState current) {
		// 준비 전에 도착한 변경은 버린다. Initialize가 최신값으로 대신 적용한다
		if (_isReady) {
			foreach (ClothPart part in AllParts) {
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
		foreach (ClothPart part in AllParts) {
			int clothId = state.Get(part);

			if (clothId == MontageState.None) { continue; }

			ApplyPart(part, clothId);
		}
	}

	private void ApplyPreviewState(MontageState state) {
		_montage.RemoveAllClothes();
		ApplyFull(state);
	}

	private void ApplyPart(ClothPart part, int clothId) {
		if (clothId == MontageState.None) {
			_montage.RemoveCloth(part);
			return;
		}

		ClothData data = ClothCatalog.Find(part, clothId);
		if (data == null) {
			Debug.LogError($"[{GetType().Name}] {part} 파츠의 옷 id({clothId})를 찾을 수 없습니다.", this);
			return;
		}

		_montage.WearCloth(part, data.MontagePrefab);
	}

	// 새 라운드가 시작될 때 이 상태를 어떻게 리셋할지는 서브클래스가 필요에 따라 확장한다.
	protected virtual void HandleRoundStateChanged(RoundState state) {
		if (!IsServer) { return; }
		if (state != RoundState.InRound) { return; }

		// 새 라운드는 새 범인이므로 이전 라운드에 조합한 몽타주를 비운다
		_montageState.Value = MontageState.Empty;
	}
}
