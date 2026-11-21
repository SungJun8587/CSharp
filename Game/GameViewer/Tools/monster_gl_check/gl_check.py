#!/usr/bin/env python3
"""몬스터 3D 셰이더 점검: Core(C#)가 계산한 정점 버퍼/팔레트/행렬을, App/Forest/MonsterShaders.cs 에서 뽑은 GLSL 그대로 헤드리스 GL(EGL, mesa 의 llvmpipe 등)에 넣어 그려 본다.
GPU/Windows 없이 셰이더 문법, 스키닝, 방향, 조명, 가장자리(다중표본)를 눈으로 확인할 수 있다. 앱의 GL 호출 순서(MonsterGl.RenderTo)와 같은 순서로 그린다.
준비: pip install moderngl pillow numpy ; dotnet build Tools/monster_gl_check (결과: Bin/Debug/MonsterGlDump.dll)
사용: python3 gl_check.py [출력 PNG]   (Assets/models 의 몇 가지 동작/방향을 한 장에 그린다)"""
import re, sys, os, subprocess, struct, io
import numpy as np, moderngl
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
SRC = os.path.join(ROOT, 'App', 'Forest', 'MonsterShaders.cs')
s = open(SRC, encoding='utf-8-sig').read()
VS = re.search(r'Vs = @"(.*?)";', s, re.S).group(1).replace('""', '"')
FS = re.search(r'Fs = @"(.*?)";', s, re.S).group(1).replace('""', '"')
ctx = moderngl.create_standalone_context(backend='egl')
print(ctx.info['GL_RENDERER'], ctx.info['GL_VERSION'])
prog = ctx.program(vertex_shader=VS, fragment_shader=FS)
MAXC = 512
ms_c = ctx.renderbuffer((MAXC, MAXC), 4, samples=4); ms_d = ctx.depth_renderbuffer((MAXC, MAXC), samples=4)
fbo = ctx.framebuffer([ms_c], ms_d)
res_c = ctx.renderbuffer((MAXC, MAXC), 4); res = ctx.framebuffer([res_c])
GLB = os.path.join(ROOT, 'Bin', 'Debug', 'MonsterGlDump.dll')      # Directory.Build.props 가 빌드 결과를 GameViewer/Bin/<구성>/ 에 모은다

def render(model, role, t, yawdeg, cell=192, flash=0.0, tag='x', gain=1.0):
    out = os.path.join(HERE, 'out', 'out_' + tag)
    subprocess.run(['dotnet', GLB, model, out, role, str(t), str(yawdeg)], check=True)
    meta = open(out + '/meta.txt').read().split()
    nv, ni, nj = int(meta[0]), int(meta[1]), int(meta[2])
    vb = open(out + '/vb.bin', 'rb').read(); ib = open(out + '/ib.bin', 'rb').read()
    vbo = ctx.buffer(vb); ibo = ctx.buffer(ib)
    vao = ctx.vertex_array(prog, [(vbo, '3f 3f 2f 4f 4f', 'aPos', 'aNrm', 'aUv', 'aJ', 'aW')], index_buffer=ibo, index_element_size=4)
    texs = []
    for i in range(99):
        pth = out + '/tex%d.png' % i
        if not os.path.exists(pth): break
        img = Image.open(pth).convert('RGBA')
        tex = ctx.texture(img.size, 4, img.tobytes()); tex.build_mipmaps(); tex.filter = (moderngl.LINEAR_MIPMAP_LINEAR, moderngl.LINEAR); tex.repeat_x = tex.repeat_y = False
        texs.append(tex)
    parts = [tuple(map(int, l.split())) for l in open(out + '/parts.txt').read().split('\n') if l.strip()]
    pal = open(out + '/pal.bin', 'rb').read()
    prog['uBones'].write(pal.ljust(64 * 64, b'\0'))
    prog['uMvp'].write(open(out + '/mvp.bin', 'rb').read()); prog['uRot'].write(open(out + '/rot.bin', 'rb').read())
    prog['uTex'].value = 0; prog['uFlash'].value = flash; prog['uGain'].value = gain
    fbo.use(); ctx.viewport = (0, 0, cell, cell)
    ctx.clear(0, 0, 0, 0, depth=1.0)
    ctx.enable(moderngl.DEPTH_TEST | moderngl.CULL_FACE); ctx.cull_face = 'back'; ctx.front_face = 'ccw'
    for st, cnt, ti in parts:                    # 프리미티브마다 텍스처(앱의 MonsterGl.RenderTo 와 같다)
        texs[min(ti, len(texs) - 1)].use(0)
        vao.render(moderngl.TRIANGLES, vertices=cnt, first=st)
    ctx.copy_framebuffer(res, fbo)
    raw = res.read(viewport=(0, 0, cell, cell), components=4)
    a = np.frombuffer(raw, np.uint8).reshape(cell, cell, 4)[::-1].astype(np.float32) / 255.0      # 위 줄부터. 미리 곱한 알파
    return a

def composite(a, bg=(92, 112, 92)):
    bgc = np.array(bg, np.float32) / 255.0
    rgb = a[..., :3] + bgc * (1 - a[..., 3:4])
    return Image.fromarray((np.clip(rgb, 0, 1) * 255 + .5).astype(np.uint8))

if __name__ == '__main__':
    os.makedirs(os.path.join(HERE, 'out'), exist_ok=True)
    M = os.path.join(ROOT, 'Assets', 'models') + os.sep
    if len(sys.argv) > 2 and sys.argv[2] == 'heroes':
        jobs = [('Warrior', 'Idle', 0.2, 0), ('Warrior', 'Attack', 0.3, 90), ('Ranger', 'Attack', 0.3, 0), ('Ranger', 'Walk', 0.2, 45),
                ('Wizard', 'Cast', 0.4, 0), ('Wizard', 'Attack', 0.3, 315), ('Monk', 'Attack', 0.2, 0), ('Monk', 'Death', 5.0, 0)]
    else:
        jobs = [('Big_Yeti', 'Idle', 0.0, 0), ('Big_Yeti', 'Walk', 0.25, 90), ('Big_Yeti', 'Attack', 0.5, 0), ('Big_Yeti', 'Death', 0.75, 45),
                ('Blob_GreenBlob', 'Walk', 0.3, 0), ('Blob_Cactoro', 'Attack', 0.3, 180), ('Flying_Ghost', 'Walk', 0.2, 270), ('Flying_Goleling', 'Hit', 0.2, 0)]
    tiles = []
    for i, (m, r, t, y) in enumerate(jobs):
        a = render(M + m + '.gltf', r, t, y, tag=str(i), gain=1.5 if m in ('Warrior', 'Ranger', 'Wizard', 'Monk') else 1.0); tiles.append(composite(a))
        print(m, r, t, y, 'alpha cover %.3f' % (a[..., 3] > 0.5).mean(), 'max a-edge', float(((a[..., 3] > 0.05) & (a[..., 3] < 0.95)).sum()))
    W = Image.new('RGB', (192 * 4, 192 * 2))
    for i, im in enumerate(tiles): W.paste(im, (192 * (i % 4), 192 * (i // 4)))
    W.save(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'out', 'sheet.png'))
