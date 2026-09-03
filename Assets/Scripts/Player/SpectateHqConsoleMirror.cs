using Unity.Netcode;
using UnityEngine;

// 관전 중인 팀원이 본부 콘솔을 열면 내 화면에도 같은 화면을 띄운다.
//
// 콘솔 UI는 씬에 하나뿐이라 미션 기기처럼 사본을 만들 수 없다. 그래서 그 하나를 켜고
// 대상이 보고 있는 화면을 따라 맞춘다. 관전자는 기절 중이라 본인이 콘솔을 쓰는 일이 없어
// 서로 다툴 일은 없다.
public sealed class SpectateHqConsoleMirror : NetworkBehaviour
{
    private PlayerSpectator _spectator;

    // 지금 구독 중인 관전 대상. 대상이 바뀌면 구독을 옮긴다.
    private Player _watched;

    // 씬 오브젝트들. 콘솔이 gitignore 대상 프리팹에 있어 인스펙터 참조를 쓸 수 없다.
    private HqScreenController _console;
    private InventoryUI _inventoryUI;
    private MontageShareUI _montageUI;
    private CCTVHub _cctvHub;

    private bool _isMirroring;

    private void Awake()
    {
        _spectator = GetComponent<PlayerSpectator>();
    }

    public override void OnNetworkSpawn()
    {
        // 관전은 내 화면에서만 일어나는 일이라 남의 복제본에서는 돌 필요가 없다.
        if (!IsOwner || _spectator == null)
        {
            enabled = false;
            return;
        }

        _spectator.TargetChanged += HandleTargetChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        if (_spectator != null)
        {
            _spectator.TargetChanged -= HandleTargetChanged;
        }

        Unwatch();
        StopMirroring();
    }

    private void HandleTargetChanged(Player target)
    {
        Unwatch();

        // 내 시점으로 돌아왔으면 남의 콘솔을 계속 띄워 둘 이유가 없다.
        if (target == null)
        {
            StopMirroring();
            return;
        }

        _watched = target;
        _watched.HqConsoleChanged += HandleConsoleChanged;

        // 이미 콘솔을 열어 둔 사람으로 전환했을 수 있다. 지금 상태를 한 번 반영한다.
        Apply(_watched.HqConsole);
    }

    private void HandleConsoleChanged(HqConsoleState previousValue, HqConsoleState newValue)
    {
        Apply(newValue);
    }

    private void Apply(HqConsoleState state)
    {
        if (!state.IsOpen)
        {
            StopMirroring();
            return;
        }

        if (!StartMirroring()) return;

        HqScreenController.ConsoleTab tab = (HqScreenController.ConsoleTab)state.Tab;
        _console.ShowTab(tab);

        // 탭 안쪽 상태는 그 화면이 켜진 뒤에 맞춘다. 꺼져 있는 화면을 건드리지 않기 위해서고,
        // CCTV는 화면이 활성화되면서 번호 변경을 구독하므로 순서를 지켜야 표시까지 따라온다.
        switch (tab)
        {
            case HqScreenController.ConsoleTab.Cctv:
                ResolveCctvHub()?.SwitchToIndex(state.CctvCamera);
                break;

            case HqScreenController.ConsoleTab.Map:
                ApplyMinimapMode((MinimapScreenController.MinimapMode)state.MinimapMode);
                break;

            case HqScreenController.ConsoleTab.Montage:
                _console.Montage?.ShowPart((ClothPart)state.MontagePart);
                break;
        }
    }

    private void ApplyMinimapMode(MinimapScreenController.MinimapMode mode)
    {
        MinimapScreenController minimap = _console.Minimap;
        if (minimap == null) return;

        if (mode == MinimapScreenController.MinimapMode.Underground)
        {
            minimap.ShowUnderground();
        }
        else
        {
            minimap.ShowField();
        }
    }

    private bool StartMirroring()
    {
        if (_isMirroring) return true;

        _console = ResolveConsole();
        if (_console == null) return false;

        _isMirroring = true;

        _console.gameObject.SetActive(true);

        // 켠 뒤에 막아야 한다. OnEnable 이 ESC 스택에 자기를 올리기 때문이다.
        _console.SetInteractable(false);

        SetSideUisVisible(false);
        return true;
    }

    private void StopMirroring()
    {
        if (!_isMirroring) return;

        _isMirroring = false;

        if (_console != null)
        {
            _console.gameObject.SetActive(false);
        }

        SetSideUisVisible(true);
    }

    // 대상 화면에서는 콘솔을 여는 동안 이 둘이 꺼진다. 관전자 화면도 같게 맞춘다.
    private void SetSideUisVisible(bool visible)
    {
        _inventoryUI ??= FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        _montageUI ??= FindFirstObjectByType<MontageShareUI>(FindObjectsInactive.Include);

        if (_inventoryUI != null)
        {
            _inventoryUI.gameObject.SetActive(visible);
        }

        if (_montageUI != null)
        {
            _montageUI.gameObject.SetActive(visible);
        }
    }

    private HqScreenController ResolveConsole()
    {
        // 닫혀 있는 동안에는 비활성 상태라 비활성 오브젝트까지 훑어야 찾을 수 있다.
        return _console != null
            ? _console
            : FindFirstObjectByType<HqScreenController>(FindObjectsInactive.Include);
    }

    private CCTVHub ResolveCctvHub()
    {
        return _cctvHub != null
            ? _cctvHub
            : _cctvHub = FindFirstObjectByType<CCTVHub>(FindObjectsInactive.Include);
    }

    private void Unwatch()
    {
        if (_watched == null) return;

        _watched.HqConsoleChanged -= HandleConsoleChanged;
        _watched = null;
    }
}
