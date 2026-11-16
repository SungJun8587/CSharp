#!/usr/bin/env python3
"""render_monsters.py : 몬스터 3D 렌더(시험판). render_heroes.py 의 도형 렌더/셀 셰이딩을 그대로 쓴다.

사용:  python3 render_monsters.py <slime|wolf> <출력폴더> [--sheet]
  <출력폴더>/{각도:000}.png            서 있기 (각도 0,90,180,270)
  <출력폴더>/atk_{각도:000}_{자세}.png 공격 5자세     <출력폴더>/hit_{각도:000}.png 피격
  <출력폴더>/death_{각도:000}_{단계}.png 사망 3단계    <출력폴더>/idle_{각도:000}_{프레임:00}.png 대기 8프레임(숨쉬기 + 눈 깜빡임)
모델은 +z 가 앞(각도 0 에서 카메라를 본다), +y 가 위. 400x400, 발 밑 가운데 고정.
"""
import math, os, sys
import numpy as np
from PIL import Image
import render_heroes as H
H.FEET_Y = 0.80                 # 바닥 원판이 화면 아래로 잘리지 않게 영웅(0.92)보다 위로 올린다
from render_heroes import Builder, C, rot, raster, shade, _ext

ONES = np.ones(3)
EYE_W = C(250, 248, 250)
_EYE_REAL = False
INK = C(40, 24, 36)
IDLE_N = 8
WALK_N = 6
IDLE_BLINK = 6


# ------------------------------------------------------------------ 공통: 도형 전체를 변환 / 땅에 붙이기
def transform(prims, M=None, pivot=(0, 0, 0), shift=(0, 0, 0), scale=(1, 1, 1)):
    """scale 은 축별 배율(몸을 납작/길게), M 은 그 뒤에 pivot 을 축으로 도는 회전, shift 는 평행 이동."""
    pivot = np.asarray(pivot, float); shift = np.asarray(shift, float); sc = np.asarray(scale, float)
    for p in prims:
        c = p.c * sc
        if p.box: pass
        elif p.cone: p.a = np.array([p.a[0] * sc[0], p.a[1] * sc[0], p.a[2] * sc[1]])
        else: p.a = p.a * sc
        if M is not None:
            c = M @ (c - pivot) + pivot
            p.R = M @ p.R
        p.c = c + shift
    return prims


def ground(prims):
    """가장 낮은 곳이 y=0(발 밑)에 닿게 올리거나 내린다."""
    lo = min(float(p.c[1] - _ext(p)) for p in prims)
    for p in prims: p.c = p.c + np.array([0, -lo, 0])
    return prims


def eyes(b, expr, pos, sz, col=None, sep=0.0):
    """눈 한 쌍. pos = 오른쪽 눈 중심, 왼쪽은 x 를 뒤집는다. expr: normal / blink / hurt / dead / glare(노려봄)"""
    LINE = INK
    for s in (-1, 1):
        x, y, z = pos[0] * s, pos[1], pos[2]
        if _EYE_REAL:
            if expr in ('normal', 'glare'):
                h = 0.80 if expr == 'normal' else 0.5
                b.E((x, y, z), (sz * 1.15, sz * (h + 0.35), sz * 0.55), INK, sh=ONES * 0.8)                                                     # 눈구멍
                b.E((x, y, z + sz * 0.18), (sz * 0.85, sz * h, sz * 0.42), col if col is not None else C(200, 160, 50), sh=ONES * 0.85, emis=True)   # 홍채
                b.E((x, y, z + sz * 0.42), (sz * 0.30, sz * h * 0.9, sz * 0.18), INK, sh=ONES * 0.8)                                            # 동공
                b.E((x - s * sz * 0.28, y + sz * 0.26, z + sz * 0.55), (sz * 0.12, sz * 0.12, sz * 0.08), C(255, 255, 255), emis=True)
                if expr == 'glare': b.E((x, y + sz * 0.78, z + sz * 0.12), (sz * 1.25, sz * 0.22, sz * 0.5), INK, rot('z', 0.45 * s), sh=ONES * 0.8)  # 찌푸린 눈썹
            elif expr == 'blink' or expr == 'dead':
                b.E((x, y - sz * 0.1, z + sz * 0.15), (sz * 1.1, sz * 0.14, sz * 0.5), INK, sh=ONES * 0.8)
            else:
                b.E((x, y, z), (sz * 1.15, sz * 0.5, sz * 0.5), INK, sh=ONES * 0.8)
                b.E((x, y + sz * 0.1, z + sz * 0.3), (sz * 0.7, sz * 0.14, sz * 0.2), col if col is not None else C(200, 160, 50), sh=ONES * 0.85)
            continue
        if expr in ('normal', 'glare'):
            b.E((x, y, z), (sz * 1.0, sz * (0.92 if expr == 'normal' else 0.60), sz * 0.55), EYE_W, sh=ONES * 0.9)
            b.E((x - s * sz * 0.05, y - sz * 0.03, z + sz * 0.22), (sz * 0.62, sz * (0.62 if expr == 'normal' else 0.45), sz * 0.40), col if col is not None else INK, sh=ONES * 0.8)
            b.E((x - s * sz * 0.22, y + sz * 0.22, z + sz * 0.50), (sz * 0.2, sz * 0.22, sz * 0.14), C(255, 255, 255), emis=True)
            if expr == 'glare':
                b.E((x, y + sz * 0.62, z + sz * 0.1), (sz * 1.1, sz * 0.2, sz * 0.5), INK, rot('z', 0.38 * s), sh=ONES * 0.8)       # 치켜올린 눈썹 = 화난 눈
        elif expr == 'blink':
            b.E((x, y - sz * 0.2, z + sz * 0.15), (sz * 1.0, sz * 0.16, sz * 0.5), LINE, sh=ONES * 0.8)
        elif expr == 'hurt':
            for sg in (-1, 1):
                b.seg((x - s * sz * 0.9, y + sz * 0.75 * sg, z + sz * 0.2), (x + s * sz * 0.8, y, z + sz * 0.2), sz * 0.16, sz * 0.16, LINE, sh=ONES * 0.8)
        else:       # dead: X
            for sg in (-1, 1):
                b.seg((x - sz * 0.8, y + sz * 0.8 * sg, z + sz * 0.2), (x + sz * 0.8, y - sz * 0.8 * sg, z + sz * 0.2), sz * 0.16, sz * 0.16, LINE, sh=ONES * 0.8)


# ------------------------------------------------------------------ 슬라임
SL_BODY, SL_LIGHT, SL_DARK = C(92, 212, 96), C(168, 244, 150), C(52, 150, 78)
SL_SH = np.array([0.50, 0.62, 0.72])


def build_slime(sq=1.0, sw=1.0, lift=0.0, lean=0.0, expr='normal', mouth='smile'):
    b = Builder()
    b.group(); b.E((0, 0.66, 0), (0.88, 0.82, 0.88), SL_BODY, sh=SL_SH, spec=0.35)             # 몸
    b.group(); b.E((0, 0.14, 0), (0.98, 0.17, 0.98), SL_DARK, sh=SL_SH)                         # 바닥에 퍼진 아랫단
    b.group(); b.E((-0.22, 1.34, -0.05), (0.36, 0.17, 0.32), SL_LIGHT, sh=SL_SH, spec=0.5)      # 머리 위 반짝이는 볼록이
    b.group(); b.E((0.50, 1.08, 0.30), (0.16, 0.10, 0.14), C(200, 255, 200), emis=True)         # 하이라이트 점
    b.group(); b.E((0.88, 0.34, 0.1), (0.20, 0.18, 0.20), SL_BODY, sh=SL_SH)                    # 옆으로 삐져나온 방울
    b.group()
    for (x, y, z, r) in ((-0.58, 0.98, 0.52, 0.11), (0.36, 1.18, -0.46, 0.09), (-0.72, 0.50, 0.42, 0.08), (0.64, 0.58, -0.52, 0.10)):
        b.E((x, y, z), (r, r, r), SL_LIGHT, sh=SL_SH, spec=0.6)                                 # 몸 속 방울이 비쳐 보이는 볼록이
    b.E((-0.62, 0.20, 0.72), (0.18, 0.12, 0.18), SL_BODY, sh=SL_SH)                              # 바닥에 흘러내린 방울
    b.E((0.40, 0.17, 0.90), (0.14, 0.09, 0.14), SL_BODY, sh=SL_SH)
    b.E((0.66, 1.00, 0.36), (0.06, 0.22, 0.05), C(230, 255, 230), rot('z', -0.5), emis=True)   # 젖은 광택 줄기
    for s_ in (-1, 1): b.E((s_ * 0.54, 0.66, 0.74), (0.12, 0.07, 0.04), C(255, 150, 150), sh=ONES * 0.95)   # 볼터치
    b.group()
    eyes(b, expr, (0.30, 0.80, 0.82), 0.17, INK)
    if mouth == 'smile':
        b.E((0, 0.54, 0.86), (0.15, 0.05, 0.05), C(40, 70, 50), sh=ONES * 0.8)
    elif mouth == 'open':
        b.E((0, 0.52, 0.86), (0.15, 0.13, 0.06), C(40, 20, 36), sh=ONES * 0.8)
        b.E((0, 0.47, 0.89), (0.09, 0.05, 0.03), C(230, 110, 130), sh=ONES * 0.9)
    elif mouth == 'flat':
        b.E((0, 0.52, 0.86), (0.09, 0.025, 0.04), C(40, 70, 50), sh=ONES * 0.8)
    transform(b.P, scale=(sw, sq, sw), shift=(0, lift, lean))
    return b.P


# 공격: 웅크림 -> 뛰어오름 -> 늘어남 -> 내려앉아 퍼짐 -> 회복
SL_ATK = [
    dict(sq=0.74, sw=1.14, expr='glare', mouth='flat'),
    dict(sq=1.00, sw=0.98, lift=0.15, lean=0.12, expr='glare', mouth='open'),
    dict(sq=1.16, sw=0.88, lift=0.50, lean=0.30, expr='glare', mouth='open'),
    dict(sq=1.00, sw=1.02, lift=0.18, lean=0.36, expr='glare', mouth='open'),
    dict(sq=0.62, sw=1.26, lean=0.18, expr='normal', mouth='open'),
]
SL_HIT = dict(sq=0.80, sw=1.14, lean=-0.14, expr='hurt', mouth='open')
SL_DEATH = [dict(sq=0.72, sw=1.12, lean=-0.1, expr='hurt', mouth='open'), dict(sq=0.40, sw=1.32, expr='hurt', mouth='open'), dict(sq=0.17, sw=1.62, expr='dead', mouth='flat')]


def slime_idle(k):
    t = k / IDLE_N
    s = math.sin(2 * math.pi * t)
    return dict(sq=1.0 + 0.035 * s, sw=1.0 - 0.02 * s, expr='blink' if k == IDLE_BLINK else 'normal')


# ------------------------------------------------------------------ 늑대
WF_FUR, WF_BELLY, WF_DARK = C(112, 100, 92), C(182, 170, 150), C(60, 52, 52)
WF_SH = np.array([0.55, 0.52, 0.72])


def build_wolf(pitch=0.0, shift=(0, 0, 0), jaw=0.0, crouch=0.0, expr='normal', roll=0.0, legs=0.0, tail=0.0, snap=True):
    """pitch: 앞이 들리는 각(양수 = 코가 위), crouch: 몸이 낮아지는 양(0~1), jaw: 아래턱 벌림(라디안), roll: 옆으로 쓰러지는 각, legs: 앞다리를 드는 정도, tail: 꼬리 처짐"""
    b = Builder()
    drop = 0.34 * crouch
    b.group(); b.E((0, 0.98 - drop, 0.0), (0.44, 0.46, 0.80), WF_FUR, sh=WF_SH)              # 몸통
    b.group(); b.E((0, 1.02 - drop, 0.50), (0.46, 0.52, 0.44), WF_FUR, sh=WF_SH)              # 가슴(앞이 두툼함)
    b.group(); b.E((0, 0.94 - drop, -0.56), (0.40, 0.42, 0.40), WF_FUR, sh=WF_SH)             # 엉덩이
    b.group(); b.E((0, 0.78 - drop, 0.15), (0.34, 0.26, 0.76), WF_BELLY, sh=WF_SH)            # 배(밝은 색)
    b.group(); b.E((0, 1.36 - drop * 0.6, 0.62), (0.34, 0.26, 0.40), WF_DARK, sh=WF_SH)        # 어깨 갈기
    b.group(); b.seg((0, 1.18 - drop, 0.72), (0, 1.42 - drop * 0.6, 1.08), 0.26, 0.22, WF_FUR, n=1, sh=WF_SH)      # 목
    hy = 1.50 - drop * 0.6
    b.group(); b.E((0, hy, 1.22), (0.30, 0.28, 0.32), WF_FUR, sh=WF_SH)                       # 머리
    b.group(); b.E((0, hy - 0.09, 1.58), (0.15, 0.13, 0.30), WF_BELLY, sh=WF_SH)              # 주둥이
    b.group(); b.E((0, hy - 0.02, 1.86), (0.08, 0.06, 0.06), C(30, 22, 30), sh=ONES * 0.8, spec=0.4)   # 코
    b.group()
    for s in (-1, 1):
        b.cone((s * 0.17, hy + 0.20, 1.08), 0.12, 0.01, 0.34, WF_DARK, k=0.5, R=rot('z', -0.22 * s) @ rot('x', -0.18), sh=WF_SH)     # 귀
        b.cone((s * 0.17, hy + 0.22, 1.10), 0.065, 0.005, 0.26, C(220, 150, 150), k=0.4, R=rot('z', -0.22 * s) @ rot('x', -0.18), sh=SH_PINK)
    b.group()
    eyes(b, expr, (0.15, hy + 0.07, 1.46), 0.062, C(244, 176, 40))
    # 아래턱: 피벗은 입 뒤쪽
    jy, jz = hy - 0.20, 1.44
    jc = np.array([0, -0.02, 0.22])
    Rj = rot('x', jaw)
    jp = np.array([0, jy, jz]) + Rj @ jc
    b.group(); b.E(jp, (0.12, 0.05, 0.24), WF_BELLY, Rj, sh=WF_SH)
    if jaw > 0.15:
        b.group(); b.E((0, hy - 0.2, 1.62), (0.09, 0.035, 0.2), C(150, 50, 70), sh=ONES * 0.8)       # 벌린 입 속
        for s in (-1, 1):
            b.cone((s * 0.07, hy - 0.16, 1.74), 0.025, 0.002, 0.12, C(250, 250, 240), R=rot('x', math.pi), sh=ONES * 0.9)         # 송곳니
    # 다리 (몸을 LEG_UP 만큼 들어 올려 다리를 길게 한다)
    n_body = len(b.P)
    b.group()
    for s in (-1, 1):
        f_hip = np.array([s * 0.26, 0.86 + LEG_UP - drop, 0.52]); f_paw = np.array([s * 0.26, 0.08, 0.60 + 0.0])
        if legs > 0: f_paw = f_paw + np.array([0, 0.55 * legs, 0.50 * legs])
        f_mid = (f_hip + f_paw) / 2 + np.array([0, 0, -0.04])
        b.seg(f_hip, f_mid, 0.17, 0.12, WF_FUR, n=1, sh=WF_SH); b.seg(f_mid, f_paw, 0.11, 0.08, WF_FUR, n=1, sh=WF_SH)
        b.E(f_paw + [0, 0.02, 0.10], (0.12, 0.07, 0.17), WF_DARK, sh=WF_SH)
        h_hip = np.array([s * 0.28, 0.88 + LEG_UP - drop, -0.55]); h_knee = h_hip + [0, -0.30 - LEG_UP / 2, -0.12]; h_paw = np.array([s * 0.28, 0.08, -0.40])
        b.seg(h_hip, h_knee, 0.20, 0.13, WF_FUR, n=1, sh=WF_SH); b.seg(h_knee, h_paw, 0.12, 0.08, WF_FUR, n=1, sh=WF_SH)
        b.E(h_paw + [0, 0.02, 0.10], (0.11, 0.07, 0.16), WF_DARK, sh=WF_SH)
    n_legs = len(b.P)
    # 꼬리
    b.group(); b.seg((0, 1.02 - drop, -0.88), (0, 1.12 - drop - 0.35 * tail, -1.48), 0.16, 0.10, WF_FUR, n=2, sh=WF_SH)
    b.E((0, 1.12 - drop - 0.35 * tail, -1.56), (0.12, 0.12, 0.2), WF_DARK, sh=WF_SH)
    P = b.P
    for i, p in enumerate(P):
        if i < n_body or i >= n_legs: p.c = p.c + np.array([0, LEG_UP, 0])
    if pitch: transform(P, rot('x', -pitch), pivot=(0, 0.95, 0.0))
    if roll: transform(P, rot('z', roll), pivot=(0, 0.0, 0.0))
    if snap: ground(P)
    transform(P, shift=shift)
    return P


LEG_UP = 0.20
SH_PINK = np.array([0.7, 0.5, 0.6])
WF_ATK = [
    dict(crouch=1.0, pitch=-0.12, tail=0.3, expr='glare'),
    dict(crouch=0.6, pitch=0.10, jaw=0.35, shift=(0, 0.05, 0.18), expr='glare'),
    dict(crouch=0.0, pitch=0.32, jaw=0.75, legs=1.0, shift=(0, 0.28, 0.55), expr='glare'),
    dict(crouch=0.1, pitch=0.18, jaw=0.85, legs=0.8, shift=(0, 0.14, 0.62), expr='glare'),
    dict(crouch=0.5, pitch=-0.05, jaw=0.2, shift=(0, 0, 0.30), expr='normal'),
]
WF_HIT = dict(pitch=0.30, shift=(0, 0, -0.18), jaw=0.45, expr='hurt', tail=0.4)
WF_DEATH = [dict(pitch=0.25, shift=(0, 0, -0.2), jaw=0.5, expr='hurt', roll=0.45), dict(pitch=0.1, jaw=0.4, expr='hurt', roll=1.05), dict(pitch=0.0, jaw=0.15, expr='dead', roll=1.52, tail=0.6)]


def wolf_idle(k):
    t = k / IDLE_N
    s = math.sin(2 * math.pi * t)
    return dict(shift=(0, 0.015 * s, 0), tail=0.15 + 0.15 * s, expr='blink' if k == IDLE_BLINK else 'normal')


# ------------------------------------------------------------------ 공통 래퍼: 모델 부품 + 몸 전체 변형(납작/기울임/쓰러짐)
def finish(P, sq=1.0, sw=1.0, lift=0.0, lean=0.0, pitch=0.0, roll=0.0, ground_snap=False):
    transform(P, scale=(sw, sq, sw))
    if pitch: transform(P, rot('x', pitch), pivot=(0, 0.3, 0))
    if roll: transform(P, rot('z', roll), pivot=(0, 0.0, 0))
    if ground_snap or roll: ground(P)
    transform(P, shift=(0, lift, lean))
    return P


def mouth_at(b, mouth, pos, w):
    x, y, z = pos
    if mouth == 'smile': b.E((x, y, z), (w, w * 0.32, w * 0.35), C(50, 34, 44), sh=ONES * 0.8)
    elif mouth == 'open':
        b.E((x, y - w * 0.1, z), (w, w * 0.85, w * 0.4), C(40, 20, 36), sh=ONES * 0.8)
        b.E((x, y - w * 0.45, z + w * 0.08), (w * 0.62, w * 0.3, w * 0.2), C(230, 110, 130), sh=ONES * 0.9)
    else: b.E((x, y, z), (w * 0.6, w * 0.16, w * 0.3), C(50, 34, 44), sh=ONES * 0.8)


# ------------------------------------------------------------------ 버섯
MU_CAP, MU_CAP_L, MU_STEM, MU_STEM_D = C(206, 52, 66), C(236, 104, 108), C(246, 230, 200), C(206, 180, 150)
MU_SH = np.array([0.52, 0.55, 0.74])


def build_mushroom(expr='normal', mouth='smile', **kw):
    b = Builder()
    b.group(); b.E((0, 0.56, 0), (0.47, 0.64, 0.47), MU_STEM, sh=MU_SH)                          # 기둥
    b.group(); b.E((0, 1.30, 0), (0.98, 0.46, 0.98), MU_CAP, sh=MU_SH, spec=0.25)                 # 갓
    b.group(); b.E((0, 1.04, 0), (0.98, 0.12, 0.98), MU_CAP_L, sh=MU_SH)                          # 갓 안쪽 테두리
    b.group()
    for (x, y, z, r) in ((0.0, 1.74, 0.05, 0.30), (0.62, 1.55, 0.40, 0.20), (-0.60, 1.52, 0.44, 0.18), (0.10, 1.56, -0.74, 0.20), (-0.55, 1.50, -0.40, 0.16)):
        b.E((x, y, z), (r, r * 0.35, r), C(252, 244, 232), sh=ONES * 0.92)                         # 흰 점
    for s_ in (-1, 1): b.E((s_ * 0.24, 0.08, 0.14), (0.18, 0.10, 0.25), MU_STEM_D, sh=MU_SH)         # 발
    for (x, y, z, r) in ((-0.2, 1.72, -0.2, 0.14), (0.45, 1.66, -0.3, 0.15), (-0.78, 1.56, 0.1, 0.14), (0.80, 1.555, 0.15, 0.13)):
        b.E((x, y, z), (r, r * 0.35, r), C(252, 244, 232), sh=ONES * 0.92)                       # 흰 점 더
    b.group(); b.E((0, 0.93, 0), (0.58, 0.07, 0.58), C(240, 226, 196), sh=MU_SH)                   # 기둥 목 주름 치마
    b.E((0, 1.10, 0), (1.0, 0.05, 1.0), MU_CAP * 0.82, sh=MU_SH)                                  # 갓 가장자리 두께
    b.group()
    for (x, z) in ((-0.36, 0.34), (0.38, 0.30), (0.05, 0.46)):
        b.cone((x, 0.06, z), 0.05, 0.0, 0.30, C(96, 170, 70), sh=MU_SH)                           # 발밑 풀잎
    b.group()
    eyes(b, expr, (0.18, 0.72, 0.42), 0.12, INK)
    mouth_at(b, mouth, (0, 0.48, 0.45), 0.10)
    return b.P


# ------------------------------------------------------------------ 선인장
CA_BODY, CA_LIGHT, CA_DARK = C(86, 190, 80), C(150, 226, 120), C(52, 130, 66)
CA_SH = np.array([0.50, 0.62, 0.72])


def build_cactus(expr='normal', mouth='smile', **kw):
    b = Builder()
    b.group(); b.E((0, 0.14, 0), (0.66, 0.17, 0.66), CA_DARK, sh=CA_SH)                          # 밑동 흙무더기
    b.group(); b.E((0, 1.12, 0), (0.52, 1.00, 0.52), CA_BODY, sh=CA_SH, spec=0.2)                # 몸통
    b.group(); b.E((-0.14, 1.9, 0.0), (0.22, 0.30, 0.22), CA_LIGHT, sh=CA_SH)                     # 몸통 위쪽 밝은 곳
    for s_ in (-1, 1):
        b.group(); b.seg((s_ * 0.40, 0.95, 0.05), (s_ * 0.88, 1.10, 0.12), 0.20, 0.18, CA_BODY, n=1, sh=CA_SH)
        b.seg((s_ * 0.88, 1.10, 0.12), (s_ * 0.96, 1.62, 0.12), 0.19, 0.17, CA_BODY, n=1, sh=CA_SH)
        b.E((s_ * 0.96, 1.68, 0.12), (0.17, 0.17, 0.17), CA_LIGHT, sh=CA_SH)
    b.group()
    for a in (0.85, -0.85, 1.7, -1.7, 2.55, -2.55, math.pi):                                      # 세로 골
        b.E((0.5 * math.sin(a), 1.12, 0.5 * math.cos(a)), (0.035, 0.94, 0.035), CA_DARK, sh=CA_SH)
    for k in range(16):                                                                          # 가시(뾰족하게 바깥으로)
        a = k * 0.9; y = 0.5 + 0.09 * k
        if 0.95 < y < 1.6 and math.cos(a) > 0.45: continue
        d = np.array([math.sin(a), 0.18, math.cos(a)]); p = np.array([0.52 * math.sin(a), y, 0.52 * math.cos(a)])
        b.seg(p, p + d * 0.13, 0.03, 0.004, C(250, 246, 220), n=1, sh=ONES * 0.9)
    b.group()
    for k in range(5):                                                                           # 머리 꽃잎
        tt = k * 2 * math.pi / 5
        b.E((0.15 * math.cos(tt), 2.24, 0.15 * math.sin(tt)), (0.11, 0.045, 0.07), C(250, 130, 170), rot('y', -tt), sh=ONES * 0.9)
    b.E((0, 2.3, 0), (0.07, 0.05, 0.07), C(255, 220, 90), emis=True)
    b.group()
    eyes(b, expr, (0.20, 1.34, 0.47), 0.13, INK)
    mouth_at(b, mouth, (0, 1.10, 0.50), 0.10)
    return b.P


# ------------------------------------------------------------------ 폭탄
BO_BODY, BO_LIGHT, BO_DARK = C(110, 72, 176), C(168, 130, 224), C(40, 30, 52)
BO_SH = np.array([0.50, 0.50, 0.74])


def build_bomb(expr='normal', mouth='smile', **kw):
    b = Builder()
    b.group(); b.E((0, 0.88, 0), (0.80, 0.80, 0.80), BO_BODY, sh=BO_SH, spec=0.4)                # 몸
    b.group(); b.E((-0.3, 1.40, 0.2), (0.28, 0.14, 0.2), BO_LIGHT, sh=BO_SH, spec=0.5)           # 하이라이트
    b.group(); b.E((0, 1.66, 0), (0.22, 0.13, 0.22), BO_DARK, sh=BO_SH)                          # 꼭지
    b.group(); b.seg((0, 1.70, 0), (0.18, 2.02, 0.0), 0.05, 0.05, C(150, 110, 70), n=1, sh=ONES * 0.8)       # 심지
    b.E((0.20, 2.08, 0.0), (0.15, 0.15, 0.15), C(255, 190, 60), emis=True)
    b.E((0.20, 2.08, 0.0), (0.09, 0.09, 0.09), C(255, 250, 210), emis=True)
    for s_ in (-1, 1): b.E((s_ * 0.34, 0.10, 0.16), (0.24, 0.12, 0.30), BO_DARK, sh=BO_SH)        # 발
    b.group()
    for y in (0.40, 1.36):
        b.E((0, y, 0), (0.71 if y < 1 else 0.69, 0.065, 0.71 if y < 1 else 0.69), BO_DARK, sh=BO_SH, spec=0.7)   # 쇠 띠
    for x in (-0.32, 0.0, 0.32):
        b.E((x, 0.40, 0.58), (0.05, 0.05, 0.04), C(210, 205, 220), sh=BO_SH, spec=0.9)                    # 리벳
    b.group()
    for (x, y, z) in ((0.40, 2.30, 0.10), (0.06, 2.26, 0.12), (0.34, 1.96, -0.05)):
        b.E((x, y, z), (0.05, 0.05, 0.05), C(255, 230, 140), emis=True)                              # 불똥
    b.group()
    eyes(b, expr, (0.28, 1.02, 0.70), 0.19, INK)
    mouth_at(b, mouth, (0, 0.62, 0.76), 0.14)
    return b.P


# ------------------------------------------------------------------ 불꽃
FL_OUT, FL_MID, FL_IN = C(236, 96, 30), C(255, 160, 40), C(255, 226, 110)
FL_SH = np.array([0.8, 0.6, 0.5])


def tongue(b, x, z, h, bx, bz, r, col, emis=False):
    """굽은 불꽃 혀: 밑에서 위로 두 번 꺾이며 가늘어진다."""
    sh = np.array([0.95, 0.8, 0.6]) if emis else FL_SH
    p0 = np.array([x, 1.15, z]); p1 = p0 + np.array([bx * 0.35, h * 0.50, bz * 0.35]); p2 = p0 + np.array([bx * 0.85, h * 0.85, bz * 0.85]); p3 = p0 + np.array([bx, h, bz])
    b.group()
    b.seg(p0, p1, r, r * 0.78, col, n=1, sh=sh, emis=emis)
    b.seg(p1, p2, r * 0.78, r * 0.42, col, n=1, sh=sh, emis=emis)
    b.seg(p2, p3, r * 0.42, 0.02, col, n=1, sh=sh, emis=emis)


def build_flame(expr='glare', mouth='grin', sway=0.0, **kw):
    """sway: 혀가 흔들리는 정도(-1~1). 걷거나 서 있을 때 불꽃이 일렁이게 한다."""
    b = Builder()
    b.group(); b.cone((0, 0.02, 0), 0.08, 0.56, 0.70, FL_OUT, sh=FL_SH)                          # 아래 꼬리(땅에서 올라오는 불)
    b.group(); b.E((0, 0.98, 0), (0.60, 0.72, 0.54), FL_MID, sh=FL_SH)                           # 불꽃 몸
    sw = sway
    tongue(b, 0.0, -0.05, 1.70, 0.20 * sw, 0.0, 0.32, FL_OUT)                                    # 가운데 큰 혀
    tongue(b, -0.22, 0.02, 1.25, -0.16 + 0.18 * sw, 0.04, 0.24, FL_MID)
    tongue(b, 0.22, 0.02, 1.40, 0.18 + 0.18 * sw, 0.04, 0.24, FL_MID)
    tongue(b, -0.36, -0.10, 0.80, -0.22 + 0.15 * sw, -0.05, 0.20, FL_OUT)
    tongue(b, 0.38, -0.10, 0.92, 0.24 + 0.15 * sw, -0.05, 0.20, FL_OUT)
    tongue(b, -0.14, -0.34, 1.05, -0.10 + 0.15 * sw, -0.30, 0.22, FL_OUT)
    tongue(b, 0.16, -0.32, 0.95, 0.12 + 0.15 * sw, -0.28, 0.22, FL_OUT)
    tongue(b, 0.0, -0.02, 1.15, 0.12 * sw, 0.0, 0.17, FL_IN, emis=True)                          # 한가운데 밝은 혀
    b.group(); b.E((0, 0.92, 0.20), (0.46, 0.60, 0.22), FL_IN, sh=np.array([0.95, 0.8, 0.6]), emis=True)     # 속불꽃(밝게)
    b.group(); b.E((0, 0.03, 0.0), (0.70, 0.02, 0.60), C(120, 44, 24), sh=FL_SH)                       # 바닥 그을음
    b.group()
    for (x, y, z, r) in ((-0.82, 1.55, 0.1, 0.06), (0.86, 1.30, 0.0, 0.05), (-0.62, 2.10, -0.2, 0.05), (0.60, 2.30, 0.1, 0.06), (0.1, 2.75, -0.1, 0.05), (-0.9, 0.9, 0.2, 0.05), (0.80, 0.70, 0.3, 0.045)):
        b.E((x, y, z), (r, r * 1.3, r), FL_IN, emis=True)                                        # 날아오르는 불씨
    b.group()
    eyes(b, expr, (0.21, 1.06, 0.48), 0.13, C(120, 40, 20))
    if mouth == 'grin':
        b.E((0, 0.78, 0.50), (0.17, 0.05, 0.05), C(120, 40, 20), sh=ONES * 0.8)      # 넓게 웃는 입(여우 코처럼 보이지 않게 가로로 길게)
    else: mouth_at(b, mouth, (0, 0.78, 0.52), 0.11)
    return b.P


# ------------------------------------------------------------------ 큰 뱀(지렁이)
WO_BODY, WO_BELLY, WO_DARK = C(206, 56, 64), C(244, 224, 196), C(140, 36, 60)
WO_SH = np.array([0.54, 0.50, 0.70])


def build_worm(expr='normal', mouth='open', **kw):
    b = Builder()
    b.group(); b.E((0, 0.36, 0), (1.0, 0.38, 1.0), WO_DARK, sh=WO_SH)                            # 똬리
    for (y, z, r) in ((0.95, 0.0, 0.66), (1.55, -0.04, 0.62), (2.15, -0.10, 0.58), (2.70, 0.02, 0.54)):
        b.group(); b.E((0, y, z), (r, 0.42, r), WO_BODY, sh=WO_SH)                               # 몸 마디
        b.E((0, y - 0.02, z + r * 0.62), (r * 0.62, 0.34, r * 0.5), WO_BELLY, sh=WO_SH)          # 배 줄무늬
    b.group()
    for (y, z, r) in ((0.95, 0.0, 0.66), (1.55, -0.04, 0.62), (2.15, -0.10, 0.58), (2.70, 0.02, 0.54)):
        b.cone((0, y + 0.18, z - r * 0.85), 0.10, 0.0, 0.32, WO_DARK, R=rot('x', -1.1), sh=WO_SH)       # 등 가시
    b.group(); b.E((0, 3.16, 0.36), (0.62, 0.54, 0.78), WO_BODY, sh=WO_SH, spec=0.2)             # 머리
    b.group()
    for (x, y, z) in ((0.3, 3.66, 0.22), (-0.3, 3.64, 0.30), (0.0, 3.72, 0.50), (0.45, 3.50, 0.6)):
        b.E((x, y, z), (0.13, 0.07, 0.13), WO_BELLY, sh=ONES * 0.92)                              # 점무늬
    b.group(); b.E((0, 2.86, 0.86), (0.36, 0.14, 0.40), WO_DARK, sh=WO_SH)                       # 아래턱
    b.group()
    eyes(b, expr, (0.30, 3.28, 0.98), 0.14, C(40, 170, 150))
    mouth_at(b, mouth, (0, 2.98, 1.08), 0.15)
    b.group(); b.E((0.0, 3.0, 1.12), (0.05, 0.05, 0.04), C(250, 250, 240), sh=ONES * 0.9)         # 송곳니
    for x in (-0.20, -0.10, 0.10, 0.20):
        b.cone((x, 3.04, 1.08), 0.04, 0.0, 0.14, C(250, 250, 240), R=rot('x', math.pi), sh=ONES * 0.9)   # 이빨
    return b.P


# ------------------------------------------------------------------ 게
CR_BODY, CR_LIGHT, CR_CLAW, CR_DARK = C(104, 196, 176), C(232, 226, 190), C(206, 56, 62), C(60, 130, 120)
CR_SH = np.array([0.50, 0.58, 0.74])


def build_crab(expr='normal', mouth='smile', **kw):
    b = Builder()
    b.group(); b.E((0, 0.62, 0), (1.04, 0.44, 0.80), CR_BODY, sh=CR_SH, spec=0.2)                # 등딱지
    b.group(); b.E((0, 0.90, -0.05), (0.78, 0.2, 0.58), CR_LIGHT, sh=CR_SH)                      # 등 무늬
    b.group()
    for (x, y, z, r) in ((-0.46, 0.98, 0.12, 0.13), (0.46, 0.98, 0.12, 0.13), (0.0, 1.02, -0.30, 0.14), (-0.70, 0.84, -0.12, 0.10), (0.70, 0.84, -0.12, 0.10)):
        b.E((x, y, z), (r, r * 0.7, r), CR_LIGHT, sh=CR_SH, spec=0.3)                              # 등딱지 혹
    for (x, z) in ((-0.62, 0.52), (0.62, 0.52), (-0.30, 0.66), (0.30, 0.66)):
        b.cone((x, 0.70, z), 0.06, 0.0, 0.2, CR_CLAW, R=rot('x', 1.2), sh=CR_SH)                  # 가장자리 가시
    for (x, y, z) in ((0.82, 0.74, -0.22), (0.92, 0.66, -0.12), (0.76, 0.66, -0.36)):
        b.E((x, y, z), (0.07, 0.07, 0.07), C(236, 232, 214), sh=ONES * 0.92)                       # 따개비
    for s_ in (-1, 1):
        b.group(); b.seg((s_ * 0.92, 0.70, 0.30), (s_ * 1.42, 1.08, 0.80), 0.16, 0.14, CR_BODY, n=1, sh=CR_SH)       # 팔
        b.E((s_ * 1.52, 1.22, 0.92), (0.46, 0.40, 0.40), CR_CLAW, sh=CR_SH, spec=0.2)                               # 집게 몸
        b.cone((s_ * 1.52 + s_ * 0.18, 1.34, 1.18), 0.17, 0.02, 0.42, CR_CLAW, k=0.7, R=rot('x', 1.3), sh=CR_SH)    # 집게 날
        b.cone((s_ * 1.52 - s_ * 0.18, 1.24, 1.18), 0.15, 0.02, 0.38, CR_CLAW, k=0.7, R=rot('x', 1.3), sh=CR_SH)
        for k in range(3):                                                                                           # 다리
            z0 = 0.28 - 0.30 * k
            b.group(); b.seg((s_ * 0.92, 0.46, z0), (s_ * 1.42, 0.30, z0 - 0.05), 0.07, 0.06, CR_DARK, n=1, sh=CR_SH)
            b.seg((s_ * 1.42, 0.30, z0 - 0.05), (s_ * 1.58, 0.02, z0 - 0.08), 0.06, 0.04, CR_DARK, n=1, sh=CR_SH)
        b.group(); b.seg((s_ * 0.30, 0.98, 0.48), (s_ * 0.34, 1.38, 0.58), 0.05, 0.05, CR_BODY, n=1, sh=CR_SH)     # 눈자루
    b.group()
    eyes(b, expr, (0.34, 1.50, 0.60), 0.17, INK)
    mouth_at(b, mouth, (0, 0.62, 0.78), 0.14)
    return b.P


# ------------------------------------------------------------------ 사람형 공통 부품
def _legs(b, col, boot, sh, x=0.19, top=0.70, r=0.13, foot=(0.16, 0.08, 0.22)):
    for s_ in (-1, 1):
        b.group(); b.seg((s_ * x, top, 0), (s_ * (x + 0.02), 0.12, 0.02), r, r * 0.86, col, n=1, sh=sh)
        b.E((s_ * (x + 0.02), 0.09, 0.10), foot, boot, sh=sh)


def _arm(b, s_, sh_pt, hand, r, col, hand_col, sh, hr=0.10):
    mid = ((sh_pt[0] + hand[0]) / 2 + s_ * 0.06, (sh_pt[1] + hand[1]) / 2, (sh_pt[2] + hand[2]) / 2 + 0.06)
    b.group(); b.seg(sh_pt, mid, r, r * 0.86, col, n=1, sh=sh); b.seg(mid, hand, r * 0.86, r * 0.74, col, n=1, sh=sh)
    b.E(hand, (hr, hr, hr), hand_col, sh=sh)


def _visor_eyes(b, expr, y, z, sz, col):
    b.group(); b.E((0, y - 0.02, z - 0.10), (0.27, 0.12, 0.16), C(26, 22, 34), sh=ONES * 0.8)         # 투구 눈 틈
    if expr == 'blink': eyes(b, 'blink', (0.11, y, z + 0.02), sz, col)
    else: eyes(b, 'glare' if expr in ('normal', 'glare') else expr, (0.11, y, z + 0.02), sz, col)


# ------------------------------------------------------------------ 고블린
GB_SKIN, GB_DARK, GB_CLOTH, GB_EAR = C(104, 132, 68), C(70, 92, 50), C(104, 76, 54), C(158, 116, 100)
GB_SH = np.array([0.52, 0.58, 0.74])
STEEL, STEEL_D, WOOD = C(190, 196, 208), C(120, 126, 142), C(120, 80, 50)
WOOD_SH = np.array([0.5, 0.5, 0.7])


def _goblin_head(b, expr, mouth, y=1.62, skin=GB_SKIN, eyec=C(240, 200, 40)):
    b.group(); b.E((0, y, 0.05), (0.38, 0.34, 0.34), skin, sh=GB_SH, spec=0.15)                      # 머리
    for s_ in (-1, 1):
        b.group()
        b.cone((s_ * 0.30, y + 0.04, 0.0), 0.15, 0.01, 0.58, skin, k=0.3, R=rot('z', -1.30 * s_) @ rot('y', 0.2 * s_), sh=GB_SH)    # 큰 뾰족 귀
        b.cone((s_ * 0.34, y + 0.04, 0.02), 0.08, 0.005, 0.40, GB_EAR, k=0.25, R=rot('z', -1.30 * s_) @ rot('y', 0.2 * s_), sh=ONES * 0.8)
    b.group(); b.E((0, y - 0.07, 0.38), (0.08, 0.06, 0.07), GB_DARK, sh=GB_SH)                         # 코
    b.group()
    eyes(b, expr, (0.15, y + 0.06, 0.34), 0.09, eyec)
    mouth_at(b, mouth, (0, y - 0.18, 0.33), 0.10)


def build_goblin(expr='normal', mouth='smile', **kw):
    b = Builder()
    _legs(b, GB_DARK, GB_CLOTH, GB_SH)
    b.group(); b.cone((0, 0.42, 0), 0.36, 0.24, 0.30, GB_CLOTH, k=0.8, sh=WOOD_SH)                    # 허리 천
    b.group(); b.E((0, 0.98, 0.0), (0.34, 0.40, 0.28), GB_SKIN, sh=GB_SH)                             # 몸통
    b.group(); b.E((0, 0.86, 0.14), (0.26, 0.26, 0.18), C(168, 208, 108), sh=GB_SH)                   # 배
    _arm(b, -1, (-0.34, 1.20, 0), (-0.52, 0.76, 0.14), 0.10, GB_SKIN, GB_SKIN, GB_SH)
    _arm(b, 1, (0.34, 1.20, 0), (0.54, 0.80, 0.22), 0.10, GB_SKIN, GB_SKIN, GB_SH)
    b.group(); b.seg((0.56, 0.84, 0.26), (0.62, 1.22, 0.52), 0.06, 0.02, STEEL, n=1, sh=ONES * 0.9, spec=0.5)   # 단검
    b.E((0.55, 0.84, 0.24), (0.14, 0.04, 0.05), STEEL_D, sh=ONES * 0.8)
    _goblin_head(b, expr, mouth)
    return b.P


GB_SM_ROBE, GB_SM_ROBE_D, GB_SM_GLOW = C(88, 56, 120), C(56, 36, 80), C(120, 240, 230)


def build_goblin_shaman(expr='normal', mouth='smile', **kw):
    b = Builder()
    b.group(); b.cone((0, 0.04, 0), 0.54, 0.30, 1.18, GB_SM_ROBE, k=0.82, sh=GB_SH)                    # 로브
    b.group(); b.cone((0, 0.04, 0), 0.56, 0.52, 0.12, GB_SM_ROBE_D, k=0.82, sh=GB_SH)                  # 단
    b.group(); b.E((0, 1.30, 0.04), (0.32, 0.20, 0.26), GB_SM_ROBE_D, sh=GB_SH)                         # 어깨 망토
    b.group(); b.E((0, 1.02, 0.30), (0.10, 0.24, 0.05), C(240, 200, 80), sh=ONES * 0.9)                  # 목걸이/띠
    _arm(b, -1, (-0.30, 1.28, 0.02), (-0.46, 0.92, 0.30), 0.10, GB_SM_ROBE, GB_SKIN, GB_SH)
    _arm(b, 1, (0.30, 1.28, 0.02), (0.56, 1.02, 0.30), 0.10, GB_SM_ROBE, GB_SKIN, GB_SH)
    b.group(); b.seg((0.60, 0.04, 0.30), (0.62, 2.06, 0.30), 0.06, 0.05, WOOD, n=2, sh=WOOD_SH)          # 지팡이
    b.E((0.62, 2.18, 0.30), (0.17, 0.17, 0.17), GB_SM_GLOW, emis=True)                                  # 지팡이 구슬
    _goblin_head(b, expr, mouth, y=1.64, eyec=C(120, 240, 230))
    b.group(); b.cone((0, 1.88, 0.0), 0.40, 0.02, 0.74, GB_SM_ROBE_D, k=0.95, R=rot('x', -0.22), sh=GB_SH)    # 뾰족 모자
    b.E((0, 1.90, 0.02), (0.46, 0.06, 0.44), GB_SM_ROBE, sh=GB_SH)
    return b.P


GB_WR_ARM, GB_WR_ARM_D = C(112, 82, 56), C(72, 54, 42)


def build_goblin_warrior(expr='normal', mouth='smile', **kw):
    b = Builder()
    _legs(b, GB_DARK, GB_WR_ARM_D, GB_SH, r=0.15, foot=(0.18, 0.09, 0.24))
    b.group(); b.cone((0, 0.44, 0), 0.40, 0.30, 0.28, GB_WR_ARM_D, k=0.8, sh=WOOD_SH)
    b.group(); b.E((0, 1.00, 0), (0.42, 0.44, 0.32), GB_WR_ARM, sh=WOOD_SH, spec=0.15)                   # 가죽 갑옷
    b.group(); b.E((0, 0.98, 0.26), (0.30, 0.34, 0.08), GB_WR_ARM_D, sh=WOOD_SH)
    for s_ in (-1, 1):
        b.group(); b.E((s_ * 0.50, 1.28, 0), (0.18, 0.14, 0.18), STEEL_D, sh=ONES * 0.8, spec=0.3)         # 어깨 보호대
    _arm(b, -1, (-0.46, 1.24, 0), (-0.62, 0.82, 0.18), 0.12, GB_SKIN, GB_SKIN, GB_SH, hr=0.11)
    _arm(b, 1, (0.46, 1.24, 0), (0.64, 0.88, 0.28), 0.12, GB_SKIN, GB_SKIN, GB_SH, hr=0.11)
    b.group(); b.B((-0.70, 0.92, 0.22), (0.05, 0.38, 0.30), WOOD, sh=WOOD_SH)                           # 방패
    b.E((-0.76, 0.92, 0.22), (0.05, 0.12, 0.12), STEEL, sh=ONES * 0.9, spec=0.5)
    b.group(); b.seg((0.66, 0.90, 0.30), (0.70, 1.78, 0.52), 0.07, 0.05, STEEL, n=2, sh=ONES * 0.9, spec=0.5)   # 큰 칼
    b.E((0.66, 0.96, 0.29), (0.20, 0.05, 0.06), STEEL_D, sh=ONES * 0.8)
    _goblin_head(b, expr, mouth, y=1.70)
    b.group(); b.E((0, 1.92, 0.04), (0.40, 0.20, 0.36), STEEL_D, sh=ONES * 0.85, spec=0.4)             # 투구 윗부분
    for s_ in (-1, 1):
        b.cone((s_ * 0.30, 1.92, 0.0), 0.08, 0.01, 0.34, C(240, 232, 200), R=rot('z', -0.55 * s_), sh=ONES * 0.9)   # 뿔
    return b.P


# ------------------------------------------------------------------ 늑대 걷기
def _wolf_walk(k):
    t = k / float(WALK_N); p = abs(math.sin(math.pi * t)); s = math.sin(2 * math.pi * t)
    return dict(shift=(0, 0.05 * p, 0), pitch=0.05 * s, legs=0.30 * p, tail=0.2 + 0.2 * s)


# ------------------------------------------------------------------ 나무 정령
TR_BARK, TR_BARK_D, TR_LEAF, TR_LEAF_L = C(104, 78, 54), C(66, 48, 38), C(62, 118, 54), C(104, 160, 76)
TR_SH = np.array([0.50, 0.55, 0.72])


def build_treant(expr='normal', mouth='open', **kw):
    b = Builder()
    for (x, z, a) in ((-0.34, 0.16, -0.5), (0.34, 0.16, 0.5), (0.0, -0.34, 0.0), (-0.30, -0.22, -0.3), (0.30, -0.22, 0.3)):
        b.group(); b.seg((x * 0.6, 0.6, z * 0.6), (x * 1.9, 0.06, z * 1.9 + 0.08), 0.22, 0.11, TR_BARK_D, n=1, sh=TR_SH)      # 뿌리 다리
    b.group(); b.E((0, 1.20, 0), (0.64, 0.92, 0.56), TR_BARK, sh=TR_SH)                                  # 줄기
    b.group(); b.E((0, 1.60, 0.0), (0.58, 0.50, 0.52), TR_BARK, sh=TR_SH)
    for k_ in range(5):
        a = k_ * 1.3
        b.group(); b.seg((0.50 * math.sin(a), 0.5 + 0.28 * k_, 0.46 * math.cos(a)), (0.54 * math.sin(a + 0.2), 0.9 + 0.28 * k_, 0.5 * math.cos(a + 0.2)), 0.07, 0.04, TR_BARK_D, n=1, sh=TR_SH)   # 껍질 줄기선
    for s_ in (-1, 1):
        b.group(); b.seg((s_ * 0.56, 1.80, 0.0), (s_ * 1.02, 1.34, 0.18), 0.19, 0.14, TR_BARK, n=1, sh=TR_SH)
        b.seg((s_ * 1.02, 1.34, 0.18), (s_ * 1.16, 0.70, 0.30), 0.14, 0.11, TR_BARK, n=1, sh=TR_SH)
        for j in (-1, 0, 1):
            b.cone((s_ * 1.16 + j * 0.07, 0.68, 0.32), 0.05, 0.005, 0.26, TR_BARK_D, R=rot('x', math.pi - 0.2 * j) @ rot('z', 0.0), sh=TR_SH)          # 가지 손가락
        b.E((s_ * 0.9, 2.12, 0), (0.08, 0.08, 0.08), TR_LEAF_L, sh=ONES * 0.9)
    b.group()
    for (x, y, z, r, c_) in ((0, 2.28, 0, 0.62, TR_LEAF), (-0.5, 2.1, 0.1, 0.44, TR_LEAF), (0.52, 2.12, -0.05, 0.46, TR_LEAF), (0.1, 2.62, -0.05, 0.42, TR_LEAF_L), (-0.2, 2.56, 0.3, 0.30, TR_LEAF_L), (0.0, 2.1, -0.4, 0.40, TR_LEAF)):
        b.E((x, y, z), (r, r * 0.72, r), c_, sh=TR_SH)                                                   # 잎사귀 덩이
    b.group(); b.E((0, 1.30, 0.40), (0.34, 0.50, 0.22), TR_BARK_D, sh=TR_SH)                              # 얼굴 움푹한 곳
    b.group()
    eyes(b, expr, (0.20, 1.52, 0.52), 0.12, C(250, 190, 60))
    mouth_at(b, mouth, (0, 1.14, 0.56), 0.16)
    return b.P


# ------------------------------------------------------------------ 갑옷 기사(어둠 / 강철)
DK_ARM, DK_ARM_D, DK_TRIM, DK_GLOW = C(70, 64, 86), C(34, 30, 46), C(126, 70, 170), C(210, 90, 255)
IK_ARM, IK_ARM_D, IK_TRIM, IK_PLUME = C(158, 164, 176), C(96, 102, 116), C(176, 140, 60), C(170, 44, 44)
KN_SH = np.array([0.45, 0.50, 0.72])


def _knight(b, expr, arm, arm_d, trim, glow, *, wide=1.0, horns=False, plume=None, cape=None, big_sword=False, round_shield=False):
    sh = KN_SH
    _legs(b, arm_d, arm_d, sh, r=0.15 * wide, foot=(0.17, 0.09, 0.24))
    b.group(); b.E((0, 0.66, 0.0), (0.36 * wide, 0.20, 0.28), arm_d, sh=sh)                              # 허리
    if cape is not None:
        b.group(); b.B((0, 1.05, -0.36), (0.44 * wide, 0.66, 0.04), cape, R=rot('x', 0.06), sh=sh)      # 망토
    b.group(); b.E((0, 1.14, 0.0), (0.44 * wide, 0.46, 0.32), arm, sh=sh, spec=0.35)                   # 흉갑
    b.group(); b.E((0, 1.12, 0.26), (0.20 * wide, 0.24, 0.07), trim, sh=sh, spec=0.4)                  # 가슴 문장
    for s_ in (-1, 1):
        b.group(); b.E((s_ * 0.56 * wide, 1.46, 0), (0.22, 0.16, 0.22), arm, sh=sh, spec=0.35)         # 어깨 갑옷
        if horns: b.cone((s_ * 0.60 * wide, 1.56, 0), 0.07, 0.005, 0.30, arm_d, R=rot('z', -0.3 * s_), sh=sh)
    _arm(b, -1, (-0.50 * wide, 1.38, 0), (-0.64 * wide, 0.88, 0.16), 0.12, arm_d, arm, sh, hr=0.12)
    _arm(b, 1, (0.50 * wide, 1.38, 0), (0.66 * wide, 0.92, 0.30), 0.12, arm_d, arm, sh, hr=0.12)
    if round_shield:
        b.group(); b.E((-0.78 * wide, 0.96, 0.22), (0.07, 0.44, 0.44), arm, sh=sh, spec=0.4)
        b.E((-0.84 * wide, 0.96, 0.22), (0.05, 0.14, 0.14), trim, sh=sh, spec=0.5)
    else:
        b.group(); b.B((-0.74 * wide, 0.96, 0.22), (0.05, 0.42, 0.28), arm_d, sh=sh)
        b.E((-0.80 * wide, 0.96, 0.22), (0.04, 0.12, 0.10), trim, sh=sh, spec=0.5)
    top = 2.3 if big_sword else 1.86
    b.group(); b.seg((0.68 * wide, 0.92, 0.32), (0.72 * wide, top, 0.58), 0.09 if big_sword else 0.07, 0.05, STEEL, n=2, sh=ONES * 0.9, spec=0.6)   # 검
    b.E((0.68 * wide, 0.98, 0.31), (0.22, 0.05, 0.07), trim, sh=ONES * 0.85, spec=0.4)
    if big_sword: b.E((0.70 * wide, 1.6, 0.46), (0.03, 0.55, 0.05), glow, emis=True)                    # 검날 빛
    b.group(); b.E((0, 1.84, 0.02), (0.36, 0.34, 0.34), arm, sh=sh, spec=0.4)                           # 투구
    b.group(); b.E((0, 1.68, 0.04), (0.28, 0.14, 0.26), arm_d, sh=sh)                                   # 턱받이
    _visor_eyes(b, expr, 1.84, 0.40, 0.08, glow)
    if horns:
        for s_ in (-1, 1): b.cone((s_ * 0.24, 1.96, 0.0), 0.09, 0.005, 0.52, C(210, 200, 190), R=rot('z', -0.65 * s_), sh=ONES * 0.9)
    if plume is not None:
        b.group(); b.cone((0, 2.08, -0.02), 0.07, 0.03, 0.32, plume, k=2.0, R=rot('x', -0.5), sh=ONES * 0.85)
        b.E((0, 2.06, -0.12), (0.06, 0.18, 0.20), plume, sh=ONES * 0.85)


def build_darkknight(expr='normal', mouth='flat', **kw):
    b = Builder(); _knight(b, expr, DK_ARM, DK_ARM_D, DK_TRIM, DK_GLOW, wide=1.06, horns=True, cape=C(100, 30, 60), big_sword=True); return b.P


def build_ironknight(expr='normal', mouth='flat', **kw):
    b = Builder(); _knight(b, expr, IK_ARM, IK_ARM_D, IK_TRIM, C(250, 220, 90), plume=IK_PLUME, round_shield=True); return b.P


# ------------------------------------------------------------------ 신규 6종(사실적 화풍): 박쥐 / 독거미 / 해골 전사 / 바위 골렘 / 유령 / 독전갈
BT_FUR, BT_FUR_D, BT_MEM, BT_EAR = C(66, 52, 54), C(40, 32, 36), C(94, 62, 70), C(150, 104, 110)
BT_SH = np.array([0.50, 0.50, 0.70])


def build_bat(expr='normal', mouth='open', flap=0.0, **kw):
    b = Builder()
    b.group(); b.E((0, 0.86, 0.0), (0.34, 0.42, 0.30), BT_FUR, sh=BT_SH)                                 # 몸통
    b.group(); b.E((0, 0.70, 0.10), (0.24, 0.26, 0.18), C(110, 92, 90), sh=BT_SH)                         # 배(밝은 털)
    b.group(); b.E((0, 1.30, 0.12), (0.27, 0.25, 0.25), BT_FUR, sh=BT_SH)                                 # 머리
    b.group(); b.E((0, 1.20, 0.38), (0.14, 0.10, 0.14), BT_FUR_D, sh=BT_SH)                               # 주둥이
    b.group(); b.E((0, 1.22, 0.50), (0.05, 0.04, 0.04), C(20, 16, 20), sh=ONES * 0.8, spec=0.3)           # 코
    for s_ in (-1, 1):
        b.group(); b.cone((s_ * 0.16, 1.46, 0.06), 0.15, 0.01, 0.52, BT_FUR, k=0.25, R=rot('z', -0.2 * s_), sh=BT_SH)       # 큰 귀
        b.cone((s_ * 0.16, 1.48, 0.10), 0.09, 0.005, 0.40, BT_EAR, k=0.2, R=rot('z', -0.2 * s_), sh=ONES * 0.75)
        # 날개: 팔뼈 + 손가락 뼈 4개 + 막
        a = 0.55 * flap
        sh_pt = np.array([s_ * 0.30, 1.02, -0.02])
        elbow = sh_pt + np.array([s_ * 0.62, 0.12 + 0.55 * a, -0.10])
        tip = elbow + np.array([s_ * 0.72, 0.42 + 0.9 * a, -0.04])
        b.group(); b.seg(sh_pt, elbow, 0.07, 0.05, BT_FUR_D, n=1, sh=BT_SH); b.seg(elbow, tip, 0.05, 0.025, BT_FUR_D, n=2, sh=BT_SH)
        fing = []
        for j, (dx, dy, dz) in enumerate(((0.55, -0.30, -0.22), (0.35, -0.65, -0.30), (0.15, -0.90, -0.28))):
            fp = elbow + np.array([s_ * dx * 1.1, dy * (1.0 - 0.45 * a) - 0.12, dz * 1.3])
            b.seg(elbow, fp, 0.035, 0.015, BT_FUR_D, n=1, sh=BT_SH); fing.append(fp)
        # 막(손가락 사이를 납작한 타원으로 메움)
        pts = [tip] + fing + [np.array([s_ * 0.34, 0.62, -0.20])]
        for q0, q1 in zip(pts[:-1], pts[1:]):
            mid = (q0 + q1 + elbow + np.array([s_ * 0.0, 0.0, 0.0])) / 3.0
            ctr = (q0 + q1 + elbow) / 3.0
            b.E(ctr, (abs(q0[0] - q1[0]) * 0.38 + 0.22, abs(q0[1] - q1[1]) * 0.3 + 0.10, 0.04), BT_MEM, rot('z', 0.0), sh=BT_SH)
        b.group(); b.seg((s_ * 0.16, 0.50, 0.0), (s_ * 0.20, 0.30, 0.02), 0.04, 0.03, BT_FUR_D, n=1, sh=BT_SH)      # 발
    b.group()
    eyes(b, expr, (0.11, 1.34, 0.30), 0.055, C(220, 60, 50))
    if mouth == 'open':
        for s_ in (-1, 1): b.cone((s_ * 0.05, 1.15, 0.50), 0.02, 0.002, 0.10, C(240, 236, 226), R=rot('x', math.pi), sh=ONES * 0.9)   # 송곳니
    return b.P


SP_BODY, SP_DARK, SP_MARK = C(52, 40, 36), C(30, 24, 24), C(150, 70, 40)
SP_SH = np.array([0.52, 0.50, 0.70])


def build_spider(expr='normal', mouth='open', step=0.0, **kw):
    b = Builder()
    b.group(); b.E((0, 0.62, -0.62), (0.58, 0.50, 0.70), SP_BODY, sh=SP_SH, spec=0.2)                     # 배
    b.group(); b.E((0, 0.90, -0.70), (0.24, 0.06, 0.36), SP_MARK, sh=SP_SH)                               # 등 무늬
    b.group(); b.E((0, 0.58, 0.06), (0.38, 0.30, 0.38), SP_DARK, sh=SP_SH, spec=0.15)                     # 머리가슴
    b.group(); b.E((0, 0.52, 0.38), (0.22, 0.20, 0.18), SP_DARK, sh=SP_SH)                                # 얼굴
    for s_ in (-1, 1):
        b.group(); b.seg((s_ * 0.10, 0.40, 0.50), (s_ * 0.12, 0.18, 0.62), 0.07, 0.04, C(110, 60, 40), n=1, sh=SP_SH)        # 엄니
    legs = [(0.42, 0.20), (0.44, -0.05), (0.42, -0.30), (0.36, -0.55)]
    for i, (zx, zz) in enumerate(legs):
        for s_ in (-1, 1):
            ph = math.sin(step * math.pi + i * 1.6 + (0 if s_ > 0 else math.pi))
            hip = np.array([s_ * 0.30, 0.60, zz * 0.6 + 0.05]); knee = np.array([s_ * (0.95 + 0.10 * i), 0.96 + 0.16 * ph, zz * 1.3 + 0.18 - 0.1 * i])
            foot = np.array([s_ * (1.40 + 0.12 * i), 0.04 + max(0, ph) * 0.14, zz * 1.9 + 0.30 - 0.18 * i])
            b.group(); b.seg(hip, knee, 0.075, 0.06, SP_DARK, n=1, sh=SP_SH); b.seg(knee, foot, 0.06, 0.025, SP_BODY, n=2, sh=SP_SH)
    b.group()
    eyes(b, expr, (0.10, 0.62, 0.44), 0.050, C(200, 40, 40))
    for (x, y) in ((0.22, 0.58), (0.05, 0.72), (0.17, 0.70)):
        for s_ in (-1, 1): b.E((x * s_, y, 0.40), (0.035, 0.035, 0.03), C(160, 30, 30), emis=True)
    return b.P


SK_BONE, SK_BONE_D, SK_RUST = C(214, 204, 178), C(150, 140, 120), C(120, 100, 88)
SK_SH = np.array([0.52, 0.48, 0.64])


def build_skeleton(expr='normal', mouth='open', **kw):
    b = Builder()
    for s_ in (-1, 1):
        b.group(); b.seg((s_ * 0.18, 0.70, 0), (s_ * 0.20, 0.14, 0.02), 0.07, 0.05, SK_BONE, n=2, sh=SK_SH)               # 다리뼈
        b.E((s_ * 0.20, 0.38, 0.04), (0.08, 0.08, 0.08), SK_BONE_D, sh=SK_SH)                                        # 무릎
        b.E((s_ * 0.20, 0.07, 0.10), (0.12, 0.06, 0.20), SK_BONE, sh=SK_SH)                                          # 발
    b.group(); b.E((0, 0.76, 0), (0.34, 0.14, 0.18), SK_BONE_D, sh=SK_SH)                                            # 골반
    b.group(); b.seg((0, 0.80, -0.02), (0, 1.55, 0.0), 0.06, 0.06, SK_BONE_D, n=3, sh=SK_SH)                           # 척추
    for k in range(5):                                                                                               # 갈비뼈
        y = 1.46 - 0.14 * k; w = 0.36 - 0.03 * abs(k - 1.5)
        b.group()
        for s_ in (-1, 1): b.E((s_ * w * 0.9, y, 0.06), (0.24, 0.035, 0.20), SK_BONE, rot('z', -0.22 * s_), sh=SK_SH)
    b.group(); b.E((0, 1.62, 0.12), (0.06, 0.28, 0.07), SK_BONE, sh=SK_SH)                                           # 가슴뼈
    for s_ in (-1, 1):
        b.group(); b.E((s_ * 0.46, 1.58, 0), (0.12, 0.10, 0.12), SK_BONE_D, sh=SK_SH)                                # 어깨
    _arm(b, -1, (-0.46, 1.56, 0), (-0.62, 0.98, 0.16), 0.06, SK_BONE, SK_BONE, SK_SH, hr=0.08)
    _arm(b, 1, (0.46, 1.56, 0), (0.64, 1.02, 0.30), 0.06, SK_BONE, SK_BONE, SK_SH, hr=0.08)
    b.group(); b.E((-0.72, 1.00, 0.20), (0.06, 0.42, 0.42), WOOD, sh=WOOD_SH); b.E((-0.78, 1.00, 0.20), (0.05, 0.14, 0.14), SK_RUST, sh=ONES * 0.8, spec=0.3)   # 나무 방패
    b.group(); b.seg((0.66, 0.96, 0.30), (0.72, 1.92, 0.52), 0.06, 0.05, SK_RUST, n=2, sh=ONES * 0.85, spec=0.3)     # 녹슨 검
    b.E((0.66, 1.02, 0.29), (0.18, 0.04, 0.06), SK_BONE_D, sh=ONES * 0.8)
    b.group(); b.E((0, 1.90, 0.04), (0.30, 0.28, 0.30), SK_BONE, sh=SK_SH, spec=0.15)                                # 두개골
    b.group(); b.E((0, 1.68, 0.14), (0.20, 0.12, 0.20), SK_BONE_D, sh=SK_SH)                                         # 턱
    for k in range(-2, 3): b.E((k * 0.05, 1.71, 0.33), (0.022, 0.05, 0.03), C(236, 230, 214), sh=ONES * 0.9)           # 이빨
    b.group()
    eyes(b, expr, (0.11, 1.94, 0.30), 0.075, C(255, 150, 40))
    return b.P


GL_ROCK, GL_ROCK_D, GL_MOSS, GL_RUNE = C(126, 120, 110), C(84, 80, 76), C(76, 112, 56), C(255, 150, 50)
GL_SH = np.array([0.48, 0.50, 0.70])


def build_golem(expr='normal', mouth='flat', **kw):
    b = Builder()
    for s_ in (-1, 1):
        b.group(); b.B((s_ * 0.34, 0.38, 0.0), (0.24, 0.38, 0.26), GL_ROCK_D, rot('y', 0.1 * s_), sh=GL_SH)           # 다리 바위
        b.B((s_ * 0.36, 0.08, 0.10), (0.28, 0.08, 0.34), GL_ROCK, rot('y', -0.1 * s_), sh=GL_SH)
    b.group(); b.B((0, 0.92, 0.0), (0.52, 0.18, 0.34), GL_ROCK_D, sh=GL_SH)                                          # 허리
    b.group(); b.B((0, 1.50, 0.0), (0.66, 0.46, 0.42), GL_ROCK, rot('y', 0.05), sh=GL_SH)                            # 몸통
    b.group(); b.E((0, 1.50, 0.42), (0.20, 0.26, 0.05), GL_RUNE, emis=True)                                          # 가슴 룬
    b.group(); b.B((0, 1.78, -0.1), (0.50, 0.12, 0.30), GL_MOSS, rot('x', 0.1), sh=GL_SH)                            # 이끼 어깨띠
    for s_ in (-1, 1):
        b.group(); b.B((s_ * 0.86, 1.76, 0.0), (0.26, 0.24, 0.26), GL_ROCK, rot('z', 0.2 * s_), sh=GL_SH)           # 어깨
        b.E((s_ * 0.88, 1.98, 0.0), (0.20, 0.07, 0.20), GL_MOSS, sh=GL_SH)
        b.seg((s_ * 0.90, 1.55, 0.0), (s_ * 1.02, 0.84, 0.12), 0.26, 0.24, GL_ROCK_D, n=2, sh=GL_SH)                 # 팔
        b.B((s_ * 1.06, 0.62, 0.18), (0.30, 0.28, 0.30), GL_ROCK, rot('y', 0.2 * s_), sh=GL_SH)                      # 주먹
    b.group(); b.B((0, 2.02, 0.06), (0.26, 0.22, 0.26), GL_ROCK, sh=GL_SH)                                           # 머리
    b.group(); b.B((0, 1.92, 0.24), (0.18, 0.06, 0.08), GL_ROCK_D, sh=GL_SH)
    b.group()
    eyes(b, expr, (0.12, 2.04, 0.30), 0.07, C(255, 170, 50))
    return b.P


GH_BODY, GH_DARK, GH_GLOW = C(176, 196, 220), C(110, 130, 168), C(150, 240, 255)
GH_SH = np.array([0.62, 0.66, 0.84])


def build_ghost(expr='normal', mouth='open', sway=0.0, **kw):
    b = Builder()
    for k, (y, r) in enumerate(((1.62, 0.44), (1.46, 0.48), (1.28, 0.50), (1.08, 0.50), (0.90, 0.46), (0.72, 0.40), (0.56, 0.34), (0.42, 0.28), (0.30, 0.22), (0.20, 0.16), (0.12, 0.10))):
        b.group(); b.E((0.12 * sway * (1 - y / 2.0), y, -0.04 * k), (r, 0.22, r * 0.92), GH_BODY, sh=GH_SH)       # 아래로 가늘어지는 꼬리
    b.group(); b.E((0, 1.80, 0.02), (0.40, 0.40, 0.36), GH_BODY, sh=GH_SH)                                       # 머리
    for s_ in (-1, 1):
        shp = np.array([s_ * 0.44, 1.46, 0.0]); hand = np.array([s_ * (0.86 + 0.08 * sway), 0.86 + 0.1 * sway, 0.30])
        b.group(); b.seg(shp, (shp + hand) / 2 + np.array([0, -0.05, 0.08]), 0.14, 0.11, GH_BODY, n=1, sh=GH_SH); b.seg((shp + hand) / 2 + np.array([0, -0.05, 0.08]), hand, 0.11, 0.07, GH_BODY, n=1, sh=GH_SH)
        for j in (-1, 0, 1): b.cone(hand + np.array([j * 0.05, 0.0, 0.02]), 0.04, 0.005, 0.26, GH_DARK, R=rot('x', math.pi - 0.3) @ rot('z', 0.15 * j * s_), sh=GH_SH)   # 길쭉한 손가락
    b.group(); b.E((0, 1.64, 0.28), (0.20, 0.12, 0.10), C(40, 48, 72), sh=ONES * 0.8)                           # 벌어진 입 속
    b.group(); b.E((0, 1.86, 0.18), (0.30, 0.14, 0.12), C(40, 48, 72), sh=ONES * 0.8)                           # 눈 그늘
    b.group()
    eyes(b, expr, (0.13, 1.88, 0.34), 0.075, GH_GLOW)
    return b.P


SC_BODY, SC_DARK, SC_CLAW = C(132, 70, 40), C(78, 40, 28), C(156, 86, 50)
SC_SH = np.array([0.52, 0.48, 0.66])


def build_scorpion(expr='normal', mouth='open', step=0.0, tail=0.0, **kw):
    b = Builder()
    b.group(); b.E((0, 0.46, 0.20), (0.46, 0.24, 0.52), SC_BODY, sh=SC_SH, spec=0.3)                              # 머리가슴
    for k in range(5):                                                                                           # 배 마디
        b.group(); b.E((0, 0.44, -0.28 - 0.26 * k), (0.40 - 0.04 * k, 0.20 - 0.01 * k, 0.17), SC_BODY if k % 2 == 0 else SC_DARK, sh=SC_SH, spec=0.3)
    # 꼬리: 위로 휘어 올라가 침이 앞을 향한다
    pts = [np.array([0, 0.50, -1.55]), np.array([0, 0.74, -1.80]), np.array([0, 1.14, -1.84]), np.array([0, 1.50, -1.62]), np.array([0, 1.70, -1.26]), np.array([0, 1.68, -0.90])]
    for k, p in enumerate(pts):
        q = p + np.array([0, 0.18 * tail * k / 5.0, 0.0])
        b.group(); b.E(q, (0.15 - 0.012 * k, 0.15 - 0.012 * k, 0.17), SC_BODY if k % 2 == 0 else SC_DARK, sh=SC_SH, spec=0.3)
    st = pts[-1] + np.array([0, 0.18 * tail, 0])
    b.group(); b.E(st + np.array([0, -0.04, 0.16]), (0.12, 0.12, 0.16), C(70, 150, 70), sh=SC_SH, spec=0.4)         # 독낭
    b.cone(st + np.array([0, -0.06, 0.28]), 0.06, 0.005, 0.30, C(40, 30, 30), R=rot('x', math.pi / 2 + 0.2), sh=SC_SH)   # 침
    for s_ in (-1, 1):
        b.group(); b.seg((s_ * 0.34, 0.50, 0.50), (s_ * 0.74, 0.54, 0.98), 0.10, 0.09, SC_BODY, n=1, sh=SC_SH)                # 집게 팔
        b.E((s_ * 0.78, 0.56, 1.22), (0.24, 0.12, 0.30), SC_CLAW, sh=SC_SH, spec=0.3)                              # 집게 몸
        b.cone((s_ * 0.88, 0.58, 1.36), 0.09, 0.01, 0.34, SC_CLAW, k=0.6, R=rot('x', math.pi / 2) @ rot('z', 0.25 * s_), sh=SC_SH, spec=0.3)
        b.cone((s_ * 0.68, 0.58, 1.36), 0.08, 0.01, 0.30, SC_CLAW, k=0.6, R=rot('x', math.pi / 2) @ rot('z', -0.2 * s_), sh=SC_SH, spec=0.3)
        for i in range(4):
            ph = math.sin(step * math.pi + i * 1.7 + (0 if s_ > 0 else math.pi))
            hip = np.array([s_ * 0.34, 0.40, 0.40 - 0.28 * i]); knee = np.array([s_ * 0.84, 0.50 + 0.10 * ph, 0.46 - 0.30 * i]); foot = np.array([s_ * 1.06, 0.03 + max(0, ph) * 0.1, 0.60 - 0.32 * i])
            b.group(); b.seg(hip, knee, 0.06, 0.05, SC_DARK, n=1, sh=SC_SH); b.seg(knee, foot, 0.05, 0.025, SC_DARK, n=1, sh=SC_SH)
    b.group()
    eyes(b, expr, (0.12, 0.66, 0.62), 0.045, C(230, 200, 60))
    return b.P


# ------------------------------------------------------------------ 존 테마 몬스터 6종: 설인(설원) 용암 도마뱀(화산) 늪 두꺼비(늪) 바다 소라게(해안) 수정 사마귀(수정 동굴) 폭풍 독수리(하늘)
YT_FUR, YT_FUR_D, YT_FACE, YT_ICE = C(226, 232, 240), C(170, 182, 200), C(150, 178, 206), C(170, 226, 255)
YT_SH = np.array([0.58, 0.62, 0.80])


def build_yeti(expr='normal', mouth='open', **kw):
    b = Builder()
    for s_ in (-1, 1):
        b.group(); b.E((s_ * 0.28, 0.50, 0.0), (0.26, 0.50, 0.28), YT_FUR, sh=YT_SH)                                   # 다리
        b.E((s_ * 0.29, 0.07, 0.14), (0.27, 0.10, 0.36), YT_FUR_D, sh=YT_SH)                                            # 발
    b.group(); b.E((0, 1.36, 0.0), (0.70, 0.66, 0.48), YT_FUR, sh=YT_SH)                                                # 몸통(털뭉치)
    b.group(); b.E((0, 1.22, 0.30), (0.42, 0.42, 0.26), YT_FUR_D, sh=YT_SH)                                             # 배
    for k in range(7):                                                                                                  # 등/어깨 털 가시
        a = -0.9 + 0.3 * k
        b.cone((a * 0.7, 1.70 + 0.1 * (1 - abs(a)), -0.30), 0.10, 0.01, 0.34, YT_FUR, R=rot('x', -0.9) @ rot('z', -a * 0.5), sh=YT_SH)
    for s_ in (-1, 1):
        b.group(); b.E((s_ * 0.74, 1.74, 0.0), (0.34, 0.32, 0.34), YT_FUR, sh=YT_SH)                                    # 어깨
        b.seg((s_ * 0.76, 1.66, 0.0), (s_ * 0.96, 0.98, 0.26), 0.24, 0.20, YT_FUR, n=2, sh=YT_SH)                       # 긴 팔
        b.E((s_ * 0.98, 0.80, 0.30), (0.32, 0.30, 0.32), YT_FUR, sh=YT_SH)                                              # 주먹 털
        for j in (-1, 0, 1): b.cone((s_ * 0.98 + j * 0.10, 0.64, 0.46), 0.05, 0.005, 0.18, C(60, 66, 80), R=rot('x', 2.6), sh=YT_SH)   # 발톱
    b.group(); b.E((0, 1.86, 0.30), (0.30, 0.20, 0.22), YT_FUR, sh=YT_SH)                                               # 턱수염 털
    b.group(); b.E((0, 2.10, 0.10), (0.42, 0.40, 0.38), YT_FUR, sh=YT_SH)                                               # 머리
    b.E((0, 2.04, 0.40), (0.28, 0.26, 0.10), YT_FACE, sh=YT_SH)                                                         # 얼굴
    b.E((0, 2.22, 0.40), (0.34, 0.07, 0.11), YT_FUR_D, sh=YT_SH)                                                        # 눈썹뼈
    for s_ in (-1, 1):
        b.cone((s_ * 0.26, 2.40, 0.0), 0.09, 0.005, 0.40, YT_ICE, R=rot('z', -0.35 * s_), sh=YT_SH, spec=0.4)           # 얼음 뿔
        b.cone((s_ * 0.11, 1.90, 0.46), 0.05, 0.005, 0.22, C(244, 240, 228), R=rot('x', math.pi), sh=YT_SH)             # 엄니
    b.group()
    eyes(b, expr, (0.13, 2.10, 0.47), 0.065, YT_ICE)
    mouth_at(b, mouth, (0, 1.90, 0.46), 0.12)
    return b.P


LZ_ROCK, LZ_ROCK_D, LZ_LAVA = C(74, 54, 50), C(42, 32, 32), C(255, 128, 40)
LZ_SH = np.array([0.50, 0.50, 0.70])


def build_lizard(expr='normal', mouth='open', step=0.0, tail=0.0, **kw):
    b = Builder()
    b.group(); b.E((0, 0.56, 0.0), (0.44, 0.32, 0.84), LZ_ROCK, sh=LZ_SH, spec=0.1)                                     # 몸통
    b.group(); b.E((0, 0.40, 0.0), (0.36, 0.18, 0.66), LZ_LAVA, sh=ONES * 0.9, emis=True)                               # 배(용암빛)
    for k in range(6):                                                                                                  # 등의 갈라진 용암 틈
        z = 0.55 - 0.20 * k
        b.group(); b.E((0, 0.84, z), (0.16 - 0.01 * k, 0.04, 0.10), LZ_LAVA, sh=ONES * 0.9, emis=True)
        b.cone((0, 0.84, z - 0.06), 0.10, 0.005, 0.26 - 0.015 * k, LZ_ROCK_D, sh=LZ_SH)
    b.group(); b.E((0, 0.70, 1.02), (0.38, 0.30, 0.44), LZ_ROCK, sh=LZ_SH, spec=0.1)                                    # 머리
    b.E((0, 0.62, 1.34), (0.27, 0.19, 0.28), LZ_ROCK, sh=LZ_SH)                                                         # 주둥이
    b.E((0, 0.48, 1.20), (0.18, 0.05, 0.28), LZ_LAVA if mouth == 'open' else LZ_ROCK_D, sh=ONES * 0.9, emis=(mouth == 'open'))   # 입 안
    for k in range(5):                                                                                                  # 목 뒤 방패 가시
        b.group(); b.cone((0, 0.96 - 0.02 * k, 0.74 - 0.14 * k), 0.12, 0.01, 0.32 - 0.03 * k, LZ_ROCK_D, R=rot('x', -0.35), sh=LZ_SH)
    for s_ in (-1, 1):
        for j in range(3): b.cone((s_ * 0.16, 0.54, 1.18 + 0.12 * j), 0.025, 0.003, 0.09, C(240, 230, 210), R=rot('x', math.pi), sh=ONES * 0.9)   # 아래 이빨
    for s_ in (-1, 1):
        b.cone((s_ * 0.18, 0.86, 0.90), 0.07, 0.005, 0.26, LZ_ROCK_D, R=rot('x', -0.5) @ rot('z', -0.3 * s_), sh=LZ_SH)  # 뿔
    # 꼬리: 뒤로 길게 뻗다 끝이 달궈진다
    for k in range(6):
        q = np.array([0, 0.54 + 0.04 * k + 0.12 * tail * k / 5.0, -0.82 - 0.22 * k]); r = 0.32 - 0.04 * k
        b.group(); b.E(q, (r, r * 0.8, 0.20), LZ_LAVA if k >= 4 else LZ_ROCK, sh=ONES * 0.9 if k >= 4 else LZ_SH, emis=k >= 4, spec=0.1)
    for s_ in (-1, 1):
        for i, z0 in enumerate((0.55, -0.45)):
            ph = math.sin(step * math.pi + i * math.pi + (0 if s_ > 0 else math.pi))
            hip = np.array([s_ * 0.38, 0.50, z0]); knee = np.array([s_ * 0.72, 0.38 + 0.08 * ph, z0 + 0.04]); foot = np.array([s_ * 0.74, 0.04 + max(0, ph) * 0.10, z0 + 0.12 + 0.08 * ph])
            b.group(); b.seg(hip, knee, 0.15, 0.11, LZ_ROCK, n=1, sh=LZ_SH); b.seg(knee, foot, 0.11, 0.08, LZ_ROCK_D, n=1, sh=LZ_SH)
            b.E(foot + np.array([0, 0.0, 0.10]), (0.13, 0.05, 0.17), LZ_ROCK_D, sh=LZ_SH)
    b.group()
    eyes(b, expr, (0.21, 0.88, 1.20), 0.075, C(255, 220, 60))
    return b.P


TD_SKIN, TD_BELLY, TD_WART, TD_SPOT = C(100, 124, 60), C(182, 188, 112), C(70, 92, 48), C(150, 70, 150)
TD_SH = np.array([0.54, 0.56, 0.74])


def build_toad(expr='normal', mouth='open', **kw):
    b = Builder()
    b.group(); b.E((0, 0.62, 0.0), (0.82, 0.58, 0.74), TD_SKIN, sh=TD_SH, spec=0.35)                                    # 몸
    b.group(); b.E((0, 0.40, 0.30), (0.60, 0.36, 0.44), TD_BELLY, sh=TD_SH, spec=0.2)                                   # 배
    for k, (x, y, z) in enumerate(((-0.34, 1.04, -0.15), (0.30, 1.08, -0.30), (0.0, 1.14, 0.0), (-0.50, 0.92, 0.15), (0.52, 0.94, 0.10), (0.10, 1.02, -0.45), (-0.16, 0.98, 0.30))):
        b.group(); b.E((x, y, z), (0.09, 0.07, 0.09), TD_SPOT if k % 3 == 0 else TD_WART, sh=TD_SH, spec=0.4)           # 사마귀 / 독 돌기
    for s_ in (-1, 1):
        b.group(); b.E((s_ * 0.34, 1.16, 0.34), (0.22, 0.20, 0.22), TD_SKIN, sh=TD_SH, spec=0.35)                       # 튀어나온 눈두덩
        b.group(); b.E((s_ * 0.78, 0.38, -0.26), (0.28, 0.32, 0.50), TD_SKIN, sh=TD_SH)                                 # 뒷다리 허벅지
        b.E((s_ * 0.86, 0.07, 0.22), (0.26, 0.07, 0.36), TD_BELLY, sh=TD_SH)                                            # 뒷발
        b.seg((s_ * 0.56, 0.56, 0.44), (s_ * 0.70, 0.10, 0.70), 0.13, 0.10, TD_SKIN, n=1, sh=TD_SH)                     # 앞다리
        b.E((s_ * 0.72, 0.06, 0.84), (0.22, 0.06, 0.18), TD_BELLY, sh=TD_SH)
    b.group(); b.E((0, 0.40, 0.60), (0.34, 0.22 + (0.10 if mouth == 'open' else 0.0), 0.20), TD_BELLY, sh=TD_SH)        # 울음주머니
    b.group()
    if mouth == 'open':
        b.E((0, 0.62, 0.70), (0.62, 0.10, 0.14), C(60, 24, 40), sh=ONES * 0.8)
        b.E((0, 0.58, 0.74), (0.38, 0.07, 0.10), C(230, 110, 130), sh=ONES * 0.9)
    else:
        b.E((0, 0.62, 0.72), (0.62, 0.035, 0.10), C(50, 34, 44), sh=ONES * 0.8)
    eyes(b, expr, (0.34, 1.20, 0.52), 0.12, C(230, 190, 40))
    return b.P


HC_BODY, HC_CLAW, HC_SHELL, HC_RIDGE, HC_SPOT = C(212, 98, 66), C(232, 120, 76), C(238, 206, 170), C(196, 112, 96), C(218, 84, 80)
HC_SH = np.array([0.52, 0.56, 0.74])


def build_hermit(expr='normal', mouth='smile', step=0.0, **kw):
    b = Builder()
    b.group(); b.E((0, 0.46, 0.38), (0.52, 0.32, 0.60), HC_BODY, sh=HC_SH, spec=0.2)                                    # 몸
    # 등의 소라 껍데기: 큰 구 + 감긴 이랑 + 뾰족한 끝
    b.group(); b.E((0, 1.00, -0.30), (0.78, 0.72, 0.80), HC_SHELL, sh=HC_SH, spec=0.35)
    for k in range(4):
        t = k / 3.0
        b.group(); b.E((0.08 * (1 - t), 0.84 + 0.30 * t + 0.1, -0.10 - 0.22 * t), (0.82 - 0.22 * k, 0.16, 0.82 - 0.22 * k), HC_RIDGE, rot('x', 0.35 + 0.1 * k), sh=HC_SH, spec=0.3)
    b.cone((0, 1.50, -0.52), 0.42, 0.04, 0.78, HC_SHELL, R=rot('x', -0.55), sh=HC_SH, spec=0.35)
    for x, y, z in ((-0.34, 1.12, 0.22), (0.42, 0.98, 0.14), (-0.50, 0.78, -0.22), (0.20, 1.22, 0.12), (-0.10, 1.30, -0.10), (0.50, 0.70, -0.30), (-0.62, 1.00, -0.02)):
        b.group(); b.E((x, y, z), (0.10, 0.10, 0.07), HC_SPOT, sh=HC_SH)
    for s_, (big) in ((-1, 0.0), (1, 1.0)):                                                                              # 오른쪽 집게가 크다
        r = 0.34 + 0.16 * big
        b.group(); b.seg((s_ * 0.46, 0.58, 0.60), (s_ * 0.92, 0.84, 1.00), 0.14 + 0.04 * big, 0.12 + 0.04 * big, HC_BODY, n=1, sh=HC_SH)
        b.E((s_ * 1.00, 0.94, 1.16), (r, r * 0.8, r * 0.9), HC_CLAW, sh=HC_SH, spec=0.25)
        b.cone((s_ * 1.00 + s_ * r * 0.4, 1.02, 1.30), r * 0.38, 0.02, r * 1.2, HC_CLAW, k=0.7, R=rot('x', 1.35), sh=HC_SH, spec=0.25)
        b.cone((s_ * 1.00 - s_ * r * 0.4, 0.92, 1.30), r * 0.34, 0.02, r * 1.1, HC_CLAW, k=0.7, R=rot('x', 1.35), sh=HC_SH, spec=0.25)
        for i in range(3):
            ph = math.sin(step * math.pi + i * 2.1 + (0 if s_ > 0 else math.pi)); z0 = 0.46 - 0.30 * i
            b.group(); b.seg((s_ * 0.50, 0.40, z0), (s_ * 0.94, 0.30 + 0.06 * ph, z0 - 0.04), 0.07, 0.06, C(160, 70, 52), n=1, sh=HC_SH)
            b.seg((s_ * 0.94, 0.30 + 0.06 * ph, z0 - 0.04), (s_ * 1.06, 0.02 + max(0, ph) * 0.08, z0 - 0.06), 0.06, 0.04, C(160, 70, 52), n=1, sh=HC_SH)
        b.group(); b.seg((s_ * 0.22, 0.76, 0.84), (s_ * 0.26, 1.18, 0.92), 0.05, 0.05, HC_BODY, n=1, sh=HC_SH)          # 눈자루
    b.group()
    eyes(b, expr, (0.26, 1.30, 0.96), 0.15, INK)
    mouth_at(b, mouth, (0, 0.44, 0.94), 0.13)
    return b.P


MT_BODY, MT_DARK, MT_CRY, MT_CRY2 = C(86, 66, 138), C(52, 40, 92), C(140, 232, 255), C(248, 150, 232)
MT_SH = np.array([0.54, 0.52, 0.76])


def build_mantis(expr='normal', mouth='open', step=0.0, **kw):
    b = Builder()
    b.group(); b.E((0, 1.00, -0.52), (0.32, 0.30, 0.78), MT_BODY, rot('x', -0.2), sh=MT_SH, spec=0.3)                    # 배
    b.group(); b.E((0, 1.32, -0.02), (0.28, 0.52, 0.26), MT_BODY, rot('x', 0.15), sh=MT_SH, spec=0.3)                   # 가슴
    for k, (z, h) in enumerate(((-0.30, 0.50), (-0.58, 0.60), (-0.86, 0.44), (-0.12, 0.38))):                           # 등에서 자란 수정
        col = MT_CRY if k % 2 == 0 else MT_CRY2
        b.group(); b.cone((0.0, 1.10 + 0.10 * (k == 3), z), 0.12, 0.005, h, col, R=rot('x', -0.5 - 0.1 * k) @ rot('z', 0.15 * (k - 1.5)), sh=ONES * 0.9, spec=0.6, emis=True)
        b.cone((0.10 * (-1) ** k, 1.04, z), 0.08, 0.005, h * 0.7, col, R=rot('z', 0.5 * (-1) ** k) @ rot('x', -0.3), sh=ONES * 0.9, spec=0.6, emis=True)
    b.group(); b.E((0, 1.90, 0.20), (0.32, 0.30, 0.27), MT_BODY, sh=MT_SH, spec=0.3)                                    # 머리
    b.cone((0, 2.10, 0.16), 0.10, 0.005, 0.34, MT_CRY2, R=rot('x', -0.25), sh=ONES * 0.9, spec=0.6, emis=True)         # 이마 수정
    for s_ in (-1, 1): b.cone((s_ * 0.18, 2.06, 0.12), 0.06, 0.005, 0.22, MT_CRY, R=rot('z', -0.5 * s_), sh=ONES * 0.9, spec=0.6, emis=True)
    b.E((0, 1.78, 0.34), (0.16, 0.14, 0.12), MT_DARK, sh=MT_SH)                                                         # 턱
    for s_ in (-1, 1):
        b.group(); b.E((s_ * 0.22, 1.96, 0.30), (0.12, 0.17, 0.10), MT_CRY, sh=ONES * 0.9, spec=0.5, emis=True)         # 겹눈(수정)
        b.seg((s_ * 0.08, 2.08, 0.34), (s_ * 0.36, 2.52, 0.50), 0.025, 0.012, MT_DARK, n=1, sh=MT_SH)                   # 더듬이
        # 낫 팔: 위팔 -> 아래팔(가시)
        sh_pt = np.array([s_ * 0.20, 1.58, 0.06]); el = np.array([s_ * 0.34, 1.92, 0.62]); wr = np.array([s_ * 0.30, 1.10, 1.06])
        b.group(); b.seg(sh_pt, el, 0.10, 0.09, MT_BODY, n=1, sh=MT_SH); b.seg(el, wr, 0.095, 0.06, MT_BODY, n=2, sh=MT_SH, spec=0.3)
        for j in range(5):
            t = 0.2 + 0.16 * j; p = el + (wr - el) * t
            b.cone(p + np.array([-s_ * 0.04, 0, 0.04]), 0.035, 0.004, 0.17, MT_CRY, R=rot('x', 1.2) @ rot('z', -s_ * 0.3), sh=ONES * 0.9, spec=0.5)
        b.cone(wr, 0.05, 0.005, 0.30, MT_CRY, R=rot('x', math.pi) @ rot('z', s_ * 0.2), sh=ONES * 0.9, spec=0.5, emis=True)   # 끝 낫날
        for i in range(2):                                                                                              # 뒷다리 두 쌍
            ph = math.sin(step * math.pi + i * math.pi + (0 if s_ > 0 else math.pi)); z0 = -0.20 - 0.50 * i
            hip = np.array([s_ * 0.20, 1.02, z0]); knee = np.array([s_ * 0.66, 1.10 + 0.06 * ph, z0 - 0.06]); foot = np.array([s_ * 0.82, 0.05 + max(0, ph) * 0.10, z0 + 0.12])
            b.group(); b.seg(hip, knee, 0.095, 0.075, MT_BODY, n=1, sh=MT_SH, spec=0.3); b.seg(knee, foot, 0.075, 0.04, MT_DARK, n=2, sh=MT_SH); b.E(knee, (0.10, 0.10, 0.10), MT_CRY, sh=ONES * 0.9, spec=0.5, emis=True)
    b.group()
    eyes(b, expr, (0.10, 1.96, 0.40), 0.04, C(255, 255, 255))
    return b.P


EG_BODY, EG_LIGHT, EG_HEAD, EG_BEAK, EG_TIP = C(88, 80, 96), C(220, 212, 200), C(236, 232, 226), C(238, 190, 60), C(110, 190, 255)
EG_SH = np.array([0.52, 0.54, 0.74])


def build_eagle(expr='normal', mouth='open', flap=0.0, **kw):
    b = Builder()
    b.group(); b.E((0, 0.98, 0.0), (0.42, 0.50, 0.60), EG_BODY, rot('x', 0.25), sh=EG_SH)                                # 몸
    b.group(); b.E((0, 0.92, 0.22), (0.27, 0.34, 0.34), EG_LIGHT, rot('x', 0.25), sh=EG_SH)                              # 가슴
    b.group(); b.E((0, 1.46, 0.26), (0.24, 0.24, 0.26), EG_HEAD, sh=EG_SH)                                              # 머리
    b.E((0, 1.40, 0.28), (0.15, 0.12, 0.26), EG_HEAD, sh=EG_SH)
    for k in range(3): b.cone((0, 1.64, 0.14 - 0.06 * k), 0.07, 0.005, 0.26 - 0.03 * k, EG_BODY, R=rot('x', -0.7 - 0.25 * k), sh=EG_SH)   # 관모 깃
    b.group(); b.cone((0, 1.42, 0.46), 0.11, 0.012, 0.34, EG_BEAK, R=rot('x', math.pi / 2 + 0.35), sh=EG_SH, spec=0.4)    # 부리
    b.cone((0, 1.34, 0.50), 0.08, 0.01, 0.20, EG_BEAK, R=rot('x', math.pi / 2 + 0.75), sh=EG_SH, spec=0.4)
    for k in range(5):                                                                                                  # 꽁지깃 부채
        a = (k - 2) * 0.22
        b.group(); b.E((math.sin(a) * 0.5, 0.74, -0.74 - math.cos(a) * 0.1), (0.10, 0.03, 0.46), EG_BODY if k % 2 == 0 else EG_LIGHT, rot('y', a), sh=EG_SH)
        b.E((math.sin(a) * 0.82, 0.74, -1.10 - math.cos(a) * 0.06), (0.08, 0.03, 0.12), EG_TIP, rot('y', a), sh=ONES * 0.9, emis=True)
    a = 0.6 * flap
    for s_ in (-1, 1):
        sh_pt = np.array([s_ * 0.30, 1.14, 0.0]); tip = sh_pt + np.array([s_ * 1.30, 0.22 + 0.95 * a, -0.34])
        b.group(); b.seg(sh_pt, tip, 0.10, 0.04, EG_BODY, n=3, sh=EG_SH)
        for j in range(7):                                                                                              # 날개깃
            t = 0.12 + j * 0.13; p = sh_pt + (tip - sh_pt) * t
            L = 0.46 - 0.04 * j
            ang = math.atan2(tip[1] - sh_pt[1], abs(tip[0] - sh_pt[0]))
            col = EG_BODY if j < 5 else EG_BODY
            b.E(p + np.array([0, -0.04, -0.30 - 0.04 * j]), (0.17, 0.06, L), col, rot('z', -s_ * ang), sh=EG_SH)
            b.E(p + np.array([0, 0.03, -0.18 - 0.03 * j]), (0.15, 0.05, L * 0.7), C(116, 106, 124) if j % 2 == 0 else EG_BODY, rot('z', -s_ * ang), sh=EG_SH)       # 덧깃(두 겹)
            b.E(p + np.array([0, -0.04, -0.30 - 0.04 * j - L * 0.86]), (0.12, 0.025, 0.09), EG_TIP, rot('z', -s_ * ang), sh=ONES * 0.9, emis=True)
        b.group(); b.seg((s_ * 0.18, 0.66, 0.04), (s_ * 0.20, 0.28, 0.18), 0.06, 0.05, EG_BEAK, n=1, sh=EG_SH)          # 다리
        for j in (-1, 0, 1):
            b.cone((s_ * 0.20 + j * 0.07, 0.22, 0.18), 0.03, 0.004, 0.18, C(60, 56, 60), R=rot('x', 1.9), sh=EG_SH)    # 발톱
    b.group()
    eyes(b, expr, (0.13, 1.52, 0.40), 0.05, C(255, 210, 60))
    return b.P



def _flap_walk(k):
    t = k / float(WALK_N)
    return dict(flap=math.sin(2 * math.pi * t), lift=0.05 * math.sin(2 * math.pi * t))


def _step_walk(k):
    t = k / float(WALK_N)
    return dict(step=2.0 * math.sin(2 * math.pi * t), lift=0.02 * abs(math.sin(2 * math.pi * t)))


# ------------------------------------------------------------------ 자세 모음
def poses_for(kind):
    """kind: hop(통통 뛰기) / lunge(몸을 앞으로 내지르기) / flare(불꽃이 확 커짐) / rear(뱀이 머리를 젖혔다 내려침). 반환: 공격 5, 피격, 사망 3"""
    if kind == 'hop':
        atk = [dict(sq=0.76, sw=1.12, expr='glare', mouth='flat'), dict(sq=1.0, sw=0.98, lift=0.15, lean=0.12, expr='glare', mouth='open'),
               dict(sq=1.14, sw=0.90, lift=0.50, lean=0.30, expr='glare', mouth='open'), dict(sq=1.0, sw=1.02, lift=0.18, lean=0.36, expr='glare', mouth='open'),
               dict(sq=0.66, sw=1.20, lean=0.18, expr='normal', mouth='open')]
    elif kind == 'lunge':
        atk = [dict(pitch=-0.16, sq=0.94, expr='glare', mouth='flat'), dict(pitch=0.06, lean=0.10, expr='glare', mouth='open'),
               dict(pitch=0.30, lean=0.34, sq=0.97, expr='glare', mouth='open'), dict(pitch=0.40, lean=0.44, sq=0.95, expr='glare', mouth='open'),
               dict(pitch=0.10, lean=0.22, expr='normal', mouth='open')]
    elif kind == 'flare':
        atk = [dict(sq=0.85, sw=1.05, expr='glare', mouth='flat'), dict(sq=1.05, sw=1.12, expr='glare', mouth='open'),
               dict(sq=1.18, sw=1.30, lean=0.2, expr='glare', mouth='open'), dict(sq=1.10, sw=1.22, lean=0.3, expr='glare', mouth='open'),
               dict(sq=0.95, sw=1.08, expr='normal', mouth='open')]
    else:   # rear
        atk = [dict(pitch=-0.30, sq=1.03, expr='glare', mouth='open'), dict(pitch=-0.12, lean=-0.1, expr='glare', mouth='open'),
               dict(pitch=0.20, lean=0.2, expr='glare', mouth='open'), dict(pitch=0.44, lean=0.46, sq=0.95, expr='glare', mouth='open'),
               dict(pitch=0.20, lean=0.3, expr='normal', mouth='open')]
    if kind == 'hop':
        hit = dict(sq=0.84, sw=1.12, lean=-0.14, expr='hurt', mouth='open')
        death = [dict(sq=0.76, sw=1.10, lean=-0.1, expr='hurt', mouth='open'), dict(sq=0.42, sw=1.30, expr='hurt', mouth='open'), dict(sq=0.2, sw=1.55, expr='dead', mouth='flat')]
    elif kind == 'flare':
        hit = dict(sq=0.88, sw=1.0, lean=-0.1, expr='hurt', mouth='open')
        death = [dict(sq=0.80, sw=0.95, expr='hurt', mouth='open'), dict(sq=0.48, sw=0.82, expr='hurt', mouth='open'), dict(sq=0.14, sw=0.62, expr='dead', mouth='flat')]
    elif kind == 'rear':
        hit = dict(pitch=-0.25, lean=-0.1, expr='hurt', mouth='open')
        death = [dict(pitch=-0.35, sq=0.9, expr='hurt', mouth='open'), dict(pitch=0.15, sq=0.55, sw=1.15, expr='hurt', mouth='open'), dict(sq=0.24, sw=1.55, expr='dead', mouth='open')]
    else:
        hit = dict(pitch=-0.22, lean=-0.12, sq=0.96, expr='hurt', mouth='open')
        death = [dict(pitch=-0.30, sq=0.96, roll=0.35, expr='hurt', mouth='open'), dict(roll=1.0, expr='hurt', mouth='open'), dict(roll=1.52, expr='dead', mouth='flat')]
    return atk, hit, death


def _hop_walk(k):
    t = k / float(WALK_N); p = abs(math.sin(math.pi * t))
    return dict(lift=0.28 * p, sq=0.86 + 0.20 * p, sw=1.10 - 0.12 * p, lean=0.05 * p)


def _waddle_walk(k):
    t = k / float(WALK_N); p = abs(math.sin(math.pi * t))
    return dict(lift=0.08 * p, roll=0.10 * math.sin(2 * math.pi * t), sq=0.97 + 0.03 * p)


def _slither_walk(k):
    t = k / float(WALK_N)
    return dict(pitch=0.09 * math.sin(2 * math.pi * t), sq=1.0 + 0.02 * math.sin(4 * math.pi * t), lean=0.04 * math.sin(2 * math.pi * t))


def _scuttle_walk(k):
    t = k / float(WALK_N); p = abs(math.sin(math.pi * t))
    return dict(lift=0.06 * p, roll=0.05 * math.sin(2 * math.pi * t), sq=0.97 + 0.03 * p)


def _float_walk(k):
    t = k / float(WALK_N)
    return dict(lift=0.10 * math.sin(2 * math.pi * t), sq=1.0 + 0.05 * math.sin(2 * math.pi * t + 1), sw=1.0 - 0.03 * math.sin(2 * math.pi * t + 1), sway=math.sin(2 * math.pi * t))


WALKS = {'flap': lambda k: _flap_walk(k), 'step': lambda k: _step_walk(k), 'hop': _hop_walk, 'waddle': _waddle_walk, 'slither': _slither_walk, 'scuttle': _scuttle_walk, 'float': _float_walk}


def make_model(parts, kind, idle_amp=0.035, idle_sw=0.02, death_override=None, walk='hop'):
    atk, hit, death = poses_for(kind)
    if death_override: death = death_override

    def build(sq=1.0, sw=1.0, lift=0.0, lean=0.0, pitch=0.0, roll=0.0, **kw):
        return finish(parts(**kw), sq, sw, lift, lean, pitch, roll)

    def idle(k):
        t = k / IDLE_N; s = math.sin(2 * math.pi * t)
        return dict(sq=1.0 + idle_amp * s, sw=1.0 - idle_sw * s, expr='blink' if k == IDLE_BLINK else None)
    if walk == 'float':
        _i = idle
        idle = lambda k: dict(_i(k), sway=math.sin(2 * math.pi * k / IDLE_N))      # 불꽃은 서 있어도 혀가 일렁인다
    if walk == 'flap':
        _i = idle
        idle = lambda k: dict(_i(k), flap=math.sin(2 * math.pi * k / IDLE_N * 2.0))      # 박쥐는 서 있어도 날개를 친다
    return dict(build=build, atk=atk, hit=hit, death=death, idle=idle, walk=WALKS[walk])


def _slime_parts(**kw): return build_slime(**kw)


MODELS = {
    'slime': dict(build=build_slime, atk=SL_ATK, hit=SL_HIT, death=SL_DEATH, idle=slime_idle, walk=WALKS['hop']),
    'wolf': dict(build=build_wolf, atk=WF_ATK, hit=WF_HIT, death=WF_DEATH, idle=wolf_idle, walk=_wolf_walk),
    'goblin': make_model(build_goblin, 'lunge', walk='waddle'),
    'goblin_shaman': make_model(build_goblin_shaman, 'lunge', walk='waddle'),
    'goblin_warrior': make_model(build_goblin_warrior, 'lunge', walk='waddle'),
    'treant': make_model(build_treant, 'rear', idle_amp=0.02, walk='waddle'),
    'darkknight': make_model(build_darkknight, 'lunge', idle_amp=0.02, walk='waddle'),
    'ironknight': make_model(build_ironknight, 'lunge', idle_amp=0.02, walk='waddle'),
    'bat': make_model(build_bat, 'lunge', idle_amp=0.0, idle_sw=0.0, walk='flap'),
    'spider': make_model(build_spider, 'lunge', walk='step'),
    'skeleton': make_model(build_skeleton, 'lunge', idle_amp=0.015, walk='waddle'),
    'golem': make_model(build_golem, 'rear', idle_amp=0.015, walk='waddle'),
    'ghost': make_model(build_ghost, 'float' if False else 'flare', idle_amp=0.03, idle_sw=0.02, walk='float'),
    'scorpion': make_model(build_scorpion, 'lunge', walk='step'),
    'mushroom': make_model(build_mushroom, 'hop', walk='waddle'),
    'cactus': make_model(build_cactus, 'lunge', walk='waddle'),
    'bomb': make_model(build_bomb, 'hop', walk='waddle'),
    'flame': make_model(build_flame, 'flare', idle_amp=0.07, idle_sw=0.05, walk='float'),
    'worm': make_model(build_worm, 'rear', idle_amp=0.02, walk='slither'),
    'yeti': make_model(build_yeti, 'lunge', idle_amp=0.02, walk='waddle'),
    'lizard': make_model(build_lizard, 'lunge', walk='step'),
    'toad': make_model(build_toad, 'hop', walk='waddle'),
    'hermit': make_model(build_hermit, 'lunge', walk='scuttle'),
    'mantis': make_model(build_mantis, 'lunge', idle_amp=0.015, walk='step'),
    'eagle': make_model(build_eagle, 'lunge', idle_amp=0.0, idle_sw=0.0, walk='flap'),
    'crab': make_model(build_crab, 'lunge', walk='scuttle', death_override=[dict(pitch=-0.2, roll=0.5, expr='hurt', mouth='open'), dict(roll=1.9, expr='hurt', mouth='open'), dict(roll=3.1, expr='dead', mouth='flat')]),
}
# 이번에 3D 로 바꾸는 몬스터(늑대는 시험판만 있고 쓰지 않는다)
USE_3D = ['slime', 'mushroom', 'cactus', 'bomb', 'flame', 'worm', 'crab', 'wolf', 'goblin', 'goblin_shaman', 'goblin_warrior', 'treant', 'darkknight', 'ironknight', 'bat', 'spider', 'skeleton', 'golem', 'ghost', 'scorpion', 'yeti', 'lizard', 'toad', 'hermit', 'mantis', 'eagle']


ZOOM = {'yeti': 0.70, 'lizard': 0.86, 'toad': 0.98, 'hermit': 0.76, 'mantis': 0.72, 'eagle': 0.80, 'bat': 0.62, 'spider': 0.70, 'skeleton': 0.80, 'golem': 0.70, 'ghost': 0.80, 'scorpion': 0.66, 'wolf': 0.72, 'goblin': 0.95, 'goblin_shaman': 0.78, 'goblin_warrior': 0.82, 'treant': 0.74, 'darkknight': 0.72, 'ironknight': 0.80, 'cactus': 0.80, 'worm': 0.70, 'crab': 0.72, 'flame': 0.88, 'mushroom': 0.92, 'bomb': 0.92}      # 쓰러지거나 뛰어올라도 400x400 밖으로 나가지 않게 줄여 그린다


EXPORT_SIZE = 200          # 게임에 넣는 그림 한 변(렌더는 400 으로 하고 줄인다). 몬스터는 화면에서 100 픽셀 안팎이라 충분하고, 메모리가 4분의 1



# ------------------------------------------------------------------ 사실적 셰이딩(셀 셰이딩 대신): 부드러운 명암 + 틈 어둡힘 + 재질 무늬 + 금속 반사
from PIL import ImageFilter
REAL = set()        # 사실적으로 그릴 몬스터 이름(아래 MATERIAL 과 함께 쓴다)
MATERIAL = {}       # 이름 -> dict(freq, amp, streak(세로로 늘어난 결, 털/나무), spec, metal)


def _hash3(ix, iy, iz):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def _vnoise(p):
    i = np.floor(p).astype(np.int64); f = p - i; f = f * f * (3 - 2 * f)
    out = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * _hash3(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz)
    return out


def _fbm(p, octaves=3):
    tot, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        tot = tot + amp * _vnoise(p * (2 ** o)); norm += amp; amp *= 0.5
    return tot / norm


def shade_real(R, mask, theta, mat):
    depth, col, nrm, sh, spec, emis, gid = R['depth'], R['col'], R['nrm'], R['sh'], R['spec'], R['emis'], R['gid']
    M = depth.shape[0]
    ys, xs = np.nonzero(mask)
    t = depth[ys, xs]
    sx = (xs - M / 2 + 0.5) / H.PXU; sy = -(ys - H.N * H.FEET_Y + 0.5) / H.PXU
    P = sx[:, None] * H.r_ + sy[:, None] * H.u_ + (4.0 - t)[:, None] * H.w_
    Pm = P @ rot('y', theta)                                     # 모델 좌표(카메라가 돌아도 무늬가 몸에 붙어 있게)
    n = nrm[ys, xs]; c = col[ys, xs]; shd = sh[ys, xs]; sp = spec[ys, xs]; em = emis[ys, xs]
    L = H.LIGHT
    lam = np.clip((n @ L + 0.18) / 1.18, 0, 1)
    fill = np.clip(n @ np.array([0.7, 0.2, -0.3]), 0, 1) * 0.16
    hemi = 0.5 + 0.5 * n[:, 1]
    # 틈/겹침 어둡힘: 주변보다 안쪽(멀리)인 곳
    dz = np.where(mask, depth, np.nan)
    fill_v = float(np.nanmax(dz)) if np.isfinite(dz).any() else 0.0
    from scipy.ndimage import gaussian_filter
    blur = gaussian_filter(np.where(mask, depth, fill_v).astype(np.float32), H.SS * 6)[ys, xs]
    ao = 1.0 - np.clip((t - blur) * 3.2, 0, 0.55)
    # 재질 무늬
    fr, amp, sk = mat.get('freq', 5.0), mat.get('amp', 0.18), mat.get('streak', 0.0)
    q = Pm * fr
    if sk: q = q * np.array([1.0, 1.0 - sk * 0.7, 1.0])
    tx = _fbm(q, 3)
    fine = _fbm(Pm * fr * 6.0, 2)
    grain = 1.0 + amp * (tx - 0.5) * 2.0 + amp * 0.5 * (fine - 0.5) * 2.0
    base = c * grain[:, None]
    shadow = np.clip(base * shd * 0.78, 0, 1)
    litc = np.clip(base * 1.10 + 0.02, 0, 1)
    k = (lam ** 0.85)[:, None]
    rgb = shadow * (1 - k) + litc * k
    rgb = rgb * (0.62 + 0.38 * hemi)[:, None] * ao[:, None] + (fill[:, None] * np.array([0.55, 0.65, 1.0]))
    # 반사
    hv = H.LIGHT + H.w_; hv = hv / np.linalg.norm(hv)
    ndh = np.clip(n @ hv, 0, 1)
    metal = mat.get('metal', 0.0)
    power = 18 + sp * 90 + metal * 60
    spc = (ndh ** power) * (sp * 0.85 + metal * 0.9)
    rgb = rgb + spc[:, None]
    if metal:
        env = np.clip(0.35 + 0.65 * n[:, 1], 0, 1) * (0.55 + 0.45 * np.clip(n[:, 0] * 0.6 + 0.5, 0, 1))
        rgb = rgb * (1 - 0.35 * metal) + (c * 0.55 + 0.35) * env[:, None] * 0.55 * metal * (0.6 + 0.8 * lam)[:, None]
    rim = np.clip(1 - n @ H.w_, 0, 1) ** 3 * 0.14
    rgb = rgb + rim[:, None] * np.array([0.55, 0.65, 0.95])
    rgb = np.where(em[:, None], np.clip(c * 1.15, 0, 1), rgb)
    al = mat.get('alpha', 1.0)
    out = np.zeros((M, M, 4)); out[ys, xs, :3] = np.clip(rgb, 0, 1); out[ys, xs, 3] = al
    # 가는 바깥 윤곽(셀 만화풍의 굵은 선 대신 어두운 가장자리)
    w = max(1, int(round(H.SS * 0.8)))
    dil = mask.copy()
    for dy in range(-w, w + 1):
        for dx in range(-w, w + 1):
            if dx * dx + dy * dy <= w * w:
                sh_ = np.zeros_like(mask); ys0, ys1 = max(0, dy), M + min(0, dy); xs0, xs1 = max(0, dx), M + min(0, dx)
                sh_[ys0:ys1, xs0:xs1] = mask[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]; dil |= sh_
    outer = dil & ~mask
    oy, ox = np.nonzero(outer)
    out[oy, ox, :3] = np.array([26, 20, 24]) / 255.0 if al >= 1 else np.array([70, 90, 120]) / 255.0; out[oy, ox, 3] = al
    pm = out.copy(); pm[..., :3] *= pm[..., 3:4]
    pm = pm.reshape(M // H.SS, H.SS, M // H.SS, H.SS, 4).mean((1, 3))
    a = pm[..., 3:4]; rgbf = np.where(a > 1e-6, pm[..., :3] / np.maximum(a, 1e-6), 0)
    return Image.fromarray((np.clip(np.concatenate([rgbf, a], -1), 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA')


def draw(name, kw, ang):
    global _EYE_REAL
    _EYE_REAL = name in REAL
    H.PXU = 96 * H.SS * ZOOM.get(name, 1.0)
    kw = {k: v for k, v in kw.items() if v is not None}
    prims = MODELS[name]['build'](**kw)
    R = raster(prims, math.radians(ang))
    if name in REAL: return shade_real(R, np.isfinite(R['depth']), math.radians(ang), MATERIAL.get(name, {}))
    return shade(R, np.isfinite(R['depth']))


def export(assets, json_path, render=True):
    """USE_3D 몬스터를 Assets/<이름>/ 에 렌더해 넣고 sprites.json 의 해당 항목(보스판 포함)을 갱신한다."""
    import json
    from concurrent.futures import ProcessPoolExecutor
    raw = open(json_path, 'rb').read()
    bom, crlf = raw.startswith(b'\xef\xbb\xbf'), b'\r\n' in raw
    doc = json.loads(raw.decode('utf-8-sig'))
    if render:
        with ProcessPoolExecutor() as ex:
            list(ex.map(_export_one, [(n, os.path.join(assets, n)) for n in USE_3D]))
    for n in USE_3D:
        d = os.path.join(assets, n)
        m = MODELS[n]
        # 합친 상자: 서 있기(방향 4) 와 모든 자세
        def bbox(files):
            x0, y0, x1, y1 = 10 ** 9, 10 ** 9, -1, -1
            for f in files:
                bb = Image.open(f).convert('RGBA').getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox()
                if bb: x0, y0, x1, y1 = min(x0, bb[0]), min(y0, bb[1]), max(x1, bb[2] - 1), max(y1, bb[3] - 1)
            return [x0, y0, x1, y1]
        stand = bbox([f'{d}/{a:03d}.png' for a in (0, 90, 180, 270)])
        allf = [f'{d}/{f}' for f in os.listdir(d) if f.endswith('.png')]
        union = bbox(allf)
        foot = int(round(H.SZ * H.FEET_Y * EXPORT_SIZE / H.SZ))
        for e in doc['sprites']:
            if e.get('kind') != 'monster' or e['pattern'].split('/')[0] != n: continue
            # height = 서 있는 모습의 키(월드 단위). 예전 그림과 같은 크기로 보이게 그대로 둔다(뷰어가 bounds 로 모든 자세를 담는다).
            e['render'] = '3d'
            e['bounds'] = union; e['footPx'] = foot
            e['attackFrames'] = len(m['atk']); e['attackPattern'] = n + '/atk_{0:000}_{1}.png'
            e['walkFrames'] = WALK_N; e['walkPattern'] = n + '/walk_{0:000}_{1:00}.png'
            e['idleFrames'] = IDLE_N; e['idlePattern'] = n + '/idle_{0:000}_{1:00}.png'
            e['hitPattern'] = n + '/hit_{0:000}.png'
            e['deathFrames'] = len(m['death']); e['deathPattern'] = n + '/death_{0:000}_{1}.png'
        print(n, 'stand', stand, 'union', union, 'foot', foot)
    txt = json.dumps(doc, indent=2, ensure_ascii=False)
    if crlf: txt = txt.replace('\n', '\r\n')
    open(json_path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + txt.encode('utf-8'))


def draw_small(name, kw, ang):
    return draw(name, kw, ang).resize((EXPORT_SIZE, EXPORT_SIZE), Image.LANCZOS)


def _export_one(a):
    n, out = a
    os.makedirs(out, exist_ok=True)
    m = MODELS[n]
    for ang in (0, 90, 180, 270):
        draw_small(n, {}, ang).save(f'{out}/{ang:03d}.png')
        for i, kw in enumerate(m['atk']): draw_small(n, kw, ang).save(f'{out}/atk_{ang:03d}_{i}.png')
        draw_small(n, m['hit'], ang).save(f'{out}/hit_{ang:03d}.png')
        for i, kw in enumerate(m['death']): draw_small(n, kw, ang).save(f'{out}/death_{ang:03d}_{i}.png')
        for k in range(IDLE_N): draw_small(n, m['idle'](k), ang).save(f'{out}/idle_{ang:03d}_{k:02d}.png')
        for k in range(WALK_N): draw_small(n, m['walk'](k), ang).save(f'{out}/walk_{ang:03d}_{k:02d}.png')
    return n


def main():
    if sys.argv[1] == '--export':
        return export(sys.argv[2], sys.argv[3], '--json-only' not in sys.argv)
    name, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    m = MODELS[name]
    for ang in (0, 90, 180, 270):
        draw(name, {}, ang).save(f'{out}/{ang:03d}.png')
        for i, kw in enumerate(m['atk']): draw(name, kw, ang).save(f'{out}/atk_{ang:03d}_{i}.png')
        draw(name, m['hit'], ang).save(f'{out}/hit_{ang:03d}.png')
        for i, kw in enumerate(m['death']): draw(name, kw, ang).save(f'{out}/death_{ang:03d}_{i}.png')
        for k in range(IDLE_N): draw(name, m['idle'](k), ang).save(f'{out}/idle_{ang:03d}_{k:02d}.png')
        print(name, ang, 'done', flush=True)


REAL.update(['yeti', 'lizard', 'toad', 'hermit', 'mantis', 'eagle', 'mushroom', 'cactus', 'worm', 'crab', 'bat', 'spider', 'skeleton', 'golem', 'ghost', 'scorpion', 'wolf', 'goblin', 'goblin_shaman', 'goblin_warrior', 'treant', 'darkknight', 'ironknight'])
MATERIAL.update({
    'slime': dict(freq=3.0, amp=0.05, metal=0.0, alpha=0.9), 'mushroom': dict(freq=8.0, amp=0.20), 'cactus': dict(freq=9.0, amp=0.22, streak=0.8),
    'bomb': dict(freq=6.0, amp=0.10, metal=0.7), 'worm': dict(freq=7.0, amp=0.20, streak=0.3, metal=0.1), 'crab': dict(freq=8.0, amp=0.18, metal=0.3),
    'bat': dict(freq=10.0, amp=0.30, streak=0.6), 'spider': dict(freq=9.0, amp=0.22, metal=0.15), 'skeleton': dict(freq=8.0, amp=0.24),
    'golem': dict(freq=5.0, amp=0.42), 'ghost': dict(freq=4.0, amp=0.10, alpha=0.82), 'scorpion': dict(freq=8.0, amp=0.18, metal=0.35),
    'wolf': dict(freq=11.0, amp=0.34, streak=0.8, spec=0.1),
    'yeti': dict(freq=11.0, amp=0.36, streak=0.8), 'lizard': dict(freq=7.0, amp=0.32, metal=0.1), 'toad': dict(freq=9.0, amp=0.22, spec=0.2),
    'hermit': dict(freq=8.0, amp=0.18, metal=0.2), 'mantis': dict(freq=8.0, amp=0.16, metal=0.3), 'eagle': dict(freq=11.0, amp=0.30, streak=0.7),
    'goblin': dict(freq=9.0, amp=0.30), 'goblin_shaman': dict(freq=7.0, amp=0.20), 'goblin_warrior': dict(freq=7.0, amp=0.22),
    'treant': dict(freq=7.0, amp=0.42, streak=0.85),
    'darkknight': dict(freq=4.0, amp=0.06, metal=0.8), 'ironknight': dict(freq=4.0, amp=0.05, metal=1.0),
})


if __name__ == '__main__':
    main()

