using System.Collections.Concurrent;
using System.Reflection;
using ProducerConsumer;

namespace BlockingCollection
{
    /// <summary>데이터 전달용 큐</summary>
    public class ProducerConsumerQueue<T>
    {
        /// <summary>추가된 데이터 개수</summary>
        private int _addCount = 0;

        /// <summary>추출된 데이터 개수</summary>
        private int _takeCount = 0;

        /// <summary>블로킹 컬렉션(내부적으로 ConcurrentQueue 사용)</summary>
        private BlockingCollection<T> _queue = new BlockingCollection<T>();

        // 최대 사이즈 제한을 걸수도 있다. 사이즈 제한이 있는 경우 큐가 가득 차면, 생산자가 대기해야 한다.
        //private BlockingCollection<int> _queue = new BlockingCollection<int>(10);

        /// <summary>추출 취소를 위한 토큰</summary>
        private CancellationTokenSource _source = new CancellationTokenSource();

        /// <summary>락을 위한 오브젝트</summary>
        public object LockObj = new object();

        /// <summary>큐 사이즈</summary>
        public int Count { get { return _queue.Count; } }

        /// <summary>추가된 데이터 개수</summary>
        public int AddedCount { get { return _addCount; } }

        /// <summary>추출한 데이터 개수</summary>
        public int TakenCount { get { return _takeCount; } }

        /// <summary>데이터 추가 중단</summary>
        public void CompleteAdding() { _queue.CompleteAdding(); }

        /// <summary>데이터 큐가 완전히 비었는지 검사</summary>
        public bool IsCompleted() { return _queue.IsCompleted; }

        /// <summary>데이터 추출 중단</summary>
        public void CancelTake() { _source.Cancel(); }

        // *************************************************************************
        // Queue에 데이터 추가
        // *************************************************************************
        public bool Add(T data)
        {
            try
            {
                _queue.Add(data);

                //++_addCount;
                Interlocked.Increment(ref _addCount);

                return true;
            }
            catch (Exception e)
            {
                // CompleteAdding를 호출하면 여기서 예외가 발생한다.
                Console.WriteLine(e.Message);
                return false;
            }
        }

        // *************************************************************************
        // Queue에 데이터 추출
        // *************************************************************************
        public bool Take(ref T data)
        {
            try
            {
                data = _queue.Take(_source.Token);

                //++_takeCount;
                Interlocked.Increment(ref _takeCount);

                return true;
            }
            catch (Exception e)
            {
                // CancelTake를 호출하면 여기서 예외가 발생한다.
                Console.WriteLine(e.Message);
                return false;
            }
        }

        // *************************************************************************
        // Queue 내용 출력하기 - 디버깅 용
        // *************************************************************************
        public void PrintContents()
        {
            Console.Write($"[Queue] Add({AddedCount}), Take({TakenCount}), Count({_queue.Count}) => ");

            PropertyInfo[] properties = typeof(T).GetProperties();
            if (properties.Length == 0)
            {
                if (typeof(T) == typeof(int) || typeof(T) == typeof(uint)
                    || typeof(T) == typeof(long) || typeof(T) == typeof(ulong)
                    || typeof(T) == typeof(string))
                {
                    foreach (T item in _queue)
                    {
                        Console.Write("{0} ", item);
                    }
                }
            }
            else
            {
                foreach (T item in _queue)
                {    
                    if (item != null)
                    {
                        int i = 0;
                        foreach (PropertyInfo property in item.GetType().GetProperties())
                        {
                            if (i == 0)
                                Console.Write("{0} : {1}", property.Name, property.GetValue(item, null));
                            else Console.Write(", {0} : {1}", property.Name, property.GetValue(item, null));
                            i++;
                        }
                    }
                }                                
            }  
            Console.WriteLine("");          
        }
    }
}