using BlockingCollection;

namespace ProducerConsumer
{
    /// <summary>생산자 소비자 공통 부모용 클래스</summary>
    public abstract class ProducerConsumerBase
    {
        /// <summary>데이터 하나 처리에 필요한 최소 시간 시뮬레이션 값(ms 단위)</summary>
        int _minProcessTime;

        /// <summary>데이터 하나 처리에 필요한 최대 시간 시뮬레이션 값(ms 단위)</summary>
        int _maxProcessTime;

        /// <summary>랜덤 값 생성</summary>
        protected Random _random = new Random();

        /// <summary>데이터 전달용 큐</summary>
        protected ProducerConsumerQueue<int> _queue;

        /// <summary>스레드 아이디</summary>
        public int ThreadId { get; private set; }

        /// <summary>데이터 처리된 개수</summary>
        public int ProcessedCount { get; set; }

        public ProducerConsumerBase(ProducerConsumerQueue<int> queue, int minProcessTime, int maxProcessTime)
        {
            _queue = queue;

            _minProcessTime = minProcessTime;
            _maxProcessTime = maxProcessTime;
        }

        // *************************************************************************
        // 스레드 시작시 호출
        // *************************************************************************
        protected void OnThreadStart()
        {
            ThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        // *************************************************************************
        // 스레드 잠시 대기(데이터 처리 시뮬레이션용)
        // *************************************************************************
        protected void ThreadWait()
        {
            Thread.Sleep(_random.Next(_minProcessTime, _maxProcessTime));
        }

        // 스레드 함수
        public abstract void ThreadRun();
    }

    /// <summary>생산자</summary>
    public class Producer : ProducerConsumerBase
    {
        // 생성자
        public Producer(ProducerConsumerQueue<int> q, int minProcessTime, int maxProcessTime)
            : base(q, minProcessTime, maxProcessTime)
        { }

        // 스레드 함수
        public override void ThreadRun()
        {
            // 스레드 시작 처리
            OnThreadStart();

            //Stopwatch stopwatch = new Stopwatch();

            while (true)
            {
                // 랜덤 데이터 생성
                int data = _random.Next(0, 100);

                //stopwatch.Restart();

                // 데이터를 큐에 추가. 스레드에 안전하다.
                if (_queue.Add(data) == false)
                    break;

                //stopwatch.Stop();

                // 대기시간이 있을 경우 표시
                //if (stopwatch.ElapsedMilliseconds != 0)
                //Console.WriteLine($"[{ThreadId:D2}] Produce Add Time : {stopwatch.ElapsedMilliseconds} ms");

                // 처리 카운트 증가
                ++ProcessedCount;

                // 디버깅용 콘솔 출력 부분 (락을 걸어야 콘솔 출력이 깨지지 않는다.)
                // Add 와 별도로 락을 걸었기 때문에 출력되는 큐의 내용은 정확하지 않을 수 있다.
                lock (_queue.LockObj)
                {
                    Console.Write($"[{ThreadId:D2}] Produce ({data:D2}) => ");
                    _queue.PrintContents();
                }

                // 스레드 잠시 대기
                ThreadWait();
            }

            // 생산자 결과 출력
            Console.WriteLine($"[{ThreadId:D2}] Produced {ProcessedCount} items");
        }
    }

    /// <summary>소비자</summary>
    public class Consumer : ProducerConsumerBase
    {
        // 생성자
        public Consumer(ProducerConsumerQueue<int> q, int minProcessTime, int maxProcessTime)
            : base(q, minProcessTime, maxProcessTime)
        { }

        // 스레드 함수
        public override void ThreadRun()
        {
            // 스레드 시작 처리
            OnThreadStart();

            while (true)
            {
                int data = 0;

                // 큐에서 데이터 하나 추출. 스레드에 안전하다.
                if (_queue.Take(ref data) == false)
                    break;

                // 처리 카운트 증가
                ++ProcessedCount;

                // 디버깅용 콘솔 출력 부분 (락을 걸어야 콘솔 출력이 깨지지 않는다.)
                // Take 와 별도로 락을 걸었기 때문에 출력되는 큐의 내용은 정확하지 않을 수 있다.
                lock (_queue.LockObj)
                {
                    Console.Write($"[{ThreadId:D2}] Consume ({data:D2}) => ");
                    _queue.PrintContents();
                }

                // 스레드 잠시 대기
                ThreadWait();
            }

            // 소비자 결과 출력
            Console.WriteLine($"[{ThreadId:D2}] Consumed {ProcessedCount} items", ProcessedCount);
        }
    }
}