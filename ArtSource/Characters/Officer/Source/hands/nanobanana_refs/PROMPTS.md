# 나노바나나 손 생성 프롬프트

## 첨부 파일
이 폴더의 파일을 함께 넣어 주세요.

| 파일 | 용도 |
|---|---|
| `ref_01_character.png` | 캐릭터 원본 (그림체·장갑 기준) |
| `ref_02_glove_closeup.png` | 원본 장갑 확대 (색, 선 굵기, 셀 채색 기준) |
| `ref_03_sabre.png` | 세이버 (A, B에서 손잡이 굵기와 길이 기준) |
| `guide_A ~ D_*.png` | 손별 방향 가이드 (손목 위치, 손가락 방향, 칼자루 방향) |

손마다 **ref_01 + ref_02 + 해당 guide**를 넣고, A와 B에는 **ref_03**도 넣어 주세요.

## 공통 규칙
아래 공통 문구를 모든 프롬프트 앞에 붙여 주세요.

```
Use the attached character image as the style reference. Draw ONLY one gloved hand of this character, in exactly the
same art style: dark navy matte glove (same colour as her gloves), thin black outline, simple two-tone anime cel
shading, no glossy highlights, no texture. Plain white background. The hand is cut off at the wrist (no sleeve, no
skin, no glove cuff or strap, no arm). Five fingers, anatomically correct. Strict side view, flat 2D, no perspective.
Follow the attached guide image for the hand orientation: the red dot is the wrist, the blue line points from the wrist
to the knuckles / fingertips. The guide's grey "sleeve" block and the labels are NOT part of the drawing.
Output a single hand centred on a square canvas, about 60% of the canvas.
```

## A. 칼과 일직선으로 쥔 손 (베기·찌르기용) — 가장 중요
```
Her RIGHT hand seen from the outside (back of the hand toward the viewer), making a fist around a sword grip.
The wrist is on the left, the fist points to the right. The thumb wraps over the TOP of the grip, the four fingers
curl around it underneath. The grip runs straight through the fist from left to right, in line with the forearm,
like the attached sabre's navy grip (about the same thickness). Show only a short piece of the navy grip sticking out
on both sides of the fist; no blade, no guard, no pommel.
```

## B. 칼을 세워 쥔 손 (기본 자세·예비 동작용)
```
Her RIGHT hand hanging down, seen from the outside (back of the hand toward the viewer), making a fist around a sword
grip. The wrist is at the top, the fist below it. The thumb is on the RIGHT side (the front), the fingers curl toward
the viewer's left. The grip passes horizontally through the fist from left to right (front to back), like the
attached sabre's navy grip. Show only a short piece of the navy grip on both sides; no blade, no guard, no pommel.
This is the same pose as her drawn fist in the character image, just holding a grip.
```

## C. 힘 뺀 편 손 (빈손 기본)
```
Her LEFT hand hanging relaxed at her side, open: fingers together and slightly curled, thumb resting along the
index finger. The wrist is at the top, fingertips point down. Seen from the side; this is the far-side hand, so the
PALM faces the viewer (her palm faces her own thigh). Full glove covering every finger.
```

## D. 손가락 벌린 손 (공격·점프 때 균형)
```
Her LEFT hand (far-side hand) reaching back and out for balance: fingers spread apart, palm facing down, wrist at the lower left,
fingers pointing to the upper right. Full glove covering every finger.
```

## 받은 뒤 처리
결과물은 `ArtSource/Characters/Officer/Source/hands/`에 넣어 주세요. 파일 이름은 `A_*.png`, `B_*.png`처럼 붙이면 돼요.
이후 처리는 제가 할게요.
- 배경 제거, 원본 장갑 색 맞추기
- 손목 피벗, 크기(주먹 0.25 u), 칼자루 위치 맞추기
- `Tools/import_hands.py`에 등록하고 게임에서 확인하기

## 팁
- 그림체가 어긋나면 ref_02(장갑 확대)를 한 번 더 붙이고 "match the glove in the second image exactly"를 추가해 보세요.
- 손가락이 6개가 되거나 손목 밴드가 생기면 "no wrist band, exactly five fingers"를 다시 강조해 주세요.
- A, B는 칼자루가 주먹을 관통하는 방향이 핵심이에요. 방향이 틀리면 가이드의 금색 선을 보라고 다시 지시해 주세요.
