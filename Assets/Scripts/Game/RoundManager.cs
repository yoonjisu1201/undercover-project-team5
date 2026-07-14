using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum RoundState
{
    Waiting, // 대기 (게임 시작 전)
    Round1,  // 1라운드 진행 중
    Round2,  // 2라운드 진행 중
    Fail,    // 게임 실패 (시간 초과)
    Success  // 게임 성공 (2라운드 검거 성공)
}

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance { get; private set; }

    [Header("라운드 제한 시간 (초 단위, 테스트용 10분)")]
    [SerializeField] private float _round1Duration = 600f;
    [SerializeField] private float _round2Duration = 600f;

    [Header("게임 종료 후 돌아갈 대기방 씬")]
    [SerializeField] private string _waitingRoomSceneName = "WaitingRoom";

    private readonly NetworkVariable<RoundState> _currentState =
        new(RoundState.Waiting, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> _roundEndTime =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public RoundState CurrentState => _currentState.Value;

    public event Action<RoundState> OnRoundStateChanged; // 라운드 상태가 바뀔 때마다 전달 (늦참 클라이언트는 스폰 시 현재 상태로 1회 발동)

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        _currentState.OnValueChanged += HandleStateChanged;
        OnRoundStateChanged?.Invoke(_currentState.Value); // OnValueChanged는 최초 동기화값에는 발동하지 않으므로 직접 1회 호출

        if (IsServer)
        {
            StartRound1(); // 게임씬에 스폰되는 것 자체가 게임 시작 신호
        }
    }

    public override void OnNetworkDespawn()
    {
        _currentState.OnValueChanged -= HandleStateChanged;
    }

    //최신 값으로 동기화
    private void HandleStateChanged(RoundState previous, RoundState current)
    {
        OnRoundStateChanged?.Invoke(current);
    }

    private void Update()
    {
        //임시 검거 테스트용: 실제 검거 판정 시스템 생기면 제거
        if (IsSpawned && Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            ReportArrestServerRpc();
        }

        if (!IsSpawned || !IsServer) return;
        if (_currentState.Value != RoundState.Round1 && _currentState.Value != RoundState.Round2) return;

        if (NetworkManager.ServerTime.Time >= _roundEndTime.Value)
        {
            _currentState.Value = RoundState.Fail; // 시간 초과로 실패 처리
            ReturnToWaitingRoom();
        }
    }

    // 게임씬 스폰 시 서버에서 자동 호출한다.
    public void StartRound1()
    {
        if (!IsServer) return;
        if (_currentState.Value != RoundState.Waiting) return;

        _roundEndTime.Value = NetworkManager.ServerTime.Time + _round1Duration;
        _currentState.Value = RoundState.Round1;
    }

    // 검거했다고 서버에서 알려주는 rpc
    // 검거 판정 로직(또는 테스트용 입력)에서 호출한다. Round1 성공 시 Round2로, Round2 성공 시 Success로 전환한다.
    [Rpc(SendTo.Server)]
    public void ReportArrestServerRpc()
    {
        switch (_currentState.Value)
        {
            case RoundState.Round1:
                _roundEndTime.Value = NetworkManager.ServerTime.Time + _round2Duration;
                _currentState.Value = RoundState.Round2;
                break;
            case RoundState.Round2:
                _currentState.Value = RoundState.Success;
                ReturnToWaitingRoom();
                break;
        }
    }

    // 게임 종료(성공/실패) 시 서버가 대기방 씬으로 전환한다.
    private void ReturnToWaitingRoom()
    {
        NetworkManager.SceneManager.LoadScene(_waitingRoomSceneName, LoadSceneMode.Single);
    }

    // 클라이언트 UI(시계 등)가 매 프레임 호출해서 남은 시간을 계산한다.
    public float GetRemainingTime()
    {
        if (!IsSpawned) return 0f;
        if (_currentState.Value != RoundState.Round1 && _currentState.Value != RoundState.Round2) return 0f;

        return Mathf.Max(0f, (float)(_roundEndTime.Value - NetworkManager.ServerTime.Time));
    }
}
