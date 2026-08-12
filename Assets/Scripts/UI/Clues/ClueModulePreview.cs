using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public class ClueModulePreview : MonoBehaviour
{
    [Header("Preview")]
    [SerializeField] private Transform _moduleSpawnPoint;
    [SerializeField] private Camera _clueCamera;
    [SerializeField] private RenderTexture _renderTexture;
    [SerializeField, Min(1)] private int _clueCount = 8;

    private readonly List<GameObject> _equippedModules = new();
    private readonly List<MontageParts> _equippedParts = new();
    private readonly List<Texture2D> _capturedTextures = new();
    private readonly List<string> _capturedPartLabels = new();
    private ClueModuleCapture _moduleCapture;

    private CriminalNpcManager _criminalManager;
    private NetworkObject _capturedCriminal;
    private bool _isCapturing;

    public IReadOnlyList<Texture2D> CapturedTextures => _capturedTextures;

    private void Start()
    {
        _moduleCapture = new ClueModuleCapture(this, _moduleSpawnPoint, _clueCamera, _renderTexture);

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
            Debug.LogWarning("[ClueModulePreview] 단서 촬영 중에는 새로고침을 수행할 수 없습니다.", this);
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
        if (!TryGetEquippedModules(_criminalManager))
        {
            Debug.LogWarning("[ClueModulePreview] 범인이 착용한 모듈을 찾지 못했습니다.", this);
            return;
        }

        ShuffleEquippedModules(_criminalManager.CriminalNpc.NetworkObjectId);

        // 캡처 결과는 단서 번호별로 보관하고, 각 ClueItem 인스턴스가 자기 Canvas에 꺼내 쓴다.
        for (int i = 0; i < _clueCount; i++)
        {
            int moduleIndex = i % _equippedModules.Count;
            Texture2D texture = await _moduleCapture.CaptureAsync(_equippedModules[moduleIndex], cancellationToken);

            _capturedTextures.Add(texture);
            _capturedPartLabels.Add(GetPartLabel(_equippedParts[moduleIndex]));

            _moduleCapture.ReleasePreview();
            await UniTask.NextFrame(cancellationToken);
        }

        RefreshSpawnedClueCanvases();
        Debug.Log($"[ClueModulePreview] 단서 촬영 완료 | 총 {_capturedTextures.Count}개", this);
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
        RefreshSpawnedClueCanvases();
    }

    private static void RefreshSpawnedClueCanvases()
    {
        foreach (ClueItem clueItem in FindObjectsByType<ClueItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            clueItem.RefreshCanvas();
        }
    }

    private async UniTask<CriminalNpcManager> WaitForCriminalManagerAsync(CancellationToken cancellationToken)
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

        outfitController.GetEquippedModules(criminalManager.CriminalFeature.Outfit, _equippedModules, _equippedParts);
        if (_equippedModules.Count > 0)
        {
            return true;
        }

        Debug.LogWarning("[ClueModulePreview] 범인이 착용한 모듈을 찾지 못했습니다.", this);
        return false;
    }

    private void ShuffleEquippedModules(ulong criminalNetworkObjectId)
    {
        System.Random random = new(unchecked((int)criminalNetworkObjectId));

        for (int i = _equippedModules.Count - 1; i > 0; i--)
        {
            int randomIndex = random.Next(i + 1);
            (_equippedModules[i], _equippedModules[randomIndex]) =
                (_equippedModules[randomIndex], _equippedModules[i]);
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

    // clueNumber는 1부터 시작한다.
    public bool TryApplyTo(int clueNumber, ClueUI clueUi)
    {
        int clueIndex = clueNumber - 1;
        if (clueUi == null || clueIndex < 0 || clueIndex >= _capturedTextures.Count)
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

        _moduleCapture?.Dispose();
        ClearCapturedTextures();
    }
}
