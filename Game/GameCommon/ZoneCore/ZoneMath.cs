// ZoneMath.cs : 존 평면 좌표 기본 타입.
namespace Game.ZoneCore;

/// <summary>존 평면 좌표(x, y). 높이(z)는 현재 단계에서 다루지 않는다.</summary>
public struct Vec2
{
    public float X;
    public float Y;

    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>두 좌표 사이 거리의 제곱을 반환한다. (sqrt 비용 회피용)</summary>
    public static float DistanceSq(in Vec2 a, in Vec2 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    public override string ToString() => $"({X}, {Y})";
}
