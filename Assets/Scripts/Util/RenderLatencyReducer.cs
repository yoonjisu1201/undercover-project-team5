using UnityEngine;

// GPU에 미리 쌓아두는 프레임 수를 줄여 입력에서 화면까지의 지연을 낮춘다.
// 드래그 중인 UI 아이콘이 OS 커서보다 뒤처져 보이는 정도를 줄이기 위한 설정이다.
public static class RenderLatencyReducer
{
    // Unity 기본값은 2다. 1로 낮추면 지연이 한 프레임 줄고, 대신 GPU가 여유 있을 때의 처리량이 약간 떨어진다.
    private const int MaxQueuedFrames = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        QualitySettings.maxQueuedFrames = MaxQueuedFrames;
    }
}
