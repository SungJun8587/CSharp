//***************************************************************************
// ServerPackets.cs : 서버가 클라이언트로 보내는 패킷을 만드는 도우미 (서버 구조체 레이아웃과 동일).
// 테스트의 가짜 서버와 오프라인 데모(DemoDirector)가 쓴다. 실제 서버에는 필요 없다.
//***************************************************************************
using System;

namespace GameViewer.Core.Protocol
{
    public static class ServerPackets
    {
        static PacketWriter W(ZonePacketType t, int size) { return new PacketWriter((ushort)t, size); }
        static PacketWriter G(GatewayPacketType t, int size) { return new PacketWriter((ushort)t, size); }

        public static byte[] LoginRes(bool ok, GatewayResult r, ulong sid)
        { PacketWriter w = G(GatewayPacketType.GwLoginRes, 14); w.U8((byte)(ok ? 1 : 0)); w.U8((byte)r); w.U64(sid); return w.Finish(); }
        public static byte[] EnterRes(bool ok, GatewayResult r, uint zone, float x, float y)
        { PacketWriter w = G(GatewayPacketType.GwEnterZoneRes, 18); w.U8((byte)(ok ? 1 : 0)); w.U8((byte)r); w.U32(zone); w.F32(x); w.F32(y); return w.Finish(); }
        public static byte[] ZoneLost(uint zone)
        { PacketWriter w = G(GatewayPacketType.GwZoneLostNotify, 8); w.U32(zone); return w.Finish(); }

        public static byte[] Spawn(ulong id, EntityType type, float x, float y, float dx, float dy, float speed, bool moving, int hp, int maxHp, bool dead, uint tpl)
        {
            PacketWriter w = W(ZonePacketType.ZcSpawnNotify, 47);
            w.U64(id); w.U8((byte)type); w.F32(x); w.F32(y); w.F32(dx); w.F32(dy); w.F32(speed);
            w.U8((byte)(moving ? 1 : 0)); w.U32((uint)hp); w.U32((uint)maxHp); w.U8((byte)(dead ? 1 : 0)); w.U32(tpl);
            return w.Finish();
        }
        public static byte[] Despawn(ulong id) { PacketWriter w = W(ZonePacketType.ZcDespawnNotify, 12); w.U64(id); return w.Finish(); }
        public static byte[] Move(ulong id, float sx, float sy, float dx, float dy, float speed)
        { PacketWriter w = W(ZonePacketType.ZcMoveNotify, 40); w.U64(id); w.F32(sx); w.F32(sy); w.F32(dx); w.F32(dy); w.F32(speed); w.U64(12345); return w.Finish(); }
        public static byte[] Stop(ulong id, float x, float y, bool arrived)
        { PacketWriter w = W(ZonePacketType.ZcStopNotify, 21); w.U64(id); w.F32(x); w.F32(y); w.U8((byte)(arrived ? 1 : 0)); return w.Finish(); }
        public static byte[] Correct(CorrectPosReason r, float x, float y)
        { PacketWriter w = W(ZonePacketType.ZcCorrectPosNotify, 13); w.U8((byte)r); w.F32(x); w.F32(y); return w.Finish(); }
        public static byte[] Damage(ulong atk, ulong tgt, int dmg, int hp, int maxHp)
        { PacketWriter w = W(ZonePacketType.ZcDamageNotify, 32); w.U64(atk); w.U64(tgt); w.U32((uint)dmg); w.U32((uint)hp); w.U32((uint)maxHp); return w.Finish(); }
        public static byte[] Death(ulong id, ulong killer) { PacketWriter w = W(ZonePacketType.ZcDeathNotify, 20); w.U64(id); w.U64(killer); return w.Finish(); }
        public static byte[] Respawn(ulong id, float x, float y, int hp, int maxHp, uint inv)
        { PacketWriter w = W(ZonePacketType.ZcRespawnNotify, 32); w.U64(id); w.F32(x); w.F32(y); w.U32((uint)hp); w.U32((uint)maxHp); w.U32(inv); return w.Finish(); }
        public static byte[] AttackReject(ulong t, AttackRejectReason r) { PacketWriter w = W(ZonePacketType.ZcAttackRejectNotify, 13); w.U64(t); w.U8((byte)r); return w.Finish(); }
        public static byte[] Heal(ulong id, int amt, int hp, int maxHp)
        { PacketWriter w = W(ZonePacketType.ZcHealNotify, 24); w.U64(id); w.U32((uint)amt); w.U32((uint)hp); w.U32((uint)maxHp); return w.Finish(); }
        public static byte[] Skill(ulong caster, uint skill, ulong target, float x, float y, uint hits)
        { PacketWriter w = W(ZonePacketType.ZcSkillNotify, 36); w.U64(caster); w.U32(skill); w.U64(target); w.F32(x); w.F32(y); w.U32(hits); return w.Finish(); }
        public static byte[] SkillReject(uint skill, SkillRejectReason r) { PacketWriter w = W(ZonePacketType.ZcSkillRejectNotify, 9); w.U32(skill); w.U8((byte)r); return w.Finish(); }
        public static byte[] Cast(ulong caster, uint skill, ulong target, float x, float y, uint castMs)
        { PacketWriter w = W(ZonePacketType.ZcSkillCastNotify, 36); w.U64(caster); w.U32(skill); w.U64(target); w.F32(x); w.F32(y); w.U32(castMs); return w.Finish(); }
        public static byte[] Cancel(ulong caster, uint skill, SkillCancelReason r) { PacketWriter w = W(ZonePacketType.ZcSkillCancelNotify, 17); w.U64(caster); w.U32(skill); w.U8((byte)r); return w.Finish(); }
        public static byte[] Projectile(uint pid, ulong caster, uint skill, ulong target, float x0, float y0, float x1, float y1, uint flight)
        { PacketWriter w = W(ZonePacketType.ZcProjectileNotify, 48); w.U32(pid); w.U64(caster); w.U32(skill); w.U64(target); w.F32(x0); w.F32(y0); w.F32(x1); w.F32(y1); w.U32(flight); return w.Finish(); }
        public static byte[] Impact(uint pid, ulong caster, uint skill, float x, float y, uint hits)
        { PacketWriter w = W(ZonePacketType.ZcSkillImpactNotify, 32); w.U32(pid); w.U64(caster); w.U32(skill); w.F32(x); w.F32(y); w.U32(hits); return w.Finish(); }
        public static byte[] Status(ulong ent, uint status, ulong caster, uint dur, bool applied)
        { PacketWriter w = W(ZonePacketType.ZcStatusNotify, 29); w.U64(ent); w.U32(status); w.U64(caster); w.U32(dur); w.U8((byte)(applied ? 1 : 0)); return w.Finish(); }

        public static Frame F(byte[] pkt) { return new Frame((ushort)(pkt[2] | (pkt[3] << 8)), pkt); }
    }
}
