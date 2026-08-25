using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

// 로딩 패널이 떠 있는 동안 안내 문구를 일정 간격으로 바꿔준다. 스폰이 오래 걸려도 화면이 멈춘 것처럼
// 보이지 않게 하는 것이 목적이다. 
public class LoadingMessageRotator : MonoBehaviour
{
    [SerializeField, Min(0.1f)]
    private float _changeInterval = 0.5f;

    // UI에 순환 할 문구배열
    //
    // 이 컴포넌트가 매 주기 텍스트를 직접 덮어쓰므로, 같은 오브젝트에 LocalizeStringEvent 를
    // 붙여도 그 값이 곧바로 지워진다. 그래서 순환 문구 자체를 현지화 대상으로 들고 있는다.
    [SerializeField]
    private LocalizedString[] _messages;

    private TMP_Text _messageText;

    private CancellationTokenSource _rotationCts;

    private void Awake()
    {
        _messageText = GetComponent<TMP_Text>();
    }

    // 로딩 패널은 SetActive로 켜고 끄기 때문에, 순환 시작/정지 시점을 OnEnable/OnDisable에 맞춘다.
    private void OnEnable()
    {
        // 이전 순환 루프 먼저 정리
        StopRotation();

        _rotationCts = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy()
        );

        RotateMessagesAsync(_rotationCts.Token).Forget();
    }

    private void OnDisable()
    {
        StopRotation();
    }

    private void StopRotation()
    {
        _rotationCts?.Cancel();
        _rotationCts?.Dispose();
        _rotationCts = null;
    }

    // 문구를 순서대로 반복한다.
    // index를 로컬 변수로 둬서, 패널이 다시 켜질 때마다 항상 첫 문구부터 시작한다.
    private async UniTaskVoid RotateMessagesAsync(CancellationToken token)
    {
        if (_messages == null || _messages.Length == 0)
        {
            return;
        }

        int index = 0;

        while (true)
        {
            _messageText.text = _messages[index].GetLocalizedString();
            index = (index + 1) % _messages.Length;

            // ignoreTimeScale: 로딩 중 timeScale이 0으로 내려가도 문구는 계속 바뀌어야 한다.(방어코드)
            // SuppressCancellationThrow: 취소를 예외로 던지지 않고 bool로 받아, 패널이 꺼질 때 루프만 조용히 끝낸다.
            bool cancelled = await UniTask
                .Delay(
                    TimeSpan.FromSeconds(_changeInterval),
                    ignoreTimeScale: true,
                    cancellationToken: token
                )
                .SuppressCancellationThrow();

            if (cancelled)
            {
                return;
            }
        }
    }
}
