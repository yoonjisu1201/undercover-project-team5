using System.Collections.Generic;
using UnityEngine;

// 지하 맵 조각(방/복도) 프리팹에 붙어, 절차적 생성기가 사용할 DoorSocket 목록을 들고 있다.
[RequireComponent(typeof(BoxCollider))]
public class UndergroundModule : MonoBehaviour
{
    [SerializeField] private DoorSocket[] _doorSockets;

    // 생성기가 이 조각을 놓아볼 때 다른 조각과 겹치는지 확인하는 용도의 트리거.
    [SerializeField] private BoxCollider _bounds;

    public IReadOnlyList<DoorSocket> DoorSockets => _doorSockets;
    public BoxCollider Bounds => _bounds;

    // 생성기가 시작 모듈로부터 몇 번째로 이어붙였는지 기록하는 런타임 상태. 다음 모듈을 고를 규칙(가중치)에 쓴다.
    public int Depth { get; set; }

    // 이 모듈이 어느 프리팹에서 Instantiate됐는지. 바로 다음 모듈이 같은 프리팹인지 확인할 때 쓴다.
    public UndergroundModule SourcePrefab { get; set; }

    // 에디터에서 수정할때마다 DoorSocket리스트 미리 받아두기.
    private void Reset()
    {
        _doorSockets = GetComponentsInChildren<DoorSocket>();
        _bounds = GetComponent<BoxCollider>();
        _bounds.isTrigger = true;
    }

    private void OnValidate()
    {
        _doorSockets = GetComponentsInChildren<DoorSocket>();

        if (_bounds == null)
        {
            _bounds = GetComponent<BoxCollider>();
        }
    }
}
