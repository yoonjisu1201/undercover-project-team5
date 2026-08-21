using UnityEngine;

// 지하 맵이 절차적으로 (재)생성될 때마다, 그 결과 범위(GetGeneratedBounds)에 맞춰
// ① SkyBox를 가리는 천장 차단막, ② 지하 전용 Local Volume의 영향 범위, ③ 유리가 비출
// Reflection Probe를 함께 갱신한다. 지하 맵은 라운드마다 크기/형태가 달라지므로 고정값 대신
// 매번 이 범위로 다시 맞춰야 한다.
[RequireComponent(typeof(UndergroundRandomMapGenerator))]
public sealed class UndergroundAmbienceSync : MonoBehaviour
{
    [Header("SkyBox 차단막 (Bounds 위를 덮는 불투명 판)")]
    [SerializeField] private Transform _skyBlocker;
    [SerializeField, Min(0.1f)] private float _skyBlockerThickness = 4f;
    [SerializeField, Min(0f)] private float _skyBlockerMargin = 5f; // 모듈 배치가 고르지 않아도 확실히 덮도록 여유를 둔다

    [Header("지하 전용 Local Volume (조도 하향)")]
    [SerializeField] private BoxCollider _volumeBounds;

    [Header("유리가 비출 Reflection Probe (Realtime)")]
    [SerializeField] private ReflectionProbe _reflectionProbe;

    private UndergroundRandomMapGenerator _generator;

    private void Awake()
    {
        _generator = GetComponent<UndergroundRandomMapGenerator>();
    }

    private void OnEnable()
    {
        _generator.OnGenerated += HandleOnGenerated;
    }

    private void OnDisable()
    {
        _generator.OnGenerated -= HandleOnGenerated;
    }

    private void HandleOnGenerated()
    {
        Bounds bounds = _generator.GetGeneratedBounds();

        UpdateSkyBlocker(bounds);
        UpdateVolumeBounds(bounds);
        UpdateReflectionProbe(bounds);
    }

    private void UpdateSkyBlocker(Bounds bounds)
    {
        if (_skyBlocker == null) return;

        _skyBlocker.position = new Vector3(bounds.center.x, bounds.max.y + _skyBlockerThickness * 0.5f, bounds.center.z);
        _skyBlocker.localScale = new Vector3(
            bounds.size.x + _skyBlockerMargin * 2f,
            _skyBlockerThickness,
            bounds.size.z + _skyBlockerMargin * 2f);
    }

    private void UpdateVolumeBounds(Bounds bounds)
    {
        if (_volumeBounds == null) return;

        BoxColliderUtility.ApplyWorldBounds(_volumeBounds, bounds);
    }

    private void UpdateReflectionProbe(Bounds bounds)
    {
        if (_reflectionProbe == null) return;

        _reflectionProbe.transform.position = bounds.center;
        _reflectionProbe.size = bounds.size;
        _reflectionProbe.RenderProbe();
    }
}
