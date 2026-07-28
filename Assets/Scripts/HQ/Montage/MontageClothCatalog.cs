using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// 몽타주에 쓰이는 옷 데이터를 파츠별로 로드해두고, id로 다시 찾을 수 있게 해줍니다.
/// 동기화된 상태에는 옷 id만 담기므로, 그 id로 프리팹을 찾는 일은 모든 클라이언트가 해야 합니다.
public class MontageClothCatalog {
	private string GetLoadPath(MontageParts part) => Path.Combine("Montage", "ClothData", part.ToString());
	
	private readonly Dictionary<MontageParts, Dictionary<int, MontageClothData>> _dataByParts = new();
	private readonly Dictionary<MontageParts, List<MontageClothData>> _orderedDataByParts = new();

	private UniTask _loadTask;
	private bool _loadStarted;

	// 옷 데이터를 전체 로드합니다.
	public UniTask EnsureLoadedAsync() {
		if (!_loadStarted) {
			_loadStarted = true;
			_loadTask = LoadAsync().Preserve();
		}

		return _loadTask;
	}

	private async UniTask LoadAsync() {
		foreach (MontageParts part in Enum.GetValues(typeof(MontageParts)).Cast<MontageParts>()) {
			List<MontageClothData> datas = Resources.LoadAll<MontageClothData>(GetLoadPath(part)).ToList();
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
	}
	
	// 해당 파츠에서 id에 맞는 옷 데이터를 반환합니다. 필요하다면 Lazy Loading을 수행.
	public MontageClothData Find(MontageParts part, int clothId) {
		// Dictionary 로딩
		if (!_dataByParts.TryGetValue(part, out Dictionary<int, MontageClothData> datas)) {
			// Dictionary Key가 없는 경우 (그 부위가 하나도 로딩되지 않은 경우) 에는 새로 추가 
			_dataByParts.TryAdd(part, new Dictionary<int, MontageClothData>());
			datas = _dataByParts[part];
		}
		
		// 로딩된 Dictionary 내부에서 사용하고자 하는 옷 데이터 로딩
		datas.TryGetValue(clothId, out MontageClothData data);
		
		// 만약 사용하고자 하는 옷 데이터가 없다면, Lazy Loading 시도한다
		if (data == null) {
			// 필요한 데이터 불러오기 (Resources.Load는 확장자(.asset)를 붙이면 안 된다)
			string loadPath = Path.Combine(GetLoadPath(part), clothId.ToString());
			MontageClothData loadedData = Resources.Load<MontageClothData>(loadPath);
			data = loadedData;
			
			// 데이터가 없다면, 에러메세지
			if (data == null) {
				Debug.LogError($"[MontageClothCatalog] id에 해당하는 옷을 불러오지 못했습니다 : {loadPath}");
			}
			// 데이터가 있다면, Dictionary에 추가
			else {
				_dataByParts[part].TryAdd(loadedData.id, loadedData);
			}
		}
		
		return data;
	}
	
	// 목록 UI에 표시할 순서대로 해당 파츠의 옷 데이터를 돌려줍니다.
	public IReadOnlyList<MontageClothData> GetAll(MontageParts part) {
		return _orderedDataByParts.TryGetValue(part, out List<MontageClothData> datas)
			? datas
			: Array.Empty<MontageClothData>();
	}
}
