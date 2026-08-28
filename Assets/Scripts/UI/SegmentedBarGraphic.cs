using UnityEngine;
using UnityEngine.UI;

// 칸 단위로 끊어지는 게이지. Image의 fillAmount는 연속으로 잘려서 칸 모양이 안 나오므로
// 칸마다 사각형을 직접 만든다.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SegmentedBarGraphic : MaskableGraphic
{
    [SerializeField, Range(1, 40)] private int _segmentCount = 12;
    [SerializeField, Min(0f)] private float _segmentGap = 3f;
    // 빈 칸도 자리는 보여줘야 남은 양이 몇 칸인지 읽힌다. 선형 색공간에서는 낮은 알파도
    // 화면에서 꽤 밝게 나오므로 눈에 맞춰 아주 낮게 잡는다.
    [SerializeField] private Color _emptyColor = new(1f, 1f, 1f, 0.035f);
    [SerializeField, Range(0f, 1f)] private float _ratio = 1f;

    // 표시 비율. 칸 경계에서 툭툭 튀지 않게 걸쳐 있는 칸은 알파로 절반만 켠다.
    public float Ratio
    {
        get => _ratio;
        set
        {
            float clamped = Mathf.Clamp01(value);

            if (Mathf.Approximately(_ratio, clamped)) return;

            _ratio = clamped;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = rectTransform.rect;

        if (rect.width <= 0f || rect.height <= 0f) return;

        float totalGap = _segmentGap * (_segmentCount - 1);
        float segmentWidth = (rect.width - totalGap) / _segmentCount;

        if (segmentWidth <= 0f) return;

        for (int i = 0; i < _segmentCount; i++)
        {
            float filled = Mathf.Clamp01(_ratio * _segmentCount - i);
            Color segmentColor = Color.Lerp(_emptyColor, color, filled);

            float xMin = rect.xMin + i * (segmentWidth + _segmentGap);

            AppendQuad(vh, segmentColor, xMin, xMin + segmentWidth, rect.yMin, rect.yMax);
        }
    }

    private static void AppendQuad(VertexHelper vh, Color quadColor, float xMin, float xMax, float yMin, float yMax)
    {
        int start = vh.currentVertCount;

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = quadColor;

        vertex.position = new Vector3(xMin, yMin);
        vh.AddVert(vertex);
        vertex.position = new Vector3(xMin, yMax);
        vh.AddVert(vertex);
        vertex.position = new Vector3(xMax, yMax);
        vh.AddVert(vertex);
        vertex.position = new Vector3(xMax, yMin);
        vh.AddVert(vertex);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }
}
