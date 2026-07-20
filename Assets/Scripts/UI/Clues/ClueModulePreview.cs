using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class ClueModulePreview : MonoBehaviour
{
    // 전체 처리 순서
    // 1. 범인 데이터 대기 → 2. 단서 UI 슬롯 탐색 → 3. 범인 파츠 촬영 → 4. 카메라/텍스처 처리
    private const string ClueImageName = "ClueImage";
    private const string BackgroundName = "BackGround";

    [Header("Preview")]
    [SerializeField] private Transform _moduleSpawnPoint;
    [SerializeField] private Camera _clueCamera;
    [SerializeField] private RenderTexture _renderTexture;

    private readonly List<GameObject> _equippedModules = new();
    private readonly List<Texture2D> _capturedTextures = new();
    private ClueModuleCapture _moduleCapture;

    private void Start()
    {
        // ClueModuleCapture를 초기화하고 범인 단서 촬영을 시작한다.
        _moduleCapture = new ClueModuleCapture(this, _moduleSpawnPoint, _clueCamera, _renderTexture);

        ShowCriminalCluesAsync().Forget();
    }

    private async UniTaskVoid ShowCriminalCluesAsync()
    {
        CancellationToken cancellationToken = this.GetCancellationTokenOnDestroy();

        //--- 1. 범인 데이터 대기 ---//
        CriminalNpcManager criminalManager = await WaitForCriminalManagerAsync(cancellationToken);
        await UniTask.WaitUntil(() => criminalManager.CriminalNpc != null, cancellationToken: cancellationToken);

        if (!TryGetEquippedModules(criminalManager))
        {
            return;
        }

        ShuffleEquippedModules();

        //--- 2. 단서 UI 슬롯 탐색 ---//
        List<(ClueUI clueUi, RawImage clueImage)> clueSlots = FindClueSlots();
        int clueCount = Mathf.Min(_equippedModules.Count, clueSlots.Count);

        //--- 3. 범인 파츠 촬영 ---//
        for (int i = 0; i < clueCount; i++)
        {
            //--- 4. 카메라/텍스처 처리는 ClueModuleCapture가 담당 ---//
            Texture2D texture = await _moduleCapture.CaptureAsync(_equippedModules[i], cancellationToken);

            if (texture != null)
            {
                ApplyCapturedTexture(clueSlots[i], texture, i);
            }

            _moduleCapture.ReleasePreview();
            await UniTask.NextFrame(cancellationToken);
        }

        Debug.Log($"[ClueModulePreview] 단서 촬영 완료 | 총 {clueCount}개", this);
    }

    private async UniTask<CriminalNpcManager> WaitForCriminalManagerAsync(
        CancellationToken cancellationToken)
    {
        CriminalNpcManager criminalManager = null;
        await UniTask.WaitUntil(() => (criminalManager = FindFirstObjectByType<CriminalNpcManager>()) != null, cancellationToken: cancellationToken);

        return criminalManager;
    }

    private bool TryGetEquippedModules(CriminalNpcManager criminalManager)
    {
        if (!criminalManager.CriminalNpc.TryGetComponent(out NpcOutfitController outfitController))
        {
            Debug.LogError("[ClueModulePreview] 범인 NPC에 NpcOutfitController가 없습니다.", this);
            return false;
        }

        // 동기화된 범인의 Outfit Feature에 해당하는 실제 파츠만 가져온다.
        outfitController.GetEquippedModules(criminalManager.CriminalFeature.Outfit, _equippedModules);
        if (_equippedModules.Count > 0)
        {
            return true;
        }

        Debug.LogWarning("[ClueModulePreview] 범인이 착용한 모듈을 찾지 못했습니다.", this);
        return false;
    }

    private void ShuffleEquippedModules()
    {
        // Fisher-Yates Shuffle로 단서 표시 순서를 무작위로 섞는다.
        for (int i = _equippedModules.Count - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);
            (_equippedModules[i], _equippedModules[randomIndex]) =
                (_equippedModules[randomIndex], _equippedModules[i]);
        }
    }

    // 단서 UI 슬롯을 찾아서 (ClueUI, RawImage) 리스트로 반환한다.
    private List<(ClueUI clueUi, RawImage clueImage)> FindClueSlots()
    {
        ClueUI[] clueUis = FindObjectsByType<ClueUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Array.Sort(clueUis, (left, right) => string.CompareOrdinal(left.name, right.name));

        var slots = new List<(ClueUI clueUi, RawImage clueImage)>();
        foreach (ClueUI clueUi in clueUis)
        {
            RawImage clueImage = Array.Find(clueUi.GetComponentsInChildren<RawImage>(true), image => image.name == ClueImageName);
            if (clueImage == null)
            {
                continue;
            }

            MoveBackgroundBehindClueImage(clueImage);
            slots.Add((clueUi, clueImage));
        }

        return slots;
    }

    private static void MoveBackgroundBehindClueImage(RawImage clueImage)
    {
        Transform background = clueImage.transform.Find(BackgroundName);
        if (background == null)
        {
            return;
        }

        background.SetParent(clueImage.transform.parent, false);
        background.SetAsFirstSibling();
        background.gameObject.SetActive(true);
    }

    private void ApplyCapturedTexture((ClueUI clueUi, RawImage clueImage) clueSlot, Texture2D texture, int clueIndex)
    {
        _capturedTextures.Add(texture);
        clueSlot.clueImage.texture = texture;
        clueSlot.clueUi.ShowClueImage(texture, $"확대 이미지 단서 {clueIndex + 1}");
    }

    private void OnDestroy()
    {
        _moduleCapture?.Dispose();

        foreach (Texture2D texture in _capturedTextures)
        {
            Destroy(texture);
        }
    }
}
