using UnityEngine;

// 지상/지하 안개를 로컬 카메라 위치로 정한다. 씬에 하나만 둔다.
//
// 문을 지날 때 토글하면 "내가 마지막으로 지난 문"이 기준이 되어, 관전으로 남의 시야를 빌리거나
// 순간이동으로 넘어갔을 때 화면과 안개가 어긋난다. 화면에 보이는 건 카메라가 있는 곳이므로
// 카메라를 기준으로 삼는다. 관전 중에는 카메라가 대상의 눈 위치에 가 있어 그대로 맞는다.
public sealed class UndergroundFogApplier : MonoBehaviour
{
    [Tooltip("지하 구역. BasementRegionSync가 라운드마다 생성된 지하 범위로 갱신한다.")]
    [SerializeField] private MapRegion _basementRegion;

    private bool _isUnderground;

    private void Start()
    {
        if (_basementRegion == null)
        {
            Debug.LogError("[UndergroundFogApplier] Basement MapRegion이 설정되지 않아 안개를 전환할 수 없습니다.", this);
            enabled = false;
            return;
        }

        // UndergroundFog는 처음 쓰이는 시점의 RenderSettings를 지상 값으로 캐싱한다.
        // 아무도 안개를 건드리기 전인 지금 잡아두게 한다.
        UndergroundFog.ApplySurface();
    }

    private void LateUpdate()
    {
        Camera camera = LocalCameraProvider.MainCamera;
        if (camera == null)
        {
            return;
        }

        bool underground = _basementRegion.Contains(camera.transform.position);
        if (underground == _isUnderground)
        {
            return;
        }

        _isUnderground = underground;

        if (underground)
        {
            UndergroundFog.ApplyUnderground();
        }
        else
        {
            UndergroundFog.ApplySurface();
        }
    }
}
