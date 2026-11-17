#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
pack_atlas.py : 영웅(또는 아무 에셋 폴더)의 개별 PNG 를 폴더마다 시트 한 장 + 색인 JSON 으로 묶는다.

  python3 pack_atlas.py <Assets 폴더> <폴더이름> [<폴더이름> ...]       예) python3 pack_atlas.py ../Assets warrior archer mage lancer

  - 각 PNG 는 투명한 가장자리를 잘라 내고 빈틈없이 쌓는다(선반 방식). 원래 크기와 잘라 낸 위치를 색인에 적어 두므로, 뷰어는 읽을 때
    원래 크기의 그림으로 정확히 되돌린다(알파 0 이 아닌 픽셀은 한 점도 달라지지 않음 - 아래에서 전부 대조한다).
  - 한 페이지는 4096x4096 이하(뷰어 읽기 한도). 넘치면 atlas_1.png, atlas_2.png ... 로 나뉜다.
  - 출력: <폴더>/atlas.png(, atlas_1.png ...), <폴더>/atlas.json
        { "version": 1, "pages": ["atlas.png"], "files": { "warrior/000.png": [페이지, x, y, w, h, ox, oy, 원래폭, 원래높이], ... } }
    키는 sprites.json 의 pattern 과 같은 경로("폴더/파일")이다. 뷰어는 이 키가 시트에 있으면 시트에서, 없으면 개별 파일에서 읽는다.
  - 마지막에 sprites.json 의 "atlases" 목록에 색인 경로를 넣어 준다(이미 있으면 그대로).
  - 개별 PNG 는 지우지 않는다. 시트로 잘 나오는 것을 확인한 뒤 지워도 된다(anchors.json 은 지우면 안 된다).
"""
import json, os, sys, glob
from PIL import Image

MAXW = 4096

def pack(assets, folder):
    files = sorted(glob.glob(os.path.join(assets, folder, '*.png')))
    files = [f for f in files if os.path.basename(f) not in ('atlas.png',) and not os.path.basename(f).startswith('atlas_')]
    items = []   # (key, 원본 이미지, bbox)
    for f in files:
        im = Image.open(f).convert('RGBA')
        bb = im.getchannel('A').getbbox()
        items.append([folder + '/' + os.path.basename(f), im, bb])
    # 높이 큰 것부터 선반에 쌓는다
    order = sorted([i for i in items if i[2]], key=lambda it: (-(it[2][3] - it[2][1]), -(it[2][2] - it[2][0]), it[0]))
    pages = []          # 페이지마다 [높이, 놓인 목록]
    placed = {}
    page = 0; x = 0; y = 0; shelf_h = 0; cur = []
    def flush():
        pages.append((y + shelf_h, cur[:]))
    for key, im, bb in order:
        w, h = bb[2] - bb[0], bb[3] - bb[1]
        if x + w > MAXW:                # 다음 선반
            y += shelf_h; x = 0; shelf_h = 0
        if y + h > MAXW:                # 다음 페이지
            flush(); cur = []; page += 1; x = 0; y = 0; shelf_h = 0
        cur.append((key, im, bb, x, y)); placed[key] = (page, x, y, w, h)
        x += w; shelf_h = max(shelf_h, h)
    flush()
    index = {'version': 1, 'pages': [], 'files': {}}
    for p, (ph, lst) in enumerate(pages):
        sheet = Image.new('RGBA', (MAXW, max(1, ph)), (0, 0, 0, 0))
        for key, im, bb, px, py in lst:
            sheet.paste(im.crop(bb), (px, py))
        name = 'atlas.png' if p == 0 else 'atlas_%d.png' % p
        sheet.save(os.path.join(assets, folder, name), optimize=True)
        index['pages'].append(name)
    for key, im, bb in items:
        fw, fh = im.size
        if not bb: index['files'][key] = [0, 0, 0, 0, 0, 0, 0, fw, fh]
        else:
            pg, px, py, w, h = placed[key]
            index['files'][key] = [pg, px, py, w, h, bb[0], bb[1], fw, fh]
    with open(os.path.join(assets, folder, 'atlas.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(index, f, separators=(',', ':'))
    # 대조: 시트에서 되돌린 그림 = 원래 그림 (알파가 0 인 곳의 색은 무시)
    sheets = [Image.open(os.path.join(assets, folder, n)).convert('RGBA') for n in index['pages']]
    bad = 0
    for key, im, bb in items:
        pg, px, py, w, h, ox, oy, fw, fh = index['files'][key]
        back = Image.new('RGBA', (fw, fh), (0, 0, 0, 0))
        if w: back.paste(sheets[pg].crop((px, py, px + w, py + h)), (ox, oy))
        a, b = im.tobytes(), back.tobytes()
        for i in range(0, len(a), 4):
            if a[i + 3] != b[i + 3] or (a[i + 3] and a[i:i + 3] != b[i:i + 3]): bad += 1; break
    total = sum(os.path.getsize(f) for f in files)
    atl = sum(os.path.getsize(os.path.join(assets, folder, n)) for n in index['pages'])
    print('%-8s %3d장 -> 시트 %d쪽 %s  PNG %.1fMB -> %.1fMB  대조 불일치 %d' % (folder, len(items), len(pages), [(MAXW, ph) for ph, _ in pages], total / 1e6, atl / 1e6, bad))
    return bad

def main():
    if len(sys.argv) < 3: print(__doc__); return 2
    assets = sys.argv[1]; bad = 0
    for folder in sys.argv[2:]: bad += pack(assets, folder)
    manifest = os.path.join(assets, 'sprites.json')
    raw = open(manifest, 'rb').read(); bom = raw.startswith(b'\xef\xbb\xbf'); text = raw.decode('utf-8-sig')
    d = json.loads(text)
    want = [f + '/atlas.json' for f in sys.argv[2:]]
    have = d.get('atlases', [])
    add = [w for w in want if w not in have]
    if add and '"atlases"' not in text:
        line = '  "atlases": [' + ', '.join('"%s"' % w for w in have + add) + '],\n'
        text = text.replace('{\n', '{\n' + line, 1)
    elif add:
        d['atlases'] = have + add; text = json.dumps(d, ensure_ascii=False, indent=2) + '\n'
    open(manifest, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))
    return 1 if bad else 0

if __name__ == '__main__':
    sys.exit(main())
