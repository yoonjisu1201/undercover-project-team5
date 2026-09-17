using System;

// NPC 의상 생성 전용 난수열. 전역 UnityEngine.Random과 분리해 다른 게임 로직의 호출 순서가
// 바뀌어도 같은 입력에서는 항상 같은 의상 결과가 나오게 한다.
public sealed class NpcOutfitRandom {
	private const uint ZeroSeedFallback = 0x6D2B79F5;
	private uint _state;

	public NpcOutfitRandom(int seed) {
		_state = unchecked((uint)seed);
		if (_state == 0) {
			_state = ZeroSeedFallback;
		}
	}

	public int NextInt(int maxExclusive) {
		if (maxExclusive <= 0) {
			throw new ArgumentOutOfRangeException(nameof(maxExclusive));
		}

		return (int)(NextUInt() % (uint)maxExclusive);
	}

	public bool NextBool(float possibility) {
		if (possibility <= 0f) {
			return false;
		}

		if (possibility >= 1f) {
			return true;
		}

		return NextUInt() <= (uint)(possibility * uint.MaxValue);
	}

	private uint NextUInt() {
		_state ^= _state << 13;
		_state ^= _state >> 17;
		_state ^= _state << 5;
		return _state;
	}
}

// 모든 클라이언트가 이미 공유받은 라운드 시드와 Netcode가 공통으로 부여한 NetworkObjectId만
// 조합한다. NPC별 시드는 따로 네트워크로 전송하지 않는다.
public static class NpcOutfitSeed {
	public static int Create(int sharedSeed, ulong networkObjectId) {
		unchecked {
			uint mixed = (uint)sharedSeed;
			mixed ^= (uint)networkObjectId;
			mixed ^= (uint)(networkObjectId >> 32) * 0x9E3779B9u;
			mixed ^= 0x85EBCA6Bu;
			mixed ^= mixed >> 16;
			mixed *= 0x7FEB352Du;
			mixed ^= mixed >> 15;
			mixed *= 0x846CA68Bu;
			mixed ^= mixed >> 16;
			return (int)mixed;
		}
	}
}
