using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MontageDressUpUI : ScreenBase {
	// 항상 탭에 같은 순서로 등장하도록 하기 위해 순서 지정
	private readonly List<SilhouetteParts> _partOrder = new()
	{
		SilhouetteParts.Hair,
		SilhouetteParts.Eyebrows,
		SilhouetteParts.Beard,
		SilhouetteParts.Hats,
		SilhouetteParts.Headphones,
		SilhouetteParts.Glasses,
		SilhouetteParts.Masks,
		SilhouetteParts.Torso,
		SilhouetteParts.Pants,
		SilhouetteParts.Arms,
		SilhouetteParts.Shoes
	};
	
	// 탭/레코드 UI에 표시할 파츠별 한글 라벨
	private static readonly Dictionary<SilhouetteParts, string> PartLabels = new Dictionary<SilhouetteParts, string> {
	   { SilhouetteParts.Torso, "상의" },
	   { SilhouetteParts.Arms, "팔" },
	   { SilhouetteParts.Pants, "바지" },
	   { SilhouetteParts.Shoes, "신발" },
	   { SilhouetteParts.Hair, "헤어" },
	   { SilhouetteParts.Hats, "모자" },
	   { SilhouetteParts.Glasses, "안경" },
	   { SilhouetteParts.Eyebrows, "눈썹" },
	   { SilhouetteParts.Beard, "수염" },
	   { SilhouetteParts.Masks, "마스크" },
	   { SilhouetteParts.Headphones, "헤드폰" },
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

	[Header("=== 실제 몽타주 오브젝트 ===")]
	[SerializeField] private Montage montageObject;

	// 파츠별 탭 버튼의 배경 이미지 (활성/비활성 색상 전환용)
	private readonly Dictionary<SilhouetteParts, Image> _tabBackgrounds = new Dictionary<SilhouetteParts, Image>();
	// 현재 활성 탭에서 생성된 레코드 행 목록 (탭 전환 시 파괴 후 재생성)
	private readonly List<MontageRecordRow> _spawnedRows = new List<MontageRecordRow>();
	// 파츠별로 마지막에 장착(할당)한 옷의 id (탭을 넘어가도 유지되는 영속 상태)
	private readonly Dictionary<SilhouetteParts, int> _assignedIds = new Dictionary<SilhouetteParts, int>();

	[Header("=== 디 버 그 용 ===")] 
	[SerializeField] private Dictionary<SilhouetteParts, List<SilhouetteClothData>> _dataByParts = new Dictionary<SilhouetteParts, List<SilhouetteClothData>>();
	[SerializeField] private SilhouetteParts _activePart = SilhouetteParts.Hair;
	
	// 탭 버튼은 한 번만 생성하면 되므로 중복 생성을 막기 위한 플래그
	private bool _tabsBuilt;

	private void Awake() {
	   montageObject.Initialize();

	   LoadDatas();
	   BuildTabs();
	}
	
	// Resources.Load를 통해 필요한 데이터 로드하기
	private void LoadDatas() { 
		foreach (SilhouetteParts part in _partOrder) {
			_dataByParts.Add(
				part,
				Resources.LoadAll<SilhouetteClothData>(
					$"Montage/ClothData/{part.ToString()}"
				).ToList()
			);
		}
	}

	private void OnEnable() {
		Debug.Log(
			$"[{name} / {GetInstanceID()}] " +
			$"didAwake={didAwake}, " +
			$"activePart={_activePart}, " +
			$"keys={string.Join(", ", _dataByParts.Keys)}"
		);
	   SetActiveTab(_activePart);
	}

	private void BuildTabs() {
	   if (_tabsBuilt) {
	      return;
	   }
	   _tabsBuilt = true;

	   // 순서 맞춰서 기반으로 탭 생성
	   foreach (SilhouetteParts part in _partOrder) {
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
	}

	private void SetActiveTab(SilhouetteParts part) {
	   _activePart = part;

	   // 선택된 탭만 활성 색상으로, 나머지는 비활성 색상으로 표시
	   foreach (KeyValuePair<SilhouetteParts, Image> pair in _tabBackgrounds) {
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

	   // 현재 활성 파츠에 해당하는 데이터만 필터링해 행으로 생성
	   foreach (SilhouetteClothData data in _dataByParts[_activePart]) {
	      string recordId = $"{_activePart.ToString()}\n{data.id:00}";

	      MontageRecordRow row = Instantiate(_recordRowPrefab, _recordContainer);
	      
	      row.Setup(
		      data,
		      recordId,
		      (_assignedIds.TryGetValue(_activePart, out int assignedId) && assignedId == data.id),
		      HandleRecordClicked);
	      
	      row.gameObject.SetActive(true);
	      _spawnedRows.Add(row);
	   }
	}

	/// <summary>
	/// 옷 목록을 눌렀을 때 선택 상태와 실제 착용 상태를 바꿉니다.
	/// 이미 입고 있는 옷을 다시 누르면 벗고,
	/// 다른 옷을 누르면 기존 옷 대신 새 옷으로 갈아입습니다.
	/// </summary>
	/// <param name="selectedRow">이번에 누른 옷입니다.</param>
	private void HandleRecordClicked(MontageRecordRow selectedRow)
	{
		// 이 부위에 지금 선택되어 있는 옷의 id가 있는지 확인합니다.
		bool hasCurrent = _assignedIds.TryGetValue(_activePart, out int currentId);

		// 이미 선택된 옷을 한 번 더 눌렀는지 확인합니다.
		bool isSelectedAgain = hasCurrent && currentId == selectedRow.Data.id;

		if (isSelectedAgain) {
			// 같은 옷을 다시 눌렀으므로 선택을 해제하고 옷을 벗깁니다.
			_assignedIds.Remove(_activePart);
			montageObject.RemoveCloth(_activePart);
			selectedRow.SetOn(false);

			return;
		}

		// 전에 골라둔 옷이 있다면, 현재 탭에 떠 있는 Row들 중에서 찾아 선택 표시를 꺼줍니다.
		if (hasCurrent) {
			MontageRecordRow previousRow = _spawnedRows.Find(row => row.Data.id == currentId);
			previousRow?.SetOn(false);
		}

		// 이번에 누른 옷을 현재 선택된 옷으로 저장합니다.
		_assignedIds[_activePart] = selectedRow.Data.id;

		// 실제 캐릭터도 이번에 고른 옷으로 갈아입힙니다.
		montageObject.WearCloth(
			_activePart,
			selectedRow.Data.ClothPrefabs
		);

		// 이번에 누른 목록의 선택 표시를 켜줍니다.
		selectedRow.SetOn(true);
	}
}
