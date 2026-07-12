# 본부 관제실 레퍼런스 및 에셋 선정

이 문서는 이슈 #10에서 정한 본부 관제실의 시각 방향과 1주차 적용 범위를 기록한다.

## 시각 방향

- 공간 구조: 실제 관제실처럼 좌석이 공용 화면을 향하되, 통로와 역할 구역이 한눈에 읽히게 구성한다.
- 형태: 실사보다 단순하고 과장된 캐주얼 3D 형태를 사용한다.
- 색감: 저채도 실내를 기본으로 청록 UI와 노란 경고색을 포인트로 사용한다.
- 소품 밀도: 플레이에 필요한 관제 장비를 우선 배치하고 생활 소품은 이후에 보강한다.

## 레퍼런스

| 레퍼런스 | 참고할 요소 |
| --- | --- |
| [Evil Genius 2](https://store.steampowered.com/app/700600/Evil_Genius_2_World_Domination/) | 캐주얼한 비밀기지 형태, 콘솔과 모니터의 단순화 |
| [Killer Frequency](https://www.team17.com/games/killer-frequency) | 라디오·전화·스위치보드 등 전자 장비와 책상 주변 소품 |
| [NASA Apollo Mission Control](https://www.nasa.gov/gallery/apollo-mission-control-restoration/) | 좌석 배열, 공용 화면, 관제석과 통로의 관계 |
| [Operation: Tango](https://www.playstation.com/games/operation-tango/) | 청록 계열 첩보 UI와 본부·현장 정보 비대칭 표현 |

## 선정 에셋

### Office Pack - Free

- Unity Asset Store Product ID: `258600`
- 보관 위치: `Assets/Imported/OfficeEssentialsPack/`
- 1주차 사용: `Desk1`, `Desk2`, `OfficeChair`, `PC`, `PCCase`, `DeskLight`, `CeilingLight1`, `CeilingLight2`, `Projector`, `Shelves1`
- 추후 보강: 책 더미, 식물, 소파, 커피포트, 자판기 등 생활 소품
- 공식 안내: <https://nappin.dev>

### 4K Tiled Ground Textures (part 2)

- Unity Asset Store Product ID: `283704`
- 보관 위치: `Assets/Imported/4K Tiled Ground Textures p2/`
- 선택 파일: `concrete_1.png`, `concrete_7.png`
- 적용 위치: `Ground.mat`, `Wall.mat`
- 전체 텍스처 팩은 약 890MB이므로 선택한 두 텍스처만 버전 관리한다.

에셋의 사용 조건은 Unity Asset Store의 해당 상품 약관을 따른다. 외부 배포 전 프로젝트의 라이선스 및 좌석 조건을 다시 확인한다.

## 1주차 적용 범위

### 필수

- 바닥과 벽 머티리얼
- 관제용 책상과 의자
- PC와 모니터 역할의 장비
- 기본 천장 조명
- 공용 화면 역할의 프로젝터
- 최소한의 선반과 수납장

### 나중에 교체하거나 추가

- 전용 CCTV 모니터 월
- 라디오와 무전 장비
- 케이블 묶음과 바닥 배선
- 노란 경고등
- 컵, 서류, 책, 식물 등 생활 소품

## 제외한 후보

- Unity Japan Office: HDRP 프로젝트 설정과 빌드 씬 변경을 요구하여 현재 URP 프로젝트에는 직접 적용하지 않는다.
- Low Poly Epic City: 본부 전용 에셋이 아니므로 블록아웃 보조 용도로만 사용한다.
