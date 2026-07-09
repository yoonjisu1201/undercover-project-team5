---
description: 사용자가 말한 내용을 .github 이슈 템플릿에 맞게 작성한다
argument-hint: <이슈 내용을 자유롭게 서술>
source: .claude/commands/issue.md
---

사용자가 말한 이슈 내용을 이 저장소의 `.github/ISSUE_TEMPLATE` 규격에 맞춰 작성한다.

## 사용자 입력

사용자가 이 명령 또는 요청과 함께 전달한 자유 서술을 사용한다.

## 절차

1. **타입 판별** - 입력 내용을 읽고 아래 7개 중 가장 알맞은 템플릿을 고른다. 애매하면 사용자에게 짧게 되묻는다.

   | 타입 | 언제 | 제목 prefix | labels |
   | --- | --- | --- | --- |
   | Feature | 새 기능·시스템·콘텐츠 추가 | `[FEAT]` | `feature`, `todo` |
   | Bug Report | 실행 중 오류·크래시·비정상 동작 | `[BUG]` | `bug`, `todo` |
   | Asset Request | 아트·사운드·애니메이션·UI 리소스 | `[ASSET]` | `asset`, `todo` |
   | Balance Tuning | 수치·밸런스·레벨 디자인 조정 | `[BALANCE]` | `balance`, `todo` |
   | Documentation | 문서 작성·수정 | `[DOCS]` | `documentation`, `todo` |
   | Refactor | 동작 유지, 구조·가독성 개선 | `[REFACTOR]` | `refactor`, `todo` |
   | Chore | 빌드·설정·정리 등 일반 작업 | `[CHORE]` | `chore` |

2. **필드 채우기** - 선택한 타입의 필드(아래 참조)를 사용자 입력에서 채운다.
   - 입력에 있는 정보는 구체적으로 채운다.
   - 정보가 없는 필드는 템플릿의 기본값(`없음`, `미정`, `확인 필요` 등)을 그대로 쓴다. 억지로 지어내지 않는다.
   - 제목은 `prefix + 핵심 요약(한글)` 형식으로 짧게 쓴다. 예: `[FEAT] 현장요원 변장 시스템 추가`
   - 체크리스트 항목은 입력 맥락에 맞게 구체화하되, 확실하지 않으면 템플릿 기본 체크리스트를 유지한다.

3. **초안 제시** - 완성한 제목 + 본문(마크다운)을 사용자에게 보여준다. 빠졌거나 확인이 필요한 필드가 있으면 한두 개만 짧게 질문한다.

4. **생성 여부 확인** - 사용자가 승인하면 `gh`로 이슈를 생성한다. 승인 전에는 절대 생성하지 않는다.

```powershell
gh issue create --repo yoonjisu1201/undercover-project-team5 `
  --title "<제목>" `
  --label "<labels>" `
  --body "<본문>"
```

본문이 길면 임시 파일에 쓰고 `--body-file`을 사용한다. 생성 후 이슈 URL을 알려준다.

## 템플릿 필드 참조

**Feature** `[FEAT]`
- 기능 요약 / 구현할 내용(체크리스트) / 화면·피드백 / 완료 기준(체크리스트) / 참고 자료

**Bug Report** `[BUG]`
- 버그 요약 / 재현 절차(번호 목록) / 실제 결과·기대 결과 / 실행 환경(기본 `Unity / Windows`) / 발생 빈도(항상·자주·가끔·한 번) / 첨부 자료

**Asset Request** `[ASSET]`
- 에셋 종류(2D 아트·3D 모델·애니메이션·SFX/BGM·UI·기타) / 요청 내용 / 스타일·규격 / 적용 위치 / 완료 기준(체크리스트) / 참고 자료

**Balance Tuning** `[BALANCE]`
- 조정 대상 / 현재 문제 / 변경안(현재·변경·이유) / 확인 방법(체크리스트) / 영향 범위 / 참고 자료

**Documentation** `[DOCS]`
- 문서 종류(기본 `README`) / 작업 목적 / 포함할 내용(체크리스트) / 참고 자료

**Refactor** `[REFACTOR]`
- 리팩터링 대상 / 개선 이유 / 개선 방향 / 확인 방법(체크리스트)

**Chore** `[CHORE]`
- 작업 종류(빌드·배포·패키지·설정·정리·기타) / 작업 목적 / 작업 내용(체크리스트) / 영향 범위

