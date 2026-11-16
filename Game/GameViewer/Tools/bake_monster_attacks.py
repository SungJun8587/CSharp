#!/usr/bin/env python3
"""몬스터 공격 자세 프레임 굽기.

Assets/<몬스터>/ 의 방향별 그림(외부 3D 회전 렌더)에는 3D 원본이 없어서 팔다리를 다시 움직일 수 없다.
대신 그림을 비강체로 휘어(앞쪽은 내밀고 뒤쪽은 처지게, 위쪽은 기울이고, 발 밑을 기준으로 눌러/늘이고, 카메라 쪽이면 커지게)
공격 자세 5장을 방향마다 만든다. 런타임이 한 장을 통째로 기울이던 것과 달리 몸 부위마다 다르게 움직인다.

  자세 0 = 예비동작(웅크리거나 젖힘), 1..4 = 내지르는 정도 0.3 / 0.55 / 0.8 / 1.0

사용법:  python3 bake_monster_attacks.py [몬스터이름 ...]     (생략하면 sprites.json 의 몬스터 전부. gv 폴더에서 실행)
결과:    Assets/<이름>/atk_<각도3자리>_<자세>.png , 그리고 sprites.json 에 attackFrames/attackPattern 을 적는다.
"""
import json, math, os, sys
import numpy as np
import cv2

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(os.path.dirname(HERE), 'Assets')

# (T 앞으로 내밈, lean 위쪽 기울임, sy 세로 배율, sx 가로 배율, depth 카메라 쪽 커짐)  T/lean 은 내용 높이에 대한 비율
GEN = [(-0.05, -0.06, 0.94, 1.03, -0.03), (0.05, 0.04, 0.99, 1.00, 0.02), (0.09, 0.08, 0.97, 1.01, 0.04), (0.12, 0.12, 0.95, 1.02, 0.06), (0.15, 0.16, 0.93, 1.03, 0.08)]
# 공격 방식(sprites.json 의 "attack")별 기본 자세. 이름이 따로 있으면 그것이 우선한다.
BY_ATTACK = {
    'lunge': GEN,
    'hop':   [(0.00, 0.00, 0.78, 1.16, 0.00), (0.02, 0.02, 0.98, 1.00, 0.02), (0.05, 0.04, 1.10, 0.94, 0.04), (0.09, 0.07, 1.16, 0.90, 0.07), (0.00, 0.00, 0.80, 1.22, 0.05)],    # 웅크림 -> 뛰어오름 -> 늘어남 -> 착지해 퍼짐
    'slam':  [(-0.05, -0.12, 1.05, 0.98, -0.04), (0.03, 0.05, 1.00, 1.00, 0.02), (0.06, 0.12, 0.96, 1.02, 0.05), (0.09, 0.18, 0.92, 1.05, 0.08), (0.11, 0.22, 0.89, 1.07, 0.10)],      # 젖혔다가 내려찍음
}
STYLE = {
    'wolf': [(-0.06, -0.08, 0.90, 1.04, -0.04), (0.06, 0.05, 1.00, 1.00, 0.02), (0.11, 0.10, 0.97, 1.02, 0.05), (0.16, 0.15, 0.95, 1.04, 0.08), (0.20, 0.19, 0.92, 1.06, 0.10)],
}


BOSS_BASE = 100         # 보스 몬스터 번호 = 100 + 일반 몬스터 번호
BOSS_SCALE = 1.8        # 보스는 같은 그림을 이만큼 크게 쓴다


def twin_of(doc, ent):
    """일반 몬스터 항목의 보스판(번호 +100, 그림은 같은 것, 키만 BOSS_SCALE 배)을 만들거나 갱신한다."""
    tpl = ent['template'] + BOSS_BASE
    boss = dict(ent)
    boss['template'] = tpl
    boss['height'] = round(min(12.0, ent['height'] * BOSS_SCALE), 2)
    boss['attack'] = 'slam'
    for i, e in enumerate(doc['sprites']):
        if e.get('kind') == 'monster' and e.get('template') == tpl:
            doc['sprites'][i] = boss
            return
    doc['sprites'].append(boss)


def smooth(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def load_union(name, entry):
    """sprites.json 항목으로 방향별 그림과 뷰어가 쓰는 것과 같은 합친 상자(발 밑 포함)를 읽는다."""
    n = entry['angles']
    pat = entry['pattern'].replace('{0:000}', '%03d')
    imgs, box = [], [10 ** 9, 10 ** 9, -1, -1]
    for i in range(n):
        deg = int(round(i * 360.0 / n))
        im = cv2.imread(os.path.join(ASSETS, pat % deg), cv2.IMREAD_UNCHANGED)
        if im.shape[2] == 3:
            im = np.dstack([im, np.full(im.shape[:2], 255, np.uint8)])
        imgs.append((deg, im))
        a = im[:, :, 3] > 8
        ys, xs = np.where(a.any(1))[0], np.where(a.any(0))[0]
        box = [min(box[0], xs[0]), min(box[1], ys[0]), max(box[2], xs[-1]), max(box[3], ys[-1])]
    return imgs, box


def warp(im, deg, box, p):
    T, lean, sy, sx, depth = p
    h, w = im.shape[:2]
    x0, y0, x1, y1 = box
    cx, foot = w / 2.0, y1 + 1.0
    hc = float(y1 - y0 + 1)
    halfw = float(max(cx - x0, x1 + 1 - cx))
    fx, fy = math.sin(math.radians(deg)), math.cos(math.radians(deg))
    X, Y = np.meshgrid(np.arange(w, dtype=np.float32), np.arange(h, dtype=np.float32))
    hh = np.clip((foot - Y) / hc, 0.0, 1.2)
    xr = (X - cx) / halfw * (1.0 if fx >= 0 else -1.0)
    # 앞쪽은 제자리, 뒤쪽은 처진다(앞으로 내미는 건 런타임의 몸 이동이 맡는다. 그림 가장자리 여유가 작아도 잘리지 않는다)
    front = smooth((xr + 1.0) / 2.0)
    side = abs(fx)
    dx = fx * hc * (T * (front - 1.0) * side + lean * hh * hh) * 1.0
    srcx = X - dx
    srcy = Y.copy()
    # 발 밑을 기준으로 세로 눌림/늘림, 가로는 몸 한가운데 기준
    srcy = foot - (foot - srcy) / sy
    srcx = cx + (srcx - cx) / sx
    # 카메라 쪽(fy>0)이면 커지고 등지면 작아진다(발 밑 기준)
    s = 1.0 + depth * fy
    srcx = cx + (srcx - cx) / s
    srcy = foot - (foot - srcy) / s
    pm = im.astype(np.float32)
    pm[:, :, :3] *= pm[:, :, 3:4] / 255.0
    out = cv2.remap(pm, srcx.astype(np.float32), srcy.astype(np.float32), cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0, 0))
    # 합친 상자 밖으로 나가면 발 밑 기준으로 줄여 넣는다
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
    al = out[:, :, 3:4]
    rgb = np.where(al > 0.5, out[:, :, :3] * 255.0 / np.maximum(al, 1e-3), 0.0)
    res = np.concatenate([np.clip(rgb, 0, 255), np.clip(al, 0, 255)], axis=2).astype(np.uint8)
    return res


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
        styles = STYLE.get(name) or BY_ATTACK.get(ent.get('attack'), GEN)
        imgs, box = load_union(name, ent)
        for deg, im in imgs:
            for k, p in enumerate(styles):
                cv2.imwrite(os.path.join(ASSETS, name, 'atk_%03d_%d.png' % (deg, k)), warp(im, deg, box, p))
        ent['attackFrames'] = len(styles)
        ent['attackPattern'] = name + '/atk_{0:000}_{1}.png'
        print(name, len(imgs), 'angles x', len(styles), 'poses')
        twin_of(doc, ent)
    txt = json.dumps(doc, indent=2, ensure_ascii=False)
    if crlf:
        txt = txt.replace('\n', '\r\n')
    open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + txt.encode('utf-8'))


if __name__ == '__main__':
    main()
