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
	// ClothCatalog이 Resources.Load(Path.Combine("ClothData", part, id))로 읽는 경로와 같아야 한다.
	private readonly string _clothDataSavePath = "Assets/Resources/ClothData";
	private int _id = 0;

	// 각 부위별로 썸네일 만들기 위해서는 어느 정도의 거리에서 캡쳐해야 하는가?
	private readonly Dictionary<ClothPart, float> SizeByParts = new Dictionary<ClothPart, float>() {
		{ ClothPart.Beard, 0.1f },
		{ ClothPart.Eyebrow, 0.1f },
		{ ClothPart.Glasses, 0.15f },
		{ ClothPart.Hair, 0.4f},
		{ ClothPart.Hat, 0.4f},
		{ ClothPart.Pants, 0.4f},
		{ ClothPart.Headphone, 0.4f},
		{ ClothPart.Mask, 0.4f},
		{ ClothPart.Arm, 0.2f},
		{ ClothPart.Shoes, 0.2f},
		{ ClothPart.Torso, 0.4f}
	};

	private readonly Dictionary<ClothPart, Transform> Parts = new Dictionary<ClothPart, Transform>();

	[ContextMenu("Generate ClothDatas")]
	private void MakeAllSprites() {
		_captureCamera.Initialize();
		Initialize();
		// 캡쳐 전 모든 옷 다 비활성화 후 시작.
		DisableAllItems();

		// 모든 이미지 생성
		foreach (var part in Parts) {
			_id = 0;
			MakeClothDatas(part.Key);
		}

		// 생성 혹은 수정이 모두 완료되면, Dirty 표시된 모든 에셋 저장
		AssetDatabase.SaveAssets();
	}

	private void Initialize() {
		_id = 0;

		Parts.Clear();
		Parts.Add(ClothPart.Beard, Beard);
		Parts.Add(ClothPart.Eyebrow, Eyebrows);
		Parts.Add(ClothPart.Glasses, Glasses);
		Parts.Add(ClothPart.Hair, Hair);
		Parts.Add(ClothPart.Hat, Hats);
		Parts.Add(ClothPart.Pants, Pants);
		Parts.Add(ClothPart.Headphone, Headphones);
		Parts.Add(ClothPart.Mask, Masks);
		Parts.Add(ClothPart.Arm, Arms);
		Parts.Add(ClothPart.Shoes, Shoes);
		Parts.Add(ClothPart.Torso, Torso);
	}

	private void MakeClothDatas(ClothPart part) {
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
			string assetPath = Capture(part, _id.ToString());
			Debug.Log($"[Capture] 종료, AssetPath = {assetPath}");
			// 캡쳐된 이미지 Sprite로 변경
			ConfigureImageToSprite(assetPath);
			// ClothData 생성(있으면 몽타주 쪽 필드만 갱신)
			CreateClothData(part, item.gameObject, assetPath);

			// 비활성화
			item.gameObject.SetActive(false);
		}
	}

	private string Capture(ClothPart part, string fileName) {
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
		// File.WriteAllBytes로 생성된 파일을 Unity AssetDatabase에 등록
		AssetDatabase.ImportAsset(
			assetPath,
			ImportAssetOptions.ForceSynchronousImport
		);

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

	// ClothData를 직접 생성한다. 이미 NPC 쪽에서 만들어둔 카드가 있으면 몽타주 쪽 필드만 채운다.
	private void CreateClothData(ClothPart part, GameObject instance, string assetPath) {
		// 저장 경로 설정
		string dataFolderPath = Path.Combine(_clothDataSavePath, part.ToString()).Replace('\\', '/');
		string dataPath = Path.Combine(dataFolderPath, $"{_id}.asset").Replace('\\', '/');

		// 폴더 없는 경우를 대비해 폴더 미리 생성
		Directory.CreateDirectory(dataFolderPath);

		// Sprite, Prefab등 Data에 저장할 값들 미리 불러오기
		Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
		GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(instance);

		// 에셋 불러오기, 없다면 생성
		ClothData data = AssetDatabase.LoadAssetAtPath<ClothData>(dataPath);
		if (data == null) {
			data = ScriptableObject.CreateInstance<ClothData>();
			AssetDatabase.CreateAsset(data, dataPath);
		}

		data.Id = _id++;
		data.MontagePrefab = prefab;
		data.Thumbnail = sprite;
		data.Part = part;

		// Dirty Flag 설정. 모든 생성이 끝나면 dirty 상태인 모든 Asset을 한번에 저장한다.
		EditorUtility.SetDirty(data);
	}
#endif
}
