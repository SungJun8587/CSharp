#!/usr/bin/env python3
"""몬스터 피격/사망 자세 프레임 굽기.

몬스터 그림은 방향별 2D 한 장이라 3D 로 다시 그릴 수 없다. 그래서 공격 자세(bake_monster_attacks.py)와 같이 그림을 휘어 만든다.

  hit_<각도3자리>.png            피격: 맞은 쪽으로 젖히고(앞이 뒤로 밀리고) 위아래로 살짝 눌린다.
  death_<각도3자리>_<단계>.png   사망 3단계: 휘청 -> 쓰러지는 중 -> 누움(발 밑을 축으로 옆으로 넘어지고 원근 때문에 납작해진다). 마지막 그림이 시체로 남는다.

그림은 방향별 합친 상자(뷰어가 쓰는 것과 같은 상자) 안에 들어가게 줄여 넣는다. 크기는 기본 그림과 같아야 한다.
사용법:  python3 bake_monster_poses.py [몬스터이름 ...]     (생략하면 sprites.json 의 몬스터 전부. gv 폴더에서 실행)
결과:    Assets/<이름>/hit_*.png, death_*.png , 그리고 sprites.json 에 hitPattern/deathFrames/deathPattern 을 적는다(보스판 포함).
"""
import json, math, os, sys
import numpy as np
import cv2
from bake_monster_attacks import ASSETS, BOSS_BASE, load_union, warp

HIT = (-0.10, -0.12, 0.93, 1.05, -0.04)
DEATH_ANGLE = (14.0, 52.0, 88.0)        # 쓰러지는 각도
DEATH_FLAT = (1.0, 0.86, 0.62)          # 세로 납작함(원근)
WIDE_ANGLE = (8.0, 20.0, 34.0)
WIDE_FLAT = (1.0, 0.78, 0.55)


def fit(out, box):
    """합친 상자 밖으로 나간 부분이 없도록 발 밑 기준으로 줄인다."""
    h, w = out.shape[:2]
    x0, y0, x1, y1 = box
    cx, foot = w / 2.0, y1 + 1.0
    a = out[:, :, 3] > 8
    if a.any():
        ys, xs = np.where(a.any(1))[0], np.where(a.any(0))[0]
        bx0, bx1, by0 = xs[0], xs[-1] + 1.0, ys[0]
        k = 1.0
        if bx0 < x0 - 0.5: k = min(k, (cx - x0) / max(1.0, cx - bx0))
        if bx1 > x1 + 1.5: k = min(k, (x1 + 1 - cx) / max(1.0, bx1 - cx))
        if by0 < y0 - 0.5: k = min(k, (foot - y0) / max(1.0, foot - by0))
        if k < 0.999:
            M = np.array([[k, 0, cx * (1 - k)], [0, k, foot * (1 - k)]], np.float32)
            out = cv2.warpAffine(out, M, (w, h), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0, 0))
    return out


def unpremul(out):
    al = out[:, :, 3:4]
    rgb = np.where(al > 0.5, out[:, :, :3] * 255.0 / np.maximum(al, 1e-3), 0.0)
    return np.clip(np.concatenate([rgb, al], axis=2), 0, 255).astype(np.uint8)


def topple(im, deg, box, ang, flat):
    """발 밑 한가운데를 축으로 옆으로 넘어뜨리고 납작하게 만든 뒤, 합친 상자 안(가운데, 발 밑 선)에 맞춰 넣는다."""
    h, w = im.shape[:2]
    x0, y0, x1, y1 = box
    cx, foot = w / 2.0, y1 + 1.0
    side = 1.0 if deg in (270, 0) else -1.0       # 방향마다 위쪽이 넘어가는 쪽을 정한다
    pad = h
    pm = im.astype(np.float32)
    pm[:, :, :3] *= pm[:, :, 3:4] / 255.0
    big = cv2.copyMakeBorder(pm, pad, 0, pad, pad, cv2.BORDER_CONSTANT, value=(0, 0, 0, 0))
    M = cv2.getRotationMatrix2D((cx + pad, foot + pad), side * ang, 1.0)
    M[1, :] = M[1, :] * flat + np.array([0, 0, (foot + pad) * (1 - flat)], np.float32)
    rot = cv2.warpAffine(big, M, (w + 2 * pad, h + 2 * pad), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0, 0))
    a = rot[:, :, 3] > 8
    ys, xs = np.where(a.any(1))[0], np.where(a.any(0))[0]
    bx0, bx1, by0, by1 = xs[0], xs[-1] + 1, ys[0], ys[-1] + 1
    bw, bh = bx1 - bx0, by1 - by0
    k = min(1.0, (x1 + 1 - x0) / float(bw), (foot - y0) / float(bh))
    crop = rot[by0:by1, bx0:bx1]
    if k < 0.999:
        crop = cv2.resize(crop, (max(1, int(round(bw * k))), max(1, int(round(bh * k)))), interpolation=cv2.INTER_AREA)
    ch, cw = crop.shape[:2]
    out = np.zeros((h, w, 4), np.float32)
    # 기울어진 정도가 작을수록 원래 서 있던 자리 그대로(가로 가운데는 몸의 중심), 많이 누울수록 상자 가운데로
    ox = int(round(cx - cw / 2.0)); oy = int(round(foot - ch))
    sx0, sy0 = max(0, ox), max(0, oy)
    ex, ey = min(w, ox + cw), min(h, oy + ch)
    out[sy0:ey, sx0:ex] = crop[sy0 - oy:ey - oy, sx0 - ox:ex - ox]
    return unpremul(out)


def main():
    names = sys.argv[1:]
    path = os.path.join(ASSETS, 'sprites.json')
    raw = open(path, 'rb').read()
    bom, crlf = raw.startswith(b'\xef\xbb\xbf'), b'\r\n' in raw
    doc = json.loads(raw.decode('utf-8-sig'))
    for ent in list(doc['sprites']):
        if ent.get('kind') != 'monster' or ent['template'] >= BOSS_BASE:
            continue
        if ent.get('render') == '3d':        # 3D 로 렌더한 몬스터(render_monsters.py)는 그림을 휘어 굽지 않는다
            continue
        name = ent['pattern'].split('/')[0]
        if names and name not in names:
            continue
        imgs, box = load_union(name, ent)
        for deg, im in imgs:
            cv2.imwrite(os.path.join(ASSETS, name, 'hit_%03d.png' % deg), unpremul(fit(warp_pm(im, deg, box), box)))
            wide = (box[2] - box[0]) > 1.1 * (box[3] - box[1])        # 옆으로 넓은 몸(게)은 옆으로 눕히면 어색해서 배를 드러내며 뒤집힌다
            angs, flats = (WIDE_ANGLE, WIDE_FLAT) if wide else (DEATH_ANGLE, DEATH_FLAT)
            for k, (a, f) in enumerate(zip(angs, flats)):
                cv2.imwrite(os.path.join(ASSETS, name, 'death_%03d_%d.png' % (deg, k)), topple(im, deg, box, a, f))
        for e2 in doc['sprites']:
            if e2.get('kind') == 'monster' and e2['pattern'] == ent['pattern']:       # 보스판도 같은 그림
                e2['hitPattern'] = name + '/hit_{0:000}.png'
                e2['deathFrames'] = len(DEATH_ANGLE)
                e2['deathPattern'] = name + '/death_{0:000}_{1}.png'
        print(name, len(imgs), 'angles: hit + death x', len(DEATH_ANGLE))
    txt = json.dumps(doc, indent=2, ensure_ascii=False)
    if crlf:
        txt = txt.replace('\n', '\r\n')
    open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + txt.encode('utf-8'))


def warp_pm(im, deg, box):
    """공격 자세와 같은 휘기(warp)를 쓰되, fit/unpremul 을 이 파일에서 하도록 곱해진 알파 상태로 돌려준다."""
    out = warp(im, deg, box, HIT)
    pm = out.astype(np.float32)
    pm[:, :, :3] *= pm[:, :, 3:4] / 255.0
    return pm


if __name__ == '__main__':
    main()
