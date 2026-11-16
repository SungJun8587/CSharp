// Inventory.cs : 플레이어의 메모리 인벤토리. 접속 중에만 유지된다(저장소 연동 전).
using System.Collections.Generic;
using Game.Generated;

namespace Game.ZoneServer;

/// <summary>
/// 아이템 종류별 수량. 종류 수는 MaxInventorySlots, 한 종류의 수량은 MaxItemStack까지다.
/// 존 스레드에서만 접근한다.
/// </summary>
public sealed class Inventory
{
    private readonly Dictionary<uint, uint> _items = new Dictionary<uint, uint>();

    /// <summary>보유 중인 아이템 종류 수.</summary>
    public int SlotCount => _items.Count;

    /// <summary>아이템 수량. 없으면 0.</summary>
    public uint GetCount(uint itemId) => _items.TryGetValue(itemId, out uint c) ? c : 0;

    /// <summary>아이템을 더한다. 새 종류인데 칸이 없거나, 합이 MaxItemStack을 넘으면 아무것도 바꾸지 않고 false.</summary>
    /// <param name="total">성공하면 더한 뒤의 수량</param>
    public bool TryAdd(uint itemId, uint count, out uint total)
    {
        total = 0;
        if (itemId == 0 || count == 0)
            return false;

        _items.TryGetValue(itemId, out uint current);
        if (current == 0 && _items.Count >= GameConsts.MaxInventorySlots)
            return false;

        ulong sum = (ulong)current + count;
        if (sum > GameConsts.MaxItemStack)
            return false;

        total = (uint)sum;
        _items[itemId] = total;
        return true;
    }
}
