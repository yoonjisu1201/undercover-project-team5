using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// 몽타주에 쓰이는 옷 데이터를 파츠별로 로드해두고, id로 다시 찾을 수 있게 해줍니다.
/// 동기화된 상태에는 옷 id만 담기므로, 그 id로 프리팹을 찾는 일은 모든 클라이언트가 해야 합니다.
public class MontageClothCatalog {
	private readonly Dictionary<MontageParts, Dictionary<int, MontageClothData>> _dataByParts = new();
	private readonly Dictionary<MontageParts, List<MontageClothData>> _orderedDataByParts = new();

	private UniTask _loadTask;
	private bool _loadStarted;

	public bool IsLoaded { get; private set; }
	
	/// 옷 데이터를 로드합니다.
	public UniTask EnsureLoadedAsync() {
		if (!_loadStarted) {
			_loadStarted = true;
			_loadTask = LoadAsync().Preserve();
		}

		return _loadTask;
	}

	private async UniTask LoadAsync() {
		foreach (MontageParts part in Enum.GetValues(typeof(MontageParts)).Cast<MontageParts>()) {
			List<MontageClothData> datas = Resources.LoadAll<MontageClothData>($"Montage/ClothData/{part}").ToList();
			Dictionary<int, MontageClothData> dataById = new Dictionary<int, MontageClothData>();

			foreach (MontageClothData data in datas) {
				// id가 겹치면 나중 것을 버린다. ToDictionary로 예외를 띄우면 로딩 자체가 멈춰버린다
				if (!dataById.TryAdd(data.id, data)) {
					Debug.LogError($"[MontageClothCatalog] {part} 파츠에 id {data.id}가 중복됩니다. ({data.name})");
				}
			}

			_orderedDataByParts[part] = datas;
			_dataByParts[part] = dataById;

			await UniTask.Yield();   // 파츠 하나씩 로드하고 한 프레임 양보
		}

		IsLoaded = true;
	}
	
	// 해당 파츠에서 id에 맞는 옷 데이터를 찾습니다. 없으면 null을 반환합니다.
	public MontageClothData Find(MontageParts part, int clothId) {
		if (!_dataByParts.TryGetValue(part, out Dictionary<int, MontageClothData> datas)) { return null; }

		return datas.TryGetValue(clothId, out MontageClothData data) ? data : null;
	}
	
	// 목록 UI에 표시할 순서대로 해당 파츠의 옷 데이터를 돌려줍니다.
	public IReadOnlyList<MontageClothData> GetAll(MontageParts part) {
		return _orderedDataByParts.TryGetValue(part, out List<MontageClothData> datas)
			? datas
			: Array.Empty<MontageClothData>();
	}
}
