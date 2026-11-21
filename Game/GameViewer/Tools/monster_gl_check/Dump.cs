using System; using System.IO; using GameViewer.Core.Model3D;
// 사용: glb <gltf> <출력폴더> <역할:Idle|Walk|Attack|Hit|Death> <초> <yawDeg>   -> vb.bin ib.bin tex.png pal.bin mvp.bin rot.bin meta.txt
class P { static void Main(string[] a) {
  GltfModel m = GltfModel.Load(a[0]); Directory.CreateDirectory(a[1]);
  AnimRole role=(AnimRole)Enum.Parse(typeof(AnimRole),a[2]); float t=float.Parse(a[3]); float yaw=float.Parse(a[4])*(float)Math.PI/180f;
  AnimClip c = AnimRoles.Find(m, role); Pose p=new Pose(m); Skeleton.Sample(m,c,t,p); float[] pal=new float[m.PaletteCount*16]; Skeleton.Palette(m,p,pal);
  float hm=Math.Max(0.2f,m.MaxY); float[] mvp=new float[16], rot=new float[16]; MonsterCamera.Build(yaw,hm,mvp,rot);
  float[] vb=m.BuildVertexBuffer(); var W=(Action<string,Array,int>)((n,arr,sz)=>{ byte[] b=new byte[arr.Length*sz]; Buffer.BlockCopy(arr,0,b,0,b.Length); File.WriteAllBytes(Path.Combine(a[1],n),b);});
  W("vb.bin",vb,4); W("ib.bin",m.Indices,4); W("pal.bin",pal,4); W("mvp.bin",mvp,4); W("rot.bin",rot,4); for(int i=0;i<m.Textures.Length;++i) File.WriteAllBytes(Path.Combine(a[1],"tex"+i+".png"),m.Textures[i]); File.WriteAllText(Path.Combine(a[1],"parts.txt"),string.Join("\n",Array.ConvertAll(m.Parts,q=>q.IndexStart+" "+q.IndexCount+" "+q.Texture)));
  File.WriteAllText(Path.Combine(a[1],"meta.txt"),$"{m.VertexCount} {m.IndexCount} {m.JointCount} {hm} {c?.Name} {(c==null?0:c.Duration)}");
} }
