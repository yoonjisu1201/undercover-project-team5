using System.Collections.Generic;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

// 지금 말하는 사람과 마이크를 끈 사람을 화면 좌측 중앙에 띄우는 음성 오버레이.
// 디스코드 음성 오버레이와 같은 방식이다. 말하기는 잠깐이지만 마이크 꺼짐은 상태라,
// 마이크를 끈 동안은 말하지 않아도 계속 보여 준다.
//
// 목록 순서는 각 클라이언트가 로컬에서 관찰한 말하기 전환 순서로 정한다. 순서를 동기화하지 않는다.
// IsSpeaking은 이미 동기화된 값이고, 순서를 서버에서 관리하면 서버 왕복 때문에
// 오버레이가 실제로 들리는 소리보다 늦게 뜬다. 음성은 Vivox로 직접 오기 때문이다.
public sealed class VoiceOverlayUI : MonoBehaviour
{
    [Header("=== 행을 담을 부모 ===")]
    [SerializeField] private RectTransform _rowParent;

    [Header("=== 행 프리팹 ===")]
    [SerializeField] private VoiceOverlayRowUI _rowPrefab;

    [Header("=== 전체를 페이드할 CanvasGroup ===")]
    [SerializeField] private CanvasGroup _canvasGroup;

    [Header("=== 말을 멈춘 뒤 행을 남겨 두는 시간 ===")]
    [Tooltip("짧게 말했을 때 깜빡이지 않도록 잠시 유지한다.")]
    [SerializeField, Min(0f)] private float _holdSeconds = 0.7f;

    [Header("=== 페이드 연출 ===")]
    [SerializeField, Min(0f)] private float _fadeDuration = 0.15f;
    [SerializeField] private Ease _fadeEase = Ease.OutQuad;

    // 말하기 시작한 순서대로 쌓인다. 앞이 먼저 말한 사람이다.
    private readonly List<Player> _speakingOrder = new();

    // 실제로 화면에 올릴 목록. 말하는 사람 다음에 마이크를 끈 사람을 붙인다.
    private readonly List<Player> _visiblePlayers = new();

    // 말을 멈춘 뒤 유지 시간이 끝나는 시각. 유지 중에 다시 말하면 이 값만 지운다.
    private readonly Dictionary<Player, float> _removeAtTime = new();

    private readonly List<VoiceOverlayRowUI> _rows = new();
    private readonly List<Player> _mutedPlayers = new();
    private readonly List<Player> _subscribedPlayers = new();
    private readonly List<Player> _expiredPlayers = new();

    private Tween _fadeTween;
    private bool _isVisible;

    private void Start()
    {
        // PlayScene은 라운드 시작 전 전원 스폰이 보장되므로 시작 시 한 번만 구독한다.
        foreach (Player player in Player.ActiveInstances)
        {
            Subscribe(player);
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnConnectionEvent += HandleConnectionEvent;
        }

        // 처음에는 아무것도 보이지 않는다.
        ApplyVisible(false, animate: false);
    }

    private void OnDestroy()
    {
        foreach (Player player in _subscribedPlayers)
        {
            if (player != null)
            {
                player.SpeakingChanged -= HandleSpeakingChanged;
            }
        }
        _subscribedPlayers.Clear();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnConnectionEvent -= HandleConnectionEvent;
        }

        _fadeTween?.Kill();
    }

    private void Subscribe(Player player)
    {
        if (player == null || _subscribedPlayers.Contains(player))
        {
            return;
        }

        player.SpeakingChanged += HandleSpeakingChanged;
        _subscribedPlayers.Add(player);

        // 구독 전에 이미 말하고 있었다면 놓치지 않도록 현재 값을 한 번 반영한다.
        if (player.IsSpeaking)
        {
            AddSpeaker(player);
        }
    }

    // 어느 플레이어가 보낸 이벤트인지 알 수 없는 시그니처라, 값이 바뀐 사람을 다시 찾아 반영한다.
    // 말하기 전환은 0.1초 주기로만 일어나므로 이 비용은 문제되지 않는다.
    private void HandleSpeakingChanged(bool isSpeaking)
    {
        SyncSpeakers();
    }

    private void SyncSpeakers()
    {
        foreach (Player player in _subscribedPlayers)
        {
            if (player == null)
            {
                continue;
            }

            if (player.IsSpeaking)
            {
                AddSpeaker(player);
            }
            else if (_speakingOrder.Contains(player) && !_removeAtTime.ContainsKey(player))
            {
                // 바로 지우지 않고 유지 시간을 준다. 짧게 말할 때 깜빡이는 것을 막는다.
                _removeAtTime[player] = Time.unscaledTime + _holdSeconds;
            }
        }

        Render();
    }

    private void AddSpeaker(Player player)
    {
        // 유지 시간 중에 다시 말하면 자리를 지킨다. 다시 맨 아래로 내려가지 않게 한다.
        _removeAtTime.Remove(player);

        if (!_speakingOrder.Contains(player))
        {
            _speakingOrder.Add(player);
        }
    }

    private void Update()
    {
        bool changed = false;

        _expiredPlayers.Clear();
        foreach (KeyValuePair<Player, float> pair in _removeAtTime)
        {
            if (pair.Key == null || Time.unscaledTime >= pair.Value)
            {
                _expiredPlayers.Add(pair.Key);
            }
        }

        foreach (Player player in _expiredPlayers)
        {
            _removeAtTime.Remove(player);
            _speakingOrder.Remove(player);
            changed = true;
        }

        // 퇴장 등으로 파괴된 플레이어를 목록에서 뺀다.
        for (int i = _speakingOrder.Count - 1; i >= 0; i--)
        {
            if (_speakingOrder[i] == null)
            {
                _speakingOrder.RemoveAt(i);
                changed = true;
            }
        }

        // 마이크 꺼짐은 변경 이벤트가 없어서 목록이 달라졌는지 매 프레임 확인한다.
        if (changed || RebuildVisibleList())
        {
            Render();
            return;
        }

        // 이름·색·다운·마이크 상태는 표시 중에도 바뀔 수 있어 매 프레임 갱신한다.
        foreach (VoiceOverlayRowUI row in _rows)
        {
            if (row.Player != null)
            {
                row.Refresh();
            }
        }
    }

    // 같은 퇴장이라도 호스트에는 ClientDisconnected로, 남은 참가자에게는 PeerDisconnected로 들어온다.
    private void HandleConnectionEvent(NetworkManager networkManager, ConnectionEventData data)
    {
        if (data.EventType == ConnectionEvent.ClientConnected ||
            data.EventType == ConnectionEvent.PeerConnected)
        {
            // 늦게 들어온 플레이어도 오버레이에 나오도록 구독한다.
            foreach (Player player in Player.ActiveInstances)
            {
                Subscribe(player);
            }
            return;
        }

        if (data.EventType != ConnectionEvent.ClientDisconnected &&
            data.EventType != ConnectionEvent.PeerDisconnected)
        {
            return;
        }

        RemovePlayer(data.ClientId);
    }

    private void RemovePlayer(ulong clientId)
    {
        int index = _subscribedPlayers.FindIndex(candidate => candidate != null && candidate.OwnerClientId == clientId);
        if (index < 0)
        {
            return;
        }

        Player player = _subscribedPlayers[index];
        if (player != null)
        {
            player.SpeakingChanged -= HandleSpeakingChanged;
        }

        _subscribedPlayers.RemoveAt(index);
        _speakingOrder.Remove(player);
        _removeAtTime.Remove(player);

        Render();
    }

    // 화면에 올릴 목록을 다시 만든다. 바뀌었으면 true.
    // 말하는 사람이 먼저(말한 순서), 그다음 마이크만 끈 사람이 접속 순서로 붙는다.
    // 이렇게 해야 마이크 끈 사람이 들락날락해도 말하는 사람의 순서가 흔들리지 않는다.
    private bool RebuildVisibleList()
    {
        int previousCount = _visiblePlayers.Count;
        bool changed = false;
        int index = 0;

        foreach (Player player in _speakingOrder)
        {
            changed |= ReplaceAt(index++, player);
        }

        _mutedPlayers.Clear();
        foreach (Player player in _subscribedPlayers)
        {
            if (player != null && player.IsMicMuted && !_speakingOrder.Contains(player))
            {
                _mutedPlayers.Add(player);
            }
        }

        // 접속 순서로 고정해, 목록이 프레임마다 뒤바뀌지 않게 한다.
        _mutedPlayers.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        foreach (Player player in _mutedPlayers)
        {
            changed |= ReplaceAt(index++, player);
        }

        if (_visiblePlayers.Count > index)
        {
            _visiblePlayers.RemoveRange(index, _visiblePlayers.Count - index);
        }

        return changed || previousCount != _visiblePlayers.Count;
    }

    private bool ReplaceAt(int index, Player player)
    {
        if (index < _visiblePlayers.Count)
        {
            if (_visiblePlayers[index] == player)
            {
                return false;
            }

            _visiblePlayers[index] = player;
            return true;
        }

        _visiblePlayers.Add(player);
        return true;
    }

    // 표시할 사람 수만큼 행을 만들어 순서대로 채운다.
    private void Render()
    {
        RebuildVisibleList();

        while (_rows.Count < _visiblePlayers.Count)
        {
            _rows.Add(Instantiate(_rowPrefab, _rowParent));
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            if (i < _visiblePlayers.Count)
            {
                _rows[i].Bind(_visiblePlayers[i]);
            }
            else
            {
                _rows[i].Clear();
            }
        }

        ApplyVisible(_visiblePlayers.Count > 0, animate: true);
    }

    private void ApplyVisible(bool visible, bool animate)
    {
        if (_canvasGroup == null || (_isVisible == visible && animate))
        {
            return;
        }

        _isVisible = visible;
        float target = visible ? 1f : 0f;

        _fadeTween?.Kill();

        if (!animate || _fadeDuration <= 0f)
        {
            _canvasGroup.alpha = target;
            return;
        }

        _fadeTween = _canvasGroup.DOFade(target, _fadeDuration)
            .SetEase(_fadeEase)
            .SetUpdate(true);
    }
}
