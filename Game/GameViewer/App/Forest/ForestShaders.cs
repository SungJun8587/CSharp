//***************************************************************************
// ForestShaders.cs : 숲 장면의 GLSL 3.30 셰이더 소스. 문자열일 뿐이라 OpenGL 없이도 읽고 테스트할 수 있다.
// 공통부(Common): 하늘색, 안개, 그림자 조회, 조명. 모든 프래그먼트 셰이더가 앞에 붙여 쓴다.
//***************************************************************************
namespace GameViewer.App.Forest
{
    public static class ForestShaders
    {
        private const string Ver = "#version 330 core\n";

        public const string CommonBase = @"
uniform vec3 uCam;
uniform vec3 uSunDir;          // 태양 쪽을 가리키는 단위 벡터
uniform float uFogDensity;
uniform float uTime;
uniform sampler2D uShadow;
uniform mat4 uLightVP;
uniform float uShadowTexel;
uniform float uShadowOn;

const vec3 FOG_COL = vec3(0.70, 0.76, 0.74);

float hash21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float vnoise(vec2 p)
{
    vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i), b = hash21(i + vec2(1.0, 0.0)), c = hash21(i + vec2(0.0, 1.0)), d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

vec3 skyColor(vec3 dir)
{
    float h = clamp(dir.y, 0.0, 1.0);
    vec3 horizon = FOG_COL * 1.10;
    vec3 zenith = vec3(0.50, 0.64, 0.80);
    vec3 c = mix(horizon, zenith, pow(h, 0.65));
    float sd = max(dot(dir, uSunDir), 0.0);
    c += vec3(1.0, 0.86, 0.60) * (pow(sd, 6.0) * 0.30 + pow(sd, 40.0) * 0.55);
    c += vec3(1.5, 1.35, 1.0) * smoothstep(0.9993, 0.9998, sd);
    return c;
}

vec3 applyFog(vec3 col, vec3 wp)
{
    vec3 v = wp - uCam;
    float d = length(v);
    vec3 dir = v / max(d, 0.001);
    float dens = uFogDensity * (1.0 + 1.1 * exp(-max(wp.y, 0.0) * 0.14));
    float fog = clamp(1.0 - exp(-d * dens), 0.0, 0.97);
    vec3 fc = mix(FOG_COL, vec3(1.0, 0.88, 0.66), pow(max(dot(dir, uSunDir), 0.0), 5.0) * 0.75);
    return mix(col, fc, fog);
}

";

        // 조명(그림자 조회 + 태양광). 화면 좌표(gl_FragCoord)를 쓰므로 프래그먼트 셰이더에만 붙인다.
        public const string Lighting = @"
float shadowAt(vec3 wp, float ndl)
{
    if (uShadowOn < 0.5) return 1.0;
    vec4 lp = uLightVP * vec4(wp, 1.0);
    vec3 p = lp.xyz / lp.w * 0.5 + 0.5;
    if (p.x < 0.0 || p.x > 1.0 || p.y < 0.0 || p.y > 1.0 || p.z > 1.0) return 1.0;
    float bias = 0.0012 + 0.0035 * (1.0 - ndl);
    // 화소마다 돌린 원판 표본 16개로 그림자 가장자리를 부드럽게(계단 없이) 만든다
    float rot = 6.2831853 * fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    float s = 0.0;
    for (int i = 0; i < 16; ++i)
    {
        float r = sqrt((float(i) + 0.5) / 16.0) * 2.6;
        float a = float(i) * 2.399963 + rot;
        s += step(p.z - bias, texture(uShadow, p.xy + vec2(cos(a), sin(a)) * (r * uShadowTexel)).r);
    }
    return s / 16.0;
}

// albedo 에 태양광(그림자 반영) + 하늘 반사광을 입힌다. trans > 0 이면 잎처럼 빛이 비쳐 보이고, softShadow 는 그림자 속에서도 남는 밝기.
vec3 shade(vec3 albedo, vec3 N, vec3 wp, float trans, float softShadow)
{
    float ndl = max(dot(N, uSunDir), 0.0);
    float sh = mix(softShadow, 1.0, shadowAt(wp, ndl));
    vec3 sunC = vec3(1.0, 0.88, 0.68) * 2.1;
    vec3 skyA = mix(vec3(0.07, 0.09, 0.07), vec3(0.28, 0.38, 0.46), N.y * 0.5 + 0.5);
    vec3 col = albedo * (sunC * ndl * sh + skyA);
    if (trans > 0.0)
    {
        vec3 V = normalize(uCam - wp);
        float t = pow(max(dot(-V, uSunDir), 0.0), 3.0);
        col += albedo * vec3(1.0, 0.92, 0.5) * (t * 1.4 * sh * trans);
    }
    return col;
}
";

        /// <summary>프래그먼트 셰이더용 공통부 전체.</summary>
        public const string Common = CommonBase + Lighting;

        // ------------------------------------------------------------------ 나무/바위/땅 (같은 정점 셰이더)

        public const string MeshVs = Ver + @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNor;
layout(location = 2) in vec2 aUv;
layout(location = 3) in float aMat;
layout(location = 4) in vec4 iA;     // x, y, z, 회전
layout(location = 5) in vec4 iB;     // 크기, 색 변화, 보조, 보조
uniform mat4 uVP;
uniform float uTime;
uniform float uWind;
out vec3 vWp;
out vec3 vN;
out vec2 vUv;
out float vMat;
out float vTint;
out float vLy;
void main()
{
    float c = cos(iA.w), s = sin(iA.w);
    vec3 p = aPos * iB.x;
    vec3 r = vec3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z);
    vec3 rn = vec3(c * aNor.x + s * aNor.z, aNor.y, -s * aNor.x + c * aNor.z);
    vec3 wp = r + iA.xyz;
    float sway = uWind * clamp(r.y * 0.1, 0.0, 1.5);
    wp.x += sin(uTime * 1.1 + iA.x * 0.37 + r.y * 0.7) * 0.06 * sway;
    wp.z += cos(uTime * 0.9 + iA.z * 0.41 + r.y * 0.6) * 0.06 * sway;
    vWp = wp; vN = rn; vUv = aUv; vMat = aMat; vTint = iB.y; vLy = aPos.y * iB.x;
    gl_Position = uVP * vec4(wp, 1.0);
}
";

        public const string MeshFs = Ver + Common + @"
in vec3 vWp;
in vec3 vN;
in vec2 vUv;
in float vMat;
in float vTint;
in float vLy;
out vec4 oColor;
uniform int uMode;              // 0 줄기, 1 잎, 2 바위
uniform sampler2D uLeafTex;
uniform vec3 uBark;
uniform float uA2C;             // 1 이면 알파-투-커버리지(다중 표본)로 잎 가장자리를 부드럽게
void main()
{
    vec3 N = normalize(vN);
    vec3 albedo;
    float alpha = 1.0, trans = 0.0, soft = 0.0;
    if (uMode == 1)
    {
        vec4 t = texture(uLeafTex, vUv);
        if (uA2C > 0.5) { alpha = clamp((t.a - 0.5) / max(fwidth(t.a), 0.001) + 0.5, 0.0, 1.0); if (alpha <= 0.0) discard; }
        else if (t.a < 0.5) discard;
        albedo = t.rgb * (0.72 + 0.5 * vMat) * vTint;
        trans = 1.0; soft = 0.45;
    }
    else if (uMode == 0)
    {
        float streak = vnoise(vec2(vUv.x * 22.0, vUv.y * 1.2));
        float fine = vnoise(vec2(vUv.x * 60.0, vUv.y * 5.0));
        albedo = uBark * (0.45 + 0.75 * streak) * (0.8 + 0.4 * fine) * vTint;
        float moss = smoothstep(2.4, 0.0, vLy) * smoothstep(0.1, 0.9, vnoise(vec2(vUv.x * 9.0, vUv.y * 2.0 + 3.0)));
        albedo = mix(albedo, vec3(0.10, 0.20, 0.06), clamp(moss * 0.8, 0.0, 0.85));
    }
    else
    {
        float n = vnoise(vWp.xz * 3.0 + vWp.y * 2.0);
        vec3 stone = vec3(0.30, 0.30, 0.29) * (0.6 + 0.8 * n) * vTint;
        albedo = mix(stone, vec3(0.13, 0.24, 0.06), smoothstep(0.25, 0.75, N.y));
    }
    vec3 col = shade(albedo, N, vWp, trans, soft);
    col = applyFog(col, vWp);
    oColor = vec4(col, alpha);
}
";

        public const string TerrainFs = Ver + Common + @"
in vec3 vWp;
in vec3 vN;
in vec2 vUv;
in float vMat;
in float vTint;
in float vLy;
out vec4 oColor;
uniform sampler2D uGround;
uniform sampler2D uDetail;
void main()
{
    vec3 N = normalize(vN);
    vec4 g = texture(uGround, (vWp.xz + 100.0) / 200.0);
    vec3 d1 = texture(uDetail, vWp.xz * 0.31).rgb;
    vec3 d2 = texture(uDetail, vWp.xz * 0.047).rgb;
    float n = d1.r * 0.6 + d2.r * 0.4;
    float dist = g.r * 3.0;
    float pm = 1.0 - smoothstep(0.45, 0.95, dist + (d1.g - 0.5) * 0.55);
    vec3 moss = mix(vec3(0.06, 0.13, 0.035), vec3(0.15, 0.24, 0.06), g.g);
    vec3 grass = mix(moss, vec3(0.17, 0.12, 0.06), smoothstep(0.45, 0.8, g.b) * 0.6) * (0.65 + 0.7 * n);
    vec3 dirt = mix(vec3(0.22, 0.13, 0.06), vec3(0.40, 0.26, 0.12), d1.b) * (0.75 + 0.5 * d2.g);
    vec3 albedo = mix(grass, dirt, pm);
    vec3 col = shade(albedo, N, vWp, 0.0, 0.0);
    col = applyFog(col, vWp);
    oColor = vec4(col, 1.0);
}
";

        // ------------------------------------------------------------------ 그림자 깊이

        public const string ShadowVs = Ver + @"
layout(location = 0) in vec3 aPos;
layout(location = 2) in vec2 aUv;
layout(location = 4) in vec4 iA;
layout(location = 5) in vec4 iB;
uniform mat4 uLightVP;
out vec2 vUv;
void main()
{
    float c = cos(iA.w), s = sin(iA.w);
    vec3 p = aPos * iB.x;
    vec3 wp = vec3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z) + iA.xyz;
    vUv = aUv;
    gl_Position = uLightVP * vec4(wp, 1.0);
}
";

        public const string ShadowFs = Ver + @"
in vec2 vUv;
uniform int uMode;
uniform sampler2D uLeafTex;
void main()
{
    if (uMode == 1 && texture(uLeafTex, vUv).a < 0.5) discard;
}
";

        // ------------------------------------------------------------------ 풀

        public const string GrassVs = Ver + @"
layout(location = 0) in vec3 aPos;
layout(location = 2) in vec2 aUv;
layout(location = 4) in vec4 iA;
layout(location = 5) in vec4 iB;
uniform mat4 uVP;
uniform vec3 uCam;
uniform float uTime;
out vec3 vWp;
out vec2 vUv;
out float vTint;
out float vH;
void main()
{
    float dist = length(iA.xz - uCam.xz);
    float sc = iB.x * (1.0 - smoothstep(48.0, 62.0, dist));
    float c = cos(iA.w), s = sin(iA.w);
    vec3 p = vec3(aPos.x * 1.15, aPos.y * 0.62, aPos.z * 1.15) * sc;
    vec3 wp = vec3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z) + iA.xyz;
    float h = aPos.y;
    wp.x += (sin(uTime * 1.7 + iB.z) * 0.10 + sin(uTime * 0.6 + iA.x * 0.3) * 0.06) * h * h * sc;
    wp.z += cos(uTime * 1.3 + iB.z) * 0.07 * h * h * sc;
    // 풀 색이 군데군데 짙고 옅게: 위치로 만든 느린 얼룩
    float grassPatch = 0.72 + 0.28 * sin(iA.x * 0.21 + 1.3) * sin(iA.z * 0.17 - 0.7) + 0.12 * sin(iA.x * 0.9 + iA.z * 0.6);
    vWp = wp; vUv = aUv; vTint = iB.y * grassPatch; vH = h;
    gl_Position = uVP * vec4(wp, 1.0);
}
";

        public const string GrassFs = Ver + Common + @"
in vec3 vWp;
in vec2 vUv;
in float vTint;
in float vH;
out vec4 oColor;
uniform sampler2D uGrassTex;
uniform float uA2C;
void main()
{
    vec4 t = texture(uGrassTex, vUv);
    float alpha = 1.0;
    if (uA2C > 0.5) { alpha = clamp((t.a - 0.5) / max(fwidth(t.a), 0.001) + 0.5, 0.0, 1.0); if (alpha <= 0.0) discard; }
    else if (t.a < 0.5) discard;
    vec3 albedo = t.rgb * vTint * vec3(0.95, 1.0, 0.85);
    float ao = mix(0.35, 1.0, vH);
    vec3 N = normalize(mix(vec3(0.0, 1.0, 0.0), uSunDir, 0.35));
    vec3 col = shade(albedo, N, vWp, 0.8, 0.35) * ao;
    col = applyFog(col, vWp);
    oColor = vec4(col, alpha);
}
";

        // ------------------------------------------------------------------ 하늘, 먼지, 후처리

        public const string SkyVs = Ver + @"
out vec2 vP;
void main()
{
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    vP = p * 2.0 - 1.0;
    gl_Position = vec4(vP, 1.0, 1.0);
}
";

        public const string SkyFs = Ver + Common + @"
in vec2 vP;
out vec4 oColor;
uniform vec3 uFwd;
uniform vec3 uRight;
uniform vec3 uUp;
uniform vec2 uTan;
void main()
{
    vec3 ray = normalize(uFwd + vP.x * uTan.x * uRight + vP.y * uTan.y * uUp);
    oColor = vec4(skyColor(ray), 1.0);
}
";

        public const string MoteVs = Ver + CommonBase + @"
layout(location = 0) in vec4 aP;       // x, y, z 씨앗 위치, w 난수
uniform mat4 uVP;
uniform float uPx;                     // 가까이서 보이는 크기(픽셀)
out float vBright;
void main()
{
    vec3 p = aP.xyz + vec3(sin(uTime * 0.13 + aP.w * 17.0), sin(uTime * 0.17 + aP.w * 5.0) * 0.6 + uTime * 0.03, cos(uTime * 0.11 + aP.w * 11.0)) * 1.5;
    vec3 r = p - uCam;
    r.xz = mod(r.xz + 14.0, 28.0) - 14.0;
    r.y = mod(r.y + 3.0, 8.0) - 3.0;
    p = uCam + r;
    float dist = length(r);
    vec4 lp = uLightVP * vec4(p, 1.0);
    vec3 q = lp.xyz / lp.w * 0.5 + 0.5;
    float lit = 1.0;
    if (uShadowOn > 0.5 && q.x > 0.0 && q.x < 1.0 && q.y > 0.0 && q.y < 1.0)
        lit = step(q.z - 0.004, textureLod(uShadow, q.xy, 0.0).r);
    float tw = 0.55 + 0.45 * sin(uTime * 1.3 + aP.w * 40.0);
    vBright = (0.06 + 0.94 * lit) * tw * (1.0 - smoothstep(9.0, 14.0, dist)) * smoothstep(0.4, 1.5, dist);
    gl_PointSize = clamp(uPx / max(dist, 0.6), 1.5, 8.0);
    gl_Position = uVP * vec4(p, 1.0);
}
";

        public const string MoteFs = Ver + @"
in float vBright;
out vec4 oColor;
void main()
{
    float d = length(gl_PointCoord - vec2(0.5)) * 2.0;
    float a = 1.0 - smoothstep(0.0, 1.0, d);
    a *= a;
    oColor = vec4(vec3(1.0, 0.92, 0.7) * vBright, a * vBright);
}
";

        public const string PostVs = Ver + @"
out vec2 vUv;
void main()
{
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
";

        public const string PostFs = Ver + @"
in vec2 vUv;
out vec4 oColor;
uniform sampler2D uColor;
uniform sampler2D uDepth;
uniform vec2 uSunUv;
uniform float uSunVis;
uniform vec2 uRes;
uniform float uTime;
float hash12(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
void main()
{
    vec3 col = texture(uColor, vUv).rgb;

    // 빛줄기: 하늘(깊이 1)의 밝은 점들을 태양 쪽으로 번지게 한다. 나뭇가지와 잎이 가린 만큼 줄기로 갈라져 보인다.
    if (uSunVis > 0.001)
    {
        const int N = 40;
        vec2 stepv = (uSunUv - vUv) / float(N) * 0.9;
        vec2 uv = vUv + stepv * hash12(gl_FragCoord.xy + uTime);
        float decay = 1.0;
        vec3 acc = vec3(0.0);
        for (int i = 0; i < N; ++i)
        {
            uv += stepv;
            float inb = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            float sky = step(0.99999, texture(uDepth, uv).r);
            vec3 c = texture(uColor, uv).rgb;
            float lum = dot(c, vec3(0.3, 0.59, 0.11));
            acc += c * (sky * inb * lum * lum) * decay;
            decay *= 0.955;
        }
        col += acc / float(N) * vec3(1.0, 0.9, 0.7) * uSunVis * 2.2;
    }

    // 블룸: 아주 밝은 점(빛나는 룬/불씨/보석, 하늘 조각)이 주변으로 번진다. 원판 표본 28개.
    {
        vec3 g = vec3(0.0);
        float rot = 6.2831853 * hash12(gl_FragCoord.xy + 7.0);
        for (int i = 0; i < 28; ++i)
        {
            float r = sqrt((float(i) + 0.5) / 28.0);
            float a = float(i) * 2.399963 + rot;
            vec2 o = vec2(cos(a), sin(a)) * r * vec2(0.020, 0.020 * uRes.x / uRes.y);
            vec3 c = texture(uColor, vUv + o).rgb;
            float lum = dot(c, vec3(0.3, 0.59, 0.11));
            g += c * smoothstep(0.80, 1.05, lum) * (1.15 - r);
        }
        col += g / 28.0 * 0.75;
    }

    float l = dot(col, vec3(0.3, 0.59, 0.11));
    col = mix(vec3(l), col, 0.9);
    col = clamp(col, 0.0, 1.0);
    col = mix(col, col * col * (3.0 - 2.0 * col), 0.45);                 // 약한 S자 곡선: 그늘은 더 깊게, 빛은 더 또렷하게
    col *= mix(vec3(0.94, 1.0, 0.99), vec3(1.0, 1.0, 0.97), smoothstep(0.1, 0.6, l));  // 그늘은 살짝 푸르게
    vec2 q = vUv - 0.5;
    float vg = 1.0 - smoothstep(0.30, 0.85, length(q * vec2(1.0, 1.05)));
    col *= mix(0.58, 1.0, vg);
    col += (hash12(gl_FragCoord.xy * 1.7 + uTime * 13.0) - 0.5) * 0.02;
    oColor = vec4(col, 1.0);
}
";

        // ------------------------------------------------------------------ 영웅 (영웅 선택 화면): 툰 음영 + 윤곽선(뒤집은 껍질)

        public const string HeroVs = Ver + @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aN;
layout(location = 2) in vec3 aCol;
layout(location = 3) in vec3 aSh;
layout(location = 4) in vec2 aSE;
uniform mat4 uVP;
uniform float uOutlinePass;     // 1 이면 윤곽선 패스: 법선 방향으로 부풀린다
uniform float uOutline;         // 부풀리는 폭(거리 1 당 월드 길이 = 화면 픽셀 수 x 픽셀당 각도)
out vec3 vWp;
out vec3 vN;
out vec3 vCol;
out vec3 vSh;
out vec2 vSE;
void main()
{
    vec3 p = aPos;
    if (uOutlinePass > 0.5)
    {
        float w = (uVP * vec4(p, 1.0)).w;
        p += normalize(aN) * (uOutline * w);
    }
    vWp = p; vN = aN; vCol = aCol; vSh = aSh; vSE = aSE;
    gl_Position = uVP * vec4(p, 1.0);
}
";

        public const string HeroFs = Ver + Common + @"
in vec3 vWp;
in vec3 vN;
in vec3 vCol;
in vec3 vSh;
in vec2 vSE;
out vec4 oColor;
uniform float uOutlinePass;
uniform vec3 uAura;           // 직업 빛깔(림 라이트/바닥 반사광)
uniform float uFoot;           // 발 밑 높이
void main()
{
    // 윤곽선: 검정이 아니라 자기 색을 아주 어둡게 한 색(만화 영화 느낌). 빛나는 부분은 윤곽선 없음.
    if (uOutlinePass > 0.5) { if (vSE.y > 0.5) discard; oColor = vec4(applyFog(vCol * vec3(0.20, 0.15, 0.17) + vec3(0.012, 0.006, 0.010), vWp), 1.0); return; }
    vec3 N = normalize(vN);
    vec3 V = normalize(uCam - vWp);
    float ndl = dot(N, uSunDir);
    float sh = shadowAt(vWp + N * 0.02, max(ndl, 0.0));

    // 3점 조명: 주광(태양) + 보조광(카메라 왼쪽 위, 차가운 빛) + 역광(뒤에서 오는 직업색)
    float key = smoothstep(-0.10, 0.42, ndl) * mix(0.62, 1.0, sh);
    float band = smoothstep(0.55, 0.80, ndl) * mix(0.5, 1.0, sh);                // 두 번째 음영 단: 가장 밝은 면
    vec3 camR = normalize(cross(vec3(0.0, 1.0, 0.0), V));
    vec3 fillDir = normalize(V * 0.35 + camR * 0.75 + vec3(0.0, 0.45, 0.0));
    float fill = smoothstep(-0.2, 0.8, dot(N, fillDir));
    vec3 sunC = vec3(1.0, 0.93, 0.80);
    vec3 skyA = mix(vec3(0.60, 0.64, 0.60), vec3(0.78, 0.86, 0.96), N.y * 0.5 + 0.5);
    vec3 shadeC = vCol * vSh * (skyA * 1.12);                                    // 그늘: 채도 있는 색조(vSh)
    vec3 litC = vCol * (sunC * 1.00 + skyA * 0.16);
    vec3 col = mix(shadeC, litC, key);
    col += vCol * sunC * (band * 0.10);
    col += vCol * vec3(0.55, 0.70, 1.0) * (fill * 0.13 * (1.0 - key * 0.6));      // 그늘진 쪽에 차가운 보조광이 닿는다

    // 윤곽광: 시선과 비스듬한 가장자리. 따뜻한 해빛색 + 직업색(뒤에서 비추는 빛)
    float ndv = max(dot(N, V), 0.0);
    float rim = pow(1.0 - ndv, 3.0) * smoothstep(-0.2, 0.5, ndl + 0.2);
    col += vec3(1.0, 0.90, 0.70) * (rim * 0.16);
    float rimA = pow(1.0 - ndv, 2.2);
    col += uAura * (rimA * 0.42);
    col += uAura * (0.34 * (1.0 - smoothstep(0.0, 1.1, vWp.y - uFoot)) * (0.55 + 0.45 * (1.0 - max(N.y, 0.0))));

    // 반짝임: 해빛 하이라이트(툰 경계) + 금속/머리카락은 하늘 반사와 넓고 부드러운 광택을 더한다
    float H = pow(max(dot(N, normalize(uSunDir + V)), 0.0), 40.0);
    col += vec3(1.0, 0.96, 0.88) * (vSE.x * smoothstep(0.30, 0.45, H) * key * 0.85);
    float Hs = pow(max(dot(N, normalize(uSunDir + V)), 0.0), 9.0);
    vec3 refl = reflect(-V, N);
    vec3 env = mix(vec3(0.20, 0.24, 0.20), vec3(0.75, 0.88, 1.0), smoothstep(-0.2, 0.9, refl.y));
    col += (env * 0.30 + vec3(1.0, 0.95, 0.85) * (Hs * 0.22 * key)) * (vSE.x * vSE.x);

    // 발 밑 접지 어둠(바닥과 닿는 부분이 둥둥 떠 보이지 않게)
    col *= mix(0.72, 1.0, smoothstep(0.0, 0.30, vWp.y - uFoot));

    // 빛나는 도형: 밝게 + 약간 색을 더해 후처리 번짐(블룸)이 잡도록 1 이상으로
    col = mix(col, vCol * 1.9 + vec3(0.10), vSE.y);
    oColor = vec4(applyFog(col, vWp), 1.0);
}
";

        public const string HeroShadowVs = Ver + @"
layout(location = 0) in vec3 aPos;
uniform mat4 uLightVP;
void main() { gl_Position = uLightVP * vec4(aPos, 1.0); }
";

        public const string HeroShadowFs = Ver + @"
void main() { }
";
    }
}
