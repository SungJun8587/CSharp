// CurrencyRows.cs : Currency.json / MonsterCurrency.json 의 행 형식. 엑셀 변환기가 만드는 파일(Generated)이 아니라 손으로 쓴 파일이다.
// 시트 헤더: Currency(Id:uint[1..], Name:string, MaxAmount:uint[1..]), MonsterCurrency(MonsterId:uint[1..], CurrencyId:uint[1..], MinAmount:uint[1..], MaxAmount:uint[1..], ChancePermille:uint[1..1000]).
using System.Collections.Generic;

namespace Game.ZoneServer;

public sealed class CurrencyRow
{
    public const string FileName = "Currency.json";

    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint MaxAmount { get; set; }
}

public sealed class CurrencyTable
{
    public List<CurrencyRow> Currency { get; set; } = new List<CurrencyRow>();
}

public sealed class MonsterCurrencyRow
{
    public const string FileName = "MonsterCurrency.json";

    public uint MonsterId { get; set; }
    public uint CurrencyId { get; set; }
    public uint MinAmount { get; set; }
    public uint MaxAmount { get; set; }
    public uint ChancePermille { get; set; }
}

public sealed class MonsterCurrencyTable
{
    public List<MonsterCurrencyRow> MonsterCurrency { get; set; } = new List<MonsterCurrencyRow>();
}
