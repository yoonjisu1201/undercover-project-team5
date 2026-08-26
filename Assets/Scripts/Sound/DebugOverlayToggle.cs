using UnityEngine;
using UnityEngine.SceneManagement;

// F8 로 켜고 끄는 개발용 표시들의 상태를 한곳에 둔다.
//
// 각 표시가 자기 bool 을 들고 있으면 오브젝트 수명이 달라서 어긋난다. 보스는 라운드마다 새로
// 스폰돼 프리팹 값(꺼짐)으로 돌아가지만, 플레이어 오브젝트는 접속이 유지되는 동안 살아 있어서
// 켜진 상태가 남는다. 그러면 로비를 거쳐 다시 들어왔을 때 F8 한 번에 둘이 반대로 켜진다.
//
// 표시 스크립트가 몇 개로 늘어나도 같은 문제가 생기지 않도록, 상태와 토글 규칙을 여기서만 다룬다.
public static class DebugOverlayToggle
{
    public static bool Shown { get; private set; }

    // 같은 프레임에 여러 표시가 같은 키 입력을 보고 각자 토글을 요청한다. 그대로 두면 두 번
    // 뒤집혀서 아무 일도 일어나지 않으므로, 한 프레임에 한 번만 받는다.
    private static int _toggledFrame = -1;

    public static void RequestToggle()
    {
        if (_toggledFrame == Time.frameCount) return;

        _toggledFrame = Time.frameCount;
        Shown = !Shown;
    }

    // 씬을 옮길 때마다 끈다. 로비로 나갔다 들어오면 처음부터 꺼진 상태여야 한다.
    // 정적 값은 도메인 리로드까지 남아서, 이걸 두지 않으면 플레이 세션을 넘어 켜진 채로 이어진다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        Shown = false;
        _toggledFrame = -1;
        SceneManager.activeSceneChanged -= HandleSceneChanged;
        SceneManager.activeSceneChanged += HandleSceneChanged;
    }

    private static void HandleSceneChanged(Scene from, Scene to)
    {
        Shown = false;
    }
}
