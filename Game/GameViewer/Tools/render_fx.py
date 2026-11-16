#!/usr/bin/env python3
"""render_fx.py : 스킬 이펙트에 쓰는 3D 렌더 스프라이트(화염구 핵 8프레임, 화살). Assets/fx/*.png
render_heroes.py 의 도형 렌더를 쓴다. 사용: python3 render_fx.py <출력폴더>
"""
import math, os, sys
import numpy as np
from PIL import Image
import render_heroes as H
H.FEET_Y = 0.5
from render_heroes import Builder, C, rot, raster, shade

SHF = np.array([0.8, 0.5, 0.35])


def fireball(k):
    r = np.random.RandomState(40 + k)
    b = Builder()
    b.group(); b.E((0, 0.9, 0), (0.95, 0.95, 0.95), C(255, 96, 30), sh=SHF, emis=False, spec=0.2)
    for i in range(7):
        a = r.rand() * 6.28; e = r.rand() * 2 - 1; q = math.sqrt(1 - e * e)
        d = np.array([math.cos(a) * q, e, math.sin(a) * q]) * (0.62 + 0.18 * r.rand())
        b.group(); b.E(np.array([0, 0.9, 0]) + d, (0.42 + 0.14 * r.rand(),) * 3, C(255, 140 + int(r.rand() * 40), 40), sh=SHF)
    b.group(); b.E((0, 0.9, 0), (0.68 + 0.04 * math.sin(k * 0.8),) * 3, C(255, 200, 80), sh=SHF, emis=True)
    b.group(); b.E((-0.12, 1.0, 0.25), (0.34, 0.34, 0.34), C(255, 250, 220), emis=True)
    return b.P


def arrow():
    b = Builder()
    wood, steel, fea = C(176, 132, 84), C(214, 220, 230), C(236, 70, 60)
    b.group(); b.seg((0, 0.9, -1.6), (0, 0.9, 1.1), 0.05, 0.05, wood, n=5, sh=SHF)
    b.group(); b.cone((0, 0.9, 1.05), 0.13, 0.0, 0.45, steel, R=rot('x', math.pi / 2), sh=SHF, spec=0.7)
    for k in range(3):
        b.group(); b.E((0, 0.9, -1.42), (0.022, 0.2, 0.30), fea, rot('z', k * 1.0472), sh=SHF)
    return b.P


def crescent():
    """초승달 베기: 오른쪽(+x)으로 볼록한 두툼한 호. 가운데가 가장 굵고 양 끝은 뾰족하다."""
    b = Builder()
    n = 22
    for i in range(n + 1):
        u = i / n; a = math.radians(-68 + 136 * u); t = math.sin(math.pi * u) ** 0.8
        x, y = -0.55 + math.cos(a), math.sin(a)
        r = 0.05 + 0.26 * t
        b.group(); b.E((x, y + 1.0, 0), (r, r * 1.05, 0.16 * (0.4 + t)), C(255, 190, 90), sh=SHF, emis=False)
        b.E((x + 0.05, y + 1.0, 0.08), (r * 0.55, r * 0.55, 0.14 * (0.4 + t)), C(255, 236, 190), sh=SHF, emis=True)
    return b.P


def spearhead():
    """창끝: 오른쪽(+x)을 향한 잎 모양 날과 자루 끝."""
    b = Builder()
    steel, dark, gold = C(222, 228, 240), C(140, 148, 170), C(232, 190, 90)
    b.group(); b.E((0, 0.9, 0.55), (0.22, 0.05, 0.62), steel, sh=SHF, spec=0.8)
    b.group(); b.E((0, 0.9, 0.55), (0.05, 0.22, 0.62), dark, sh=SHF)
    b.group(); b.E((0, 0.9, 1.05), (0.1, 0.1, 0.35), steel, sh=SHF, spec=0.8)
    b.group(); b.seg((0, 0.9, -1.0), (0, 0.9, -0.1), 0.07, 0.07, C(150, 100, 60), n=5, sh=SHF)
    b.group(); b.E((0, 0.9, -0.1), (0.14, 0.14, 0.1), gold, sh=SHF, spec=0.6)
    return b.P


def dagger():
    """단검: 오른쪽(+x)을 향한 가는 칼날."""
    b = Builder()
    steel, dark, gold = C(228, 232, 244), C(120, 128, 150), C(210, 170, 80)
    b.group(); b.E((0, 0.9, 0.5), (0.1, 0.03, 0.75), steel, sh=SHF, spec=0.9)
    b.group(); b.E((0, 0.9, 0.5), (0.025, 0.1, 0.72), dark, sh=SHF)
    b.group(); b.E((0, 0.9, 1.2), (0.05, 0.05, 0.18), steel, sh=SHF, spec=0.9)
    b.group(); b.E((0, 0.9, -0.28), (0.3, 0.07, 0.07), gold, sh=SHF)
    b.group(); b.seg((0, 0.9, -0.3), (0, 0.9, -0.85), 0.06, 0.06, C(92, 60, 48), n=5, sh=SHF)
    b.group(); b.E((0, 0.9, -0.9), (0.09, 0.09, 0.09), gold, sh=SHF)
    return b.P


def shard(k):
    """흙/돌 조각."""
    r = np.random.RandomState(70 + k)
    b = Builder()
    cols = [C(120, 92, 66), C(96, 74, 54), C(150, 118, 84)]
    for i in range(3):
        b.group(); b.E((r.rand() * 0.3 - 0.15, 0.9 + r.rand() * 0.3 - 0.15, r.rand() * 0.3 - 0.15),
                       (0.3 + 0.3 * r.rand(), 0.2 + 0.3 * r.rand(), 0.25 + 0.2 * r.rand()), cols[i % 3],
                       rot('z', r.rand() * 3) @ rot('y', r.rand() * 3), sh=SHF)
    return b.P


def bubble(k):
    """독 방울: 둥근 구와 작은 하이라이트."""
    b = Builder()
    b.group(); b.E((0, 0.9, 0), (0.7, 0.7, 0.7), C(110, 200, 70) if k % 2 == 0 else C(150, 210, 60), sh=SHF, spec=0.6)
    b.group(); b.E((-0.2, 1.1, 0.35), (0.2, 0.2, 0.2), C(235, 255, 200), emis=True)
    if k == 2: b.group(); b.E((0.45, 0.55, 0.1), (0.25, 0.25, 0.25), C(80, 170, 60), sh=SHF)
    return b.P


def orb(kind):
    """빛 구슬: heal(연두-금빛), nova(청백색)."""
    b = Builder()
    if kind == 'heal':
        b.group(); b.E((0, 0.9, 0), (0.7, 0.7, 0.7), C(120, 235, 130), sh=SHF, spec=0.5)
        b.group(); b.E((0, 0.9, 0), (0.42, 0.42, 0.42), C(235, 255, 190), emis=True)
    else:
        b.group(); b.E((0, 0.9, 0), (0.7, 0.7, 0.7), C(150, 190, 255), sh=SHF, spec=0.5)
        b.group(); b.E((0, 0.9, 0), (0.42, 0.42, 0.42), C(245, 250, 255), emis=True)
    return b.P


def spike():
    """포효 충격 뿔: 오른쪽(+x)을 향한 원뿔."""
    b = Builder()
    b.group(); b.cone((0, 0.9, 0.9), 0.32, 0.0, 1.9, C(214, 120, 70), R=rot('x', math.pi / 2), sh=SHF, spec=0.3)
    b.group(); b.cone((0, 0.9, 0.9), 0.15, 0.0, 1.9, C(255, 214, 150), R=rot('x', math.pi / 2), sh=SHF, emis=True)
    return b.P


def bonespike():
    """뼈창: 위(+y)를 향한 상아색 뿔. 밑에 마디."""
    b = Builder()
    ivo, dk = C(236, 230, 212), C(170, 160, 138)
    b.group(); b.cone((0, 0.15, 0), 0.26, 0.0, 2.1, ivo, sh=SHF, spec=0.35)
    b.group(); b.cone((0.06, 0.15, 0.12), 0.1, 0.0, 1.3, dk, R=rot('z', -0.12), sh=SHF)
    return b.P


def icespike(k):
    """얼음 가시: 위를 향한 맑은 하늘색 뿔 군락."""
    b = Builder()
    ice, hi = C(168, 222, 255), C(236, 250, 255)
    b.group(); b.cone((0, 0.15, 0), 0.26, 0.0, 2.0 - 0.3 * k, ice, sh=SHF, spec=0.9)
    b.group(); b.cone((0.3, 0.15, 0.1), 0.15, 0.0, 1.1, hi, R=rot('z', -0.28), sh=SHF, spec=0.9)
    b.group(); b.cone((-0.28, 0.15, -0.05), 0.13, 0.0, 0.9, ice, R=rot('z', 0.3), sh=SHF, spec=0.9)
    return b.P


def crystal(k):
    """수정: k=0 청록, k=1 분홍. 위를 향한 뾰족한 결정 군락."""
    b = Builder()
    c = C(130, 226, 255) if k == 0 else C(246, 150, 232)
    hi = C(232, 252, 255) if k == 0 else C(255, 226, 250)
    b.group(); b.cone((0, 0.15, 0), 0.3, 0.12, 1.3, c, sh=SHF, spec=0.9)
    b.group(); b.cone((0, 1.45, 0), 0.12, 0.0, 0.7, hi, sh=SHF, spec=0.9, emis=True)
    b.group(); b.cone((0.34, 0.15, 0.08), 0.18, 0.07, 0.8, c, R=rot('z', -0.3), sh=SHF, spec=0.9)
    b.group(); b.cone((-0.32, 0.15, -0.04), 0.16, 0.06, 0.65, hi, R=rot('z', 0.32), sh=SHF, spec=0.9)
    return b.P


def rock(k):
    """바위 조각(회색): 보스 대지 강타용."""
    r = np.random.RandomState(90 + k)
    b = Builder()
    cols = [C(132, 124, 118), C(104, 98, 94), C(160, 150, 140)]
    for i in range(3):
        b.group(); b.E((r.rand() * 0.3 - 0.15, 0.9 + r.rand() * 0.3 - 0.15, r.rand() * 0.3 - 0.15),
                       (0.3 + 0.3 * r.rand(), 0.2 + 0.3 * r.rand(), 0.25 + 0.2 * r.rand()), cols[i % 3],
                       rot('z', r.rand() * 3) @ rot('y', r.rand() * 3), sh=SHF)
    return b.P


def crop(prims, ang, size=None):
    H.PXU = 96 * H.SS * 0.9
    R = raster(prims, math.radians(ang))
    im = shade(R, np.isfinite(R['depth']))
    bb = im.getchannel('A').getbbox()
    im = im.crop(bb)
    if size:
        s = size / max(im.size)
        im = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)
    return im


def main():
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    for k in range(8):
        crop(fireball(k), 0, 96).save(f'{out}/fireball_{k}.png')
    a = crop(arrow(), 90, 200)          # 오른쪽(+x)을 향한 화살
    a.save(f'{out}/arrow.png')
    crop(crescent(), 0, 220).save(f'{out}/crescent.png')
    crop(spearhead(), 90, 220).save(f'{out}/spearhead.png')
    crop(dagger(), 90, 160).save(f'{out}/dagger.png')
    for k in range(4):
        crop(shard(k), 0, 48).save(f'{out}/shard_{k}.png')
    for k in range(3):
        crop(bubble(k), 0, 64).save(f'{out}/bubble_{k}.png')
    crop(orb('heal'), 0, 64).save(f'{out}/orb_heal.png')
    crop(orb('nova'), 0, 64).save(f'{out}/orb_nova.png')
    crop(spike(), 90, 160).save(f'{out}/spike.png')
    crop(bonespike(), 0, 160).save(f'{out}/bonespike.png')
    for k in range(2):
        crop(icespike(k), 0, 160).save(f'{out}/icespike_{k}.png')
        crop(crystal(k), 0, 160).save(f'{out}/crystal_{k}.png')
    for k in range(3):
        crop(rock(k), 0, 48).save(f'{out}/rock_{k}.png')
    print('ok', a.size)


if __name__ == '__main__':
    main()
