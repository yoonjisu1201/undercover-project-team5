using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CaptureManager : MonoBehaviour {
	[Header("=== 캡쳐에 사용할 카메라 ===")]
	[SerializeField] private CaptureCamera _captureCamera;
	RenderTexture _renderTexture;

	[Header("=== 각 부위 Transform ===")]
	[SerializeField] private Transform Beard;
	[SerializeField] private Transform Eyebrows;
	[SerializeField] private Transform Glasses;
	[SerializeField] private Transform Hair;
	[SerializeField] private Transform Hats;
	[SerializeField] private Transform Pants;
	[SerializeField] private Transform Headphones;
	[SerializeField] private Transform Masks;
	[SerializeField] private Transform Arms;
	[SerializeField] private Transform Shoes;
	[SerializeField] private Transform Torso;

#if UNITY_EDITOR
	private readonly string _captureImageSavePath = "Assets/Resources/Montage/Thumbnails";
	private readonly string _clothDataSavePath = "Assets/Resources/Montage/ClothData";
	private int _id = 0;

	// 각 부위별로 썸네일 만들기 위해서는 어느 정도의 거리에서 캡쳐해야 하는가?
	private readonly Dictionary<MontageParts, float> SizeByParts = new Dictionary<MontageParts, float>() {
		{ MontageParts.Beard, 0.1f },
		{ MontageParts.Eyebrows, 0.1f },
		{ MontageParts.Glasses, 0.15f },
		{ MontageParts.Hair, 0.4f},
		{ MontageParts.Hats, 0.4f},
		{ MontageParts.Pants, 0.4f},
		{ MontageParts.Headphones, 0.4f},
		{ MontageParts.Masks, 0.4f},
		{ MontageParts.Arms, 0.2f},
		{ MontageParts.Shoes, 0.2f},
		{ MontageParts.Torso, 0.4f}
	};

	private readonly Dictionary<MontageParts, Transform> Parts = new Dictionary<MontageParts, Transform>();

	[ContextMenu("Generate ClothDatas")]
	private void MakeAllSprites() {
		_captureCamera.Initialize();
		Initialize();
		// 캡쳐 전 모든 옷 다 비활성화 후 시작.
		DisableAllItems();

		// 모든 이미지 생성
		foreach (var part in Parts) {
			MakeClothDatas(part.Key);
		}

		// 생성 혹은 수정이 모두 완료되면, Dirty 표시된 모든 에셋 저장
		AssetDatabase.SaveAssets();
	}

	private void Initialize() {
		_id = 0;

		Parts.Clear();
		Parts.Add(MontageParts.Beard, Beard);
		Parts.Add(MontageParts.Eyebrows, Eyebrows);
		Parts.Add(MontageParts.Glasses, Glasses);
		Parts.Add(MontageParts.Hair, Hair);
		Parts.Add(MontageParts.Hats, Hats);
		Parts.Add(MontageParts.Pants, Pants);
		Parts.Add(MontageParts.Headphones, Headphones);
		Parts.Add(MontageParts.Masks, Masks);
		Parts.Add(MontageParts.Arms, Arms);
		Parts.Add(MontageParts.Shoes, Shoes);
		Parts.Add(MontageParts.Torso, Torso);
	}

	private void MakeClothDatas(MontageParts part) {
		// OrthographicSize 설정
		_captureCamera.Camera.orthographicSize = SizeByParts[part];

		Transform parts = Parts[part];

		// 전체 비활성화 한번
		foreach (Transform item in parts) { item.gameObject.SetActive(false); }

		// 하나씩 열고 캡쳐하기
		foreach (Transform item in parts) {
			// 이번 파츠 활성화
			item.gameObject.SetActive(true);

			// 캡쳐
			string assetPath = Capture(part, item.name);
			// 캡쳐된 이미지 Sprite로 변경
			ConfigureImageToSprite(assetPath);
			// MontageClothData 생성
			CreateClothData(part, item.gameObject, assetPath);

			// 비활성화
			item.gameObject.SetActive(false);
		}
	}

	private string Capture(MontageParts part, string fileName) {
		if (_captureCamera == null) {
			Debug.LogError($"[CaptureManager] 캡쳐용 카메라 없음");
			return null;
		}

		string saveFolderPath = Path.Combine(_captureImageSavePath, part.ToString()).Replace('\\', '/');
		Directory.CreateDirectory(saveFolderPath);

		// 파일 .png파일로 저장
		string assetPath = Path.Combine(saveFolderPath, $"{fileName}.png");
		File.WriteAllBytes(
			assetPath,
			_captureCamera.Capture()
		);

		return assetPath;
	}

	private void DisableAllItems() {
		foreach (Transform part in Parts.Values) {
			foreach (Transform cloth in part) {
				cloth.gameObject.SetActive(false);
			}
		}
	}

	private void ConfigureImageToSprite(string assetPath) {

		// 저장된 파일 Sprite로 변경
		TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
		if (importer == null) {
			Debug.LogError($"[CaptureManager] TextureImporter를 찾을 수 없습니다: {assetPath}");
			return;
		}

		importer.textureType = TextureImporterType.Sprite;
		importer.spriteImportMode = SpriteImportMode.Single;
		importer.SaveAndReimport();
	}

	/// <summary>
	/// ClothData를 직접 생성한다
	/// </summary>
	private void CreateClothData(MontageParts part, GameObject instance, string assetPath) {
		// 저장 경로 설정
		string dataFolderPath = Path.Combine(_clothDataSavePath, part.ToString()).Replace('\\', '/');
		string dataPath = Path.Combine(dataFolderPath, $"{instance.name}.asset").Replace('\\', '/');

		// 폴더 없는 경우를 대비해 폴더 미리 생성
		Directory.CreateDirectory(dataFolderPath);

		// Sprite, Prefab등 Data에 저장할 값들 미리 불러오기
		Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
		GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(instance);

		// 에셋 불러오기, 없다면 생성
		MontageClothData data = AssetDatabase.LoadAssetAtPath<MontageClothData>(dataPath);
		if (data == null) {
			data = ScriptableObject.CreateInstance<MontageClothData>();
			AssetDatabase.CreateAsset(data, dataPath);
		}

		data.id = _id++;
		// 장갑이면, 두 부위 모두 넣어줘야 함
		data.ClothPrefabs = prefab;
		data.ClothThumbnail = sprite;
		data.Part = part;

		// Dirty Flag 설정. 모든 생성이 끝나면 dirty 상태인 모든 Asset을 한번에 저장한다.
		EditorUtility.SetDirty(data);
	}
#endif
}
