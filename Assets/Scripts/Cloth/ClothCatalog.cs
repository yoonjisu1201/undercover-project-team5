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

	private static readonly Dictionary<ClothPart, Dictionary<int, ClothData>> DataByParts = new();
	private static readonly Dictionary<ClothPart, List<ClothData>> OrderedDataByParts = new();

	private static UniTask _loadAllTask;
	private static bool _loadAllStarted;

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

	// 목록 UI에 표시할 순서대로, 혹은 랜덤 선택 대상으로 해당 부위의 옷 데이터를 돌려줍니다.
	public static IReadOnlyList<ClothData> GetAll(ClothPart part) {
		LoadPartData(part);

		return OrderedDataByParts.TryGetValue(part, out List<ClothData> datas)
			? datas
			: Array.Empty<ClothData>();
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
	}
}
