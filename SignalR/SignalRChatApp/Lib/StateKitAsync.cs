namespace Common.Lib
{
    public class StateKitAsync<TEnum>
        where TEnum : struct, IConvertible, IComparable, IFormattable
    {
        private class StateMethodCache
        {
            public Func<Task> enterState;
            public Func<Task> tick;
        }

        StateMethodCache _stateMethods;
        protected TEnum _previousState;
        TEnum _currentState;
        Dictionary<TEnum, StateMethodCache> _stateCache = new Dictionary<TEnum, StateMethodCache>();

        public StateKitAsync()
        {
            if (!typeof(TEnum).IsEnum)
            {
                Console.WriteLine("[StateKitAsync] TEnum generic contsraint failed! You must use an enum when declaring your subclass!");
            }

            // cache all of our state methods
            var enumValues = (TEnum[])Enum.GetValues(typeof(TEnum));
            foreach (var e in enumValues)
                configureAndCacheState(e);
        }

        public async Task InitStateAsync(TEnum to)
        {
            _currentState = to;
            _stateMethods = _stateCache[_currentState];

            if (_stateMethods.enterState != null)
                await _stateMethods.enterState();
        }

        public async Task ChangeStateAsync(TEnum to)
        {
            if (_currentState.Equals(to))
                return;
            // swap previous/current
            _previousState = _currentState;
            _currentState = to;

            _stateMethods = _stateCache[_currentState];

            if (_stateMethods.enterState != null)
                await _stateMethods.enterState();
        }

        protected async Task OnStateTickAsync()
        {
            if (_stateMethods.tick != null)
                await _stateMethods.tick();
        }


        void configureAndCacheState(TEnum stateEnum)
        {
            var stateName = stateEnum.ToString();

            var state = new StateMethodCache();
            state.enterState = getDelegateForMethod(stateName + "_Enter");
            state.tick = getDelegateForMethod(stateName + "_Tick");

            _stateCache[stateEnum] = state;
        }

        Func<Task> getDelegateForMethod(string methodName)
        {
            var methodInfo = GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

            if (methodInfo != null)
                return Delegate.CreateDelegate(typeof(Func<Task>), this, methodInfo) as Func<Task>;

            return null;
        }
    }
}
