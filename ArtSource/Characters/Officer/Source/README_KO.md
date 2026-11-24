# NAI V5 파츠 분해 + 리깅 초안

입력: `마스킹 v2(1).xcf`
출력: 파츠 PNG, 마스크 PNG, 기본 본 계층/피벗, 간단한 Idle 프리뷰 GIF.

## 포함 내용
- `parts_canvas/`: 원본 좌표를 유지한 RGBA 파츠
- `parts_cropped/`: 잘라낸 파츠
- `masks/`: 사용자 마스킹 기반 마스크
- `previews/pivot_map.png`: 피벗/본 구조 표시
- `previews/rig_idle_preview.gif`: 간단한 2D 컷아웃 Idle 시범
- `rig/rig_spec.json`: 본/피벗/레이어 순서 메타데이터

## 주의
- 이 결과는 **실제 Unity/Spine 프로젝트 파일이 아니라 리깅 초안**이다.
- 넓은 팔 회전, 다리 보행, 머리카락 큰 흔들림에는 원본에 없는 가려진 면이 드러나므로 추가 보정이 필요하다.
- 현재 캐릭터 실루엣 기준 미분류 영역은 0.5%, 중복 영역은 1.31%다.
