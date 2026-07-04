using System.Threading.Channels;

namespace JwtAuthCommon.Services
{
    /// <summary>
    /// 블랙리스트 DB 기록 요청을 담아두는 인메모리 큐.
    ///
    /// TokenBlacklistService.AddToBlacklistAsync는 Redis 저장 후 이 큐에 (Jti, ExpiresAt)을
    /// 넣기만 하고 즉시 반환한다(스코프 생성·DB 접근 없음). 실제 DB 기록은
    /// BlacklistDbWriterHostedService가 백그라운드에서 큐를 배치로 소비하며 처리하므로,
    /// 트래픽이 많아져도 요청 경로에서 매번 IServiceScopeFactory.CreateScope()를
    /// 호출하는 오버헤드가 발생하지 않는다.
    ///
    /// Singleton으로 등록해 TokenBlacklistService(Singleton)와
    /// BlacklistDbWriterHostedService 양쪽에서 동일 인스턴스를 공유한다.
    /// </summary>
    public class BlacklistWriteQueue
    {
        /// <summary>DB에 기록할 블랙리스트 항목 하나를 표현</summary>
        public readonly record struct Entry(string Jti, DateTime ExpiresAt);

        // 용량 제한 없는 채널. 컨슈머(BlacklistDbWriterHostedService)가 항상 하나만 존재하고
        // 지속적으로 비워내므로 무제한 채널로도 충분하며, 프로듀서(로그아웃 등 요청 경로)가
        // 절대 블로킹되지 않도록 Unbounded로 구성한다.
        private readonly Channel<Entry> _channel = Channel.CreateUnbounded<Entry>(
            new UnboundedChannelOptions
            {
                SingleReader = true,   // 컨슈머는 BlacklistDbWriterHostedService 하나뿐
                SingleWriter = false   // 여러 요청 스레드가 동시에 Enqueue 가능
            });

        /// <summary>큐 소비를 위한 ChannelReader (HostedService 전용)</summary>
        public ChannelReader<Entry> Reader => _channel.Reader;

        /// <summary>
        /// 블랙리스트 항목을 큐에 넣는다. 논블로킹이며 실패하지 않는다(Unbounded 채널이므로 TryWrite는 항상 true).
        /// </summary>
        /// <param name="jti">JWT 고유 식별자</param>
        /// <param name="expiresAt">원본 Access 토큰의 만료 시각(UTC)</param>
        public void Enqueue(string jti, DateTime expiresAt)
        {
            _channel.Writer.TryWrite(new Entry(jti, expiresAt));
        }
    }
}
