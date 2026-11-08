//***************************************************************************
// GameViewer.Tests : 의존성 없는 콘솔 테스트. mono/mcs 와 dotnet 양쪽에서 동작한다. 종료 코드 0 = 전부 통과.
//  - 패킷 크기를 C++ sizeof 표(cpp_packet_sizes.txt)와 대조
//  - 프레이머/파서/JSON/데이터 로더/월드 모델 단위 테스트
//  - 가짜 Gateway(TCP)를 띄워 로그인 -> 입장 -> 전투 패킷 -> 명령 송신까지 왕복 테스트
//***************************************************************************
using System;
using System.Collections.Generic;
using System.IO;

namespace GameViewer.Tests
{
    public static class T
    {
        public static int Passed, Failed;
        public static void Check(bool ok, string what)
        {
            if (ok) { ++Passed; return; }
            ++Failed; Console.WriteLine("  FAIL: " + what);
        }
        public static void Eq<A>(A expect, A actual, string what)
        {
            if (EqualityComparer<A>.Default.Equals(expect, actual)) { ++Passed; return; }
            ++Failed; Console.WriteLine("  FAIL: " + what + " expected=" + expect + " actual=" + actual);
        }
        public static void Near(double expect, double actual, double eps, string what)
        {
            if (Math.Abs(expect - actual) <= eps) { ++Passed; return; }
            ++Failed; Console.WriteLine("  FAIL: " + what + " expected=" + expect + " actual=" + actual);
        }
        public static void Run(string name, Action a)
        {
            Console.WriteLine("[" + name + "]");
            try { a(); }
            catch (Exception e) { ++Failed; Console.WriteLine("  EXCEPTION: " + e); }
        }
    }

    public static class Program
    {
        /// <summary>테스트 파일/폴더를 실행 위치 기준으로 찾는다.</summary>
        public static string Dir(string sub)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] cands = new string[] { Path.Combine(baseDir, sub), sub, Path.Combine(baseDir, ".." + Path.DirectorySeparatorChar + sub) };
            foreach (string c in cands) if (Directory.Exists(c) || File.Exists(c)) return c;
            return sub;
        }

        public static int Main(string[] args)
        {
            T.Run("packet sizes vs C++ sizeof", PacketTests.SizesMatchCpp);
            T.Run("framer", PacketTests.Framer);
            T.Run("parsers", PacketTests.Parsers);
            T.Run("builders", PacketTests.Builders);
            T.Run("minijson", DataTests.MiniJsonBasics);
            T.Run("gamedata", DataTests.GameDataLoads);
            T.Run("gamedata without files", DataTests.GameDataLoadsWithoutFiles);
            T.Run("hero ids", DataTests.HeroIdsSorted);
            T.Run("core has no ui dependency", DataTests.CoreHasNoUiDependency);
            T.Run("sfx synth", SfxTests.SynthIsSane);
            T.Run("sfx wav", SfxTests.WavHeader);
            T.Run("sfx rules", SfxTests.Rules);
            T.Run("config", DataTests.Config);
            T.Run("world: move/spawn", WorldTests.MoveAndSpawn);
            T.Run("world: damage/death/respawn", WorldTests.DamageDeathRespawn);
            T.Run("world: skills", WorldTests.Skills);
            T.Run("skill shapes: data", SkillAimTests.DataShapes);
            T.Run("skill shapes: request values", SkillAimTests.Resolve);
            T.Run("skill shapes: effects", SkillAimTests.Effects);
            T.Run("attack pose: strike", AttackPoseTests.Strike);
            T.Run("attack pose: styles", AttackPoseTests.Styles);
            T.Run("attack pose: cast and kinds", AttackPoseTests.CastAndKinds);
            T.Run("attack pose: hit reaction", AttackPoseTests.HitReaction);
            T.Run("attack pose: swipe and world", AttackPoseTests.SwipeAndWorld);
            T.Run("tension: low hp", TensionTests.LowHp);
            T.Run("tension: heartbeat", TensionTests.Heartbeat);
            T.Run("tension: kill streak", TensionTests.Streak);
            T.Run("tension: threats", TensionTests.Threats);
            T.Run("tension: boss warning", TensionTests.BossWarning);
            T.Run("tension: streak in world", TensionTests.StreakInWorld);
            T.Run("tension: sfx", TensionTests.SfxMapping);
            T.Run("world: status", WorldTests.Status);
            T.Run("demo: simulation", DemoTests.SimulationRuns);
            T.Run("demo: client commands", DemoTests.ClientCommandsReachTheSimulator);
            T.Run("demo: dead cannot act", DemoTests.DeadCannotAct);
            T.Run("boss view: data", BossViewTests.BossData);
            T.Run("boss view: phases", BossViewTests.PhaseTracking);
            T.Run("boss view: ghost hp / shake", BossViewTests.GhostHpAndShake);
            T.Run("entity: walking", BossViewTests.Walking);
            T.Run("demo: boss", BossViewTests.DemoBoss);
            T.Run("session e2e (fake gateway)", SessionTests.EndToEnd);
            T.Run("session: failures", SessionTests.Failures);
            T.Run("autopilot", SessionTests.AutoPilotChasesAndAttacks);

            Console.WriteLine();
            Console.WriteLine("passed " + T.Passed + ", failed " + T.Failed);
            return T.Failed == 0 ? 0 : 1;
        }
    }
}
