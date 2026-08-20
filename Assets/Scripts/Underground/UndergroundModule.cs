using System.Collections.Generic;
using UnityEngine;

// 지하 맵 조각(방/복도) 프리팹에 붙어, 절차적 생성기가 사용할 DoorSocket 목록을 들고 있다.
[RequireComponent(typeof(BoxCollider))]
public class UndergroundModule : MonoBehaviour
{
    [SerializeField] private DoorSocket[] _doorSockets;

    // 생성기가 이 조각을 놓아볼 때 다른 조각과 겹치는지 확인하는 용도의 트리거.
    [SerializeField] private BoxCollider _bounds;

    [Header("미니맵 표시")]
    [Tooltip("이 조각을 미니맵에 그릴 그림. Bounds 박스 범위에 대응한다.")]
    [SerializeField] private Sprite _minimapSprite;

    [Tooltip("그림을 그린 방향이 조각의 로컬 방향과 다를 때 쓰는 보정값.")]
    [SerializeField] private MinimapRotation _minimapSpriteRotation = MinimapRotation.None;

    [Tooltip("그림 안에서 조각이 실제로 그려진 영역(0~1). 투명 여백이 있으면 그만큼 좁혀 준다.")]
    [SerializeField] private Rect _minimapSpriteContentRect = new Rect(0f, 0f, 1f, 1f);

    [Tooltip("그림이 덮는 로컬 XZ 크기. 0으로 두면 조각의 보이는 범위를 그대로 쓴다. 그림이 조각의 일부만 그렸을 때 직접 지정한다.")]
    [SerializeField] private Vector2 _minimapFootprintSize = Vector2.zero;

    [Tooltip("위 크기를 놓을 로컬 XZ 중심. 크기를 직접 지정했을 때만 쓰인다.")]
    [SerializeField] private Vector2 _minimapFootprintCenter = Vector2.zero;

    public IReadOnlyList<DoorSocket> DoorSockets => _doorSockets;
    public BoxCollider Bounds => _bounds;
    public Sprite MinimapSprite => _minimapSprite;
    public MinimapRotation MinimapSpriteRotation => _minimapSpriteRotation;
    public Rect MinimapSpriteContentRect => _minimapSpriteContentRect;

    // 직접 지정한 값이 있으면 그것을, 없으면 (0,0)을 돌려준다. 호출한 쪽에서 보이는 범위로 대체한다.
    public Vector2 MinimapFootprintSize => _minimapFootprintSize;
    public Vector2 MinimapFootprintCenter => _minimapFootprintCenter;

    // 생성기가 시작 모듈로부터 몇 번째로 이어붙였는지 기록하는 런타임 상태. 다음 모듈을 고를 규칙(가중치)에 쓴다.
    public int Depth { get; set; }

    // 이 모듈이 어느 프리팹에서 Instantiate됐는지. 바로 다음 모듈이 같은 프리팹인지 확인할 때 쓴다.
    public UndergroundModule SourcePrefab { get; set; }

    // 재사용되는 모듈(StartPoint 등)을 다시 생성하기 전에, 이전 라운드의 문 상태를 전부 초기화한다.
    public void ResetState()
    {
        foreach (DoorSocket socket in _doorSockets)
        {
            socket.ResetState();
        }
    }

    // 프리팹에 _bounds가 어떻게 저장돼있든, 실제로 겹침 판정 용도로만 쓰이도록 런타임에 강제한다.
    private void Awake()
    {
        _bounds = GetComponent<BoxCollider>();
        _bounds.isTrigger = true;
    }

    // 에디터에서 수정할때마다 DoorSocket리스트 미리 받아두기.
    private void Reset()
    {
        _doorSockets = GetComponentsInChildren<DoorSocket>();
        _bounds = GetComponent<BoxCollider>();
    }

    private void OnValidate()
    {
        _doorSockets = GetComponentsInChildren<DoorSocket>();

        if (_bounds == null)
        {
            _bounds = GetComponent<BoxCollider>();
        }

        // 실제 강제는 Awake가 하지만, 인스펙터에서 보다가 상태가 어긋나 보이지 않도록 여기서도 맞춰둔다.
        _bounds.isTrigger = true;
    }
}
