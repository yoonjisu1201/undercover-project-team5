using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 개인 HUD의 HP를 심장 박동 파형으로 그리는 그래픽. 스프라이트로는 심박수를 값에 따라 바꿀 수 없어서
// 선분 메시를 직접 만든다.
//
// 실제 심전도 모니터처럼 훑는 속도(_sweepSeconds)는 고정하고, 박동을 찍는 간격만 심박수로 바꾼다.
// 화면에 들어가는 박동 수를 직접 조절하면 심박수가 바뀔 때 이미 지나가던 파형까지 같이 늘어나고
// 줄어들어서, 박동이 자주 오는 게 아니라 화면이 확대·축소되는 것처럼 보인다.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EcgTraceGraphic : MaskableGraphic
{
    // 박동 한 번의 모양. (박동 안에서의 진행 0~1, 기준선에서의 높이) 형태로 적어둔다.
    // 균등 샘플링 대신 이 꼭짓점을 그대로 이어야 R 스파이크가 샘플 수에 따라 뭉개지지 않는다.
    private static readonly Vector2[] BeatShape =
    {
        new(0f, 0f),
        new(0.05f, 0f),
        new(0.14f, 0.12f),   // P
        new(0.22f, 0f),
        new(0.34f, 0f),
        new(0.38f, -0.14f),  // Q
        new(0.44f, 1f),      // R
        new(0.50f, -0.32f),  // S
        new(0.56f, 0f),
        new(0.68f, 0f),
        new(0.80f, 0.24f),   // T
        new(0.94f, 0f),
        new(1f, 0f),
    };

    // 시간을 되돌리는 주기. 긴 세션에서 float 정밀도가 떨어지지 않게 이만큼마다 원점을 옮긴다.
    private const float TimeRebaseInterval = 3600f;

    [Header("파형")]
    // 가로 폭이 담는 시간. 이 값이 훑는 속도이고, 심박수와 무관하게 고정이다.
    [SerializeField, Range(0.5f, 6f)] private float _sweepSeconds = 1.7f;
    // 박동 한 번이 그려지는 시간. 심박수가 올라가도 파형 하나의 폭은 그대로고 간격만 좁아진다.
    [SerializeField, Range(0.1f, 0.6f)] private float _beatDuration = 0.28f;
    [SerializeField, Range(20f, 260f)] private float _beatsPerMinute = 70f;
    // 파형 높이. rect 높이의 절반을 1로 본 비율이다.
    [SerializeField, Range(0.1f, 1f)] private float _amplitude = 0.85f;
    [SerializeField, Min(0.5f)] private float _lineThickness = 2.5f;
    [SerializeField] private bool _isFlatline;

    [Header("박동마다 흔들림")]
    // 모양이 매번 똑같으면 파형이 그림처럼 밋밋해진다. 실제 심전도처럼 박동마다 조금씩 달라야
    // 살아 움직이는 것으로 읽힌다. 0 으로 두면 모두 같은 모양이 된다.
    [SerializeField, Range(0f, 0.4f)] private float _amplitudeJitter = 0.12f;
    // 박동 간격의 흔들림(심박 변이도). 자를 대고 찍은 듯한 규칙성을 깨뜨린다.
    [SerializeField, Range(0f, 0.3f)] private float _timingJitter = 0.07f;
    // R 스파이크를 뺀 작은 파(P·Q·S·T)의 높이 흔들림. 스파이크보다 크게 흔들려도 어색하지 않다.
    [SerializeField, Range(0f, 0.6f)] private float _waveJitter = 0.3f;

    // 이미 찍힌 박동. 찍힐 때의 진폭을 함께 들고 있어야, 진폭이 커지는 순간 화면에 남아 있던
    // 파형까지 한꺼번에 커지지 않고 새로 오는 박동부터 커진다.
    private struct Beat
    {
        public float Time;
        public float Amplitude;
        public float WaveScale;
    }

    private readonly List<Beat> _beats = new();
    private readonly List<Vector2> _points = new();
    private float _elapsed;
    private float _nextBeatTime;

    // 심박수. HP가 낮거나 심장 소리가 들리는 동안 올려서 박동이 더 자주 찍히게 한다.
    // 이미 찍힌 박동의 위치는 건드리지 않으므로, 빨라지면 촘촘한 파형이 오른쪽부터 밀려 들어온다.
    public float BeatsPerMinute
    {
        get => _beatsPerMinute;
        set => _beatsPerMinute = Mathf.Max(0f, value);
    }

    // 파형 높이. 다음에 찍히는 박동부터 적용된다.
    public float Amplitude
    {
        get => _amplitude;
        set => _amplitude = Mathf.Clamp(value, 0.1f, 1f);
    }

    // 선 굵기. 이건 선 전체의 성질이라 바꾸는 즉시 반영한다.
    public float LineThickness
    {
        get => _lineThickness;
        set
        {
            float clamped = Mathf.Max(0.5f, value);

            if (Mathf.Approximately(_lineThickness, clamped)) return;

            _lineThickness = clamped;
            SetVerticesDirty();
        }
    }

    // 다운 상태에서 파형을 평평하게 만든다. 게이지가 "비었다"를 심박 파형으로 표현하는 방법이다.
    public bool IsFlatline
    {
        get => _isFlatline;
        set
        {
            if (_isFlatline == value) return;

            _isFlatline = value;

            // 평평해질 때 남아 있던 박동을 버리고, 돌아올 때는 지금부터 다시 센다. 그냥 두면
            // 다시 뛰기 시작하는 순간 멈춰 있던 동안의 박동이 한꺼번에 밀려든다.
            _beats.Clear();
            _nextBeatTime = _elapsed;

            SetVerticesDirty();
        }
    }

    private void Update()
    {
        if (_isFlatline) return;

        // unscaledTime 기준이라 일시정지 중에도 계속 뛴다.
        _elapsed += Time.unscaledDeltaTime;

        AdvanceBeats();
        RebaseTimeIfNeeded();

        SetVerticesDirty();
    }

    // 지나간 시간만큼 박동을 찍는다. 간격은 찍는 시점의 심박수로 정하므로, 이미 찍힌 박동은
    // 나중에 심박수가 바뀌어도 그 자리에 그대로 남는다.
    private void AdvanceBeats()
    {
        // 박동 하나가 그려지는 시간보다 간격이 짧아지면 파형이 서로 겹쳐 뭉개진다.
        float period = Mathf.Max(60f / Mathf.Max(_beatsPerMinute, 1f), _beatDuration);

        // 도메인 리로드처럼 프레임이 크게 튄 뒤에는 밀린 박동을 몰아서 찍지 않고 건너뛴다.
        if (_elapsed - _nextBeatTime > _sweepSeconds)
        {
            _nextBeatTime = _elapsed;
        }

        while (_nextBeatTime <= _elapsed)
        {
            _beats.Add(new Beat
            {
                Time = _nextBeatTime,
                // 설정한 진폭을 넘기면 스파이크가 rect 밖으로 나가므로 위로는 넘지 않게 자른다.
                Amplitude = Mathf.Clamp(_amplitude * (1f + Random.Range(-_amplitudeJitter, _amplitudeJitter)), 0.1f, 1f),
                WaveScale = 1f + Random.Range(-_waveJitter, _waveJitter),
            });

            // 간격이 흔들려도 파형 하나가 그려지는 시간보다는 길어야 서로 겹치지 않는다.
            _nextBeatTime += Mathf.Max(period * (1f + Random.Range(-_timingJitter, _timingJitter)), _beatDuration);
        }

        // 왼쪽으로 완전히 빠져나간 박동은 버린다.
        float windowStart = _elapsed - _sweepSeconds;
        int expired = 0;

        while (expired < _beats.Count && _beats[expired].Time + _beatDuration < windowStart)
        {
            expired++;
        }

        if (expired > 0)
        {
            _beats.RemoveRange(0, expired);
        }
    }

    private void RebaseTimeIfNeeded()
    {
        if (_elapsed < TimeRebaseInterval) return;

        _elapsed -= TimeRebaseInterval;
        _nextBeatTime -= TimeRebaseInterval;

        for (int i = 0; i < _beats.Count; i++)
        {
            Beat beat = _beats[i];
            beat.Time -= TimeRebaseInterval;
            _beats[i] = beat;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = rectTransform.rect;

        if (rect.width <= 0f || rect.height <= 0f) return;

        BuildPoints(rect);
        AppendLine(vh, color);
    }

    // 보이는 시간 구간을 좌표로 바꿔 담는다. 구간 밖 꼭짓점까지 만든 뒤 가로로 잘라내야
    // 화면 경계에 걸친 스파이크가 반쪽만 솟은 모양으로 남지 않는다.
    private void BuildPoints(Rect rect)
    {
        _points.Clear();

        float baseline = rect.center.y;
        float halfHeight = rect.height * 0.5f;

        if (_isFlatline)
        {
            _points.Add(new Vector2(rect.xMin, baseline));
            _points.Add(new Vector2(rect.xMax, baseline));
            return;
        }

        float windowStart = _elapsed - _sweepSeconds;
        float scale = rect.width / _sweepSeconds;

        for (int i = 0; i < _beats.Count; i++)
        {
            Beat beat = _beats[i];

            if (beat.Time > _elapsed) break;

            for (int s = 0; s < BeatShape.Length; s++)
            {
                float time = beat.Time + BeatShape[s].x * _beatDuration;

                // R 스파이크(높이 1)는 진폭 흔들림만 타고, 작은 파는 거기에 한 번 더 흔들린다.
                float shapeY = BeatShape[s].y;
                float height = Mathf.Abs(shapeY) < 0.5f ? beat.Amplitude * beat.WaveScale : beat.Amplitude;
                float y = baseline + shapeY * halfHeight * height;

                _points.Add(new Vector2(rect.xMin + (time - windowStart) * scale, y));
            }
        }

        // 양 끝에 남는 구간은 기준선으로 이어준다. 박동 모양의 첫·끝 높이가 0 이라 그냥 이으면 된다.
        if (_points.Count == 0 || _points[0].x > rect.xMin)
        {
            _points.Insert(0, new Vector2(rect.xMin, baseline));
        }

        if (_points[_points.Count - 1].x < rect.xMax)
        {
            _points.Add(new Vector2(rect.xMax, baseline));
        }

        ClipToWidth(rect);
    }

    // 가로 범위를 벗어난 구간을 경계에서 보간해 잘라낸다.
    private void ClipToWidth(Rect rect)
    {
        for (int i = _points.Count - 1; i >= 0; i--)
        {
            Vector2 point = _points[i];

            if (point.x >= rect.xMin && point.x <= rect.xMax) continue;

            float edge = point.x < rect.xMin ? rect.xMin : rect.xMax;
            Vector2? crossing = null;

            // 창 안쪽 이웃이 있으면 경계 위의 교점으로 대체하고, 없으면 버린다.
            for (int step = -1; step <= 1; step += 2)
            {
                int neighbor = i + step;

                if (neighbor < 0 || neighbor >= _points.Count) continue;
                if (_points[neighbor].x < rect.xMin || _points[neighbor].x > rect.xMax) continue;

                float span = point.x - _points[neighbor].x;

                if (Mathf.Approximately(span, 0f)) continue;

                float t = (edge - _points[neighbor].x) / span;
                crossing = Vector2.Lerp(_points[neighbor], point, t);
                break;
            }

            if (crossing.HasValue)
            {
                _points[i] = crossing.Value;
            }
            else
            {
                _points.RemoveAt(i);
            }
        }
    }

    // 꼭짓점 목록을 굵기가 있는 선으로 만든다. 꺾이는 곳에는 정사각형을 하나 더 얹어서
    // 이음새가 벌어져 보이지 않게 한다.
    private void AppendLine(VertexHelper vh, Color lineColor)
    {
        float half = _lineThickness * 0.5f;

        for (int i = 0; i < _points.Count - 1; i++)
        {
            Vector2 from = _points[i];
            Vector2 to = _points[i + 1];
            Vector2 direction = to - from;

            if (direction.sqrMagnitude <= Mathf.Epsilon) continue;

            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * half;

            AppendQuad(vh, lineColor,
                from - normal, from + normal, to + normal, to - normal);
        }

        for (int i = 1; i < _points.Count - 1; i++)
        {
            Vector2 point = _points[i];

            AppendQuad(vh, lineColor,
                point + new Vector2(-half, -half), point + new Vector2(-half, half),
                point + new Vector2(half, half), point + new Vector2(half, -half));
        }
    }

    private static void AppendQuad(VertexHelper vh, Color quadColor, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        int start = vh.currentVertCount;

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = quadColor;

        vertex.position = a;
        vh.AddVert(vertex);
        vertex.position = b;
        vh.AddVert(vertex);
        vertex.position = c;
        vh.AddVert(vertex);
        vertex.position = d;
        vh.AddVert(vertex);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }
}
