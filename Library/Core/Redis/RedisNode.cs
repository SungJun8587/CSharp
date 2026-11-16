// RedisNode.cs : Redis 서버 노드 접속 정보.
namespace Core.Redis;

/// <summary>
/// Redis 노드 하나의 접속 정보. <see cref="Id"/>는 <see cref="RedisService"/>에서 노드를 가리키는 키이며 목록 안에서 유일해야 한다.
/// </summary>
/// <param name="Id">노드 ID. 서비스에서 노드를 지정할 때 쓰는 키</param>
/// <param name="Host">서버 주소(IP 또는 DNS 이름)</param>
/// <param name="Port">서버 포트</param>
/// <param name="DbIndex">풀 커넥션이 접속 직후 SELECT로 고정할 논리 DB(0이면 생략). 구독 연결은 DB와 무관하다.</param>
/// <param name="UserId">ACL 사용자 이름(비어 있으면 <c>AUTH &lt;password&gt;</c>)</param>
/// <param name="Password">비밀번호(비어 있으면 인증 생략)</param>
public sealed record RedisNode(short Id, string Host, ushort Port, int DbIndex = 0, string UserId = "", string Password = "")
{
    // 비밀번호가 로그나 예외 메시지에 새지 않도록 ToString에서 뺀다.
    public override string ToString() => $"RedisNode(Id={Id}, Host={Host}, Port={Port}, Db={DbIndex})";
}
