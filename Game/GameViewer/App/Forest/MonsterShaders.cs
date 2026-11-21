//***************************************************************************
// MonsterShaders.cs : 몬스터 3D 그림용 GLSL(3.30 core). 뼈대 스키닝은 정점 셰이더에서 한다(관절 행렬 팔레트 uBones).
//   조명은 카메라 공간 기준 고정(왼쪽 위 앞) 하나 + 약한 테두리 빛. 
//   색은 미리 곱한 알파(premultiplied) 로 내보낸다: 몸 안쪽은 알파 1 이고 가장자리는 다중표본 해상으로 반투명이 된다.
//***************************************************************************
namespace GameViewer.App.Forest
{
    internal static class MonsterShaders
    {
        public const int MaxBones = 64;

        public const string Vs = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNrm;
layout(location = 2) in vec2 aUv;
layout(location = 3) in vec4 aJ;
layout(location = 4) in vec4 aW;
uniform mat4 uBones[64];
uniform mat4 uMvp;
uniform mat4 uRot;
out vec3 vN;
out vec2 vUv;
void main()
{
    mat4 sk = aW.x * uBones[int(aJ.x + 0.5)] + aW.y * uBones[int(aJ.y + 0.5)] + aW.z * uBones[int(aJ.z + 0.5)] + aW.w * uBones[int(aJ.w + 0.5)];
    vec4 p = sk * vec4(aPos, 1.0);
    vN = mat3(uRot) * (mat3(sk) * aNrm);
    vUv = aUv;
    gl_Position = uMvp * p;
}
";

        public const string Fs = @"#version 330 core
in vec3 vN;
in vec2 vUv;
out vec4 oC;
uniform sampler2D uTex;
uniform float uFlash;
uniform float uGain;
void main()
{
    vec3 n = normalize(vN);
    float lam = clamp(dot(n, vec3(-0.43553, 0.72590, 0.53233)), 0.0, 1.0);
    float rim = pow(clamp(1.0 - n.z, 0.0, 1.0), 3.0) * 0.10;
    vec3 c = texture(uTex, vUv).rgb * (0.62 + 0.50 * lam + rim) * uGain + (uGain - 1.0) * 0.10;
    c = mix(c, vec3(1.0), uFlash * 0.55);
    oC = vec4(min(c, vec3(1.0)), 1.0);
}
";
    }
}
