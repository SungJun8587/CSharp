#!/usr/bin/env python3
"""monsters_source.jpg (몬스터 14종이 한 장에 모인 시트) 에서 몬스터를 잘라 Assets/<이름>/ 에 방향별 PNG 로 만든다.

  - 회색 배경(126)과 발밑 그림자를 지우고, 원본이 작아서(몬스터 한 마리가 30~40px) 4배로 키워 부드러운 가장자리를 만든다.
  - 원본은 한 방향뿐이라 방향 4장(0=정면, 90=오른쪽, 180=뒤(정면과 같은 그림), 270=왼쪽)을 같은 그림과 좌우 반전으로 채운다.
사용법: python3 extract_monsters.py [이름 ...]    (gv 폴더에서. 생략하면 14종 전부)
결과:   Assets/<이름>/000.png 090.png 180.png 270.png  (sprites.json 은 건드리지 않는다)
"""
import os, sys
import cv2, numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(os.path.dirname(HERE), 'Assets')
SRC = os.path.join(HERE, 'monsters_source.jpg')
BG = 126
S = 4                       # 확대 배율

# 이름: (x0, y0, x1, y1, 원본이 바라보는 쪽 'L'/'R'/'F')  (원본 좌표, 끝은 포함하지 않음)
BOX = {
    'wolf': (14, 23, 43, 60, 'L'), 'bomb': (57, 24, 81, 60, 'F'), 'slime': (98, 35, 125, 58, 'F'),
    'mushroom': (138, 34, 165, 59, 'F'), 'crab': (179, 16, 232, 60, 'F'), 'cactus': (238, 24, 264, 59, 'F'),
    'worm': (270, 8, 314, 62, 'L'), 'goblin': (13, 70, 43, 107, 'F'), 'goblin_shaman': (47, 67, 86, 108, 'F'),
    'goblin_warrior': (100, 58, 139, 109, 'F'), 'treant': (152, 66, 182, 108, 'F'), 'darkknight': (206, 66, 237, 107, 'F'),
    'ironknight': (266, 62, 309, 108, 'F'), 'flame': (174, 94, 214, 175, 'F'),
}


def smoothstep(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def cut(img, name):
    x0, y0, x1, y1, _ = BOX[name]
    c = img[y0:y1, x0:x1].astype(np.int32)
    d = np.abs(c - BG).max(2)
    mx, mn = c.max(2), c.min(2)
    shadow = ((mx - mn) < 14) & (mx < 114) & (mx > 48)       # 발밑 그림자(무채색 어두운 회색)
    # 배경과 비슷한 색(바깥과 이어진 것)은 배경, 확실히 다른 색은 전경, 나머지는 GrabCut 이 색 분포로 가른다.
    # 은색 갑옷처럼 배경 회색과 가까운 부분도 색 분포와 윤곽으로 살린다.
    near_bg = (d < 14).astype(np.uint8)
    nb, lb, _, _ = cv2.connectedComponentsWithStats(cv2.copyMakeBorder(near_bg, 1, 1, 1, 1, cv2.BORDER_CONSTANT, value=1), 4)
    outside = (lb[1:-1, 1:-1] == lb[0, 0])
    gm = np.full(d.shape, cv2.GC_PR_FGD, np.uint8)
    gm[d > 48] = cv2.GC_FGD
    gm[outside] = cv2.GC_BGD
    gm[shadow & (d > 14)] = cv2.GC_BGD
    gm[:2, :] = cv2.GC_BGD; gm[:, :2] = cv2.GC_BGD; gm[:, -2:] = cv2.GC_BGD
    bgm = np.zeros((1, 65), np.float64); fgm = np.zeros((1, 65), np.float64)
    cu8 = c.astype(np.uint8)
    try:
        cv2.grabCut(cu8, gm, None, bgm, fgm, 6, cv2.GC_INIT_WITH_MASK)
        fg = ((gm == cv2.GC_FGD) | (gm == cv2.GC_PR_FGD)).astype(np.uint8)
    except cv2.error:
        fg = (d > 26).astype(np.uint8)
    fg[shadow & (d <= 48)] = 0
    fg = cv2.morphologyEx(fg, cv2.MORPH_OPEN, np.ones((2, 2), np.uint8))
    n, lab, st, _ = cv2.connectedComponentsWithStats(fg, 8)
    if n <= 1:
        raise SystemExit(name + ': 몬스터를 찾지 못함')
    big = 1 + int(np.argmax(st[1:, 4]))
    keep = (lab == big).astype(np.uint8)
    near = cv2.dilate(keep, np.ones((5, 5), np.uint8))       # 무기/불꽃 조각처럼 본체 가까이 있는 것만 함께 둔다
    for i in range(1, n):
        if i != big and st[i, 4] >= 14 and near[lab == i].any():
            keep[lab == i] = 1
    keep = cv2.morphologyEx(keep, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
    pk = cv2.copyMakeBorder(keep, 1, 1, 1, 1, cv2.BORDER_CONSTANT, value=0)      # 바깥에서 번지게 해 안쪽 구멍만 메운다
    ff = pk.copy(); m2 = np.zeros((pk.shape[0] + 2, pk.shape[1] + 2), np.uint8)
    cv2.floodFill(ff, m2, (0, 0), 2)
    keep = np.where(ff[1:-1, 1:-1] == 2, keep, 1).astype(np.uint8)
    keep = cv2.erode(keep, cv2.getStructuringElement(cv2.MORPH_CROSS, (3, 3)))   # 가장자리에 섞인 회색 배경 한 겹을 깎는다
    ys, xs = np.where(keep > 0)
    bx0, bx1, by0, by1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    return c[by0:by1, bx0:bx1].astype(np.uint8), keep[by0:by1, bx0:bx1]


def upscale(c, m):
    h, w = m.shape
    pad = 2
    cp = cv2.copyMakeBorder(c, pad, pad, pad, pad, cv2.BORDER_REPLICATE)
    mp = cv2.copyMakeBorder(m.astype(np.float32), pad, pad, pad, pad, cv2.BORDER_CONSTANT, value=0)
    # 가장자리의 회색 배경이 섞인 픽셀 색을 안쪽 색으로 덮어 후광을 줄인다
    er = cv2.erode((mp > 0.5).astype(np.uint8), np.ones((3, 3), np.uint8))
    inner = cp.copy().astype(np.float32)
    ring = ((mp > 0.5) & (er == 0))
    blur = cv2.GaussianBlur(cp.astype(np.float32) * er[..., None], (0, 0), 1.2)
    wgt = cv2.GaussianBlur(er.astype(np.float32), (0, 0), 1.2)
    fill = blur / np.maximum(wgt[..., None], 1e-3)
    inner[ring] = fill[ring]
    big = cv2.resize(inner, None, fx=S, fy=S, interpolation=cv2.INTER_CUBIC)
    mb = cv2.resize(mp, None, fx=S, fy=S, interpolation=cv2.INTER_LINEAR)
    mb = cv2.GaussianBlur(mb, (0, 0), 2.2)
    a = smoothstep(mb, 0.40, 0.62)
    soft = cv2.GaussianBlur(big, (0, 0), 1.0)
    big = np.clip(big + (big - soft) * 0.6, 0, 255)           # 살짝 선명하게
    ys, xs = np.where(a > 0.02)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    rgba = np.dstack([big, a * 255.0])[y0:y1, x0:x1]
    return np.clip(rgba, 0, 255).astype(np.uint8)


def main():
    img = cv2.imread(SRC)
    names = sys.argv[1:] or list(BOX)
    for name in names:
        c, m = cut(img, name)
        rgba = upscale(c, m)
        bgra = rgba                                            # cv2 는 BGR(A) 순서 그대로 쓴다
        mirror = cv2.flip(bgra, 1)
        facing = BOX[name][4]
        right, left = (bgra, mirror) if facing in ('R', 'F') else (mirror, bgra)
        out = os.path.join(ASSETS, name)
        os.makedirs(out, exist_ok=True)
        # 모든 방향 그림은 같은 크기여야 한다(뷰어가 합친 상자로 발밑을 맞춘다)
        for deg, im in ((0, bgra), (90, right), (180, bgra), (270, left)):
            cv2.imwrite(os.path.join(out, '%03d.png' % deg), im)
        print(name, bgra.shape[1], 'x', bgra.shape[0])


if __name__ == '__main__':
    main()
