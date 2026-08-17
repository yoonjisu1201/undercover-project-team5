using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

/// 옷 데이터(ClothData)를 부위별로 로드해두고, id로 다시 찾을 수 있게 해줍니다.
/// NPC 외형 생성, 몽타주 조립, 단서 캡쳐가 전부 이 카탈로그 하나를 공유합니다.
/// 네트워크 동기화 값에는 옷 id만 담기므로, 그 id로 프리팹을 찾는 일은 모든 클라이언트가 각자 로컬에서 해야 합니다.
public static class ClothCatalog {
	private static string GetLoadPath(ClothPart part) => Path.Combine("ClothData", part.ToString());

	private const string RoundPoolConfigPath = "ClothRoundPoolConfig";

	// 같은 데이터를 두 가지 모양으로 인덱싱해둔다. 한쪽만으로는 두 접근 패턴을 다 못 만족한다.
	// - DataByParts: Find(part, id)용. "id로 바로 찾기"는 Dictionary가 O(1)이라 빠르다.
	// - OrderedDataByParts: GetAll(part)용. 몽타주 UI가 id 순서대로 나열하거나 NPC가 Random.Range(0, count)로 인덱스를 뽑아 접근하려면 순서가 있는 List가 필요하다.
	private static readonly Dictionary<ClothPart, Dictionary<int, ClothData>> DataByParts = new();
	private static readonly Dictionary<ClothPart, List<ClothData>> OrderedDataByParts = new();
	private static readonly Dictionary<ClothPart, List<ClothData>> EnabledDataByParts = new();
	// (부위, 세션 시드, 라운드 번호) 조합이 캐시 키. 세션 시드까지 넣어야 다른 게임 세션에서
	// 라운드 번호가 우연히 겹쳐도 이전 세션의 로테이션 결과를 재사용하지 않는다.
	private static readonly Dictionary<(ClothPart, int, int), IReadOnlyList<ClothData>> RoundPoolCache = new();

	private static UniTask _loadAllTask;
	private static bool _loadAllStarted;
	private static ClothRoundPoolConfig _roundPoolConfig;
	private static bool _roundPoolConfigLoaded;

	// 옷 데이터를 전체 로드합니다. 몽타주 화면 진입 전에 미리 불러 로딩 hitch를 줄이는 용도.
	public static UniTask LoadAllAsync() {
		// 전체 로딩은 Server만. 나머지는 확인용 로그들
		if (!NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out Player value)) {
			Debug.LogError($"[ClothCatalog] Player Component를 찾지 못했습니다.");
			return UniTask.CompletedTask;
		}

		if (!_loadAllStarted) {
			Debug.Log($"[ClothCatalog] 옷 데이터 전체 로딩합니다.");
			_loadAllStarted = true;
			_loadAllTask = LoadAllInternalAsync().Preserve();
		}
		return _loadAllTask;
	}

	private static async UniTask LoadAllInternalAsync() {
		foreach (ClothPart part in Enum.GetValues(typeof(ClothPart)).Cast<ClothPart>()) {
			LoadPartData(part);
			await UniTask.Yield();   // 부위 하나씩 로드하고 한 프레임 양보
		}
	}

	// 해당 부위에서 id에 맞는 옷 데이터를 반환합니다. 필요하다면 Lazy Loading을 수행.
	public static ClothData Find(ClothPart part, int id) {
		// Dictionary 로딩
		if (!DataByParts.TryGetValue(part, out Dictionary<int, ClothData> datas)) {
			// Dictionary Key가 없는 경우 (그 부위가 하나도 로딩되지 않은 경우) 에는 새로 추가
			DataByParts.TryAdd(part, new Dictionary<int, ClothData>());
			datas = DataByParts[part];
		}

		// 로딩된 Dictionary 내부에서 사용하고자 하는 옷 데이터 로딩
		datas.TryGetValue(id, out ClothData data);

		// 만약 사용하고자 하는 옷 데이터가 없다면, Lazy Loading 시도한다
		if (data == null) {
			// 필요한 데이터 불러오기 (Resources.Load는 확장자(.asset)를 붙이면 안 된다)
			string loadPath = Path.Combine(GetLoadPath(part), id.ToString());
			ClothData loadedData = Resources.Load<ClothData>(loadPath);
			data = loadedData;

			// 데이터가 없다면, 에러메세지
			if (data == null) {
				Debug.LogError($"[ClothCatalog] id에 해당하는 옷을 불러오지 못했습니다 : {loadPath}");
			}
			// 데이터가 있다면, Dictionary에 추가
			else {
				DataByParts[part].TryAdd(loadedData.Id, loadedData);
			}
		}

		return data;
	}

	// 목록 UI에 표시하거나 랜덤 선택 대상으로 쓸, "이번 라운드에 실제로 사용 가능한" 옷 데이터를 돌려줍니다.
	// NPC 랜덤 생성과 몽타주 UI가 이 메서드 하나만 공유하므로, 라운드마다 로테이션되는 목록도 자동으로 양쪽에 동일하게 적용됩니다.
	public static IReadOnlyList<ClothData> GetAll(ClothPart part) {
		LoadPartData(part);

		if (!EnabledDataByParts.TryGetValue(part, out List<ClothData> enabled)) {
			enabled = new List<ClothData>();
		}

		if (!TryGetRoundContext(out int sessionSeed, out int roundIndex)) {
			// 라운드 정보가 없는 상태(에디터 툴, 스모크 테스트 등)에서는 로테이션 없이 활성 목록 전체를 그대로 쓴다.
			return enabled;
		}

		(ClothPart, int, int) cacheKey = (part, sessionSeed, roundIndex);
		if (RoundPoolCache.TryGetValue(cacheKey, out IReadOnlyList<ClothData> cachedPool)) {
			return cachedPool;
		}

		int targetCount = GetRoundPoolConfig()?.GetCount(part) ?? int.MaxValue;
		int seed = HashCode.Combine(sessionSeed, roundIndex, part);
		List<ClothData> pool = SampleForRound(enabled, targetCount, seed);

		RoundPoolCache[cacheKey] = pool;
		return pool;
	}

	// 현재 게임 세션 시드와 라운드 번호를 가져옵니다. RoundManager가 없는 상황(에디터 등)에서는 false.
	private static bool TryGetRoundContext(out int sessionSeed, out int roundIndex) {
		RoundManager roundManager = RoundManager.Instance;

		if (roundManager == null || !roundManager.IsSpawned) {
			sessionSeed = 0;
			roundIndex = 0;
			return false;
		}

		sessionSeed = roundManager.ClothPoolSessionSeed;
		roundIndex = roundManager.CurrentRoundIndex;
		return true;
	}

	// 시드 기반 부분 Fisher-Yates로 targetCount개를 뽑는다. 모든 클라이언트가 같은 seed로 같은 결과를 얻어야 한다.
	private static List<ClothData> SampleForRound(List<ClothData> pool, int targetCount, int seed) {
		if (pool.Count <= targetCount) {
			return pool;
		}

		List<ClothData> shuffled = new List<ClothData>(pool);
		System.Random random = new System.Random(seed);

		for (int i = 0; i < targetCount; i++) {
			int swapIndex = i + random.Next(shuffled.Count - i);
			(shuffled[i], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[i]);
		}

		return shuffled.GetRange(0, targetCount);
	}

	private static ClothRoundPoolConfig GetRoundPoolConfig() {
		if (!_roundPoolConfigLoaded) {
			_roundPoolConfigLoaded = true;
			_roundPoolConfig = Resources.Load<ClothRoundPoolConfig>(RoundPoolConfigPath);

			if (_roundPoolConfig == null) {
				Debug.LogWarning($"[ClothCatalog] {RoundPoolConfigPath}를 찾지 못해 라운드 로테이션 없이 활성 목록 전체를 사용합니다.");
			}
		}

		return _roundPoolConfig;
	}

	private static void LoadPartData(ClothPart part) {
		if (OrderedDataByParts.ContainsKey(part)) {
			return;
		}

		List<ClothData> datas = Resources.LoadAll<ClothData>(GetLoadPath(part))
			.OrderBy(data => data.Id)
			.ToList();
		Dictionary<int, ClothData> dataById = new Dictionary<int, ClothData>();

		foreach (ClothData data in datas) {
			// id가 겹치면 나중 것을 버린다. ToDictionary로 예외를 띄우면 로딩 자체가 멈춰버린다
			if (!dataById.TryAdd(data.Id, data)) {
				Debug.LogError($"[ClothCatalog] {part} 부위에 id {data.Id}가 중복됩니다. ({data.name})");
			}
		}

		OrderedDataByParts[part] = datas;
		DataByParts[part] = dataById;
		EnabledDataByParts[part] = datas.Where(data => data.IsEnabled).ToList();
	}
}
