#!/usr/bin/env python3
"""render_props.py : 맵 장식(바위/덤불/그루터기/수정/버섯/기둥 조각/갈대/꽃)을 3D 로 렌더해 Assets/props/*.png 로 낸다.
render_heroes.py 의 도형 렌더/셀 셰이딩을 쓴다. 그림은 알파 영역으로 잘라 저장하고, 발 밑 가운데가 그림 아래 가운데다.
사용:  python3 render_props.py <출력폴더>
"""
import math, os, sys
import numpy as np
from PIL import Image
import render_heroes as H
H.FEET_Y = 0.80
from render_heroes import Builder, C, rot, raster, shade
from render_monsters import ground

ONES = np.ones(3)
SHC = np.array([0.55, 0.58, 0.80])


def rnd(seed):
    r = np.random.RandomState(seed)
    return lambda a=0.0, b=1.0: a + (b - a) * r.rand()


def rock(seed, base, light, top=None, ember=False):
    f = rnd(seed); b = Builder()
    n = 3 + int(f(0, 2.99))
    for i in range(n):
        x, z = f(-0.5, 0.5) * (i > 0), f(-0.4, 0.4) * (i > 0)
        s = f(0.55, 1.0) * (1.0 if i == 0 else 0.62)
        b.group(); b.E((x, s * 0.55, z), (s, s * f(0.55, 0.8), s * f(0.8, 1.05)), base, rot('y', f(0, 3)), sh=SHC, spec=0.1)
        b.E((x - s * 0.15, s * 0.9, z + s * 0.05), (s * 0.55, s * 0.28, s * 0.5), light, rot('y', f(0, 3)), sh=SHC)
        if top is not None: b.E((x, s * 1.02, z), (s * 0.6, s * 0.16, s * 0.55), top, sh=SHC)
        if ember and i == 0:
            b.E((x + s * 0.2, s * 0.45, z + s * 0.9), (s * 0.18, s * 0.10, s * 0.06), C(255, 130, 40), emis=True)
    return ground(b.P)


def bush(seed, col, hi, berry=None):
    f = rnd(seed); b = Builder()
    for i in range(6):
        a = i / 6 * 6.28 + f(0, 0.5); r = 0.55 if i < 5 else 0.0
        s = f(0.42, 0.62)
        b.group(); b.E((math.cos(a) * r, s * 0.8 + f(0, 0.2), math.sin(a) * r), (s, s * 0.85, s), col, sh=SHC)
    b.group(); b.E((-0.15, 1.05, 0.1), (0.5, 0.3, 0.45), hi, sh=SHC)
    if berry is not None:
        for i in range(5):
            a = f(0, 6.28); b.E((math.cos(a) * 0.75, f(0.5, 0.9), math.sin(a) * 0.75), (0.1, 0.1, 0.1), berry, spec=0.5)
    return ground(b.P)


def stump(seed):
    f = rnd(seed); b = Builder()
    wood, bark, ring = C(120, 82, 52), C(88, 58, 38), C(214, 176, 120)
    b.group(); b.cone((0, 0, 0), 0.62, 0.50, 0.62, bark, sh=SHC)
    b.group(); b.E((0, 0.62, 0), (0.5, 0.07, 0.5), ring, sh=SHC)
    b.group(); b.E((0, 0.66, 0), (0.30, 0.04, 0.30), wood, sh=SHC)
    for i in range(3):
        a = i * 2.1 + f(0, 1); b.E((math.cos(a) * 0.6, 0.06, math.sin(a) * 0.6), (0.22, 0.14, 0.16), bark, rot('y', a), sh=SHC)
    return ground(b.P)


def crystal(seed, col, hi):
    f = rnd(seed); b = Builder()
    for i in range(5):
        a = f(0, 6.28); r = 0.0 if i == 0 else f(0.25, 0.6); h = f(1.1, 1.8) if i == 0 else f(0.5, 1.1)
        w = f(0.16, 0.26)
        b.group(); b.cone((math.cos(a) * r, 0, math.sin(a) * r), w, 0.01, h, col, sh=SHC, spec=0.6, emis=(i % 2 == 0))
    b.group(); b.E((0, 0.05, 0), (0.42, 0.07, 0.42), hi, sh=SHC)
    return ground(b.P)


def mushrooms(seed):
    f = rnd(seed); b = Builder()
    for i in range(4):
        x, z = f(-0.5, 0.5), f(-0.4, 0.4); s = f(0.5, 1.0)
        b.group(); b.cone((x, 0, z), 0.10 * s, 0.08 * s, 0.35 * s, C(238, 228, 206), sh=SHC)
        b.E((x, 0.38 * s, z), (0.30 * s, 0.17 * s, 0.30 * s), C(212, 70, 60) if i % 2 == 0 else C(224, 150, 70), sh=SHC, spec=0.3)
        b.E((x, 0.48 * s, z + 0.1 * s), (0.07 * s, 0.03 * s, 0.07 * s), C(255, 244, 230), sh=SHC)
    return ground(b.P)


def pillar(seed):
    f = rnd(seed); b = Builder()
    st, lt = C(170, 166, 160), C(206, 202, 194)
    b.group(); b.cone((0, 0, 0), 0.5, 0.42, 0.28, st, sh=SHC)
    b.group(); b.cone((0, 0.28, 0), 0.34, 0.30, f(0.9, 1.4), lt, sh=SHC)
    b.group(); b.E((0.85, 0.2, 0.2), (0.34, 0.22, 0.30), st, rot('z', 1.3), sh=SHC)
    b.group(); b.E((-0.7, 0.14, 0.5), (0.3, 0.16, 0.24), lt, rot('y', 0.6), sh=SHC)
    return ground(b.P)


def reeds(seed, col, tip):
    f = rnd(seed); b = Builder()
    for i in range(9):
        x, z = f(-0.5, 0.5), f(-0.3, 0.3); h = f(0.9, 1.6); lean = f(-0.25, 0.25)
        b.group(); b.seg((x, 0, z), (x + lean, h, z), 0.07, 0.025, col, n=4, sh=SHC)
        if i % 3 == 0: b.E((x + lean, h + 0.05, z), (0.07, 0.22, 0.07), tip, sh=SHC)
    return ground(b.P)


def flowers(seed):
    f = rnd(seed); b = Builder()
    cols = [C(250, 120, 160), C(255, 226, 90), C(190, 150, 250), C(255, 255, 255)]
    for i in range(7):
        x, z = f(-0.6, 0.6), f(-0.4, 0.4); h = f(0.3, 0.6)
        b.group(); b.seg((x, 0, z), (x, h, z), 0.025, 0.02, C(70, 150, 70), sh=SHC)
        c = cols[i % 4]
        for k in range(5):
            a = k * 1.256; b.E((x + math.cos(a) * 0.09, h + 0.02, z + math.sin(a) * 0.09), (0.07, 0.04, 0.07), c, sh=SHC)
        b.E((x, h + 0.04, z), (0.045, 0.04, 0.045), C(255, 210, 70), sh=SHC)
    return ground(b.P)


GREY, GREY_L, MOSS = C(124, 124, 132), C(168, 168, 176), C(86, 130, 70)
PROPS = {
    'rock_grey': lambda s: rock(s, GREY, GREY_L, MOSS),
    'rock_sand': lambda s: rock(s, C(190, 150, 100), C(226, 194, 140)),
    'rock_snow': lambda s: rock(s, C(120, 136, 156), C(180, 196, 214), C(246, 250, 255)),
    'rock_lava': lambda s: rock(s, C(60, 44, 46), C(104, 70, 60), None, True),
    'rock_dark': lambda s: rock(s, C(58, 54, 72), C(96, 90, 120)),
    'bush_green': lambda s: bush(s, C(62, 138, 62), C(120, 190, 88), C(220, 60, 70)),
    'bush_dry': lambda s: bush(s, C(150, 130, 70), C(204, 180, 110)),
    'bush_snow': lambda s: bush(s, C(70, 120, 96), C(210, 232, 240)),
    'bush_swamp': lambda s: bush(s, C(52, 96, 78), C(104, 150, 110)),
    'stump': stump,
    'crystal_blue': lambda s: crystal(s, C(110, 170, 255), C(70, 90, 160)),
    'crystal_purple': lambda s: crystal(s, C(190, 120, 255), C(100, 70, 160)),
    'mushrooms': mushrooms,
    'pillar': pillar,
    'reeds': lambda s: reeds(s, C(110, 150, 70), C(120, 80, 50)),
    'flowers': flowers,
}
VARIANTS = 3
ANGLES = (20, 110, 200)


def render(name, v):
    prims = PROPS[name](100 + v * 17 + sum(map(ord, name)))
    H.PXU = 96 * H.SS * 0.80
    R = raster(prims, math.radians(ANGLES[v % 3]))
    im = shade(R, np.isfinite(R['depth']))
    bb = im.getchannel('A').getbbox()
    im = im.crop(bb)
    s = min(1.0, 160.0 / max(im.size))
    return im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)


def main():
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    only = sys.argv[2:] or list(PROPS)
    for n in only:
        for v in range(VARIANTS):
            render(n, v).save(f'{out}/{n}_{v}.png')
        print(n, 'ok', flush=True)


if __name__ == '__main__':
    main()
