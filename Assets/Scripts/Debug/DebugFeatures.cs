using UnityEngine;

// 개발용 디버그를 한 번에 켜고 끄는 스위치.
//
// 빌드용으로 낼 때는 Enabled 를 false 로 두고, 개발할 때는 true 로 되돌린다.
// #if 로 막지 않는 이유는, 조건부 컴파일이면 한쪽 코드 경로가 평소에 컴파일되지 않아
// 빌드 때만 깨지는 일이 생기기 때문이다. 값만 바꾸면 어느 쪽이든 항상 같은 코드가 돈다.
//
// 끄면 사라지는 것:
//   - F8 개발용 표시(심장 박동, 보스 감지)
//   - F9 디버그 메뉴
//   - Debug.Log / LogWarning 콘솔 출력
public static class DebugFeatures
{
    // const 가 아니라 static readonly 로 둔다. const 면 false 일 때 뒤 코드가
    // "도달할 수 없음" 경고로 뒤덮인다.
    public static readonly bool Enabled = true;

    // Debug.Log 는 289 군데에서 부른다. 호출부를 하나씩 고치는 대신 로거를 한 번 막는다.
    //
    // 에러와 예외는 남긴다. 빌드에서 문제가 났을 때 로그 파일에 아무것도 없으면
    // 원인을 찾을 방법이 사라진다. 완전히 조용하게 하려면 logEnabled 를 false 로 둔다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ApplyLogFilter()
    {
        Debug.unityLogger.filterLogType = Enabled ? LogType.Log : LogType.Error;
    }
}
