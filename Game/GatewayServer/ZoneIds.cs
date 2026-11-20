// ZoneIds.cs : 존 ID 의 선(와이어) 값과 Gateway 내부 값 변환.
// 내부에서는 ZoneId 0 이 "존 없음"(클라이언트 점유 없음 / 등록 전 존 서버)이라, 와이어의 0 번 존(시험장)은 경계에서만 다른 값으로 바꿔 쓴다.
namespace Game.GatewayServer;

internal static class ZoneIds
{
    /// <summary>와이어 값 0 번 존(모든 몬스터가 나오는 시험장)을 Gateway 안에서 부르는 값. 실제 존 번호와 겹치지 않는 큰 값.</summary>
    public const uint TestInternal = 0x7FFF0000u;

    /// <summary>와이어 -> 내부. 0 은 시험장, 내부 값과 같은 와이어 값은 쓸 수 없어 0 으로 되돌아가므로 그대로 둔다(라우팅 테이블에 없어 입장 거부).</summary>
    public static uint ToInternal(uint wire) { return wire == 0 ? TestInternal : wire; }

    /// <summary>내부 -> 와이어. 클라이언트와 존 서버에 보내는 값은 항상 이쪽을 거친다.</summary>
    public static uint ToWire(uint internalId) { return internalId == TestInternal ? 0u : internalId; }
}
