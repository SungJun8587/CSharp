using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameViewer.Core.Protocol;
using Pk = GameViewer.Core.Protocol.ServerPackets;

namespace GameViewer.Tests
{
    public static class PacketTests
    {
        public static void SizesMatchCpp()
        {
            string path = Program.Dir("cpp_packet_sizes.txt");
            if (!File.Exists(path)) { Console.WriteLine("  (skip: cpp_packet_sizes.txt not found)"); return; }
            Assembly asm = typeof(Frame).Assembly;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] p = line.Trim().Split(' ');
                if (p.Length != 2) continue;
                string cpp = p[0];                                  // e.g. ZcSpawnNotifyPacket
                int expect = int.Parse(p[1]);
                string name = cpp.EndsWith("Packet") ? cpp.Substring(0, cpp.Length - 6) : cpp;
                int actual = -1;
                Type t = asm.GetType("GameViewer.Core.Protocol." + name);
                if (t != null)
                {
                    FieldInfo f = t.GetField("Size");
                    if (f != null) actual = (int)f.GetRawConstantValue();
                }
                else
                {
                    FieldInfo f = typeof(Build).GetField(name + "Size");
                    if (f != null) actual = (int)f.GetRawConstantValue();
                }
                if (actual < 0) { Console.WriteLine("  note: " + cpp + " has no C# class (server-only / not needed)"); continue; }
                T.Eq(expect, actual, "sizeof(" + cpp + ")");
            }
        }

        public static void Framer()
        {
            PacketFramer fr = new PacketFramer(4096);
            List<Frame> o = new List<Frame>();
            byte[] a = Pk.Despawn(7), b = Pk.Stop(9, 1.5f, 2.5f, true);
            byte[] all = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, all, 0, a.Length); Buffer.BlockCopy(b, 0, all, a.Length, b.Length);

            // 한 번에
            fr.Push(all, 0, all.Length, o);
            T.Eq(2, o.Count, "two packets in one push");
            // 한 바이트씩
            o.Clear();
            for (int i = 0; i < all.Length; ++i) fr.Push(all, i, 1, o);
            T.Eq(2, o.Count, "two packets byte by byte");
            T.Eq((int)ZonePacketType.ZcDespawnNotify, (int)o[0].Type, "first type");
            T.Eq((int)ZonePacketType.ZcStopNotify, (int)o[1].Type, "second type");
            // 중간에 잘림
            o.Clear();
            fr.Push(all, 0, a.Length + 3, o);
            T.Eq(1, o.Count, "partial second packet waits");
            fr.Push(all, a.Length + 3, b.Length - 3, o);
            T.Eq(2, o.Count, "second completes");
            // 큰 패킷(버퍼 확장)
            byte[] big = new byte[4000]; big[0] = 0xA0; big[1] = 0x0F; big[2] = 0xD1; big[3] = 0x07;
            o.Clear();
            fr.Push(big, 0, big.Length, o);
            T.Eq(1, o.Count, "4000 byte packet");
            // 잘못된 크기
            bool threw = false;
            try { fr.Push(new byte[] { 3, 0, 1, 0 }, 0, 4, o); } catch (PacketFormatException) { threw = true; }
            T.Check(threw, "size<4 is a protocol error");
            fr = new PacketFramer(4096);
            threw = false;
            try { fr.Push(new byte[] { 0x01, 0x20, 1, 0 }, 0, 4, o); } catch (PacketFormatException) { threw = true; }
            T.Check(threw, "size>max is a protocol error");
        }

        public static void Parsers()
        {
            ZcSpawnNotify s = ZcSpawnNotify.Parse(Pk.Spawn(0x8000000000000001UL, EntityType.Monster, 1.5f, -2.25f, 10f, 20f, 3.5f, true, 120, 150, false, 42));
            T.Eq(0x8000000000000001UL, s.EntityId, "spawn id (top bit)");
            T.Eq((byte)1, s.EntityType, "spawn type");
            T.Near(1.5, s.PosX, 1e-6, "spawn x"); T.Near(-2.25, s.PosY, 1e-6, "spawn y");
            T.Near(10, s.DestX, 1e-6, "spawn dest x"); T.Near(3.5, s.Speed, 1e-6, "spawn speed");
            T.Check(s.Moving && !s.Dead, "spawn flags");
            T.Eq(120, s.Hp, "spawn hp"); T.Eq(150, s.MaxHp, "spawn maxhp"); T.Eq(42u, s.TemplateId, "spawn template");

            ZcDamageNotify d = ZcDamageNotify.Parse(Pk.Damage(5, 6, 77, 23, 100));
            T.Eq(5UL, d.AttackerId, "dmg attacker"); T.Eq(6UL, d.TargetId, "dmg target");
            T.Eq(77, d.Damage, "dmg"); T.Eq(23, d.Hp, "dmg hp"); T.Eq(100, d.MaxHp, "dmg maxhp");

            ZcProjectileNotify p = ZcProjectileNotify.Parse(Pk.Projectile(9, 1, 3, 2, 1f, 2f, 3f, 4f, 250));
            T.Eq(9u, p.ProjectileId, "proj id"); T.Eq(250u, p.FlightMs, "proj flight"); T.Near(4, p.Y1, 1e-6, "proj y1");

            ZcStatusNotify st = ZcStatusNotify.Parse(Pk.Status(3, 1, 4, 6000, true));
            T.Check(st.Applied, "status applied"); T.Eq(6000u, st.DurationMs, "status dur");

            ZcSkillRejectNotify rj = ZcSkillRejectNotify.Parse(Pk.SkillReject(7, SkillRejectReason.Cooldown));
            T.Eq(SkillRejectReason.Cooldown, rj.Reason, "skill reject reason");

            // 짧은 패킷은 예외
            bool threw = false;
            try { ZcSpawnNotify.Parse(new byte[10]); } catch (PacketFormatException) { threw = true; }
            T.Check(threw, "truncated packet throws");
        }

        public static void Builders()
        {
            byte[] l = Build.GwLoginReq(0x1122334455667788UL);
            T.Eq(12, l.Length, "login size");
            T.Eq(12, l[0] | (l[1] << 8), "login header size");
            T.Eq(1, l[2] | (l[3] << 8), "login header type");
            T.Eq((byte)0x88, l[4], "login charId LE low byte"); T.Eq((byte)0x11, l[11], "login charId LE high byte");

            byte[] e = Build.GwEnterZoneReq(5, 2);
            T.Eq(3, e[2], "enter type"); T.Eq(5, e[4], "enter zone"); T.Eq(2, e[8], "enter hero");

            byte[] m = Build.ZcMoveReq(1f, 2f, 3f, 4f);
            T.Eq(20, m.Length, "move size"); T.Eq(2001, m[2] | (m[3] << 8), "move type");
            T.Near(4.0, BitConverter.ToSingle(m, 16), 1e-6, "move dest y");

            byte[] a = Build.ZcAttackReq(99);
            T.Eq(12, a.Length, "attack size"); T.Eq(99, a[4], "attack target");

            byte[] s = Build.ZcSkillReq(3, 7, 1.5f, 2.5f);
            T.Eq(24, s.Length, "skill size"); T.Eq(3, s[4], "skill id"); T.Eq(7, s[8], "skill target");
            T.Near(2.5, BitConverter.ToSingle(s, 20), 1e-6, "skill y");

            T.Eq(4, Build.GwLeaveZoneReq().Length, "leave size");
        }
    }
}
