using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;

public class ClueModulePreview : MonoBehaviour
{
    [Header("Preview")]
    [SerializeField] private MontageSyncManager _syncManager;
    [SerializeField, Min(1)] private int _clueCount = 8;

    private readonly List<ClothPart> _equippedParts = new();
    private readonly List<MontageState> _equippedStates = new();
    private readonly List<Texture2D> _capturedTextures = new();
    [Tooltip("확대 이미지 라벨. {0} 에 단서 번호가 들어간다.")]
    [SerializeField] private LocalizedString _magnifiedImageLabel;

    private readonly List<string> _capturedPartLabels = new();

    private CriminalNpcManager _criminalManager;
    private NetworkObject _capturedCriminal;
    private bool _isCapturing;

    public IReadOnlyList<Texture2D> CapturedTextures => _capturedTextures;

    private void Start()
    {
        _syncManager ??= FindFirstObjectByType<MontageSyncManager>(FindObjectsInactive.Include);

        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStarted += HandleRoundStarted;
        }

        RefreshCriminalCluesAsync().Forget();
    }

    private void HandleRoundStarted(int roundIndex)
    {
        if (roundIndex > 0)
        {
            RefreshCriminalCluesAsync().Forget();
        }
    }

    private async UniTask RefreshCriminalCluesAsync()
    {
        if (_isCapturing)
        {
            Debug.LogWarning("[ClueModulePreview] 단서 준비 중에는 새로고침을 수행할 수 없습니다.", this);
            return;
        }

        _isCapturing = true;

        try
        {
            CancellationToken token = this.GetCancellationTokenOnDestroy();
            _criminalManager ??= await WaitForCriminalManagerAsync(token);
            NetworkObject previousCriminal = _capturedCriminal;

            await UniTask.WaitUntil(() => _criminalManager.CriminalNpc != null && _criminalManager.CriminalNpc != previousCriminal, cancellationToken: token);
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
        if (_syncManager == null)
        {
            Debug.LogError("[ClueModulePreview] MontageSyncManager를 찾지 못했습니다.", this);
            return;
        }

        await _syncManager.InitializeAsync().AttachExternalCancellation(cancellationToken);

        if (!TryGetEquippedStates(_criminalManager))
        {
            Debug.LogWarning("[ClueModulePreview] 범인이 착용한 몽타주 모듈을 찾지 못했습니다.", this);
            return;
        }

        ShuffleEquippedParts(_criminalManager.CriminalNpc.NetworkObjectId);

        int captureCount = Mathf.Min(_clueCount, _equippedParts.Count);

        // 단서 번호별로 몽타주 카메라로 찍은 이미지를 보관하고, 단서 목록 UI가 열람 시점에 꺼내 쓴다.
        for (int i = 0; i < captureCount; i++)
        {
            ClothPart focusPart = _equippedParts[i];
            Texture2D texture = _syncManager.CaptureTemporaryState(_equippedStates[i], focusPart);
            if (texture == null)
            {
                Debug.LogWarning($"[ClueModulePreview] {focusPart} 단서 캡쳐에 실패해서 목록에서 제외합니다.", this);
                continue;
            }

            _capturedTextures.Add(texture);
            _capturedPartLabels.Add(GetPartLabel(focusPart));

            await UniTask.NextFrame(cancellationToken);
        }

        // 단서 캡처 도중 옷을 입혀가며 찍은 렌더 결과가 벽면에 남지 않도록, 마지막에 벗은 상태로 1회 렌더링한다.
        _syncManager.CaptureCleanWallState();

        Debug.Log($"[ClueModulePreview] 몽타주 카메라 단서 준비 완료 | 총 {_capturedTextures.Count}개", this);
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
        _capturedPartLabels.Clear();
    }

    private async UniTask<CriminalNpcManager> WaitForCriminalManagerAsync(CancellationToken cancellationToken)
    {
        CriminalNpcManager criminalManager = null;
        await UniTask.WaitUntil(() => (criminalManager = FindFirstObjectByType<CriminalNpcManager>()) != null, cancellationToken: cancellationToken);
        return criminalManager;
    }

    private bool TryGetEquippedStates(CriminalNpcManager criminalManager)
    {
        _equippedParts.Clear();
        _equippedStates.Clear();

        if (_syncManager == null)
        {
            Debug.LogError("[ClueModulePreview] MontageSyncManager를 찾지 못했습니다.", this);
            return false;
        }

        OutfitFeature outfit = criminalManager.CriminalFeature.Outfit;
        LogAndAddIfEquippedButNotCapturable(ClothPart.Beard, outfit.BeardNumber);
        LogAndAddIfEquippedButNotCapturable(ClothPart.Eyebrow, outfit.EyebrowsNumber);
        LogAndAddIfEquippedButNotCapturable(ClothPart.Glasses, outfit.GlassesNumber);
        LogAndAddIfEquippedButNotCapturable(ClothPart.Hair, outfit.HairNumber);
        LogAndAddIfEquippedButNotCapturable(ClothPart.Hat, outfit.HatNumber);
        LogAndAddIfEquippedButNotCapturable(ClothPart.Headphone, outfit.HeadphoneNumber);
        LogAndAddIfEquippedButNotCapturable(ClothPart.Mask, outfit.MaskNumber);
        LogAndAddIfEquippedButNotCapturable(ClothPart.Pants, outfit.PantsNumber);
        LogAndAddIfEquippedButNotCapturable(ClothPart.Shoes, outfit.ShoesNumber);
        LogAndAddIfEquippedButNotCapturable(ClothPart.Torso, outfit.TorsoNumber);

        if (_equippedParts.Count > 0)
        {
            return true;
        }

        Debug.LogWarning("[ClueModulePreview] 범인이 착용한 몽타주 모듈을 찾지 못했습니다.", this);
        return false;
    }

    // 캡쳐 가능한지 확인만 하고 상태를 남기지 않는다. ClueCaptureRules와 같은 기준(clothId >= 0
    // && MontagePrefab 존재)을 써서 ClueModulePreview와 ClueSpawner가 다른 개수를 세지 않게 한다.
    private void LogAndAddIfEquippedButNotCapturable(ClothPart part, int clothId)
    {
        if (clothId < 0)
        {
            return;
        }

        if (!ClueCaptureRules.IsCapturable(part, clothId, out ClothData data))
        {
            Debug.LogWarning($"[ClueModulePreview] {part} 파츠의 옷 id({clothId})에 몽타주용 프리팹이 없어 단서에서 제외합니다.", this);
            return;
        }

        _equippedStates.Add(MontageState.Empty.WithCloth(part, clothId));
        _equippedParts.Add(part);
    }

    private void ShuffleEquippedParts(ulong criminalNetworkObjectId)
    {
        System.Random random = new(unchecked((int)criminalNetworkObjectId));

        for (int i = _equippedParts.Count - 1; i > 0; i--)
        {
            int randomIndex = random.Next(i + 1);
            (_equippedStates[i], _equippedStates[randomIndex]) =
                (_equippedStates[randomIndex], _equippedStates[i]);
            (_equippedParts[i], _equippedParts[randomIndex]) =
                (_equippedParts[randomIndex], _equippedParts[i]);
        }
    }

    // 부위명은 몽타주 화면이 쓰는 키를 그대로 재사용한다. 같은 부위를 두 화면이 다르게 부르면 안 된다.
    private static string GetPartLabel(ClothPart part) => Localize(part switch
    {
        ClothPart.Hat => "montage_part_hat",
        ClothPart.Hair => "montage_part_hair",
        ClothPart.Torso => "montage_part_torso",
        ClothPart.Pants => "montage_part_pants",
        ClothPart.Shoes => "montage_part_shoes",
        ClothPart.Glasses => "montage_part_glasses",
        ClothPart.Mask => "montage_part_mask",
        ClothPart.Headphone => "montage_part_headphone",
        ClothPart.Beard => "montage_part_beard",
        ClothPart.Eyebrow => "montage_part_eyebrow",
        _ => "montage_part_outfit"
    });

    // 부위명은 캡처 시점에 문자열로 굳는다. 라운드 도중 언어를 바꾸면 이미 담아둔 값은 그대로 남는다.
    private static string Localize(string key)
        => new LocalizedString(LocalizationTable, key).GetLocalizedString();

    private const string LocalizationTable = "Language Table";

    // 단서 목록에서 썸네일과 부위명만 필요할 때 쓴다. clueNumber는 1부터 시작한다.
    public bool TryGetCapture(int clueNumber, out Texture2D texture, out string partLabel)
    {
        int clueIndex = clueNumber - 1;
        if (clueIndex < 0 || clueIndex >= _capturedTextures.Count)
        {
            texture = null;
            partLabel = null;
            return false;
        }

        texture = _capturedTextures[clueIndex];
        partLabel = _capturedPartLabels[clueIndex];
        return texture != null;
    }

    // clueNumber는 1부터 시작한다.
    public bool TryApplyTo(int clueNumber, ClueUI clueUi)
    {
        int clueIndex = clueNumber - 1;
        if (clueUi == null || clueIndex < 0 || clueIndex >= _capturedTextures.Count || _capturedTextures[clueIndex] == null)
        {
            return false;
        }

        clueUi.ShowClueImage(
            _capturedTextures[clueIndex],
            _magnifiedImageLabel.GetLocalizedString(clueNumber),
            _capturedPartLabels[clueIndex]);
        return true;
    }

    private void OnDestroy()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStarted -= HandleRoundStarted;
        }

        ClearCapturedTextures();
    }
}
