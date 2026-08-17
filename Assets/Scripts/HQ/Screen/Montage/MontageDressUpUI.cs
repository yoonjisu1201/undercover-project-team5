using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

/// 본부 요원이 몽타주에 옷을 입히는 조작 UI입니다.
/// 실제 조립과 상태 보관은 MontageSyncManager가 담당하고, 여기서는 요청만 보냅니다.
public class MontageDressUpUI : ScreenBase {
	
	// 항상 탭에 같은 순서로 등장하도록 하기 위해 순서 지정
	private readonly List<ClothPart> _partOrder = new()
	{
		ClothPart.Hair,
		ClothPart.Eyebrow,
		ClothPart.Beard,
		ClothPart.Hat,
		ClothPart.Headphone,
		ClothPart.Glasses,
		ClothPart.Mask,
		ClothPart.Torso,
		ClothPart.Pants,
		ClothPart.Arm,
		ClothPart.Shoes
	};

	// 탭/레코드 UI에 표시할 파츠별 한글 라벨
	private static readonly Dictionary<ClothPart, string> PartLabels = new Dictionary<ClothPart, string> {
	   { ClothPart.Torso, "상의" },
	   { ClothPart.Arm, "팔" },
	   { ClothPart.Pants, "바지" },
	   { ClothPart.Shoes, "신발" },
	   { ClothPart.Hair, "헤어" },
	   { ClothPart.Hat, "모자" },
	   { ClothPart.Glasses, "안경" },
	   { ClothPart.Eyebrow, "눈썹" },
	   { ClothPart.Beard, "수염" },
	   { ClothPart.Mask, "마스크" },
	   { ClothPart.Headphone, "헤드폰" },
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

	[Header("=== 몽타주 동기화에 필요한 오브젝트 등록 ===")]
	[SerializeField] private MontageSyncManager _syncManager;
	[SerializeField] private MontageShareManager _shareManager;

	[Header("=== 몽타주 공유하기 버튼 ===")]
	[SerializeField] private Button _montageShareButton;

	[Header("=== 몽타주 초기화 버튼 ===")]
	[SerializeField] private Button _montageResetButton;

	[Header("=== 몽타주 공유 버튼 텍스트 ===")]
	[SerializeField] private LocalizeStringEvent _shareButtonText;

	[Header("=== 내부에서 사용될 텍스트들 ===")]
	[SerializeField] private LocalizedString _sendMontage;
	[SerializeField] private LocalizedString _sendCoolDown;

	// 파츠별 탭 버튼의 배경 이미지 (활성/비활성 색상 전환용)
	private readonly Dictionary<ClothPart, Image> _tabBackgrounds = new Dictionary<ClothPart, Image>();
	// 현재 활성 탭에서 생성된 레코드 행 목록 (탭 전환 시 파괴 후 재생성)
	private readonly List<MontageRecordRow> _spawnedRows = new List<MontageRecordRow>();

	[Header("=== 디 버 그 용 ===")]
	[SerializeField] private ClothPart _activePart = ClothPart.Hair;

	// 탭 버튼은 한 번만 생성하면 되므로 중복 생성을 막기 위한 플래그
	private bool _initialized;

	public override void Initialize() {
		base.Initialize();
		
		BuildTabs();
	}

	public override void ActivateScreen() {
		base.ActivateScreen();
		
		if (_syncManager == null) {
			Debug.LogError("[MontageDressUpUi] MontageSyncManager가 없어 몽타주 화면을 구성할 수 없습니다.", this);
			return;
		}

		// 화면이 열려 있는 동안에만 선택 표시를 갱신하면 된다
		_syncManager.OnMontageStateChanged += HandleMontageStateChanged;
		_montageShareButton.onClick.AddListener(ShareMontage);
		_montageResetButton.onClick.AddListener(ResetMontage);
		
		SetActiveTab(_activePart);
		
		// 카메라 활성화
		_syncManager.MontageCamera.enabled = true;
	}

	public override void DeactivateScreen() {
		base.DeactivateScreen();

		if (_syncManager == null) {
			return;
		}

		_syncManager.OnMontageStateChanged -= HandleMontageStateChanged;
		_montageShareButton?.onClick.RemoveListener(ShareMontage);
		_montageResetButton?.onClick.RemoveListener(ResetMontage);
		
		// 카메라 비활성화
		if (_syncManager.MontageCamera != null) {
			_syncManager.MontageCamera.enabled = false;
		}
	}

	private void Update() {
		float shareCooldown = RoundManager.Instance.MontageShareCooldown - (float)(NetworkManager.Singleton.ServerTime.Time - _shareManager.LastSharedTime.Value); 
		bool shareCooldownFinished = shareCooldown <= 0f;  
		_montageShareButton.interactable = shareCooldownFinished;
		
		if (shareCooldownFinished) {
			_shareButtonText.StringReference = _sendMontage;
		} else {
			_shareButtonText.StringReference = _sendCoolDown;
			_shareButtonText.StringReference.Arguments = new List<object> { (int)shareCooldown };
		}
		
		_shareButtonText.RefreshString();
	}

	private void BuildTabs() {
	   // 중복 생성 막기 위한 코드
	   if (_initialized) { return; }

	   // 순서 맞춰서 기반으로 탭 생성
	   foreach (ClothPart part in _partOrder) {
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

	private void SetActiveTab(ClothPart part) {
	   _activePart = part;

	   // 선택된 탭만 활성 색상으로, 나머지는 비활성 색상으로 표시
	   foreach (KeyValuePair<ClothPart, Image> pair in _tabBackgrounds) {
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
	   foreach (ClothData data in ClothCatalog.GetAll(_activePart)) {
	      string recordId = $"{_activePart.ToString()}\n{data.Id:00}";

	      MontageRecordRow row = Instantiate(_recordRowPrefab, _recordContainer);

	      row.Setup(
		      data,
		      recordId,
		      assignedId == data.Id,
		      HandleRecordClicked);

	      row.gameObject.SetActive(true);
	      _spawnedRows.Add(row);
	   }
	}
	
	// 옷 목록을 눌렀을 때 서버에 착용/탈착을 요청합니다. 이미 입고 있는 옷을 다시 누르면 벗고, 다른 옷을 누르면 그 옷으로 갈아입습니다.
	private void HandleRecordClicked(MontageRecordRow selectedRow)
	{
		// 이미 선택된 옷을 한 번 더 눌렀다면 벗는다
		bool isSelectedAgain = _syncManager.State.Get(_activePart) == selectedRow.Data.Id;
		int requestedId = isSelectedAgain ? MontageState.None : selectedRow.Data.Id;

		_syncManager.RequestSetCloth(_activePart, requestedId);
	}

	// 서버가 갱신한 상태를 받아 현재 탭의 선택 표시를 맞춘다
	private void HandleMontageStateChanged(MontageState state) {
		int assignedId = state.Get(_activePart);

		foreach (MontageRecordRow row in _spawnedRows) {
			row.SetOn(row.Data.Id == assignedId);
		}
	}
	
	private void ShareMontage() {
		_shareManager.ShareMontageRpc();
	}

	private void ResetMontage() {
		_syncManager.RequestReset();
	}

}
