using System;
using System.Threading;
using System.Threading.Tasks;

namespace FcmPushAgent
{
    /// <summary>
    /// 초당 호출 횟수를 제한하는 간단한 토큰 버킷(Token Bucket) 방식 Rate Limiter.
    ///
    /// FCM(Firebase Cloud Messaging)은 프로젝트별로 초당 처리 가능한 메시지 수에 쿼터가 있습니다.
    /// ConsumerCount만으로 동시성을 제어하면 순간적으로 쿼터를 초과해 429(Too Many Requests)나
    /// QuotaExceeded 오류가 발생할 수 있으므로, 모든 Consumer가 공유하는 이 Limiter를 통해
    /// "초당 호출 허용량"을 명시적으로 제한합니다.
    ///
    /// 구현 방식:
    ///  - SemaphoreSlim을 토큰 버킷처럼 사용합니다 (가용 토큰 수 = 세마포어 카운트).
    ///  - 1초마다 백그라운드 타이머가 토큰을 maxCallsPerSecond까지 다시 채웁니다.
    ///  - 호출자는 AcquireAsync로 토큰 1개를 획득한 뒤에만 FCM을 호출합니다.
    /// </summary>
    public sealed class FcmRateLimiter : IDisposable
    {
        // 가용 토큰을 표현하는 세마포어. 초기 카운트와 최대 카운트를 모두 maxCallsPerSecond로 설정합니다.
        private readonly SemaphoreSlim _semaphore;

        // 초당 허용할 최대 호출(토큰) 수
        private readonly int _maxCallsPerSecond;

        // 1초마다 토큰을 리필하는 타이머
        private readonly Timer _refillTimer;

        /// <param name="maxCallsPerSecond">
        /// 초당 허용할 최대 FCM 호출 수. 여기서 "호출 1회"는 SendEachForMulticastAsync 1번(최대 500토큰 묶음)을 의미합니다.
        /// FCM 콘솔/쿼터 정책에 맞춰 보수적으로 설정하는 것을 권장합니다.
        /// </param>
        public FcmRateLimiter(int maxCallsPerSecond)
        {
            if (maxCallsPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCallsPerSecond), "초당 호출 수는 1 이상이어야 합니다.");

            _maxCallsPerSecond = maxCallsPerSecond;
            // 시작 시 토큰을 가득 채운 상태로 시작 (초기 버스트 허용)
            _semaphore = new SemaphoreSlim(maxCallsPerSecond, maxCallsPerSecond);

            // 1초마다 RefillTokens를 호출하는 타이머. 즉시 한 번 실행하지 않도록 dueTime을 1000ms로 둡니다.
            _refillTimer = new Timer(RefillTokens, null, 1000, 1000);
        }

        /// <summary>
        /// 토큰 1개를 획득할 때까지 비동기적으로 대기합니다.
        /// 현재 초의 쿼터가 소진되어 있으면, 다음 리필(최대 1초 후)까지 대기하게 됩니다.
        /// </summary>
        /// <param name="ct">취소 토큰</param>
        public Task AcquireAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

        /// <summary>
        /// 타이머에 의해 1초마다 호출되며, 토큰 수를 maxCallsPerSecond까지 다시 채웁니다.
        /// 이미 가득 차 있는 경우(아무도 토큰을 소비하지 않은 경우) 예외 없이 무시합니다.
        /// </summary>
        /// <param name="state">사용하지 않음 (Timer 콜백 시그니처 호환용)</param>
        private void RefillTokens(object? state)
        {
            // 현재 세마포어가 보유한 여유 토큰 수만큼만 Release해서 maxCallsPerSecond를 넘지 않도록 합니다.
            // SemaphoreSlim.Release는 최대치를 초과하면 예외를 던지므로, 부족분만 정확히 계산해서 채웁니다.
            int deficit = _maxCallsPerSecond - _semaphore.CurrentCount;
            if (deficit > 0)
            {
                _semaphore.Release(deficit);
            }
        }

        /// <summary>
        /// 타이머와 세마포어 리소스를 정리합니다.
        /// </summary>
        public void Dispose()
        {
            _refillTimer.Dispose();
            _semaphore.Dispose();
        }
    }
}
