using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

// 다운된 동안 뜨는 관전 패널. "지금 누구를 보고 있는가"는 PlayerSpectator가 갖고 있고,
// 이 컴포넌트는 그 값과 팀원들의 생존 상태를 화면에 옮기기만 한다.
[RequireComponent(typeof(Canvas))]
public sealed class SpectatorUI : MonoBehaviour
{
    // 팀원별 DownedStateChanged를 구독하면 접속 종료 시 목록을 정리하는 코드가 따라붙는다.
    // 4명을 다운된 동안에만 다시 그리는 정도라, 주기적으로 훑는 편이 더 싸다.
    private const float RowRefreshInterval = 0.25f;

    [Header("관전 대상")]
    [SerializeField] private TMP_Text _targetNameText;
    [SerializeField] private TMP_Text _targetStatusText;

    [Header("하단 안내")]
    [SerializeField] private TMP_Text _bottomHintText;

    [Header("팀원 목록")]
    [SerializeField] private SpectatorRowUI[] _rows;

    [Header("생존 상태 색")]
    [SerializeField] private Color _aliveColor = new(0.16f, 0.88f, 0.82f, 1f);
    [SerializeField] private Color _downedColor = new(0.96f, 0.25f, 0.22f, 1f);

    // 생존 여부에 따라 문구가 갈려서, 경우마다 하나씩 두고 코드에서 고른다. (PlayerListPanelUI와 같은 방식)
    [Header("문구")]
    [SerializeField] private LocalizedString _targetAliveStatus;
    [SerializeField] private LocalizedString _targetDownedStatus;
    [SerializeField] private LocalizedString _rowAliveStatus;
    [SerializeField] private LocalizedString _rowDownedStatus;
    [SerializeField] private LocalizedString _spectatingHint;
    [SerializeField] private LocalizedString _ownViewHint;

    private Canvas _canvas;
    private Player _localPlayer;
    private PlayerSpectator _spectator;

    // 화면마다 같은 순서로 보이도록 OwnerClientId로 정렬해 쓴다. (HpStatusUI와 같은 기준)
    private readonly List<Player> _orderedPlayers = new();

    private float _nextRowRefreshTime;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.enabled = false;
    }

    // PlayScene은 라운드 시작 전 전원 스폰이 보장되므로, 시작 시 한 번만 로컬 플레이어를 찾는다.
    private void Start()
    {
        foreach (Player player in Player.ActiveInstances)
        {
            if (!player.IsOwner) continue;

            _localPlayer = player;
            _spectator = player.GetComponent<PlayerSpectator>();
            break;
        }

        if (_spectator == null)
        {
            Debug.LogError("[SpectatorUI] 로컬 플레이어를 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        _localPlayer.PlayerHealth.DownedStateChanged += HandleDownedStateChanged;
        _spectator.TargetChanged += HandleTargetChanged;
        LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;
    }

    private void OnDestroy()
    {
        // 씬을 벗어날 때는 플레이어 오브젝트가 먼저 파괴돼 있을 수 있다.
        if (_localPlayer != null && _localPlayer.PlayerHealth != null)
        {
            _localPlayer.PlayerHealth.DownedStateChanged -= HandleDownedStateChanged;
        }

        if (_spectator != null)
        {
            _spectator.TargetChanged -= HandleTargetChanged;
        }

        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;
    }

    private void Update()
    {
        if (!_canvas.enabled || Time.unscaledTime < _nextRowRefreshTime) return;

        RenderRows();
    }

    // 전환 버튼의 OnClick()에 연결한다.
    public void OnSwitchTargetButtonClick()
    {
        _spectator.SwitchTarget();
    }

    private void HandleDownedStateChanged(bool previousValue, bool newValue)
    {
        _canvas.enabled = newValue;

        if (newValue)
        {
            Render();
        }
    }

    private void HandleTargetChanged(Player target) => Render();

    // 문구는 Render가 그릴 때만 계산된다. 패널이 떠 있는 중에 언어를 바꿔도 반영되도록 다시 그린다.
    private void HandleLocaleChanged(Locale locale)
    {
        if (_canvas.enabled)
        {
            Render();
        }
    }

    private void Render()
    {
        // 내 시점일 때는 관전 대상 칸에 나를 보여준다. 이때 나는 항상 기절 상태다.
        bool spectating = _spectator.CurrentTarget != null;
        Player target = spectating ? _spectator.CurrentTarget : _localPlayer;
        bool downed = target.PlayerHealth.IsDowned;

        _targetNameText.text = target.PlayerName;
        _targetStatusText.text = (downed ? _targetDownedStatus : _targetAliveStatus).GetLocalizedString();
        _targetStatusText.color = downed ? _downedColor : _aliveColor;
        _bottomHintText.text = (spectating ? _spectatingHint : _ownViewHint).GetLocalizedString();

        RenderRows();
    }

    private void RenderRows()
    {
        _nextRowRefreshTime = Time.unscaledTime + RowRefreshInterval;

        _orderedPlayers.Clear();
        _orderedPlayers.AddRange(Player.ActiveInstances);
        _orderedPlayers.Sort((left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));

        // 행마다 조회하면 0.25초마다 인원수만큼 테이블을 훑게 된다. 한 번만 읽어 돌려 쓴다.
        string aliveStatus = _rowAliveStatus.GetLocalizedString();
        string downedStatus = _rowDownedStatus.GetLocalizedString();

        int index = 0;
        for (; index < _rows.Length && index < _orderedPlayers.Count; index++)
        {
            Player player = _orderedPlayers[index];
            bool downed = player.PlayerHealth.IsDowned;

            _rows[index].Show(
                player.PlayerName,
                downed ? downedStatus : aliveStatus,
                downed ? _downedColor : _aliveColor);
        }

        // 인원보다 행이 많으면 남는 행은 접어 둔다.
        for (; index < _rows.Length; index++)
        {
            _rows[index].Hide();
        }
    }
}
