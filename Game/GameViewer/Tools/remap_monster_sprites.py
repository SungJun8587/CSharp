# -*- coding: utf-8 -*-
"""remap_monster_sprites.py : sprites.json 의 몬스터 그림을 서버 Monster.json 의 번호/이름에 맞춘다.
사용: python remap_monster_sprites.py <Assets\\sprites.json> [<GameData\\Monster.json> [<Assets 폴더>]]
Assets 폴더를 주면 그 아래에 그림 폴더가 없는 몬스터 항목은 sprites.json 에서 지운다.

sprites.json 의 "template" 은 서버 Monster.json 의 Id 이다. 그림 폴더(pattern)와 이름이 어긋나 있으면(예: 고블린 자리에 버섯)
이 도구가 template 번호만 바꿔 준다. 이미지 파일, atlases, 높이/공격 방식 같은 다른 값은 그대로다.
같은 폴더에 작은 것/큰 것(height 가 더 큰 쪽) 두 항목이 있으면 아래 표의 big 으로 고른다. 표에 없는 몬스터 항목은 지운다.
몇 번 돌려도 결과가 같다. 원본은 sprites.json.bak 으로 남긴다.
"""
import json, os, shutil, sys

# 서버 몬스터 Id -> (그림 폴더, 큰 쪽이면 True). 폴더 이름은 Assets 아래 폴더.
WANT = {
    1: ('slime', False), 2: ('toad', False), 3: ('bat', False), 4: ('lizard', False), 5: ('mushroom', False),
    6: ('hermit', False), 7: ('goblin', False), 8: ('spider', False), 9: ('cactus', False), 10: ('scorpion', False),
    11: ('mantis', False), 12: ('crab', False), 13: ('skeleton', False), 14: ('bomb', False), 15: ('ghost', False),
    16: ('worm', False), 17: ('goblin_shaman', False), 18: ('goblin_warrior', False), 19: ('darkknight', False),
    20: ('ironknight', False), 21: ('yeti', False), 22: ('golem', False),
    23: ('treant', False), 24: ('flame', False),
    # 보스 = 같은 모양의 큰 쪽 (Id = 100 + 원본 Id)
    101: ('slime', True), 108: ('spider', True), 121: ('yeti', True), 122: ('golem', True),
}

def main():
    if len(sys.argv) < 2:
        print(__doc__); return 1
    path = sys.argv[1]
    names = {}
    if len(sys.argv) > 2:
        mon = json.load(open(sys.argv[2], encoding='utf-8-sig'))['Monster']
        names = {r['Id']: r['Name'] for r in mon}
    raw = open(path, encoding='utf-8-sig').read()
    doc = json.loads(raw)
    if len(sys.argv) > 3:
        have = set(os.listdir(sys.argv[3]))
        gone = [e for e in doc['sprites'] if e.get('kind') == 'monster' and e['pattern'].split('/')[0] not in have]
        doc['sprites'] = [e for e in doc['sprites'] if not any(e is g for g in gone)]
        print('삭제(그림 폴더 없음): %d개 %s' % (len(gone), sorted({e['pattern'].split('/')[0] for e in gone})))
    ents = [e for e in doc['sprites'] if e.get('kind') == 'monster']

    groups = {}
    for e in ents:
        groups.setdefault(e['pattern'].split('/')[0], []).append(e)
    for g in groups.values():
        g.sort(key=lambda e: e.get('height', 0))

    chosen = {}
    for sid, (folder, big) in WANT.items():
        g = groups.get(folder)
        if not g:
            print('없는 그림 폴더: %s (id %d)' % (folder, sid)); return 2
        e = g[-1] if (big and len(g) > 1) else g[0]
        if big and len(g) < 2:
            print('큰 쪽이 없어 작은 쪽을 씁니다: %s (id %d)' % (folder, sid))
        chosen[sid] = e

    doc['sprites'] = [e for e in doc['sprites'] if e.get('kind') != 'monster' or any(e is c for c in chosen.values())]
    ents = [e for e in doc['sprites'] if e.get('kind') == 'monster']
    for sid, e in chosen.items():
        e['template'] = sid

    seen = {}
    for e in ents:
        if e['template'] in seen:
            print('template 중복: %d' % e['template']); return 3
        seen[e['template']] = e

    for sid in sorted(WANT):
        print('%2d %-8s -> %s%s' % (sid, names.get(sid, ''), chosen[sid]['pattern'].split('/')[0], ' (큰 쪽)' if WANT[sid][1] else ''))
    if not os.path.exists(path + '.bak'):
        shutil.copyfile(path, path + '.bak')
    out = json.dumps(doc, indent=2, ensure_ascii=False)
    open(path, 'w', encoding='utf-8', newline='').write(out + '\n')
    print('저장: ' + path)
    return 0

if __name__ == '__main__':
    sys.exit(main())
