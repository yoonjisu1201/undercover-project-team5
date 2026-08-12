using Unity.Netcode;
using UnityEngine;

// 단서 아이템. 하나의 ItemData/프리팹을 공유하고, 어떤 단서인지는 서버가 부여한 번호로 구분한다.
public class ClueItem : ItemBase, IUsable
{
    [SerializeField] private ClueUI _clueCanvasPrefab;

    private readonly NetworkVariable<int> _clueNumber =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private ClueUI _clueCanvas;

    public int ClueNumber => _clueNumber.Value;
    public string UseText => "단서 확인";
    public string UseCompletedMessage => string.Empty;

    protected override void Awake()
    {
        base.Awake();

        if (_clueCanvasPrefab == null)
        {
            Debug.LogError("[ClueItem] ClueCanvas 프리팹이 연결되지 않았습니다.", this);
            return;
        }

        _clueCanvas = Instantiate(_clueCanvasPrefab, transform);
        _clueCanvas.name = "ClueCanvas";
        _clueCanvas.gameObject.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _clueNumber.OnValueChanged += HandleClueNumberChanged;
        RefreshCanvas();
    }

    public override void OnNetworkDespawn()
    {
        _clueNumber.OnValueChanged -= HandleClueNumberChanged;
        base.OnNetworkDespawn();
    }

    // 서버 전용. NetworkObject.Spawn() 이후에 호출해야 한다.
    public void SetClueNumber(int clueNumber)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        _clueNumber.Value = clueNumber;
        RefreshCanvas();
    }

    public bool CanUse(GameObject user, out string failReason)
    {
        failReason = null;
        return _clueCanvas != null;
    }

    // 인벤토리에 새로 들어온 단서는 즉시 확인한다.
    protected override void OnAdded() => ShowClue();

    // 단서는 서버에서 소모하거나 바꿀 상태가 없다. 사용 승인 후 소유자 클라이언트가 Canvas를 연다.
    public void Use(GameObject user, PlayerInventory inventory, int selectedIndex) { }

    protected override void OnUseCompleted()
    {
        ShowClue();
    }

    private void HandleClueNumberChanged(int previousValue, int currentValue)
    {
        RefreshCanvas();
    }

    public void RefreshCanvas()
    {
        if (_clueCanvas == null)
        {
            return;
        }

        ClueModulePreview preview = FindFirstObjectByType<ClueModulePreview>(FindObjectsInactive.Include);
        if (preview == null || !preview.TryApplyTo(_clueNumber.Value, _clueCanvas))
        {
            _clueCanvas.ClearClueImage("단서");
        }
    }

    private void ShowClue()
    {
        if (_clueCanvas == null)
        {
            return;
        }

        RefreshCanvas();
        _clueCanvas.gameObject.SetActive(true);
    }
}
