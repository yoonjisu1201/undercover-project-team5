using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 다운된 동안 살아 있는 팀원의 1인칭 시야를 돌려 본다.
// 카메라를 실제로 옮기는 일은 PlayerCameraController가 맡고,
// 이 컴포넌트는 "지금 누구를 보고 있는가"만 관리한다.
public sealed class PlayerSpectator : NetworkBehaviour
{
    // 순환 자리 중 내 시점을 가리키는 값. 0 이상은 _targets의 인덱스다.
    private const int OwnViewSlot = -1;

    private PlayerHealth _health;
    private PlayerCameraController _cameraController;
    private CustomInputActions _actions;

    // 넘길 때마다 다시 채운다. 그 사이 누가 쓰러지거나 접속을 끊을 수 있어서
    // 한 번 만든 목록을 계속 들고 있어 봐야 믿을 수 없다.
    private readonly List<Player> _targets = new();

    private int _slotIndex = OwnViewSlot;

    // 지금 보고 있는 팀원. 내 시점이면 null이다.
    public Player CurrentTarget { get; private set; }

    private void Awake()
    {
        _health = GetComponent<PlayerHealth>();
        _cameraController = GetComponent<PlayerCameraController>();
        _actions = new CustomInputActions();
    }

    private void OnEnable()
    {
        _actions.Enable();
    }

    private void OnDisable()
    {
        _actions.Disable();
    }

    public override void OnNetworkSpawn()
    {
        // 관전은 내 화면에서만 일어나는 일이라 남의 복제본에서는 돌 필요가 없다.
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        _health.DownedStateChanged += HandleDownedStateChanged;

    }

    private void Update()
    {
        RetargetIfTargetLost();

        // 메뉴나 가이드북이 떠 있는 동안의 키 입력은 그쪽 몫이다. (PlayerArrestInput과 같은 차단 방식)
        if (GameplayUiMode.IsActive) return;

        if (_actions.Player.SwitchSpectateTarget.WasPressedThisFrame())
        {
            SwitchTarget();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        if (_health != null)
        {
            _health.DownedStateChanged -= HandleDownedStateChanged;
        }

    }

    // 살아 있는 팀원을 차례로 넘긴다.
    // 순환 목록의 첫 칸은 내 시점이라, 계속 넘기면 자기 몸으로 돌아온다.
    public void SwitchTarget()
    {
        if (!_health.IsDowned) return;

        RebuildTargets();

        // 내 시점 한 칸을 앞에 붙여 0부터 세는 값으로 옮기고, 넘긴 뒤 다시 되돌린다.
        int slotCount = _targets.Count + 1;
        _slotIndex = (_slotIndex + 2) % slotCount - 1;

        ApplyCurrentSlot();
    }

    private void ApplyCurrentSlot()
    {
        if (_slotIndex == OwnViewSlot)
        {
            CurrentTarget = null;
            _cameraController.EndSpectate();
            return;
        }

        CurrentTarget = _targets[_slotIndex];
        _cameraController.BeginSpectate(CurrentTarget.GetComponent<PlayerCameraController>());
    }

    // 보고 있던 팀원이 쓰러지거나 접속을 끊으면 그 시야에 머물 수 없다. 살아 있는 첫 팀원으로
    // 옮기고, 아무도 남지 않았으면 SwitchTarget이 알아서 내 시점으로 돌려준다.
    private void RetargetIfTargetLost()
    {
        // 관전 중이 아니면 잃을 대상도 없다.
        if (_slotIndex == OwnViewSlot) return;

        // 접속을 끊으면 오브젝트가 파괴되어 null이 된다.
        bool targetIsGone = CurrentTarget == null || CurrentTarget.PlayerHealth.IsDowned;

        if (!targetIsGone) return;

        // 사라지기 전 목록 기준의 인덱스는 믿을 수 없다. 처음부터 다시 세서
        // 항상 살아 있는 첫 팀원에 안착하게 한다.
        _slotIndex = OwnViewSlot;
        SwitchTarget();
    }

    // 살아 있는 다른 팀원만 모은다. 넘길 때마다 목록을 새로 만들기 때문에 순서가 흔들리면
    // 같은 자리에서 "다음 사람"이 왔다 갔다 하므로, 매번 같은 기준으로 정렬한다.
    private void RebuildTargets()
    {
        _targets.Clear();

        foreach (Player player in Player.ActiveInstances)
        {
            if (player.IsOwner || player.PlayerHealth.IsDowned) continue;

            _targets.Add(player);
        }

        _targets.Sort((left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));
    }

    private void HandleDownedStateChanged(bool previousValue, bool newValue)
    {
        if (newValue) return;

        // 소생되는 순간 관전을 끝낸다. 기상 애니메이션이 끝날 때까지 기다리면
        // 내 캐릭터가 일어나는 동안 남의 시야를 계속 보게 된다.
        _slotIndex = OwnViewSlot;
        CurrentTarget = null;
        _cameraController.EndSpectate();
    }
}
