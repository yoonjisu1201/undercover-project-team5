using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 본부 요원이 몽타주에 옷을 입히는 조작 UI입니다.
/// 실제 조립과 상태 보관은 MontageSyncManager가 담당하고, 여기서는 요청만 보냅니다.
/// 선택 표시도 동기화된 상태를 되읽어 갱신하므로 로컬 상태를 따로 들고 있지 않습니다.
/// </summary>
public class MontageDressUpUI : ScreenBase {
	// 항상 탭에 같은 순서로 등장하도록 하기 위해 순서 지정
	private readonly List<MontageParts> _partOrder = new()
	{
		MontageParts.Hair,
		MontageParts.Eyebrows,
		MontageParts.Beard,
		MontageParts.Hats,
		MontageParts.Headphones,
		MontageParts.Glasses,
		MontageParts.Masks,
		MontageParts.Torso,
		MontageParts.Pants,
		MontageParts.Arms,
		MontageParts.Shoes
	};

	// 탭/레코드 UI에 표시할 파츠별 한글 라벨
	private static readonly Dictionary<MontageParts, string> PartLabels = new Dictionary<MontageParts, string> {
	   { MontageParts.Torso, "상의" },
	   { MontageParts.Arms, "팔" },
	   { MontageParts.Pants, "바지" },
	   { MontageParts.Shoes, "신발" },
	   { MontageParts.Hair, "헤어" },
	   { MontageParts.Hats, "모자" },
	   { MontageParts.Glasses, "안경" },
	   { MontageParts.Eyebrows, "눈썹" },
	   { MontageParts.Beard, "수염" },
	   { MontageParts.Masks, "마스크" },
	   { MontageParts.Headphones, "헤드폰" },
	};

	[Header("=== 상단 ===")]
	[SerializeField] private TMP_Text _headerText;

	[Header("=== 카테고리 탭 ===")]
	[SerializeField] private Transform _tabContainer;
	[SerializeField] private GameObject _tabButtonPrefab;

	[Header("=== 레코드 목록 ===")]
	[SerializeField] private Transform _recordContainer;
	[SerializeField] private MontageRecordRow _recordRowPrefab;

	[Header("=== 색상 ===")]
	[SerializeField] private Color _tabActiveColor = new Color(0.231f, 0.910f, 0.659f, 0.16f);
	[SerializeField] private Color _tabInactiveColor = new Color(0f, 0f, 0f, 0f);

	[Header("=== MontageSyncManager 등록 ===")]
	[SerializeField] private MontageSyncManager _syncManager;

	// 파츠별 탭 버튼의 배경 이미지 (활성/비활성 색상 전환용)
	private readonly Dictionary<MontageParts, Image> _tabBackgrounds = new Dictionary<MontageParts, Image>();
	// 현재 활성 탭에서 생성된 레코드 행 목록 (탭 전환 시 파괴 후 재생성)
	private readonly List<MontageRecordRow> _spawnedRows = new List<MontageRecordRow>();

	[Header("=== 디 버 그 용 ===")]
	[SerializeField] private MontageParts _activePart = MontageParts.Hair;

	// 탭 버튼은 한 번만 생성하면 되므로 중복 생성을 막기 위한 플래그
	private bool _initialized;

	private async void OnEnable() {
		if (_syncManager == null) {
			Debug.LogError("[MontageDressUpUi] MontageSyncManager가 없어 몽타주 화면을 구성할 수 없습니다.", this);
			return;
		}

		// 화면이 열려 있는 동안에만 선택 표시를 갱신하면 된다
		_syncManager.OnMontageStateChanged += HandleMontageStateChanged;

		// 조립에 필요한 옷 데이터는 역할과 무관하게 전원이 로드한다 (MontageSyncManager가 담당)
		await _syncManager.InitializeAsync();

		// 로딩을 기다리는 동안 화면이 닫히거나 파괴됐을 수 있다
		if (!this || !isActiveAndEnabled) { return; }

		// 현장 요원은 몽타주를 볼 수만 있고 조합할 수는 없으므로 조작 UI를 만들지 않는다
		if (!IsLocalPlayerHeadquarter()) { return; }

		BuildTabs();
		SetActiveTab(_activePart);
	}

	private void OnDisable() {
		if (_syncManager != null) {
			_syncManager.OnMontageStateChanged -= HandleMontageStateChanged;
		}
	}

	private static bool IsLocalPlayerHeadquarter() {
		if (!NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent<Player>(out var player)) {
			Debug.LogError($"[MontageDressUpUi] PlayerObject 로딩 실패");
			return false;
		}

		return player.PlayerRole == Role.Headquarter;
	}

	private void BuildTabs() {
	   // 중복 생성 막기 위한 코드
	   if (_initialized) { return; }

	   // 순서 맞춰서 기반으로 탭 생성
	   foreach (MontageParts part in _partOrder) {
	      GameObject tabObj = Instantiate(_tabButtonPrefab, _tabContainer);
	      tabObj.name = $"Tab_{part}";
	      tabObj.SetActive(true);

	      // 내부 텍스트 값 수정
	      TMP_Text label = tabObj.GetComponentInChildren<TMP_Text>();
	      if (label != null) {
	         label.text = PartLabels.TryGetValue(part, out string koreanLabel) ? koreanLabel : part.ToString();
	         label.ForceMeshUpdate();
	      }

	      Button button = tabObj.GetComponent<Button>();
	      button.onClick.AddListener(() => SetActiveTab(part));
	      _tabBackgrounds[part] = tabObj.GetComponent<Image>();
	   }

	   _initialized = true;
	}

	private void SetActiveTab(MontageParts part) {
	   _activePart = part;

	   // 선택된 탭만 활성 색상으로, 나머지는 비활성 색상으로 표시
	   foreach (KeyValuePair<MontageParts, Image> pair in _tabBackgrounds) {
	      pair.Value.color = pair.Key == part ? _tabActiveColor : _tabInactiveColor;
	   }

	   RefreshRecords();
	}

	private void RefreshRecords() {
	   // 이전 탭에서 생성된 레코드 행을 모두 정리
	   foreach (MontageRecordRow row in _spawnedRows) {
	      Destroy(row.gameObject);
	   }
	   _spawnedRows.Clear();

	   // 지금 이 파츠에 입고 있는 옷 (동기화된 상태가 유일한 기준)
	   int assignedId = _syncManager.State.Get(_activePart);

	   // 현재 활성 파츠에 해당하는 데이터만 필터링해 행으로 생성
	   foreach (MontageClothData data in _syncManager.Catalog.GetAll(_activePart)) {
	      string recordId = $"{_activePart.ToString()}\n{data.id:00}";

	      MontageRecordRow row = Instantiate(_recordRowPrefab, _recordContainer);

	      row.Setup(
		      data,
		      recordId,
		      assignedId == data.id,
		      HandleRecordClicked);

	      row.gameObject.SetActive(true);
	      _spawnedRows.Add(row);
	   }
	}
	
	// 옷 목록을 눌렀을 때 서버에 착용/탈착을 요청합니다. 이미 입고 있는 옷을 다시 누르면 벗고, 다른 옷을 누르면 그 옷으로 갈아입습니다.
	private void HandleRecordClicked(MontageRecordRow selectedRow)
	{
		// 이미 선택된 옷을 한 번 더 눌렀다면 벗는다
		bool isSelectedAgain = _syncManager.State.Get(_activePart) == selectedRow.Data.id;
		int requestedId = isSelectedAgain ? MontageState.None : selectedRow.Data.id;

		_syncManager.RequestSetCloth(_activePart, requestedId);
	}

	// 서버가 갱신한 상태를 받아 현재 탭의 선택 표시를 맞춘다
	private void HandleMontageStateChanged(MontageState state) {
		int assignedId = state.Get(_activePart);

		foreach (MontageRecordRow row in _spawnedRows) {
			row.SetOn(row.Data.id == assignedId);
		}
	}
}
