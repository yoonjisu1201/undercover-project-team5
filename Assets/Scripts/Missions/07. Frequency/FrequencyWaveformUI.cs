using TMPro;
using UnityEngine;
using UnityEngine.UI;

// P1 역할의 신호 파형 화면. 안테나 방향을 좌우로 돌려 신호가 가장 센 방향을 찾아내고, 그 각도를 P2에게 알려주는 역할이다.
// 주파수는 이 화면에서 만질 수 없다. 방향을 찾아 P2를 보내는 것까지가 P1의 일이다.
// FM 사운드도 신호를 듣는 이 화면에서 낸다. 미동기 상태에서는 FM_Idle, P3가 다이얼을 돌리는 동안 FM_Tunning, 맞으면 FM_Correct.
public sealed class FrequencyWaveformUI : MonoBehaviour
{
    // 좌우 버튼을 한 번 누를 때 안테나가 돌아가는 각도다.
    private const int BearingStepDegrees = 2;
    // 파형 갱신 간격이다. 매 프레임 갱신하면 눈이 아파서 일정 간격으로만 흔든다.
    private const float WaveRefreshSeconds = 0.05f;
    // 주파수가 마지막으로 바뀐 뒤 이 시간이 지나면 튜닝 사운드를 끝낸다.
    private const float TuningSoundHoldSeconds = 0.35f;

    [Header("파형")]
    // 왼쪽부터 순서대로 배치된 세로 바들이다. 각 바의 높이로 파형을 표현한다.
    [SerializeField] private RectTransform[] _waveBars;

    [Header("동기화 상태")]
    [SerializeField] private TMP_Text _syncStateText;
    [SerializeField] private TMP_Text _syncHintText;

    [Header("안테나 방향")]
    [SerializeField] private TMP_Text _bearingText;
    [SerializeField] private Button _rotateLeftButton;
    [SerializeField] private Button _rotateRightButton;

    [Header("레이더")]
    // 안테나를 들고 있는 요원의 표식이다. 목표 방향 기준 상대 각도만큼 돌아간다.
    [SerializeField] private RectTransform _playerMark;
    // 목표 지점 표식. 항상 북쪽(위)을 가리키므로 회전시키지 않는다. 소지자가 없을 때 숨기려고 참조만 들고 있다.
    [SerializeField] private RectTransform _targetMark;
    // 존까지 남은 거리를 알려준다. P1이 이 값을 보고 요원을 유도한다.
    [SerializeField] private TMP_Text _distanceText;

    [Header("사운드")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _idleClip;
    [SerializeField] private AudioClip _tuningClip;
    [SerializeField] private AudioClip _correctClip;

    private FrequencySyncState _syncState;
    private float _nextWaveRefreshTime;
    // P3가 다이얼을 돌리는 중인지 판단하려고, 공유 주파수가 마지막으로 바뀐 시점을 기억한다.
    private float _lastFrequencyChangeTime = -1f;
    private float _lastKnownFrequency;
    private bool _correctPlayed;

    private void Awake()
    {
        _syncState = FindFirstObjectByType<FrequencySyncState>();
        if (_syncState != null)
        {
            _syncState.OnStateChanged += Redraw;
            _lastKnownFrequency = _syncState.CurrentFrequency;
        }

        _rotateLeftButton?.onClick.AddListener(() => Rotate(-BearingStepDegrees));
        _rotateRightButton?.onClick.AddListener(() => Rotate(BearingStepDegrees));

        Redraw();
    }

    private void OnDestroy()
    {
        if (_syncState != null)
        {
            _syncState.OnStateChanged -= Redraw;
        }
    }

    private void Rotate(int degrees)
    {
        if (_syncState != null && !_syncState.AntennaPlaced)
        {
            _syncState.SubmitAntennaBearing(_syncState.AntennaBearing + degrees);
        }
    }

    private void Update()
    {
        UpdateSound();

        if (Time.time < _nextWaveRefreshTime)
        {
            return;
        }

        _nextWaveRefreshTime = Time.time + WaveRefreshSeconds;
        RefreshWaveBars();
    }

    // 상태에 맞는 루프 사운드를 유지하고, 완료 순간에만 성공 사운드를 한 번 낸다.
    private void UpdateSound()
    {
        if (_audioSource == null || _syncState == null)
        {
            return;
        }

        // 다이얼은 P3가 돌리므로, 이 화면에서는 공유 주파수가 움직이는 것으로 튜닝 중임을 판단한다.
        if (!Mathf.Approximately(_syncState.CurrentFrequency, _lastKnownFrequency))
        {
            _lastKnownFrequency = _syncState.CurrentFrequency;
            _lastFrequencyChangeTime = Time.time;
        }

        if (_syncState.IsCompleted)
        {
            if (!_correctPlayed)
            {
                _correctPlayed = true;
                _audioSource.loop = false;
                _audioSource.clip = _correctClip;
                _audioSource.Play();
            }

            return;
        }

        bool tuning = Time.time - _lastFrequencyChangeTime <= TuningSoundHoldSeconds;
        AudioClip desired = tuning ? _tuningClip : _idleClip;
        if (desired == null || _audioSource.clip == desired)
        {
            return;
        }

        _audioSource.clip = desired;
        _audioSource.loop = true;
        _audioSource.Play();
    }

    // 신호가 셀수록 규칙적인 사인파에 가까워지고, 약할수록 잡음으로 뭉개진다.
    private void RefreshWaveBars()
    {
        if (_waveBars == null || _waveBars.Length == 0)
        {
            return;
        }

        float signal = _syncState != null ? _syncState.SignalStrength01 : 0f;

        for (int index = 0; index < _waveBars.Length; index++)
        {
            RectTransform bar = _waveBars[index];
            if (bar == null)
            {
                continue;
            }

            float phase = Time.time * 6f + index * 0.6f;
            float wave = (Mathf.Sin(phase) + 1f) * 0.5f;
            float noise = UnityEngine.Random.value;
            // 신호가 셀 때는 사인파 비중을, 약할 때는 잡음 비중을 높인다.
            float height = Mathf.Lerp(noise, wave, signal);

            bar.anchorMin = new Vector2(bar.anchorMin.x, 0.5f - height * 0.5f);
            bar.anchorMax = new Vector2(bar.anchorMax.x, 0.5f + height * 0.5f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(bar.sizeDelta.x, 0f);
        }
    }

    private void Redraw()
    {
        float signal = _syncState != null ? _syncState.SignalStrength01 : 0f;

        if (_bearingText != null && _syncState != null)
        {
            _bearingText.text = $"안테나 방향: {_syncState.AntennaBearing}°";
        }

        ApplyRadarMarks();

        // 안테나가 자리 잡으면 방향 탐색이 끝나 더 돌릴 수 없다.
        bool placed = _syncState != null && _syncState.AntennaPlaced;
        SetButtonUsable(_rotateLeftButton, !placed);
        SetButtonUsable(_rotateRightButton, !placed);

        if (_syncStateText == null || _syncHintText == null)
        {
            return;
        }

        if (_syncState == null)
        {
            _syncStateText.text = "신호 없음";
            _syncHintText.text = "장비를 확인하세요";
            return;
        }

        if (_syncState.IsCompleted)
        {
            _syncStateText.text = "동기화 완료";
            _syncHintText.text = "통신이 연결되었습니다";
        }
        else if (placed)
        {
            _syncStateText.text = "안테나 설치 완료";
            _syncHintText.text = "주파수 조정을 기다립니다";
        }
        else if (_syncState.BearingNearMatched)
        {
            _syncStateText.text = "거의 일치함";
            _syncHintText.text = "약간의 조정이 필요합니다";
        }
        else if (signal > 0f)
        {
            _syncStateText.text = "신호 감지";
            _syncHintText.text = "방향을 더 좁히세요";
        }
        else
        {
            _syncStateText.text = "신호 없음";
            _syncHintText.text = "안테나 방향을 돌려 신호를 찾으세요";
        }
    }

    // 목표는 항상 북쪽(위)에 고정하고, 안테나를 든 요원 표식만 상대 각도만큼 돌린다.
    // 두 표식이 겹치면 요원이 목표 방향 선상에 있다는 뜻이라 그대로 앞으로 걸으면 된다.
    private void ApplyRadarMarks()
    {
        bool hasHolder = _syncState != null && _syncState.HasHolder;

        if (_playerMark != null)
        {
            _playerMark.gameObject.SetActive(hasHolder);
            if (hasHolder)
            {
                // 레이더는 시계 방향이 +방위각이므로 부호를 뒤집어 Z 회전에 넣는다.
                _playerMark.localRotation = Quaternion.Euler(0f, 0f, -_syncState.HolderRelativeBearing);
            }
        }

        if (_targetMark != null)
        {
            // 목표는 회전시키지 않는다. 프리팹에서 위를 향하도록 둔 상태를 그대로 유지한다.
            _targetMark.localRotation = Quaternion.identity;
        }

        if (_distanceText == null)
        {
            return;
        }

        if (!hasHolder)
        {
            _distanceText.text = "안테나 미소지";
        }
        else if (_syncState.AntennaPlaced)
        {
            _distanceText.text = "목표 지점 도착";
        }
        else
        {
            _distanceText.text = $"남은 거리 {_syncState.HolderDistance:0.0}m";
        }
    }

    private static void SetButtonUsable(Button button, bool usable)
    {
        if (button != null)
        {
            button.interactable = usable;
        }
    }
}
