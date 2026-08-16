using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public class ClueModulePreview : MonoBehaviour
{
    [Header("Preview")]
    [SerializeField] private MontageSyncManager _syncManager;
    [SerializeField, Min(1)] private int _clueCount = 8;

    private readonly List<MontageParts> _equippedParts = new();
    private readonly List<MontageState> _equippedStates = new();
    private readonly List<Texture2D> _capturedTextures = new();
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
            MontageParts focusPart = _equippedParts[i];
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

        AddClueState(MontageParts.Beard, criminalManager.CriminalFeature.Outfit.BeardNumber);
        AddClueState(MontageParts.Eyebrows, criminalManager.CriminalFeature.Outfit.EyebrowsNumber);
        AddClueState(MontageParts.Glasses, criminalManager.CriminalFeature.Outfit.GlassesNumber);
        AddClueState(MontageParts.Hair, criminalManager.CriminalFeature.Outfit.HairNumber);
        AddClueState(MontageParts.Hats, criminalManager.CriminalFeature.Outfit.HatNumber);
        AddClueState(MontageParts.Headphones, criminalManager.CriminalFeature.Outfit.HeadphoneNumber);
        AddClueState(MontageParts.Arms, criminalManager.CriminalFeature.Outfit.ArmNumber);
        AddClueState(MontageParts.Masks, criminalManager.CriminalFeature.Outfit.MaskNumber);
        AddClueState(MontageParts.Pants, criminalManager.CriminalFeature.Outfit.PantsNumber);
        AddClueState(MontageParts.Shoes, criminalManager.CriminalFeature.Outfit.ShoesNumber);
        AddClueState(MontageParts.Torso, criminalManager.CriminalFeature.Outfit.TorsoNumber);

        if (_equippedParts.Count > 0)
        {
            return true;
        }

        Debug.LogWarning("[ClueModulePreview] 범인이 착용한 몽타주 모듈을 찾지 못했습니다.", this);
        return false;
    }

    private void AddClueState(MontageParts part, int npcOutfitIndex)
    {
        if (npcOutfitIndex < 0)
        {
            return;
        }

        if (!_syncManager.TryGetClothIdByIndex(part, npcOutfitIndex, out int clothId))
        {
            Debug.LogWarning($"[ClueModulePreview] {part} 파츠의 NPC 인덱스({npcOutfitIndex})에 맞는 몽타주 데이터를 찾지 못했습니다.", this);
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

    private static string GetPartLabel(MontageParts part) => part switch
    {
        MontageParts.Hats => "모자",
        MontageParts.Hair => "머리",
        MontageParts.Torso => "상의",
        MontageParts.Pants => "하의",
        MontageParts.Shoes => "신발",
        MontageParts.Arms => "손",
        MontageParts.Glasses => "안경",
        MontageParts.Masks => "마스크",
        MontageParts.Headphones => "헤드폰",
        MontageParts.Beard => "수염",
        MontageParts.Eyebrows => "눈썹",
        _ => "의상"
    };

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
            $"확대 이미지 단서 {clueNumber}",
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
