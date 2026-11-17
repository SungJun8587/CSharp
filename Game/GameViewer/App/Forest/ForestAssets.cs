//***************************************************************************
// ForestAssets.cs : 숲을 그리는 데 필요한 CPU 쪽 재료(배치, 메시, 텍스처)를 한 번에 만든다.
//   OpenGL 과 무관한 순수 계산이라 백그라운드 스레드에서 돌려도 된다(약 1~2초).
//   GPU 에 올리는 일은 ForestRenderer 가 UI 스레드에서 한다.
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.App.Forest
{
    public sealed class ForestAssets
    {
        public const int DefaultSeed = 7;
        public ForestWorld World;
        public ForestLayout Layout;
        public ForestCamera Camera;
        public MeshData[] Trunks = new MeshData[ForestLayout.TreeVariants], Leaves = new MeshData[ForestLayout.TreeVariants];
        public MeshData[] Rocks = new MeshData[ForestLayout.RockVariants];
        public MeshData Terrain, GrassTuft;
        public TexImage LeafTex, GrassTex, GroundTex, DetailTex;
        public float[][] TreeInst = new float[ForestLayout.TreeVariants][], RockInst = new float[ForestLayout.RockVariants][];
        public float[] GrassInst;

        public static ForestAssets Build(int seed)
        {
            ForestAssets a = new ForestAssets();
            a.World = new ForestWorld(seed);
            a.Layout = a.World.Generate();
            a.Camera = new ForestCamera(a.World);
            a.Camera.FitHeroStage(a.Layout);
            for (int v = 0; v < ForestLayout.TreeVariants; ++v)
            {
                MeshData t, l; ForestMeshes.BuildTree(v, out t, out l);
                a.Trunks[v] = t; a.Leaves[v] = l; a.TreeInst[v] = Flatten(a.Layout.Trees[v]);
            }
            for (int v = 0; v < ForestLayout.RockVariants; ++v) { a.Rocks[v] = ForestMeshes.BuildRock(v); a.RockInst[v] = Flatten(a.Layout.Rocks[v]); }
            a.Terrain = ForestMeshes.BuildTerrain(a.World, 120);
            a.GrassTuft = ForestMeshes.BuildGrassTuft();
            a.GrassInst = Flatten(a.Layout.Grass);
            a.LeafTex = ForestTextures.LeafCluster(256);
            a.GrassTex = ForestTextures.GrassTuft(128, 256);
            a.GroundTex = ForestTextures.GroundMap(a.World, 1024);
            a.DetailTex = ForestTextures.GroundDetail(256);
            return a;
        }

        /// <summary>인스턴스 목록을 GPU 에 그대로 올릴 float 배열(인스턴스당 8개)로 편다.</summary>
        public static float[] Flatten(List<Inst> l)
        {
            float[] a = new float[l.Count * 8];
            for (int i = 0; i < l.Count; ++i)
            {
                Inst t = l[i]; int o = i * 8;
                a[o] = t.X; a[o + 1] = t.Y; a[o + 2] = t.Z; a[o + 3] = t.Rot; a[o + 4] = t.Scale; a[o + 5] = t.Tint; a[o + 6] = t.A; a[o + 7] = t.B;
            }
            return a;
        }
    }
}
