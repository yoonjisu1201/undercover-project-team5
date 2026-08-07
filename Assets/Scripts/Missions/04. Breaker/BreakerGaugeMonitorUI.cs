using DG.Tweening;
using TMPro;
using UnityEngine;

// C 역할이 HQ에서 보는 배터리 회로 게이지 전용 화면. 배터리 슬롯이나 확인 버튼 없이 보기만 한다.
// 목표 전력은 수치로 보여주지 않는다. 대신 목표 전력이 곧 게이지의 최대값이라, 바늘이 끝까지 차면 정답이다.
public sealed class BreakerGaugeMonitorUI : MonoBehaviour
{
    // 바늘은 스프라이트가 위를 향하므로 Z 회전 +90이 왼쪽 끝(0W), -90이 오른쪽 끝(목표값)이다.
    private const float SweepStartAngle = 90f;
    private const float FullSweepDegrees = 180f;
    // 목표를 조금이라도 초과하면 초과량과 무관하게 이 각도로 고정한다.
    private const float OverflowSweepDegrees = 190f;
    // 연출 시간은 A 화면과 같아야 하므로 공유 상태(BreakerCircuitState)의 값을 그대로 쓴다.
    private const float SweepDuration = BreakerCircuitState.MeasurementSweepSeconds;
    private const float ResultDelaySeconds = BreakerCircuitState.ResultDelaySeconds;

    private const string UnderTargetStatus = "전력 복구 필요";
    private const string OverTargetStatus = "전력 초과";
    private const string MatchedStatus = "전력 충족";

    [SerializeField] private RectTransform _needle;
    [SerializeField] private TMP_Text _progressText;
    [SerializeField] private TMP_Text _statusText;
    // 바늘과 함께 올라가는 측정 전력 숫자다.
    [SerializeField] private TMP_Text _wattText;

    private BreakerCircuitState _circuitState;
    private bool _completionShown;
    // 측정 연출 중에는 회로 값 변화로 바늘과 문구를 즉시 갱신하지 않는다. 연출이 끝난 뒤 한 번에 반영한다.
    private bool _isMeasuring;
    // 확인을 눌러 측정 연출을 한 번 끝냈는지. 완료 상태를 화면에 유지할지 판단하는 기준이다.
    private bool _hasMeasured;
    // 지금 화면에 표시 중인 전력. 0으로 되돌릴 때 여기서부터 내려간다.
    private int _displayedWatt;
    private Tween _measureTween;
    private Tween _resultTween;

    private void Awake()
    {
        // 브레이커 미션은 라운드당 하나만 존재하므로, 다른 오브젝트에 있는 공유 회로 상태를 찾아서 구독한다.
        _circuitState = FindFirstObjectByType<BreakerCircuitState>();
        _progressText ??= FindText("ProgressText") ?? FindText("ResultProgressText");

        if (_circuitState != null)
        {
            _circuitState.OnCircuitChanged += HandleCircuitChanged;
            _circuitState.OnMeasurementRequested += PlayMeasurementSweep;
        }

        HandleCircuitChanged();
    }

    private void OnDestroy()
    {
        if (_circuitState != null)
        {
            _circuitState.OnCircuitChanged -= HandleCircuitChanged;
            _circuitState.OnMeasurementRequested -= PlayMeasurementSweep;
        }

        _measureTween?.Kill();
        _resultTween?.Kill();
    }

    // 바늘과 숫자는 A가 확인을 누를 때만 움직인다. 전원이 켜졌다는 이유로 먼저 최종값을 보여주면 연출이 사라진다.
    // 전원이 꺼지면(배치를 다시 바꾸는 중) 0으로 되돌려, 다음 확인에서 0부터 다시 올라가게 한다.
    private void HandleCircuitChanged()
    {
        if (_circuitState == null || _isMeasuring)
        {
            ApplyProgressText();
            return;
        }

        ApplyProgressText();

        // 완료를 확정하는 시점은 레버가 올라간 순간이지만, 화면은 확인을 눌러 측정 연출을 끝낸 뒤에만 결과를 유지한다.
        // 그래서 측정 전에는 완료 여부와 무관하게 0을 유지한다.
        if (_circuitState.IsCompleted && _hasMeasured)
        {
            return;
        }

        if (_circuitState.PowerOn)
        {
            return;
        }

        PlayResetSweep();
    }

    // 레버를 내려 수정 모드로 돌아갈 때, 바늘과 숫자를 지금 값에서 0까지 되돌리는 연출이다.
    private void PlayResetSweep()
    {
        int targetWatt = _circuitState.TargetWatt;
        int fromWatt = _displayedWatt;
        float fromSweep = CalculateSweep(fromWatt, targetWatt);

        _measureTween?.Kill();
        _resultTween?.Kill();

        if (fromWatt <= 0)
        {
            _hasMeasured = false;
            ApplyMeasuredValues(0, targetWatt);
            return;
        }

        // 되돌리는 동안에도 다른 갱신이 끼어들지 않게 측정 중으로 잠근다.
        _isMeasuring = true;

        _measureTween = DOVirtual.Float(1f, 0f, SweepDuration, progress =>
            {
                ApplyNeedleSweep(fromSweep * progress);
                ApplyWattText(Mathf.RoundToInt(fromWatt * progress));
            })
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                _isMeasuring = false;
                _hasMeasured = false;
                ApplyMeasuredValues(0, targetWatt);
            });
    }

    // 바늘·숫자·상태 문구를 한 번에 같은 값으로 맞춘다.
    private void ApplyMeasuredValues(int currentWatt, int targetWatt)
    {
        ApplyNeedleSweep(CalculateSweep(currentWatt, targetWatt));
        ApplyWattText(currentWatt);
        ApplyStatusText(currentWatt, targetWatt);
    }

    // A가 확인을 누른 순간, 바늘과 숫자를 0에서 측정값까지 올린다.
    // 트윈 하나로 진행률만 굴리고 바늘 각도와 숫자를 같은 값에서 계산해, 목표를 초과했을 때(바늘은 190도 고정)도 둘이 어긋나지 않는다.
    private void PlayMeasurementSweep()
    {
        if (_circuitState == null)
        {
            return;
        }

        int targetWatt = _circuitState.TargetWatt;
        // 완료된 뒤에는 레버를 내려 측정값이 0이 되어도 목표값까지 올린다.
        int currentWatt = _circuitState.IsCompleted ? targetWatt : _circuitState.CurrentWatt;
        float endSweep = CalculateSweep(currentWatt, targetWatt);

        _measureTween?.Kill();
        _resultTween?.Kill();
        _isMeasuring = true;

        // 항상 0에서 시작한다. 첫 트윈 갱신을 기다리지 않고 바로 0으로 맞춰 둔다.
        ApplyNeedleSweep(0f);
        ApplyWattText(0);

        _measureTween = DOVirtual.Float(0f, 1f, SweepDuration, progress =>
            {
                ApplyNeedleSweep(endSweep * progress);
                ApplyWattText(Mathf.RoundToInt(currentWatt * progress));
            })
            .SetEase(Ease.OutCubic)
            .OnComplete(() => FinishMeasurement(currentWatt, targetWatt));
    }

    // 바늘이 멈춘 시점에 최종값과 상태 문구를 확정하고, 목표와 정확히 일치했을 때만 잠시 뒤 결과 창을 띄운다.
    private void FinishMeasurement(int currentWatt, int targetWatt)
    {
        _isMeasuring = false;
        _hasMeasured = true;
        ApplyMeasuredValues(currentWatt, targetWatt);

        if (_completionShown || targetWatt <= 0 || currentWatt != targetWatt)
        {
            return;
        }

        _completionShown = true;
        ApplyProgressText();
        _resultTween = DOVirtual.DelayedCall(
            ResultDelaySeconds,
            () => GetComponent<MissionUIController>()?.ShowCompletedState());
    }

    private void ApplyNeedleSweep(float sweep)
    {
        if (_needle != null)
        {
            _needle.localRotation = Quaternion.Euler(0f, 0f, SweepStartAngle - sweep);
        }
    }

    private void ApplyWattText(int watt)
    {
        _displayedWatt = watt;

        if (_wattText != null)
        {
            _wattText.text = $"[전력] : {watt:00}W";
        }
    }

    // 목표 전력을 게이지의 최대값으로 삼아 0~180도로 환산하고, 목표를 넘으면 190도로 고정한다.
    private static float CalculateSweep(int currentWatt, int targetWatt)
    {
        if (targetWatt <= 0)
        {
            // A가 아직 패널을 열지 않아 목표가 정해지기 전에는 0W 위치에 둔다.
            return 0f;
        }

        return currentWatt <= targetWatt
            ? (float)currentWatt / targetWatt * FullSweepDegrees
            : OverflowSweepDegrees;
    }

    // 목표 대비 현재 전력 상태를 문구로 알린다. 목표가 정해지기 전에는 아직 부족한 상태로 본다.
    private void ApplyStatusText(int currentWatt, int targetWatt)
    {
        if (_statusText == null)
        {
            return;
        }

        if (targetWatt <= 0 || currentWatt < targetWatt)
        {
            _statusText.text = $"[시스템] : {UnderTargetStatus}";
        }
        else if (currentWatt > targetWatt)
        {
            _statusText.text = $"[시스템] : {OverTargetStatus}";
        }
        else
        {
            _statusText.text = $"[시스템] : {MatchedStatus}";
        }
    }

    private void ApplyProgressText()
    {
        if (_progressText != null)
        {
            bool completed = _circuitState != null && _circuitState.IsCompleted;
            _progressText.text = completed ? "진행도  1 / 1" : "진행도  0 / 1";
        }
    }

    private TMP_Text FindText(string childName)
    {
        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == childName)
            {
                return text;
            }
        }

        return null;
    }
}
