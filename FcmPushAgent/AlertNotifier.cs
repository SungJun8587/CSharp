using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FcmPushAgent
{
    /// <summary>
    /// 운영 중 주의가 필요한 이벤트(job 실패, 실패율 임계치 초과, 무효 토큰 급증 등)를
    /// Slack 호환 Webhook으로 전송합니다.
    ///
    /// Slack Incoming Webhook, Discord(Slack 호환 모드), Mattermost 등
    /// "{\"text\": \"...\"}" 형식의 JSON POST를 받는 대부분의 협업툴에서 그대로 사용 가능합니다.
    ///
    /// WebhookUrl이 비어 있으면 모든 메서드가 조용히 아무 동작도 하지 않습니다(Null Object 패턴과 유사).
    /// 이렇게 하면 알림 설정을 하지 않은 개발 환경에서도 코드를 수정할 필요가 없습니다.
    /// </summary>
    public class AlertNotifier
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // Slack 등 Webhook URL. null/빈 문자열이면 알림 기능이 비활성화됩니다.
        private readonly string? _webhookUrl;

        // 메시지 앞에 붙는 식별자. 여러 환경(운영/스테이징)에서 같은 채널을 공유할 때 구분 용도로 사용합니다.
        private readonly string _environmentLabel;

        /// <param name="webhookUrl">Slack 호환 Incoming Webhook URL. null/빈 문자열이면 알림 비활성화.</param>
        /// <param name="environmentLabel">알림 메시지에 표시할 환경 이름 (예: "production", "staging")</param>
        public AlertNotifier(string? webhookUrl, string environmentLabel)
        {
            _webhookUrl = webhookUrl;
            _environmentLabel = environmentLabel;
        }

        /// <summary>이 인스턴스가 실제로 알림을 보낼 수 있는 상태인지 여부.</summary>
        public bool IsEnabled => !string.IsNullOrWhiteSpace(_webhookUrl);

        /// <summary>
        /// job이 완료된 후 실패율을 계산해, 설정된 임계치를 초과하면 경고 알림을 보냅니다.
        /// 정상 범위(임계치 이하)라면 알림을 보내지 않아 채널에 불필요한 잡음을 만들지 않습니다.
        /// </summary>
        /// <param name="jobId">대상 작업 ID</param>
        /// <param name="totalRead">총 발송 대상 유저 수</param>
        /// <param name="totalSuccess">발송 성공 건수</param>
        /// <param name="totalFailure">발송 실패 건수</param>
        /// <param name="failureRateThreshold">경고를 보낼 실패율 임계치 (0.0~1.0, 예: 0.05 = 5%)</param>
        /// <param name="ct">취소 토큰</param>
        public async Task NotifyIfFailureRateExceededAsync(
            string jobId, long totalRead, long totalSuccess, long totalFailure,
            double failureRateThreshold, CancellationToken ct)
        {
            if (!IsEnabled || totalRead == 0)
                return;

            double failureRate = (double)totalFailure / totalRead;
            if (failureRate < failureRateThreshold)
                return; // 정상 범위 -> 알림 없음

            var text =
                $":warning: *[{_environmentLabel}] 푸시 발송 실패율 임계치 초과*\n" +
                $"- job_id: `{jobId}`\n" +
                $"- 총 대상: {totalRead:N0}건\n" +
                $"- 성공: {totalSuccess:N0}건 / 실패: {totalFailure:N0}건\n" +
                $"- 실패율: {failureRate:P1} (임계치 {failureRateThreshold:P1})";

            await SendAsync(text, ct);
        }

        /// <summary>
        /// job이 예외로 인해 중단되었을 때(재시도 대기 상태로 전이될 때) 알림을 보냅니다.
        /// </summary>
        /// <param name="jobId">대상 작업 ID</param>
        /// <param name="errorMessage">예외 메시지</param>
        /// <param name="ct">취소 토큰</param>
        public async Task NotifyJobErrorAsync(string jobId, string errorMessage, CancellationToken ct)
        {
            if (!IsEnabled)
                return;

            var text =
                $":x: *[{_environmentLabel}] 푸시 발송 작업 오류*\n" +
                $"- job_id: `{jobId}`\n" +
                $"- 오류: {errorMessage}\n" +
                $"- 진행상황은 체크포인트에 저장되어 자동 재시도됩니다.";

            await SendAsync(text, ct);
        }

        /// <summary>
        /// job이 정상적으로 모두 완료되었을 때 요약 알림을 보냅니다.
        /// </summary>
        /// <param name="jobId">대상 작업 ID</param>
        /// <param name="totalRead">총 발송 대상 유저 수</param>
        /// <param name="totalSuccess">발송 성공 건수</param>
        /// <param name="totalFailure">발송 실패 건수</param>
        /// <param name="elapsed">총 소요 시간</param>
        /// <param name="ct">취소 토큰</param>
        public async Task NotifyJobCompletedAsync(
            string jobId, long totalRead, long totalSuccess, long totalFailure, TimeSpan elapsed, CancellationToken ct)
        {
            if (!IsEnabled)
                return;

            var text =
                $":white_check_mark: *[{_environmentLabel}] 푸시 발송 완료*\n" +
                $"- job_id: `{jobId}`\n" +
                $"- 총 대상: {totalRead:N0}건\n" +
                $"- 성공: {totalSuccess:N0}건 / 실패: {totalFailure:N0}건\n" +
                $"- 소요시간: {elapsed:hh\\:mm\\:ss}";

            await SendAsync(text, ct);
        }

        /// <summary>
        /// 실제 Webhook으로 JSON POST 요청을 전송하는 공통 내부 메서드.
        /// 네트워크 오류 등으로 알림 전송에 실패하더라도, 본 발송 로직에 영향을 주지 않도록
        /// 예외를 흡수하고 콘솔에만 경고를 남깁니다(알림 실패가 푸시 발송 자체를 막아서는 안 되므로).
        /// </summary>
        /// <param name="text">전송할 메시지 본문 (Slack mrkdwn 형식)</param>
        /// <param name="ct">취소 토큰</param>
        private async Task SendAsync(string text, CancellationToken ct)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { text });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await HttpClient.PostAsync(_webhookUrl, content, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[경고] 알림 Webhook 전송 실패: HTTP {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                // 알림 전송 실패가 전체 발송 파이프라인을 중단시켜서는 안 되므로 여기서 흡수합니다.
                Console.WriteLine($"[경고] 알림 Webhook 전송 중 예외: {ex.Message}");
            }
        }
    }
}
