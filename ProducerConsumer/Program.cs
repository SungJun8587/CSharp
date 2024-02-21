using BlockingCollection;

namespace ProducerConsumer
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("Configuring worker thread...");

            ProducerConsumerQueue<int> queue = new ProducerConsumerQueue<int>();

            // 생산자 정의
            Producer[] producerList = new[]
            {
                new Producer(queue, 100, 300),
                new Producer(queue, 200, 400),
            };

            // 소비자 정의 - 처리 속도를 느리게 하면 큐가 점점 쌓인다.
            Consumer[] consumerList = new[]
            {
                //new Consumer(queue, 100, 300),
                //new Consumer(queue, 100, 300),
                new Consumer(queue, 100, 300),
                new Consumer(queue, 200, 600),
            };

            // 생산자 소비자 태스크 정의
            var producerTasks = new Task[producerList.Length];
            var consumerTasks = new Task[consumerList.Length];

            // 생산자 생성
            for (int i = 0; i < producerTasks.Length; ++i)
                producerTasks[i] = new Task(producerList[i].ThreadRun);

            // 소비자 생성
            for (int i = 0; i < consumerTasks.Length; ++i)
                consumerTasks[i] = new Task(consumerList[i].ThreadRun);

            Console.WriteLine("Launching producer and consumer threads...");

            // 생산자 태스크 실행
            Array.ForEach(producerTasks, t => t.Start());

            // 소비자 태스크 실행
            Array.ForEach(consumerTasks, t => t.Start());

            // ESC 키를 누르면 생산자를 중단한다는 안내
            Console.WriteLine("Press ESC to stop producers");

            // 키 입력이 있는가?
            while (true)
            {
                /*
                if (Console.KeyAvailable)
                {
                    // 입력된 키가 ESC인가?
                    var keyInfo = Console.ReadKey();
                    if (keyInfo.Key == ConsoleKey.Escape)
                    {
                        // 생산자 스레드 중단 요청
                        Console.WriteLine("Signaling producer threads to terminate...");
                        queue.CompleteAdding();
                        break;
                    }
                }
                */
            }

            // 생산자 스레드 종료 대기
            Task.WaitAll(producerTasks);

            // ESC 키를 누르면 소비자를 중단한다는 안내
            Console.WriteLine("Press ESC to stop consumers");

            // 큐가 빌 때까지 계속 대기
            while (queue.IsCompleted() == false)
            {
                /*
                if (Console.KeyAvailable)
                {
                    // 입력된 키가 ESC인가?
                    var keyInfo = Console.ReadKey();
                    if (keyInfo.Key == ConsoleKey.Escape)
                    {
                        // 소비자 스레드 중단 요청
                        Console.WriteLine("Signaling consumer threads to terminate...");
                        queue.CancelTake();
                        break; // while 빠져나가기
                    }
                }
                */
            }

            // 소비자 스레드 종료 대기
            Task.WaitAll(consumerTasks);

            Console.WriteLine("========================================");

            // 전체 생산량 계산
            int totalProduced = 0;
            foreach (var item in producerList)
            {
                Console.WriteLine($"[{item.ThreadId:D2}] Produced count : {item.ProcessedCount}");
                totalProduced += item.ProcessedCount;
            }

            // 전체 소비량 계산
            int totalConsumed = 0;
            foreach (var item in consumerList)
            {
                Console.WriteLine($"[{item.ThreadId:D2}] Consumed count : {item.ProcessedCount}");
                totalConsumed += item.ProcessedCount;
            }

            // 결과 출력
            Console.WriteLine($"Total Produced count : {totalProduced}");
            Console.WriteLine($"Total Consumed count : {totalConsumed}");
            Console.WriteLine($"Queue count : {queue.Count}");
            Console.WriteLine($"Queue add count : {queue.AddedCount}");
            Console.WriteLine($"Queue take count : {queue.TakenCount}");

            // 결과 검증 코드
            if (queue.AddedCount != totalProduced)
                Console.WriteLine($"ERROR : _queue.AddCount != totalProduced");

            if (queue.TakenCount != totalConsumed)
                Console.WriteLine($"ERROR : _queue.TakeCount != totalConsumed");

            if (queue.Count != (totalProduced - totalConsumed))
                Console.WriteLine($"ERROR : _queue.Count != (totalProduced - totalConsumed)");

            // 종료 대기
            Console.WriteLine("Press ENTER to exit.");
            Console.ReadLine();
        }
    }
}