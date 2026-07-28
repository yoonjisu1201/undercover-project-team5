using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 검거 추격 단계의 게이지 바와 "왜 게이지가 안 오르는지" 안내 문구를 보여주는 UI.
// 패널 표시/게이지 값은 ArrestChaseManager의 이벤트를 구독해서 갱신하고,
// 안내 문구만 추격 중(Chasing)일 때 매 프레임 로컬 플레이어의 위치/인벤토리를 확인해서 갱신한다.
// (범위 안/밖, 도구 장착 여부는 "계속 변하는 값"이라 이벤트로는 알 수 없어 폴링이 불가피하다)
public class ArrestChaseUI : MonoBehaviour
{
    [SerializeField] private GameObject _chasePanel;  // 추격 중(+완료 순간)에만 보여줄 패널
    [SerializeField] private Slider _gaugeSlider;      // 검거 게이지 바
    [SerializeField] private TMP_Text _gaugeText;      // "검거 진행 68%" 같은 퍼센트 텍스트

    [SerializeField] private Image[] _participantIcons;                        // 참여 인원 아이콘 2개
    [SerializeField] private Color _participantActiveColor = Color.yellow;     // 단축키 홀드 중인 인원 수만큼 앞에서부터 이 색으로 바뀐다. 기본은 흰색.
    [SerializeField] private TMP_Text _participantCountText;                   // "0/2" 같은 홀드 인원 수 텍스트

    [SerializeField] private TMP_Text _distanceText;   // 로컬 플레이어와 타겟 NPC 사이 거리 ("12m" 등)

    [SerializeField] private GameObject _promptPanel;  // "도구가 필요합니다" 등 안내 문구 패널
    [SerializeField] private TMP_Text _promptText;

    // _promptPanel(경고문구) 의 "범위 안에 있는지" 판정에만 쓰는 여유 반경. 실제 검거 반경(5m)보다 크게 잡아서
    // 5m 경계를 막 통과하는 순간 서버의 HoldingCount가 아직 갱신 전이라 문구가 잘못 표시되는 걸 막는다.
    [SerializeField] private float _promptVisibilityRadius = 10f;

    [SerializeField] private float _promptShowDelay = 0.25f; // 서버 HoldingCount 반영 지연으로 인한 순간적인 오탐을 무시하는 유예시간

    private string _pendingPromptMessage;
    private float _pendingPromptSince;

    [SerializeField] private RectTransform _targetingFrame;      // UI_TargetReticle. _chasePanel의 자식이라 표시/숨김은 부모가 처리, 여기서는 위치만 갱신한다.
    [SerializeField] private float _targetingHeightOffset = 1f;  // NPC 발밑 대신 몸통 높이에 맞추기 위한 오프셋

    [SerializeField] private float _targetingReferenceDistance = 5f; // 이 거리일 때 프레임이 원본 크기(1배)로 보이도록 기준을 잡는다.
    [SerializeField] private float _targetingMinScale = 0.5f;
    [SerializeField] private float _targetingMaxScale = 2f;

    private void Start()
    {
        _chasePanel.SetActive(false);
        _promptPanel.SetActive(false);

        ArrestChaseManager.Instance.OnStateChanged += HandleStateChanged;
        ArrestChaseManager.Instance.OnGaugeChanged += HandleGaugeChanged;

        // 늦참 클라이언트가 추격 도중 스폰되는 경우, OnValueChanged는 최초 동기화 값에는 발동하지 않으므로
        // 직접 한 번 호출해서 현재 상태를 반영해줘야 한다. (RoundManager.OnRoundStateChanged와 동일한 이유)
        HandleStateChanged(ArrestChaseManager.Instance.CurrentState);
        HandleGaugeChanged(ArrestChaseManager.Instance.Gauge);
    }

    private void OnDestroy()
    {
        if (ArrestChaseManager.Instance != null)
        {
            ArrestChaseManager.Instance.OnStateChanged -= HandleStateChanged;
            ArrestChaseManager.Instance.OnGaugeChanged -= HandleGaugeChanged;
        }
    }

    private void Update()
    {
        // 안내 문구는 "지금 이 순간" 로컬 플레이어의 위치/장착 상태에 달려있어서 이벤트로 알 수 없다.
        // 추격 중일 때만 계산하면 되므로 그 외에는 아무것도 하지 않는다.
        if (ArrestChaseManager.Instance == null || ArrestChaseManager.Instance.CurrentState != ArrestChaseState.Chasing)
        {
            return;
        }

        UpdateChasePanelVisibility();
        UpdatePrompt();
        UpdateTargetingFrame();
        UpdateParticipantIcons();
    }

    private void HandleStateChanged(ArrestChaseState state)
    {
        // 표시 여부는 Update()의 UpdateChasePanelVisibility()가 반경 진입 여부로 매 프레임 판단한다.
        // 여기서는 추격이 끝나거나 리셋될 때(Idle) 패널을 확실히 꺼주기만 한다.
        if (state != ArrestChaseState.Chasing && state != ArrestChaseState.Completed)
        {
            _chasePanel.SetActive(false);
        }

        // 추격이 끝나거나 리셋되면 안내 문구도 같이 지운다.
        if (state != ArrestChaseState.Chasing)
        {
            _promptPanel.SetActive(false);
        }
    }

    // 로컬 플레이어가 대상 NPC의 검거 반경 안에 들어왔을 때만 ChasePanel(게이지 바 + 타겟팅 프레임)을 보여준다.
    // (거리를 여기서 이미 계산하므로, 같은 값을 거리 텍스트 갱신에도 그대로 쓴다)
    private void UpdateChasePanelVisibility()
    {
        NetworkObject target = ArrestChaseManager.Instance.Target;
        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;

        if (target == null || localPlayer == null)
        {
            _chasePanel.SetActive(false);
            return;
        }

        float distance = Vector3.Distance(localPlayer.transform.position, target.transform.position);
        _chasePanel.SetActive(distance <= ArrestChaseManager.Instance.CaptureRadius);
        _distanceText.text = $"{Mathf.RoundToInt(distance)}m";
    }

    // 단축키(AlienGun)를 홀드 중인 인원 수만큼 앞에서부터 아이콘을 노란색으로, 나머지는 흰색으로 표시한다.
    private void UpdateParticipantIcons()
    {
        int holdingCount = ArrestChaseManager.Instance.HoldingCount;
        for (int i = 0; i < _participantIcons.Length; i++)
        {
            _participantIcons[i].color = i < holdingCount ? _participantActiveColor : Color.white;
        }
        _participantCountText.text = $"{holdingCount}/{ArrestChaseManager.RequiredParticipants}";
    }

    // 대상 NPC의 월드 좌표를 화면 좌표로 변환해 타겟팅 프레임(UI_TargetReticle) 위치를 갱신한다.
    private void UpdateTargetingFrame()
    {
        NetworkObject target = ArrestChaseManager.Instance.Target;
        if (target == null)
        {
            Debug.LogWarning("[ArrestChaseUI] 추격 대상 NPC가 없어 타겟팅 프레임을 갱신하지 못했습니다.");
            return;
        }

        Vector3 worldPos = target.transform.position + Vector3.up * _targetingHeightOffset;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

        // 대상이 카메라 뒤에 있으면(z<0) WorldToScreenPoint가 반대편 좌표를 반환하므로 프레임을 숨긴다.
        bool isBehindCamera = screenPos.z < 0f;
        _targetingFrame.gameObject.SetActive(!isBehindCamera);
        if (isBehindCamera) return;

        _targetingFrame.position = screenPos;

        // 카메라와의 거리가 가까울수록 크게, 멀수록 작게 — NPC의 화면상 겉보기 크기 변화를 따라간다.
        float distance = Vector3.Distance(Camera.main.transform.position, target.transform.position);
        float scale = Mathf.Clamp(_targetingReferenceDistance / distance, _targetingMinScale, _targetingMaxScale);
        _targetingFrame.localScale = Vector3.one * scale;
    }

    private void HandleGaugeChanged(float gauge)
    {
        _gaugeSlider.value = gauge;
        _gaugeText.text = $"{Mathf.RoundToInt(gauge * 100f)}%";
    }

    // "지금 내가(로컬 플레이어) 왜 게이지에 기여하지 못하고 있는지"를 판단해서 안내 문구를 띄운다.
    // 서버 계산과 별개로, 로컬 플레이어 본인의 위치/인벤토리는 클라이언트가 이미 알고 있으므로 여기서 직접 계산한다.
    private void UpdatePrompt()
    {
        NetworkObject target = ArrestChaseManager.Instance.Target;
        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;

        if (target == null || localPlayer == null)
        {
            Debug.LogWarning("[ArrestChaseUI] 추격 대상 NPC 또는 로컬 플레이어를 찾을 수 없어 안내 문구를 갱신하지 못했습니다.");
            _promptPanel.SetActive(false);
            return;
        }

        float distance = Vector3.Distance(localPlayer.transform.position, target.transform.position);
        bool isInRange = distance <= _promptVisibilityRadius;

        // 범위 밖이면 아직 참여할 상황이 아니므로 안내할 게 없다.
        if (!isInRange)
        {
            _promptPanel.SetActive(false);
            _pendingPromptMessage = null;
            return;
        }

        // R키를 누르고 있지 않으면 아직 시도한 게 아니므로 안내할 게 없다.
        bool isHoldingArrestKey = localPlayer.TryGetComponent(out PlayerArrestInput arrestInput)
            && arrestInput.IsHoldingArrestKey;

        if (!isHoldingArrestKey)
        {
            _promptPanel.SetActive(false);
            _pendingPromptMessage = null;
            return;
        }

        bool hasToolEquipped = localPlayer.TryGetComponent(out PlayerInventory inventory)
            && inventory.TryGetSelectedItem(out string itemId)
            && itemId == ArrestChaseManager.CaptureToolItemId;

        string desiredMessage = !hasToolEquipped
            ? "검거 도구가 필요합니다"
            : ArrestChaseManager.Instance.HoldingCount < ArrestChaseManager.RequiredParticipants
                ? "다른 요원 1명이 필요합니다"
                : null;

        if (desiredMessage == null)
        {
            _promptPanel.SetActive(false); // 조건을 다 채웠으면 안내 없이 게이지 바만 보여준다.
            _pendingPromptMessage = null;
            return;
        }

        // 서버의 HoldingCount가 아직 반영되지 않아 생기는 순간적인 오탐을 걸러내기 위해,
        // 같은 메시지가 _promptShowDelay 이상 지속될 때만 실제로 띄운다.
        if (desiredMessage != _pendingPromptMessage)
        {
            _pendingPromptMessage = desiredMessage;
            _pendingPromptSince = Time.time;
        }

        if (Time.time - _pendingPromptSince >= _promptShowDelay)
        {
            ShowPrompt(desiredMessage);
        }
        else
        {
            _promptPanel.SetActive(false);
        }
    }

    private void ShowPrompt(string text)
    {
        _promptPanel.SetActive(true);
        _promptText.text = text;
    }
}
