// RedisBinaryArgs.cs : 바이너리 인자를 포함하는 Redis 명령 인자 목록.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Core.Redis;

/// <summary>
/// 문자열과 바이너리(byte[]) 인자를 섞어 담는 명령 인자 목록. <c>IReadOnlyList&lt;string&gt;</c>이므로 기존 SendCommand 계열에 그대로 넘길 수 있고,
/// <see cref="RedisCommandBuilder"/>가 바이너리 인자를 원본 바이트 그대로 인코딩한다.
///
/// <para>문자열 인덱서는 로그/진단용이다 — 바이너리 인자는 UTF-8 로 해석한 문자열(깨질 수 있음)을 돌려준다.
/// 인자는 <c>Add</c> 때 복사해 두므로 보낸 뒤에 원본 배열을 바꿔도 안전하다. 목록 자체는 Add 가 끝난 뒤에 보내야 한다(보낸 뒤 Add 하지 않는다).</para>
/// </summary>
public sealed class RedisBinaryArgs : IReadOnlyList<string>
{
    private readonly List<object> _items;       // string 또는 byte[]

    public RedisBinaryArgs() => _items = new List<object>();

    public RedisBinaryArgs(int capacity) => _items = new List<object>(capacity);

    private RedisBinaryArgs(List<object> items) => _items = items;

    public RedisBinaryArgs Add(string text)
    {
        _items.Add(text ?? throw new ArgumentNullException(nameof(text)));
        return this;
    }

    /// <summary>바이너리 인자를 추가한다(복사해 둔다).</summary>
    public RedisBinaryArgs Add(ReadOnlySpan<byte> data)
    {
        _items.Add(data.ToArray());
        return this;
    }

    public int Count => _items.Count;

    public string this[int index] => _items[index] is string s ? s : Encoding.UTF8.GetString((byte[])_items[index]);

    /// <summary>인자 i 가 바이너리면 그 원본, 아니면 null.</summary>
    internal byte[]? BinaryAt(int index) => _items[index] as byte[];

    /// <summary>대기 큐/재시도가 쥐는 사본(항목은 Add 때 복사해 둔 불변 값이라 목록만 복사한다).</summary>
    internal RedisBinaryArgs Copy() => new RedisBinaryArgs(new List<object>(_items));

    public IEnumerator<string> GetEnumerator()
    {
        for (int i = 0; i < _items.Count; ++i)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
