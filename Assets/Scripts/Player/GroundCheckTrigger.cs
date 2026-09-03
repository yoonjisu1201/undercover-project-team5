using System.Collections.Generic;
using UnityEngine;

// 발밑 접지 판정. 겹침 계산(Physics.CheckBox) 대신 트리거 콜라이더로 받는다.
//
// 계산으로 하면 판정 범위가 코드 안의 숫자로만 존재해서, 캡슐 바닥과 바닥면 사이 어디에
// 걸쳐 있는지 눈으로 확인할 수 없다. 콜라이더로 두면 씬에서 그대로 보이고 인스펙터에서
// 크기와 위치를 잡을 수 있다.
//
// Enter/Exit 로 목록을 들고 있는다. Stay 로 매 스텝 확인하면 리지드바디가 잠든 순간
// 이벤트가 끊겨서, 가만히 서 있을 때 접지가 풀린다.
[RequireComponent(typeof(BoxCollider))]
public class GroundCheckTrigger : MonoBehaviour
{
	[Tooltip("이 레이어에 닿았을 때만 땅으로 친다")]
	[SerializeField] private LayerMask _jumpableSurfaceMask;

	// 착지 예측용. 판정 기준이 갈리지 않도록 같은 마스크를 쓴다.
	public LayerMask JumpableSurfaceMask => _jumpableSurfaceMask;

	// 닿아 있는 동안 유지되는 목록. 콜라이더가 파괴되거나 꺼지면 Exit 가 오지 않을 수 있어
	// 읽을 때마다 죽은 항목을 걷어낸다.
	private readonly List<Collider> _touching = new();

	public bool IsGrounded
	{
		get
		{
			PruneDeadEntries();
			return _touching.Count > 0;
		}
	}

	private void Awake()
	{
		// 트리거가 아니면 발밑 상자가 몸을 밀어 올린다.
		GetComponent<BoxCollider>().isTrigger = true;
	}

	private void OnDisable()
	{
		_touching.Clear();
	}

	private void OnTriggerEnter(Collider other)
	{
		if (!IsJumpableSurface(other) || _touching.Contains(other))
		{
			return;
		}

		_touching.Add(other);
	}

	private void OnTriggerExit(Collider other)
	{
		_touching.Remove(other);
	}

	private bool IsJumpableSurface(Collider other)
	{
		// 트리거는 발판이 아니다. 맵 구역 판정용 볼륨처럼 공중까지 뻗은 트리거가 Ground
		// 레이어로 놓여 있으면, 떨어지는 내내 접지로 잡혀 공중에서 계속 점프하게 된다.
		// 원래 쓰던 Physics.CheckSphere 도 QueryTriggerInteraction.Ignore 로 같은 것을 걸렀다.
		if (other.isTrigger)
		{
			return false;
		}

		return (_jumpableSurfaceMask.value & (1 << other.gameObject.layer)) != 0;
	}

	private void PruneDeadEntries()
	{
		for (int i = _touching.Count - 1; i >= 0; i--)
		{
			Collider entry = _touching[i];
			if (entry == null || !entry.enabled || !entry.gameObject.activeInHierarchy)
			{
				_touching.RemoveAt(i);
			}
		}
	}
}
