using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Formatting.Compact;

namespace FcmPushAgent
{
    /// <summary>
    /// 애플리케이션 진입점.
    /// appsettings.json을 읽어 전역 기본 설정(PushSchedulerDefaults)을 구성하고,
    /// Serilog 로깅을 초기화한 뒤 FcmPushAgent를 띄워 push_jobs 테이블을 영구적으로 폴링하게 합니다.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// 프로그램 시작점.
        /// 1) 설정 로드 -> 2) Serilog 로깅 초기화 -> 3) 의존성(레포지토리/스케줄러) 생성
        /// -> 4) Ctrl+C 취소 핸들러 등록 -> 5) 폴링 루프를 시작해 종료될 때까지 실행합니다.
        /// </summary>
        /// <param name="args">커맨드라인 인자 (현재 사용하지 않음)</param>
        public static async Task Main(string[] args)
        {
            // appsettings.json을 실행 파일 위치 기준으로 로드.
            // optional:false 이므로 파일이 없으면 즉시 예외가 발생합니다(설정 누락을 조기에 발견하기 위함).
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            // MySQL 연결 문자열은 필수 설정이므로, 없으면 즉시 명확한 예외로 실패시킵니다.
            var connectionString = config.GetConnectionString("MySql")
                ?? throw new InvalidOperationException("ConnectionStrings:MySql 설정이 필요합니다.");

            // 전역 기본 옵션 구성. 각 항목이 appsettings.json에 없으면 합리적인 기본값을 사용합니다.
            var defaults = new PushSchedulerDefaults
            {
                // DB에서 한 번에 읽어올 row 수 (keyset pagination 1회 조회 크기)
                DbFetchSize = int.Parse(config["PushDefaults:DbFetchSize"] ?? "2000"),
                // FCM Multicast 1회 호출당 토큰 수 (FCM 정책상 최대 500)
                FcmBatchSize = int.Parse(config["PushDefaults:FcmBatchSize"] ?? "500"),
                // job 1개당 동시에 FCM 발송하는 Consumer 워커 수
                ConsumerCount = int.Parse(config["PushDefaults:ConsumerCount"] ?? "8"),
                // Producer-Consumer 간 Channel 버퍼 최대 크기
                ChannelCapacity = int.Parse(config["PushDefaults:ChannelCapacity"] ?? "20000"),
                // Firebase 서비스 계정 키 파일 경로
                CredentialPath = config["Firebase:CredentialPath"] ?? "serviceAccountKey.json",
                // push_jobs 테이블을 몇 초 간격으로 폴링할지
                PollingIntervalSeconds = int.Parse(config["PushDefaults:PollingIntervalSeconds"] ?? "10"),
                // 한 프로세스가 동시에 처리할 수 있는 job 최대 개수
                MaxConcurrentJobs = int.Parse(config["PushDefaults:MaxConcurrentJobs"] ?? "3"),
                // claimed_at이 이 시간(분)보다 오래되면 죽은 워커로 간주하고 재선점 허용
                StaleLeaseMinutes = int.Parse(config["PushDefaults:StaleLeaseMinutes"] ?? "5"),
                // 처리 중인 job의 lease(claimed_at)를 몇 초마다 갱신할지
                LeaseRenewalSeconds = int.Parse(config["PushDefaults:LeaseRenewalSeconds"] ?? "60"),
                // FCM 처리율 제어: 프로세스 전체가 공유하는 초당 최대 FCM 호출(Multicast) 수
                MaxFcmCallsPerSecond = int.Parse(config["PushDefaults:MaxFcmCallsPerSecond"] ?? "20"),
                // 모니터링: Slack 호환 Webhook URL (없으면 알림 비활성화)
                AlertWebhookUrl = config["Monitoring:AlertWebhookUrl"],
                // 모니터링: 알림 메시지에 표시할 환경 이름
                EnvironmentLabel = config["Monitoring:EnvironmentLabel"] ?? "production",
                // 모니터링: 이 실패율(0.0~1.0)을 초과하면 경고 알림 발송
                FailureRateAlertThreshold = double.Parse(config["Monitoring:FailureRateAlertThreshold"] ?? "0.05")
            };

            // Serilog 초기화: 콘솔(사람이 읽기 좋은 텍스트)과 파일(JSON, 구조화된 로그)에 동시 출력합니다.
            // 파일은 날짜별로 롤링되며 CompactJsonFormatter로 저장되어, 추후 ELK/Datadog 등
            // 로그 수집 파이프라인에 그대로 연결하기 좋은 형태입니다.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.WithProperty("Application", "FcmPushAgent")
                .WriteTo.Console()
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    path: "logs/mass-push-.json",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14) // 최근 14일치 로그만 보관
                .CreateLogger();

            var cts = new CancellationTokenSource();

            // Ctrl+C / 프로세스 종료 신호 -> 프로세스를 즉시 죽이지 않고 CancellationToken으로 안전하게 취소.
            // e.Cancel = true로 기본 종료 동작을 막고, 우리가 만든 취소 토큰을 트리거합니다.
            // 이렇게 하면 진행 중인 job이 진행 상황을 DB에 저장할 시간을 벌 수 있습니다.
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            // 레포지토리/스케줄러 의존성 구성 (단순 생성자 주입)
            var jobRepo = new PushJobRepository(connectionString);
            var cleanupRepo = new TokenCleanupRepository(connectionString);
            var scheduler = new FcmPushAgent(connectionString, defaults, jobRepo, cleanupRepo);

            Log.Information("[프로그램] 시작. push_jobs 테이블을 폴링하며 예약된 발송을 처리합니다.");
            Log.Information("[프로그램] 새 발송을 등록하려면 push_jobs 테이블에 INSERT 하세요.");

            try
            {
                // 장기 실행 루프: 프로세스가 떠 있는 동안 계속 폴링하며 예약된 작업을 실행.
                // 이 호출은 취소되기 전까지 반환되지 않습니다.
                await scheduler.RunForeverAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C 등으로 정상적으로 취소된 경우. 에러가 아니라 정상 종료 경로입니다.
                Log.Information("프로그램이 안전하게 종료되었습니다. 다시 실행하면 미완료 작업을 이어서 진행합니다.");
            }
            finally
            {
                // 버퍼에 남아있는 로그를 모두 flush하고 로거를 정리합니다 (특히 파일 sink에 중요).
                Log.CloseAndFlush();
            }
        }
    }
}
