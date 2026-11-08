#!/usr/bin/env python3
"""render_heroes.py : 오리지널 영웅(전사/궁수/마법사) 렌더. 타원체+상자로 만든 스타일라이즈드 3D 인체를 셀 셰이딩+외곽선으로 그린다.

사용:  python3 render_heroes.py <warrior|archer|mage> <출력폴더> [--walk N] [각도 ...]
  정지 그림  : <출력폴더>/{각도:000}.png            (기본 0,20,...,340 = 18방향)
  네 영웅 모두 몸과 무기를 따로 낸다: 몸(무기 없음) {각도}.png / walk_*.png / atk_{각도}_{자세}.png,
    무기 {spear|sword|bow|staff}_{포즈}_{각도}_{b|f}.png (b = 몸 뒤에 가려지는 부분, f = 몸 앞으로 나오는 부분), 손/무기끝 앵커 anchors.json
    lancer 는 포즈 0 = 서 있는 창, 1..5 = 공격 자세. 전사/궁수/마법사는 공격 동작을 뷰어가 코드로 그리므로 포즈 0 = 대기/걷기 무기, 1 = 빈 그림(공격 중 숨김).
  걷기 그림  : <출력폴더>/walk_{각도:000}_{프레임:00}.png  (--walk N)
각도 0 = 카메라를 보고 서 있음, 커지면 화면 오른쪽 -> 위(뒷모습) -> 왼쪽. 400x400, 투명 배경.
모든 그림은 같은 기준점(발 밑 가운데 고정)에 그려지므로 돌 때 흔들리지 않는다.
의존: numpy, Pillow.  (전부 직접 만든 형태이며 외부 모델/텍스처를 쓰지 않는다)
"""
import math, os, sys
import numpy as np
from PIL import Image

SS = 2
SZ = 400
N = SZ * SS
PXU = 96 * SS
FEET_Y = 0.92                      # 발 밑이 화면 아래에서 몇 % 지점인지
ELEV = math.radians(28)
r_ = np.array([1, 0, 0.])
u_ = np.array([0, math.cos(ELEV), -math.sin(ELEV)])
w_ = np.array([0, math.sin(ELEV), math.cos(ELEV)])
LIGHT = np.array([-0.45, 0.8, 0.55]); LIGHT /= np.linalg.norm(LIGHT)
OUTLINE = np.array([34, 22, 30]) / 255.


def rot(ax, ang):
    c, s = math.cos(ang), math.sin(ang)
    if ax == 'x': return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
    if ax == 'y': return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def frame_y(d):
    """로컬 y 축이 d 방향인 회전."""
    y = np.asarray(d, float); y = y / np.linalg.norm(y)
    t = np.array([1., 0, 0]) if abs(y[0]) < 0.9 else np.array([0, 0, 1.])
    x = np.cross(t, y); x /= np.linalg.norm(x)
    z = np.cross(x, y)
    return np.stack([x, y, z], 1)


def C(r, g, b): return np.array([r, g, b], float) / 255.


# 그림자 색조(곱): 피부는 따뜻하게, 나머지는 푸르스름하게
SH_DEF = np.array([0.52, 0.55, 0.76])
SH_SKIN = np.array([0.80, 0.58, 0.60])
SKIN = C(255, 218, 196)
SKIN_L = C(255, 190, 178)


class Prim:
    __slots__ = ('c', 'a', 'R', 'col', 'gid', 'sh', 'spec', 'emis', 'box', 'cone', 'k', 'part')

    def __init__(s, c, a, R, col, gid, sh=SH_DEF, spec=0.0, emis=False, box=False, cone=False, k=1.0):
        s.c = np.asarray(c, float); s.a = np.asarray(a, float); s.R = np.eye(3) if R is None else R
        s.col = col; s.gid = gid; s.sh = sh; s.spec = spec; s.emis = emis; s.box = box; s.cone = cone; s.k = k; s.part = 0


class Builder:
    def __init__(self): self.P = []; self.gid = 0; self.part = 0; self.anchors = {}      # part 0 = 몸, 1 = 무기(몸과 따로 그린다)

    def group(self): self.gid += 1; return self.gid

    def E(self, c, a, col, R=None, gid=None, **kw):
        self.P.append(Prim(c, a, R, col, self.gid if gid is None else gid, **kw)); self.P[-1].part = self.part

    def B(self, c, h, col, R=None, gid=None, **kw):
        self.P.append(Prim(c, h, R, col, self.gid if gid is None else gid, box=True, **kw)); self.P[-1].part = self.part

    def cone(self, c, r0, r1, H, col, k=1.0, R=None, gid=None, **kw):
        """밑면 중심 c, 로컬 y 방향으로 높이 H. 밑 반지름 r0 -> 윗 반지름 r1, z 방향은 k 배(납작한 단면). 치마/로브/모자/긴 머리용."""
        self.P.append(Prim(c, (r0, r1, H), R, col, self.gid if gid is None else gid, cone=True, k=k, **kw)); self.P[-1].part = self.part

    def seg(self, p0, p1, r0, r1, col, n=1, rr=None, gid=None, **kw):
        """p0 -> p1 를 잇는 가는 타원체 (n 조각으로 굵기가 바뀜)."""
        p0 = np.asarray(p0, float); p1 = np.asarray(p1, float)
        d = p1 - p0; L = np.linalg.norm(d)
        if L < 1e-6: return
        R = frame_y(d)
        for k in range(n):
            t = (k + 0.5) / n
            c = p0 + d * t
            r = r0 + (r1 - r0) * t
            self.E(c, (r, L / n / 2 * 1.35 + r * 0.35, r if rr is None else rr), col, R, gid, **kw)


# ----------------------------------------------------------------------------- 인체

def lerp(a, b, t): return a + (b - a) * t


WALK_STEP = 1.0        # 딛은 발이 땅 위를 뒤로 가는 길이(렌더 단위). 뷰어는 프레임 한 장당 WALK_STEP*PXU/(STANCE*프레임 수) 원본 픽셀씩 걷는다
WALK_ARM = 0.50        # 팔 흔들림
WALK_SHIFT = 0.045     # 체중 이동: 몸통 위쪽이 좌우로 움직이는 거리


def weight_shift(y, shift):
    """높이 y 에 있는 부분이 체중 이동으로 옮겨지는 x 거리. 발 근처는 거의 안 움직이고 몸통 위쪽은 전부 움직인다."""
    return shift * min(1.0, max(0.0, (y - 0.1) / 1.2))


LEG1, LEG2 = 0.66, 0.68          # 허벅지, 정강이
GROUND = 0.11                    # 발목이 바닥에 닿을 때의 높이 (서 있을 때 엉덩이 1.45 - 다리 1.34)
STANCE = 0.6                     # 한 걸음 주기 중 발이 땅에 붙어 있는 비율
LEG_BEND = 1.30                  # 걸을 때 딛은 다리가 몸 아래에서 갖는 길이 (조금 굽힌 채)
LIFT = 0.22                      # 발을 내딛을 때 땅에서 드는 높이
HEEL = 0.10                      # 딛은 발의 뒤꿈치가 떨어지는 높이


def _leg_t(ph, side):
    """다리 한쪽의 걸음 주기 위치 0..1. 0 = 발뒤꿈치가 땅에 닿는 순간. 왼발은 ph=0 에서, 오른발은 반 주기 뒤에 닿는다."""
    t = (ph / (2 * math.pi) + (0.0 if side > 0 else 0.5)) % 1.0
    return t


def _foot_target(t):
    """발목 목표 (앞뒤 z, 높이 y). 딛는 동안 땅 위를 일정한 속도로 뒤로 가고(미끄러지지 않는다), 내딛는 동안 들려 앞으로 간다."""
    if t < STANCE:
        s = t / STANCE
        k = min(1.0, max(0.0, (s - 0.7) / 0.3))
        return WALK_STEP * (0.5 - s), GROUND + HEEL * k * k * (3 - 2 * k)
    u = (t - STANCE) / (1.0 - STANCE)
    f = u - 0.5 * math.sin(2 * math.pi * u) / (2 * math.pi)       # 끝에서 느리게
    return WALK_STEP * (-0.5 + f), GROUND + HEEL * (1 - u) * (1 - u) + LIFT * math.sin(math.pi * u)


def pelvis_height(ph):
    """딛은 발이 모두 바닥에 닿는 엉덩이 높이. 두 발이 벌어진 순간이 가장 낮다."""
    h = 0.0
    for side in (1, -1):
        t = _leg_t(ph, side)
        if t < STANCE:
            z, y = _foot_target(t)
            h = max(h, y + math.sqrt(LEG_BEND * LEG_BEND - z * z))
    return h


def _ik(hip_y, z, y):
    """엉덩이 (0, hip_y) 에서 발목 (z, y) 로 가는 두 관절 다리. 무릎은 앞으로 굽는다. 반환: (knee_z, knee_y, 정강이 각)."""
    dz, dy = z, hip_y - y
    d = min(math.hypot(dz, dy), (LEG1 + LEG2) * 0.999)
    a = math.atan2(dz, dy)
    cb = max(-1.0, min(1.0, (LEG1 * LEG1 + d * d - LEG2 * LEG2) / (2 * LEG1 * d)))
    th = a + math.acos(cb)
    kz, ky = LEG1 * math.sin(th), hip_y - LEG1 * math.cos(th)
    phi = math.atan2(z - kz, ky - y)
    return kz, ky, phi


def limb_pose(ph, side, moving, amp=None):
    """다리 한쪽. side=+1(왼쪽, x+) / -1(오른쪽). 반환: (hip, knee, ankle, foot_tilt). 걸을 때는 엉덩이 높이 변화(bob)를 뺀 좌표를 돌려준다(호출한 쪽이 bob 을 더한다)."""
    hip = np.array([0.13 * side, 1.45, 0.0])      # 이후 몸통 이하는 UP 만큼 올려 다리가 길어 보이게 한다
    if not moving:
        knee = hip + np.array([0, -LEG1, 0.0])
        return hip, knee, knee + np.array([0, -LEG2, 0.0]), 0.0
    h = pelvis_height(ph)
    z, y = _foot_target(_leg_t(ph, side))
    kz, ky, phi = _ik(h, z, y)
    bob = h - 1.45
    knee = np.array([hip[0], ky - bob, kz])
    ankle = np.array([hip[0], y - bob, z])
    return hip, knee, ankle, phi


def arm_pose(ph, side, moving, amp, abd=0.12, bend=0.35, fixed=None):
    """팔 한쪽. fixed=(upper_angle_x, bend, abd) 이면 고정 자세."""
    sh = np.array([0.30 * side, 2.02, 0.0])
    if fixed is not None:
        th, bd, ab = fixed
    else:
        th = (-amp * math.cos(ph - 0.3 + (0 if side > 0 else math.pi))) if moving else 0.0   # 같은 쪽 다리와 반대로 흔든다
        bd, ab = bend + (0.6 * max(0.0, th) if moving else 0), abd
    L1, L2 = 0.42, 0.40
    dir1 = np.array([side * math.sin(ab), -math.cos(ab) * math.cos(th), math.cos(ab) * math.sin(th)])
    elbow = sh + L1 * dir1 / np.linalg.norm(dir1)
    th2 = th + bd
    dir2 = np.array([side * math.sin(ab) * 0.6, -math.cos(th2), math.sin(th2)])
    hand = elbow + L2 * dir2 / np.linalg.norm(dir2)
    return sh, elbow, hand


def head(b, hair, eye, eyec2=None, tilt=0.0):
    """얼굴(피부, 눈, 입). 중심 (0,2.52,0)."""
    b.group()
    hc = np.array([0, 2.52, 0.0])
    b.E(hc, (0.285, 0.30, 0.285), SKIN, sh=SH_SKIN)
    b.E(hc + [0, -0.12, 0.07], (0.215, 0.20, 0.21), SKIN, sh=SH_SKIN)          # 턱
    b.E(hc + [0, -0.24, 0.0], (0.07, 0.11, 0.07), SKIN, sh=SH_SKIN)             # 목
    for s in (-1, 1):
        b.E(hc + [s * 0.285, -0.02, -0.02], (0.04, 0.07, 0.05), SKIN, sh=SH_SKIN)  # 귀
        # 눈: 흰자 -> 홍채 -> 동공 -> 하이라이트
        ex, ey, ez = s * 0.122, 2.48, 0.232
        b.E((ex, ey, ez), (0.077, 0.100, 0.036), C(250, 248, 250), sh=np.ones(3) * 0.9)
        b.E((ex, ey - 0.005, ez + 0.012), (0.063, 0.088, 0.031), eye, sh=np.ones(3) * 0.8)
        b.E((ex, ey - 0.012, ez + 0.026), (0.034, 0.056, 0.021), C(40, 20, 34), sh=np.ones(3) * 0.8)
        b.E((ex - s * 0.022, ey + 0.035, ez + 0.037), (0.019, 0.022, 0.013), C(255, 255, 255), emis=True)
        b.E((ex, ey + 0.088, ez - 0.004), (0.082, 0.018, 0.03), hair * 0.55, sh=np.ones(3) * 0.8)     # 눈썹/속눈썹 선
        b.E((s * 0.19, 2.42, 0.2), (0.045, 0.025, 0.02), C(255, 160, 160), sh=np.ones(3) * 0.95)     # 볼터치
    b.E((0, 2.395, 0.262), (0.028, 0.009, 0.012), C(190, 80, 90), sh=np.ones(3) * 0.8)                # 입
    b.E((0, 2.44, 0.285), (0.012, 0.018, 0.012), SKIN_L, sh=SH_SKIN)                                   # 코


def hair_cap(b, hair, hair_l, front=True):
    b.group()
    hc = np.array([0, 2.52, 0.0])
    b.E(hc + [0, 0.045, -0.035], (0.325, 0.33, 0.33), hair, spec=0.28)
    if front:
        for s in (-1, 0, 1):          # 앞머리 가닥
            b.E(hc + [s * 0.15, 0.17, 0.2], (0.13, 0.085, 0.11), hair, rot('z', -s * 0.5), spec=0.25)
        b.E(hc + [0, 0.20, 0.2], (0.11, 0.07, 0.10), hair_l, spec=0.6)
        for s in (-1, 1):             # 옆머리
            b.E(hc + [s * 0.285, -0.12, 0.07], (0.065, 0.24, 0.085), hair, spec=0.5)


def build(name, ph, moving, pose=None):
    """pose (공격 자세): {'arm': 무기 든 손 (위팔 각, 굽힘, 벌림), 'pitch': 무기가 위에서 앞으로 숙은 각도(도), 'free': 빈 손 팔, 'draw_arm': 궁수 시위 당기는 팔, 'draw': 시위를 당긴 정도 0..1}. 없으면 서 있는 자세."""
    b = Builder()
    amp = 0.45
    sway = math.sin(ph) if moving else 0.0
    # 걷기: 땅을 딛은 발이 늘 바닥에 닿도록 엉덩이 높이를 정한다. 두 발이 벌어진 순간(접지)이 가장 낮고 다리가 몸 아래를 지날 때 가장 높다.
    # 체중은 딛은 발 쪽으로 몸통 위쪽을 조금 옮긴다 (WALK_SHIFT).
    bob, shift = 0.0, 0.0
    if moving:
        bob = pelvis_height(ph) - 1.45
        shift = WALK_SHIFT * math.cos(ph - 0.6 * math.pi)       # 왼발을 딛는 중간(ph = 0.3 주기)에 왼쪽으로 가장 쏠린다

    # ---- 캐릭터별 팔레트/설정
    if name == 'warrior':
        hair, hair_l = C(206, 70, 40), C(255, 130, 70)
        cloth, cloth2, trim, metal = C(196, 44, 52), C(52, 38, 44), C(236, 176, 70), C(180, 186, 200)
        boot = C(96, 62, 50); eye = C(70, 170, 130)
    elif name == 'archer':
        hair, hair_l = C(238, 220, 168), C(255, 246, 210)
        cloth, cloth2, trim, metal = C(70, 150, 84), C(236, 228, 196), C(206, 160, 70), C(170, 150, 120)
        boot = C(112, 78, 54); eye = C(80, 190, 230)
    elif name == 'rogue':
        hair, hair_l = C(104, 80, 172), C(176, 150, 236)
        cloth, cloth2, trim, metal = C(62, 56, 90), C(150, 70, 190), C(70, 210, 190), C(206, 214, 230)
        boot = C(40, 36, 56); eye = C(236, 190, 60)
    elif name == 'lancer':
        hair, hair_l = C(238, 200, 98), C(255, 236, 160)
        cloth, cloth2, trim, metal = C(184, 192, 206), C(36, 124, 134), C(226, 188, 88), C(206, 214, 228)
        boot = C(120, 130, 148); eye = C(80, 150, 230)
    else:
        hair, hair_l = C(190, 186, 238), C(236, 232, 255)
        cloth, cloth2, trim, metal = C(76, 92, 214), C(40, 40, 110), C(250, 214, 110), C(200, 205, 225)
        boot = C(52, 48, 100); eye = C(170, 80, 220)

    pants = C(52, 46, 74) if name == 'rogue' else (C(74, 88, 104) if name == 'lancer' else None)
    # ---- 다리 (맨 안쪽부터)
    b.group()
    legs = {}
    for s in (1, -1):
        hip, knee, ankle, phi = limb_pose(ph, s, moving)
        hip = hip + [0, bob, 0]; knee = knee + [0, bob, 0]; ankle = ankle + [0, bob, 0]
        legs[s] = (hip, knee, ankle, phi)
        b.seg(hip, knee, 0.118, 0.092, SKIN if pants is None else pants, n=3, sh=SH_SKIN if pants is None else SH_DEF)                      # 허벅지
        long_boot = name in ('warrior', 'archer', 'mage')
        boot_col = boot
        b.seg(knee + (ankle - knee) * 0.02, ankle, 0.085, 0.075, boot_col, n=2, spec=0.3)   # 정강이 부츠
        b.E(knee, (0.095, 0.075, 0.095), boot_col, spec=0.3)                      # 무릎 덮개
        fwd = np.array([0, 0, 1.0])
        b.E(ankle + [0, -0.04, 0.085], (0.075, 0.062, 0.15), boot_col * 0.9, rot('x', -phi * 0.6), spec=0.3)  # 발
        b.E(ankle + [0, -0.075, 0.12], (0.062, 0.03, 0.14), cloth2 * 0.7, rot('x', -phi * 0.6))             # 밑창
        b.E(knee + (ankle - knee) * 0.25, (0.095, 0.05, 0.095), trim, frame_y(ankle - knee))               # 부츠 단 장식

    nlegs = len(b.P)
    # ---- 몸통
    b.group()
    b.E((0, 1.36 + bob, 0), (0.25, 0.17, 0.19), cloth2)                           # 골반/속옷
    b.E((0, 1.62 + bob, 0), (0.19, 0.22, 0.14), SKIN, sh=SH_SKIN)                 # 허리
    b.E((0, 1.93 + bob, 0.005), (0.235, 0.20, 0.17), cloth if name != 'archer' else cloth, spec=0.1)   # 가슴 장식
    b.E((0, 1.95 + bob, 0.115), (0.19, 0.115, 0.085), cloth * 1.02)
    b.E((0, 2.15 + bob, 0.0), (0.17, 0.08, 0.12), SKIN, sh=SH_SKIN)               # 어깨선
    b.E((0, 1.50 + bob, 0.0), (0.265, 0.05, 0.205), cloth2 * 0.9, spec=0.2)       # 벨트
    b.E((0, 1.50 + bob, 0.20), (0.06, 0.06, 0.03), trim, spec=0.8)                # 버클

    # 치마/로브 (걸을 때 다리 흔들림에 따라 앞뒤로 살짝)
    if name == 'mage':
        b.cone((0, 0.36 + bob, 0), 0.56, 0.25, 1.10, cloth, k=0.72, spec=0.1)
        b.E((0, 0.36 + bob, 0.0), (0.56, 0.035, 0.40), trim, spec=0.5)             # 단 장식
        b.E((0, 0.88 + bob, 0.285), (0.06, 0.45, 0.05), cloth2)                    # 앞 패널
        b.E((0, 0.88 + bob, 0.295), (0.015, 0.45, 0.03), trim, spec=0.8)
    elif name == 'archer':
        b.cone((0, 1.08 + bob, 0), 0.385, 0.26, 0.40, cloth, k=0.85, spec=0.1)
        b.E((0, 1.07 + bob, 0.0), (0.385, 0.05, 0.325), cloth2, spec=0.1)          # 치맛단
        b.E((0, 1.20 + bob, 0.23), (0.10, 0.19, 0.06), trim, spec=0.3)
    elif name == 'rogue':
        b.cone((0, 1.18 + bob, 0), 0.34, 0.26, 0.30, cloth, k=0.85, spec=0.1)              # 짧은 상의 자락
        for s in (-1, 1):
            b.E((s * 0.16, 1.16 + bob, 0.2), (0.08, 0.16, 0.04), cloth2, rot('z', s * 0.12))
        b.E((0, 1.46 + bob, 0.0), (0.27, 0.03, 0.21), trim, rot('x', 0.15), spec=0.6)       # 교차 벨트 줄
    else:
        b.cone((0, 1.09 + bob, 0), 0.37, 0.26, 0.38, cloth, k=0.85, spec=0.1)
        b.E((0, 1.09 + bob, 0.0), (0.37, 0.045, 0.31), cloth2, spec=0.2)
        for s in (-1, 1):
            b.E((s * 0.22, 1.13 + bob, 0.18), (0.09, 0.17, 0.05), cloth2 * 1.3, rot('z', s * 0.2))

    # ---- 팔
    farm = {'warrior': (-0.5, 0.6, 0.16), 'archer': None, 'mage': (-0.2, 0.7, 0.14)}
    har = None                                                                      # 궁수: 시위를 당기는 오른손 위치 (활 시위가 따라온다)
    if name == 'archer' and pose:
        har = arm_pose(ph, -1, moving, WALK_ARM, fixed=pose['draw_arm'])[2] + [0, bob, 0]
    for s in (1, -1):
        fixed = None
        if name in ('warrior', 'lancer') and s == -1: fixed = pose['arm'] if pose else (-0.35, 0.55, 0.20)           # 오른손: 검 / 창
        if name == 'warrior' and s == 1 and pose: fixed = pose['free']
        if name == 'archer' and s == 1: fixed = pose['arm'] if pose else (0.85, 0.40, 0.30)              # 왼손: 활을 앞으로
        if name == 'archer' and s == -1 and pose: fixed = pose['draw_arm']
        if name == 'mage' and s == -1: fixed = pose['arm'] if pose else (-0.20, 0.9, 0.30)               # 오른손: 지팡이
        if name == 'mage' and s == 1 and pose: fixed = pose['free']
        if name == 'rogue': fixed = (0.45 + (0.28 * math.sin(ph + (0 if s > 0 else math.pi)) if moving else 0.0), 0.85, 0.28)   # 양손 단검
        sh_, el, ha = arm_pose(ph, s, moving, WALK_ARM, fixed=fixed)
        sh_ = sh_ + [0, bob, 0]; el = el + [0, bob, 0]; ha = ha + [0, bob, 0]
        sleeve = cloth2 if name not in ('archer', 'rogue') else cloth
        b.seg(sh_, el, 0.072, 0.060, SKIN, n=2, sh=SH_SKIN)
        b.seg(el, ha, 0.060, 0.048, sleeve if name in ('mage', 'rogue') else SKIN, n=2, sh=SH_SKIN if name not in ('mage', 'rogue') else SH_DEF)
        if name in ('warrior', 'lancer'): b.seg(el + (ha - el) * 0.25, ha, 0.062, 0.058, metal, n=1, spec=0.7)    # 팔목 보호대
        if name == 'mage': b.E(ha + (el - ha) * 0.3, (0.1, 0.12, 0.1), cloth, frame_y(ha - el), spec=0.1)  # 넓은 소매
        b.E(ha, (0.060, 0.060, 0.060), SKIN, sh=SH_SKIN)
        # 어깨
        if name == 'warrior':
            b.E(sh_ + [s * 0.04, 0.03, 0], (0.15, 0.10, 0.15), cloth2, spec=0.6)
            b.E(sh_ + [s * 0.07, 0.07, 0], (0.12, 0.05, 0.12), metal, spec=0.8)
            b.seg(sh_ + [s * 0.1, 0.08, 0], sh_ + [s * 0.2, 0.28, 0], 0.035, 0.005, metal, spec=0.9)     # 어깨 가시
            b.seg(sh_ + [s * 0.12, 0.05, 0.1], sh_ + [s * 0.26, 0.12, 0.2], 0.03, 0.004, metal, spec=0.9)
        elif name == 'lancer':
            b.E(sh_ + [s * 0.04, 0.03, 0], (0.15, 0.10, 0.15), cloth, spec=0.8)             # 은빛 견갑
            b.E(sh_ + [s * 0.07, 0.07, 0], (0.12, 0.05, 0.12), trim, spec=0.8)
        elif name == 'archer':
            b.E(sh_ + [s * 0.03, 0.02, 0], (0.12, 0.08, 0.12), cloth * 0.95, spec=0.1)
        elif name == 'rogue':
            b.E(sh_ + [s * 0.03, 0.02, 0], (0.12, 0.085, 0.12), cloth, spec=0.2)
            b.E(sh_ + [s * 0.04, 0.06, 0], (0.10, 0.03, 0.10), trim, spec=0.7)
        else:
            b.E(sh_ + [s * 0.04, 0.04, 0], (0.14, 0.09, 0.14), cloth2, spec=0.3)
            b.E(sh_ + [s * 0.06, 0.07, 0.0], (0.11, 0.035, 0.11), trim, spec=0.8)

        # ---- 무기
        if name == 'warrior' and s == -1:
            if pose:
                pa = math.radians(pose['pitch']); d = np.array([-0.30, math.cos(pa), math.sin(pa)])
            else:
                d = np.array([-0.45, -0.86, 0.18])
            d /= np.linalg.norm(d)
            R = frame_y(d)
            gid = b.group()
            b.part = 1                                                                     # 검은 몸과 따로 그린다
            b.E(ha - d * 0.18, (0.04, 0.2, 0.04), C(70, 40, 34), R)                       # 손잡이
            b.E(ha - d * 0.38, (0.065, 0.065, 0.065), trim, R, spec=0.8)                  # 폼멜
            b.B(ha + d * 0.05, (0.2, 0.035, 0.05), trim, R, spec=0.8)                     # 가드
            b.B(ha + d * 0.12 + d * 0.56, (0.100, 0.56, 0.028), C(214, 222, 236), R, spec=1.0)   # 칼날
            b.B(ha + d * 0.12 + d * 0.56, (0.032, 0.54, 0.034), C(140, 150, 176), R, spec=1.0)   # 피[홈]
            b.E(ha + d * 1.24, (0.095, 0.1, 0.028), C(214, 222, 236), R, spec=1.0)        # 칼끝
            b.part = 0
            up = np.array([0, 0.15, 0.0])           # 몸통 이하는 UP 만큼 올리는 보정과 같다
            b.anchors = {'grip': ha + up, 'tip': ha + d * 1.34 + up, 'butt': ha - d * 0.45 + up}
        if name == 'lancer' and s == -1:
            phi = math.radians(pose['pitch']) if pose else 0.0
            d = np.array([-0.06 * math.cos(phi) + 0.10 * math.sin(phi), math.cos(phi), 0.10 * math.cos(phi) + math.sin(phi)]); d /= np.linalg.norm(d)
            R = frame_y(d)
            b.part = 1
            b.group()
            b.seg(ha - d * 1.0, ha + d * 1.75, 0.032, 0.032, C(120, 84, 60), n=3, spec=0.6)     # 자루
            b.E(ha + d * 0.05, (0.05, 0.05, 0.05), trim, spec=0.9)                                # 손잡이 장식
            b.E(ha + d * 1.78, (0.055, 0.04, 0.055), trim, R, spec=0.9)                           # 창날 받침
            b.cone(ha + d * 1.80, 0.06, 0.0, 0.50, C(226, 234, 246), k=0.45, R=R, spec=1.0)       # 창날
            b.E(ha + d * 1.80 + np.array([0, 0, 0.05]), (0.012, 0.2, 0.012), C(150, 160, 186), R, spec=1.0)
            b.E(ha - d * 1.0, (0.045, 0.06, 0.045), trim, R, spec=0.8)                            # 석돌
            b.part = 0
            up = np.array([0, 0.15, 0.0])           # 아래 "몸통 이하는 UP 만큼 올린다" 와 같은 보정
            b.anchors = {'grip': ha + up, 'tip': ha + d * 2.30 + up, 'butt': ha - d * 1.0 + up}
        if name == 'rogue':
            d = np.array([s * 0.32, -0.30, 0.90]); d /= np.linalg.norm(d)
            R = frame_y(d)
            b.group()
            b.E(ha - d * 0.07, (0.032, 0.09, 0.032), C(60, 40, 50), R)                      # 손잡이
            b.B(ha + d * 0.05, (0.09, 0.02, 0.04), trim, R, spec=0.8)                       # 가드
            b.B(ha + d * 0.34, (0.050, 0.28, 0.016), C(222, 230, 244), R, spec=1.0)         # 칼날
            b.E(ha + d * 0.64, (0.045, 0.07, 0.016), C(222, 230, 244), R, spec=1.0)         # 칼끝
        if name == 'archer' and s == 1:
            d = np.array([0.0, 1.0, 0.0])
            b.group()
            b.part = 1                                                                     # 활은 몸과 따로 그린다
            # 활: 손을 중심으로 위아래로 휘어진 호 (앞으로 볼록)
            K = 12
            for k in range(K + 1):
                t = (k / K) * 2 - 1
                y = ha[1] + t * 0.9
                z = ha[2]
                x = ha[0] + 0.05 + (1 - t * t) * 0.30
                nxt = ha[1] + ((k + 1) / K * 2 - 1) * 0.9
                dz = 0.0
                b.E((x, y, z), (0.045, 0.095, 0.04), C(150, 100, 60) if abs(t) > 0.12 else C(60, 38, 30), frame_y([2 * t * 0.30 * -1 * (2 / K) * -1, 0.18, 0]), spec=0.6)
            mid = np.array([ha[0] + 0.05, ha[1], ha[2]])
            draw = pose.get('draw', 0.0) if pose else 0.0
            nock = mid + (har - mid) * draw if draw > 0 else mid                      # 시위는 당기는 손까지 따라온다
            b.seg(np.array([ha[0] + 0.05, ha[1] + 0.9, ha[2]]), nock, 0.009, 0.009, C(240, 238, 226), n=2, emis=True)    # 시위
            b.seg(nock, np.array([ha[0] + 0.05, ha[1] - 0.9, ha[2]]), 0.009, 0.009, C(240, 238, 226), n=2, emis=True)
            if draw > 0.5:                                                           # 화살을 메긴 채
                tipA = mid + np.array([0.0, 0.0, 0.95]); dirA = (tipA - nock) / np.linalg.norm(tipA - nock)
                b.seg(nock, tipA, 0.016, 0.016, C(214, 200, 170), n=2, spec=0.3)
                b.cone(tipA, 0.04, 0.0, 0.16, C(220, 228, 240), k=0.45, R=frame_y(dirA), spec=1.0)
                for sgn in (-1, 1): b.E(nock + dirA * 0.06 + np.array([0, sgn * 0.03, 0]), (0.03, 0.05, 0.012), C(120, 200, 120), spec=0.1)
            b.E((ha[0] + 0.05, ha[1] + 0.92, ha[2]), (0.04, 0.05, 0.04), trim, spec=0.8)
            b.E((ha[0] + 0.05, ha[1] - 0.92, ha[2]), (0.04, 0.05, 0.04), trim, spec=0.8)
            b.part = 0
            up = np.array([0, 0.15, 0.0])
            b.anchors = {'grip': ha + up, 'tip': ha + np.array([0.05, 0.97, 0.0]) + up, 'butt': ha + np.array([0.05, -0.97, 0.0]) + up}
        if name == 'mage' and s == -1:
            b.group()
            b.part = 1                                                                     # 지팡이는 몸과 따로 그린다
            if pose:
                pa = math.radians(pose['pitch']); dm = np.array([-0.11, math.cos(pa), math.sin(pa)]); dm /= np.linalg.norm(dm)
                top = ha + dm * 1.41; bot = ha - dm * 1.2
            else:
                top = ha + np.array([-0.16, 1.40, 0.04]); bot = ha - np.array([-0.10, 1.2, 0.04])
            b.seg(bot, top, 0.032, 0.032, C(120, 84, 60), n=3, spec=0.6)
            b.E(top, (0.075, 0.075, 0.075), trim, spec=0.9)
            for k in range(3):
                a = k * 2.1
                b.seg(top, top + [math.cos(a) * 0.14, 0.2, math.sin(a) * 0.14], 0.03, 0.01, trim, spec=0.9)
            b.E(top + [0, 0.19, 0], (0.075, 0.12, 0.075), C(140, 230, 255), emis=True)         # 수정
            b.E(top + [0, 0.19, 0], (0.05, 0.2, 0.05), C(200, 250, 255), rot('z', 0.5), emis=True)
            b.part = 0
            up = np.array([0, 0.15, 0.0])
            b.anchors = {'grip': ha + up, 'tip': top + np.array([0, 0.35, 0]) + up, 'butt': bot + up}

    # ---- 머리와 머리카락
    hc = np.array([0, bob, 0])
    n0 = len(b.P)
    head(b, hair, eye)
    hair_cap(b, hair, hair_l)
    for p in b.P[n0:]: p.c = p.c + hc
    b.group()
    sw = sway * 0.06
    if name == 'warrior':
        # 높이 묶은 포니테일 (움직일 때 흔들림)
        base = np.array([0, 2.74 + bob, -0.2])
        b.E(base + [0, 0.02, 0.03], (0.09, 0.08, 0.09), trim, spec=0.9)
        for k in range(15):
            t = k / 14.0
            c = base + np.array([sw * t * 2.0, 0.12 - 0.95 * t ** 1.3 + 0.18 * math.sin(t * 3.0), -0.12 - 0.42 * math.sin(t * 2.2) - 0.05 * t])
            r = 0.115 * (1 - 0.6 * t)
            b.E(c, (r, 0.11, r * 0.95), hair if k % 2 == 0 else hair * 0.94, rot('x', 0.7 * (1 - t)), spec=0.6)
        b.E((0, 2.62 + bob, -0.3), (0.2, 0.22, 0.12), hair)                       # 뒷머리 볼륨
    elif name == 'rogue':
        # 후드 아래로 삐져나온 짧은 단발 + 뒤로 날리는 스카프 꼬리
        b.E((0, 2.30 + bob, -0.2), (0.30, 0.20, 0.17), hair, spec=0.25)
        for s in (-1, 1):
            b.E((s * 0.27, 2.28 + bob, 0.04), (0.06, 0.16, 0.08), hair, spec=0.25)
    else:
        # 긴 생머리: 등 뒤로 늘어지는 사슬
        b.cone((sw * 1.6, 1.50 + bob, -0.27), 0.12, 0.30, 1.12, hair, k=0.72, R=rot('x', 0.06), spec=0.28)
        b.E(np.array([sw * 1.6, 1.50 + bob, -0.31]), (0.10, 0.09, 0.07), hair, spec=0.28)       # 머리끝
        for s in (-1, 1):                                                          # 앞으로 내려온 옆머리
            b.E((s * 0.26, 2.20 + bob, 0.1), (0.055, 0.24, 0.07), hair, spec=0.6)
    if name == 'archer':
        b.group()
        # 후드가 달린 망토 (등 뒤로 펄럭)
        for k in range(5):
            t = k / 4.0
            b.E((0, 2.0 - 0.75 * t + bob, -0.2 - 0.06 * t - sway * 0.04 * t), (0.27 + 0.1 * t, 0.2, 0.05), cloth * (0.9 - 0.1 * t), rot('x', 0.15 - 0.15 * t))
        b.E((0, 2.2 + bob, -0.12), (0.3, 0.1, 0.12), cloth * 0.9)                  # 어깨 망토
        # 화살통
        R = rot('z', 0.35)
        b.E((0.16, 1.95 + bob, -0.3), (0.075, 0.33, 0.075), C(120, 80, 54), R, spec=0.3)
        for k in range(3):
            b.seg((0.17 + k * 0.03, 2.25 + bob, -0.3 + k * 0.02), (0.2 + k * 0.03, 2.5 + bob, -0.3 + k * 0.02), 0.012, 0.012, C(210, 200, 180))
            b.E((0.2 + k * 0.03, 2.52 + bob, -0.3 + k * 0.02), (0.03, 0.05, 0.012), cloth, spec=0.1)
        b.E((0, 1.28 + bob, 0.0), (0.04, 0.4, 0.04), C(0, 0, 0), None) if False else None
        # 나뭇잎 머리장식
        b.group()
        for s in (-1, 1):
            b.E((s * 0.21, 2.74 + bob, 0.15), (0.05, 0.12, 0.025), C(110, 200, 110), rot('z', s * 0.9), spec=0.4)
    if name == 'lancer':
        b.group()
        # 청록색 너덜너덜한 망토 (등 뒤, 아랫단이 갈라짐)
        for k in range(6):
            t = k / 5.0
            b.E((0, 2.05 - 0.80 * t + bob, -0.22 - 0.08 * t - sway * 0.05 * t), (0.27 + 0.10 * t, 0.2, 0.05), cloth2 * (0.95 - 0.15 * t), rot('x', 0.15 - 0.2 * t))
        for j, x in enumerate((-0.3, -0.12, 0.08, 0.28)):
            L = 0.28 + 0.1 * (j % 2)
            b.E((x, 1.12 - L / 2 + bob, -0.46 - sway * 0.05), (0.07, L, 0.04), cloth2 * 0.78, rot('x', -0.2))
        b.E((0, 2.2 + bob, -0.12), (0.3, 0.1, 0.12), cloth2 * 0.9)
        b.E((-0.3, 2.14 + bob, 0.02), (0.1, 0.1, 0.08), cloth2)
        # 서클릿
        b.E((0, 2.72 + bob, 0.1), (0.30, 0.025, 0.27), trim, rot('x', 0.15), spec=0.9)
        b.E((0, 2.74 + bob, 0.3), (0.035, 0.05, 0.02), C(90, 200, 220), emis=True)
        # 흉갑 중앙 보석
        b.E((0, 1.98 + bob, 0.19), (0.05, 0.06, 0.02), C(90, 200, 220), emis=True)
    if name == 'mage':
        b.group()
        hat = C(52, 66, 170)
        b.E((0, 2.78 + bob, 0.0), (0.50, 0.04, 0.47), hat, rot('x', 0.1), spec=0.2)          # 챙
        b.E((0, 2.78 + bob, 0.0), (0.36, 0.05, 0.34), trim, rot('x', 0.1), spec=0.8)           # 띠
        b.cone((0, 2.82 + bob, -0.01), 0.31, 0.03, 0.62, hat, k=0.95, R=rot('x', -0.32), spec=0.2)
        b.E((0, 3.40 + bob, -0.21), (0.04, 0.04, 0.04), trim, spec=0.9)                      # 끝 방울
        b.E((0, 2.9 + bob, 0.21), (0.06, 0.06, 0.03), C(255, 220, 120), emis=True)             # 모자 보석
        b.E((0, 1.30 + bob, -0.24 - 0.04 * sway), (0.3, 0.7, 0.05), cloth2 * 0.9, rot('x', 0.1))   # 짧은 망토
    if name == 'rogue':
        b.group()
        hood = C(58, 50, 92)
        b.E((0, 2.62 + bob, -0.19), (0.37, 0.36, 0.30), hood, spec=0.15)                     # 후드 (얼굴 앞은 비워 둔다)
        b.cone((0, 2.78 + bob, -0.16), 0.24, 0.02, 0.36, hood, k=0.9, R=rot('x', -0.6), spec=0.15)   # 후드 끝 뾰족하게 뒤로
        b.E((0, 2.93 + bob, -0.02), (0.30, 0.05, 0.27), hood * 1.1, rot('x', 0.3))             # 이마 위 후드 가장자리
        for s in (-1, 1):
            b.E((s * 0.33, 2.55 + bob, -0.04), (0.05, 0.22, 0.15), hood * 0.9)                  # 옆 가장자리
        # 스카프(목) + 뒤로 날리는 꼬리
        b.E((0, 2.20 + bob, 0.04), (0.18, 0.07, 0.17), trim, spec=0.4)
        for k in range(6):
            t = k / 5.0
            b.E((0.1 + sw * t * 2, 2.15 + bob - 0.12 * t, -0.2 - 0.16 * t - 0.03 * math.sin(ph * 2 + t * 4) * (1 if moving else 0)),
                (0.07, 0.06, 0.07 + 0.03 * t), trim * (1.0 - 0.1 * t), spec=0.3)
        b.E((0, 1.55 + bob, -0.24 - 0.04 * sway), (0.27, 0.45, 0.05), hood * 0.9, rot('x', 0.15))   # 짧은 망토
    if shift:
        for p in b.P: p.c = p.c + np.array([weight_shift(p.c[1], shift), 0.0, 0.0])
        b.anchors = {k: v + np.array([weight_shift(v[1], shift), 0.0, 0.0]) for k, v in b.anchors.items()}
    for p in b.P[nlegs:]: p.c = p.c + np.array([0, 0.15, 0.0])
    return b.P, b.anchors


# ----------------------------------------------------------------------------- 렌더

def raster(prims, theta, pad=0):
    """도형들을 깊이 버퍼로 찍는다. 반환: 깊이/색/법선/그늘색/반사/발광/그룹 버퍼."""
    Ry = rot('y', theta)
    M = N + 2 * pad * SS            # 캔버스 한 변(바깥으로 pad 그림 픽셀씩 여유: 길게 뻗는 무기가 잘리지 않게)
    depth = np.full((M, M), np.inf)
    col = np.zeros((M, M, 3)); nrm = np.zeros((M, M, 3)); sh = np.tile(SH_DEF, (M, M, 1))
    spec = np.zeros((M, M)); emis = np.zeros((M, M), bool); gid = np.zeros((M, M), np.int32)
    D = -w_
    for p in prims:
        cw = Ry @ p.c; Rw = Ry @ p.R
        R_b = float(np.linalg.norm(p.a)) if p.box else (float(max(p.a[0], p.a[1]) * max(1.0, p.k) + p.a[2]) if p.cone else float(p.a.max()))
        px = M / 2 + (cw @ r_) * PXU
        py = pad * SS + N * FEET_Y - (cw @ u_) * PXU
        rb = R_b * PXU + 3
        x0, x1 = int(max(0, px - rb)), int(min(M, px + rb + 1))
        y0, y1 = int(max(0, py - rb)), int(min(M, py + rb + 1))
        if x0 >= x1 or y0 >= y1: continue
        xs, ys = np.meshgrid(np.arange(x0, x1), np.arange(y0, y1))
        sx = (xs - M / 2 + 0.5) / PXU
        sy = -(ys - pad * SS - N * FEET_Y + 0.5) / PXU
        O = sx[..., None] * r_ + sy[..., None] * u_ + 4.0 * w_
        if p.cone:
            r0, r1, H = p.a; k = p.k; mm = (r1 - r0) / H
            q0 = (O - cw) @ Rw
            qd = Rw.T @ D
            q0 = q0.copy(); q0[..., 2] /= k; qd = qd.copy(); qd[2] /= k
            qx, qy, qz = q0[..., 0], q0[..., 1], q0[..., 2]
            rr0 = r0 + mm * qy
            A = qd[0] ** 2 + qd[2] ** 2 - (mm * qd[1]) ** 2
            B = 2 * (qx * qd[0] + qz * qd[2] - mm * qd[1] * rr0)
            Cc = qx * qx + qz * qz - rr0 * rr0
            disc = B * B - 4 * A * Cc
            ok = disc > 0
            sq = np.sqrt(np.where(ok, disc, 0))
            Ad = A if abs(A) > 1e-9 else 1e-9
            ta = (-B - sq) / (2 * Ad); tb = (-B + sq) / (2 * Ad)
            t1_ = np.minimum(ta, tb); t2_ = np.maximum(ta, tb)
            y1_ = qy + t1_ * qd[1]; y2_ = qy + t2_ * qd[1]
            v1 = ok & (y1_ >= 0) & (y1_ <= H) & (t1_ > 0)
            v2 = ok & ~v1 & (y2_ >= 0) & (y2_ <= H) & (t2_ > 0)
            hit = v1 | v2
            t = np.where(v1, t1_, np.where(v2, t2_, np.inf))
            tf_ = np.where(hit, t, 0.0)
            hx = qx + tf_ * qd[0]; hy = qy + tf_ * qd[1]; hz = qz + tf_ * qd[2]
            rh = r0 + mm * hy
            nl = np.stack([hx, -rh * mm, hz / k], -1)
            nl = nl / (np.linalg.norm(nl, axis=-1, keepdims=True) + 1e-9)
            nw = nl @ Rw.T
        elif p.box:
            q0 = (O - cw) @ Rw          # = Rw.T (O-c)
            qd = Rw.T @ D
            with np.errstate(divide='ignore', invalid='ignore'):
                t1 = (-p.a - q0) / qd; t2 = (p.a - q0) / qd
            tn = np.minimum(t1, t2); tf = np.maximum(t1, t2)
            tmin = tn.max(-1); tmax = tf.min(-1)
            hit = (tmax >= tmin) & (tmax > 0)
            t = np.where(hit, tmin, np.inf)
            ax = tn.argmax(-1)
            nl = np.zeros(q0.shape)
            sgn = -np.sign(qd)
            for i in range(3):
                nl[..., i] = np.where(ax == i, sgn[i], 0)
            nw = nl @ Rw.T
        else:
            inv = Rw.T / p.a[:, None]
            q0 = (O - cw) @ inv.T
            qd = inv @ D
            A = qd @ qd; B = 2 * (q0 @ qd); Cc = (q0 * q0).sum(-1) - 1
            disc = B * B - 4 * A * Cc
            hit = disc > 0
            t = np.where(hit, (-B - np.sqrt(np.where(hit, disc, 0))) / (2 * A), np.inf)
            q = q0 + np.where(hit, t, 0.0)[..., None] * qd
            nw = (q / p.a) @ Rw.T
            nw = nw / (np.linalg.norm(nw, axis=-1, keepdims=True) + 1e-9)
        sl = (slice(y0, y1), slice(x0, x1))
        m = hit & (t < depth[sl])
        if not m.any(): continue
        d_ = depth[sl]; d_[m] = t[m]
        for arr, val in ((col, p.col), (sh, p.sh)):
            a_ = arr[sl]; a_[m] = val
        n_ = nrm[sl]; n_[m] = nw[m]
        s_ = spec[sl]; s_[m] = p.spec
        e_ = emis[sl]; e_[m] = p.emis
        g_ = gid[sl]; g_[m] = p.gid
    return {'depth': depth, 'col': col, 'nrm': nrm, 'sh': sh, 'spec': spec, 'emis': emis, 'gid': gid}


def shade(R, mask):
    """raster 결과 중 mask 에 해당하는 픽셀을 셀 셰이딩 + 외곽선으로 칠해 RGBA 그림으로 만든다."""
    depth, col, nrm, sh, spec, emis, gid = R['depth'], R['col'], R['nrm'], R['sh'], R['spec'], R['emis'], R['gid']
    n = nrm
    M = depth.shape[0]
    lam = np.clip((n * LIGHT).sum(-1), 0, 1)
    hemi = 0.5 + 0.5 * n[..., 1]
    tt = lam * 0.72 + hemi * 0.28
    def sstep(a, b, x):
        z = np.clip((x - a) / (b - a), 0, 1); return z * z * (3 - 2 * z)
    lo, hi = sstep(0.30, 0.38, tt), sstep(0.62, 0.70, tt)
    shadow = col * sh
    mid = col * np.array([0.90, 0.90, 0.95])
    lit = np.clip(col * 1.05 + 0.03, 0, 1)
    rgb = shadow * (1 - lo)[..., None] + mid * (lo * (1 - hi))[..., None] + lit * hi[..., None]
    # 반사광(머리카락/금속): 좁은 띠
    hv = LIGHT + w_; hv /= np.linalg.norm(hv)
    sp = np.clip((n * hv).sum(-1), 0, 1)
    rgb = rgb + (sstep(0.90, 0.93, sp) * spec * 0.55)[..., None]
    # 림 라이트
    rim = np.clip(1 - (n * w_).sum(-1), 0, 1) ** 3 * 0.16
    rgb = rgb + rim[..., None] * np.array([0.55, 0.65, 1.0])
    rgb = np.where(emis[..., None], np.clip(col * 1.1, 0, 1), rgb)
    rgb = np.clip(rgb, 0, 1)

    # 외곽선: 바깥 윤곽(두께 w) + 안쪽 경계(부위가 다르고 깊이가 크게 차이날 때)
    w = int(round(SS * 1.3))
    def shift(a, dy, dx, fill):
        out = np.full_like(a, fill)
        ys0, ys1 = max(0, dy), M + min(0, dy); xs0, xs1 = max(0, dx), M + min(0, dx)
        out[ys0:ys1, xs0:xs1] = a[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
        return out
    dil = mask.copy()
    for dy in range(-w, w + 1):
        for dx in range(-w, w + 1):
            if dx * dx + dy * dy <= w * w: dil |= shift(mask, dy, dx, False)
    outer = dil & ~mask
    dfin = np.where(mask, depth, 0.0)
    inner = np.zeros((M, M), bool)
    for dy, dx in ((1, 0), (0, 1), (-1, 0), (0, -1)):
        gn = shift(gid, dy, dx, 0); dn = shift(dfin, dy, dx, 0.0); mn = shift(mask, dy, dx, False)
        inner |= mask & mn & (gn != gid) & (np.abs(dn - dfin) > 0.035)
    inner_d = inner.copy()
    for dy, dx in ((1, 0), (0, 1), (-1, 0), (0, -1)): inner_d |= shift(inner, dy, dx, False) & mask
    rgb = np.where(inner_d[..., None], rgb * 0.55 + OUTLINE * 0.45 * 0.6, rgb)
    out = np.zeros((M, M, 4)); out[..., :3] = np.where(outer[..., None], OUTLINE, rgb)
    out[..., 3] = (mask | outer).astype(float)
    pm = out.copy(); pm[..., :3] *= pm[..., 3:4]
    pm = pm.reshape(M // SS, SS, M // SS, SS, 4).mean((1, 3))
    a = pm[..., 3:4]; rgbf = np.where(a > 1e-6, pm[..., :3] / np.maximum(a, 1e-6), 0)
    img = np.concatenate([rgbf, a], -1)
    return Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA')


def project(p3, theta):
    """3D 점을 그림(SZ x SZ) 안 좌표로."""
    cw = rot('y', theta) @ np.asarray(p3, float)
    return [round(float(N / 2 + (cw @ r_) * PXU) / SS, 2), round(float(N * FEET_Y - (cw @ u_) * PXU) / SS, 2)]


def to_image(img):
    return Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA')


def render(name, theta, ph, moving, pose=None):
    """몸만(무기 제외) 그린 그림과 무기 앵커."""
    prims, anchors = build(name, ph, moving, pose)
    R = raster([p for p in prims if p.part == 0], theta)
    return shade(R, np.isfinite(R['depth'])), {k: project(v, theta) for k, v in anchors.items()}


WEAPON_PAD = 160       # 창 그림은 몸 그림보다 사방으로 이만큼(그림 픽셀) 크다. 같은 기준점에서 pad 만큼 안쪽으로 겹치면 몸과 맞는다


def render_weapon(name, theta, ph, moving, pose=None):
    """무기만 그린 그림 둘: (몸 뒤에 가려지는 부분, 몸 앞으로 나오는 부분). 몸과 깊이를 비교해 가른다."""
    prims, _ = build(name, ph, moving, pose)
    Rb = raster([p for p in prims if p.part == 0], theta, WEAPON_PAD)
    Rw = raster([p for p in prims if p.part == 1], theta, WEAPON_PAD)
    mw = np.isfinite(Rw['depth'])
    front = mw & (Rw['depth'] < Rb['depth'])
    return shade(Rw, mw & ~front), shade(Rw, front)


# 공격 자세: 팔과 무기가 함께 움직이는 5장. 뷰어는 내지름 정도(HeroWeapons.Swing)에 맞춰 0번(모은 자세)에서 4번(다 내지른 자세)까지 고른다.
# 팔은 (위팔 각, 굽힘, 벌림): 위팔 각 0 = 아래, + 는 앞으로 들어 올림. pitch = 무기가 위에서 앞으로 숙은 각도(도): 0 = 위, 90 = 앞, 180 = 아래.
ATK = {
    'lancer': [      # 뒤로 당김 -> 내지름 -> 쭉 뻗음 -> 유지 -> 거두는 중
        {'arm': (-0.80, 1.00, 0.18), 'pitch': 74.0},
        {'arm': (0.30, 0.75, 0.18), 'pitch': 84.0},
        {'arm': (1.35, 0.12, 0.10), 'pitch': 90.0},
        {'arm': (1.28, 0.16, 0.10), 'pitch': 89.0},
        {'arm': (0.45, 0.65, 0.18), 'pitch': 58.0},
    ],
    'warrior': [     # 머리 위로 젖힘 -> 내려치는 중 -> 앞으로 베어 내림
        {'arm': (2.60, 0.90, 0.22), 'pitch': -25.0, 'free': (0.50, 0.50, 0.45)},
        {'arm': (2.20, 0.55, 0.20), 'pitch': 30.0, 'free': (0.30, 0.45, 0.50)},
        {'arm': (1.60, 0.35, 0.18), 'pitch': 85.0, 'free': (-0.10, 0.40, 0.55)},
        {'arm': (1.15, 0.22, 0.16), 'pitch': 125.0, 'free': (-0.40, 0.40, 0.60)},
        {'arm': (0.85, 0.15, 0.16), 'pitch': 150.0, 'free': (-0.60, 0.40, 0.62)},
    ],
    'archer': [      # 시위를 끝까지 당김 -> 놓는 중 -> 놓은 뒤
        {'arm': (1.40, 0.10, 0.10), 'draw_arm': (1.55, 2.30, 0.14), 'draw': 1.0},
        {'arm': (1.40, 0.10, 0.10), 'draw_arm': (1.30, 1.90, 0.18), 'draw': 0.45},
        {'arm': (1.38, 0.12, 0.10), 'draw_arm': (0.00, 1.00, 0.24), 'draw': 0.12},
        {'arm': (1.34, 0.15, 0.12), 'draw_arm': (-0.35, 0.70, 0.28), 'draw': 0.0},
        {'arm': (1.20, 0.22, 0.16), 'draw_arm': (-0.45, 0.55, 0.30), 'draw': 0.0},
    ],
    'mage': [        # 지팡이를 곧게 모음 -> 앞으로 들어 올림 -> 내지름
        {'arm': (-0.20, 0.90, 0.30), 'pitch': -4.0, 'free': (0.35, 0.60, 0.35)},
        {'arm': (0.20, 0.80, 0.26), 'pitch': 10.0, 'free': (0.70, 0.55, 0.35)},
        {'arm': (0.70, 0.55, 0.22), 'pitch': 28.0, 'free': (1.05, 0.45, 0.35)},
        {'arm': (1.10, 0.35, 0.20), 'pitch': 45.0, 'free': (1.30, 0.40, 0.35)},
        {'arm': (1.30, 0.25, 0.18), 'pitch': 55.0, 'free': (1.45, 0.35, 0.35)},
    ],
}


def _save(img, path): img.save(path)


WEAPON_FILE = {'lancer': 'spear', 'warrior': 'sword', 'archer': 'bow', 'mage': 'staff'}


def _job(args):
    name, out, ang, k, nwalk = args
    th = math.radians(ang)
    res = {'ang': ang}
    if name in ATK:
        # 몸(무기 없음) 서기/걷기/공격 5장, 무기(뒤/앞 두 겹) 서기 + 공격 5자세, 손/무기끝 앵커
        wf = WEAPON_FILE[name]
        if k is None:
            img, an = render(name, th, 0.0, False); _save(img, f'{out}/{ang:03d}.png')
            res['idle'] = an
            for pi in range(len(ATK[name]) + 1):
                pose = None if pi == 0 else ATK[name][pi - 1]
                if pi > 0:
                    bi, an2 = render(name, th, 0.0, False, pose); _save(bi, f'{out}/atk_{ang:03d}_{pi - 1}.png')
                else:
                    an2 = an
                back, front = render_weapon(name, th, 0.0, False, pose)
                _save(back, f'{out}/{wf}_{pi}_{ang:03d}_b.png'); _save(front, f'{out}/{wf}_{pi}_{ang:03d}_f.png')
                res.setdefault('pose', {})[pi] = an2
        else:
            ph = 2 * math.pi * k / nwalk
            img, an = render(name, th, ph, True); _save(img, f'{out}/walk_{ang:03d}_{k:02d}.png')
            res['walk'] = (k, an)
        return res
    if k is None:
        img, an = render(name, th, 0.0, False)
        _save(img, f'{out}/{ang:03d}.png')
    else:
        ph = 2 * math.pi * k / nwalk
        img, an = render(name, th, ph, True)
        _save(img, f'{out}/walk_{ang:03d}_{k:02d}.png')
    return res


def write_anchors(out, results, nwalk, name='lancer'):
    """창술사 앵커(그림 좌표, 400x400 기준)를 anchors.json 으로. pose 0 = 서 있는 창, 1.. = 공격 자세."""
    import json
    angs = sorted({r['ang'] for r in results})
    npose = len(ATK[name]) + 1
    data = {'angles': len(angs), 'poses': npose, 'walkFrames': nwalk, 'grip': [], 'tip': [], 'butt': [], 'walkGrip': []}
    by = {r['ang']: r for r in results if 'pose' in r}
    walk = {}
    for r in results:
        if 'walk' in r: walk.setdefault(r['ang'], {})[r['walk'][0]] = r['walk'][1]['grip']
    for key in ('grip', 'tip', 'butt'):
        data[key] = [[by[a]['pose'][pi][key] for a in angs] for pi in range(npose)]
    data['walkGrip'] = [[walk[a][k] for k in range(nwalk)] for a in angs] if nwalk else []
    with open(f'{out}/anchors.json', 'w') as fp: json.dump(data, fp, separators=(',', ':'))


if __name__ == '__main__':
    import multiprocessing as mp
    name, out = sys.argv[1], sys.argv[2]
    rest = sys.argv[3:]
    nwalk = 0
    if '--walk' in rest:
        i = rest.index('--walk'); nwalk = int(rest[i + 1]); del rest[i:i + 2]
    os.makedirs(out, exist_ok=True)
    angs = [int(a) for a in rest] or list(range(0, 360, 20))
    jobs = [(name, out, a, None, 0) for a in angs]
    jobs += [(name, out, a, k, nwalk) for a in angs for k in range(nwalk)]
    results = []
    with mp.Pool(max(1, mp.cpu_count())) as pool:
        for i, r in enumerate(pool.imap_unordered(_job, jobs)):
            results.append(r)
            if i % 20 == 0: print(f'{i}/{len(jobs)}', flush=True)
    if name in WEAPON_FILE: write_anchors(out, results, nwalk, name)
