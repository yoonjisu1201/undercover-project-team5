using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using Unity.VisualScripting;
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

    private CriminalNpcManager _criminalManager;
    private NetworkObject _capturedCriminal;
    private bool _isCapturing;

    // 결과 패널 등 외부에서 촬영된 단서 이미지를 읽기 전용으로 참조하기 위한 프로퍼티
    public IReadOnlyList<Texture2D> CapturedTextures => _capturedTextures;

    private void Start()
    {
        // ClueModuleCapture를 초기화하고 범인 단서 촬영을 시작한다.
        _moduleCapture = new ClueModuleCapture(this, _moduleSpawnPoint, _clueCamera, _renderTexture);

        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }

        RefreshCriminalCluesAsync().Forget();
    }

    private void HandleRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Round2)
        {
            RefreshCriminalCluesAsync().Forget();
        }
    }

    private async UniTask RefreshCriminalCluesAsync()
    {
        if (_isCapturing)
        {
            Debug.LogWarning("[ClueModulePreview] 단서 촬영 중에는 새로고침을 수행할 수 없습니다.", this);
            return;
        }

        _isCapturing = true;

        try
        {
            CancellationToken token = this.GetCancellationTokenOnDestroy();
            _criminalManager ??= await WaitForCriminalManagerAsync(token);
            NetworkObject previousCriminal = _capturedCriminal;

            // RoundState와 새 범인 NetworkVariable의 도착 순서가 다를 수 있으므로
            // 실제 범인이 변경될 때까지 기다린다.
            await UniTask.WaitUntil(() => _criminalManager.CriminalNpc != null && _criminalManager.CriminalNpc != previousCriminal, cancellationToken: token);

            // 같은 네트워크 프레임에 CriminalFeature도 갱신될 시간을 준다.
            await UniTask.NextFrame(token);

            ClearCapturedTextures();
            await ShowCriminalCluesAsync(token);

            _capturedCriminal = _criminalManager.CriminalNpc;
        }

        finally
        {
            _isCapturing = false;
        }
    }

    private async UniTask ShowCriminalCluesAsync(CancellationToken cancellationToken)
    {
        if (!TryGetEquippedModules(_criminalManager))
        {
            Debug.LogWarning("[ClueModulePreview] 범인이 착용한 모듈을 찾지 못했습니다.", this);
            return;
        }

        ShuffleEquippedModules();   // 단서 표시 순서를 무작위로 섞는다.

        List<(ClueUI clueUi, RawImage clueImage)> clueSlots = FindClueSlots();

        // UI 슬롯을 최대한 채우고, 착용 모듈보다 슬롯이 많으면 처음부터 다시 사용한다. -> 단서 중복
        for (int i = 0; i < clueSlots.Count; i++)
        {
            GameObject module = _equippedModules[i % _equippedModules.Count];
            Texture2D texture = await _moduleCapture.CaptureAsync(module, cancellationToken);

            if (texture != null)
            {
                // 단서 UI 슬롯에 촬영된 텍스처를 적용하고, 확대 이미지 단서로 표시한다.
                ApplyCapturedTexture(clueSlots[i], texture, i);
            }

            _moduleCapture.ReleasePreview();
            await UniTask.NextFrame(cancellationToken);
        }
        Debug.Log($"[ClueModulePreview] 단서 촬영 완료 | 총 {clueSlots.Count}개", this);
    }

    private void ClearCapturedTextures()
    {
        foreach (Texture2D texture in _capturedTextures)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
        _capturedTextures.Clear();

        foreach (var slot in FindClueSlots())
        {
            slot.clueUi.ClearClueImage("단서");
        }
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
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
        _moduleCapture?.Dispose();
        ClearCapturedTextures();
    }
}
