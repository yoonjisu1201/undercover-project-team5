// 미니맵 스프라이트를 그린 방향이 월드 방향과 다를 때 쓰는 회전 보정값.
// 지상 구역 스프라이트와 지하 모듈 스프라이트가 같은 규칙을 쓴다.
public enum MinimapRotation {
	None = 0,
	CW90 = 90,
	Half = 180,
	CCW90 = 270
}
