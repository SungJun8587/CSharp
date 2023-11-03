
using DotNetWebAPI.Model;
using Newtonsoft.Json;
using Serilog.Extensions.Logging;

namespace DotNetWebAPI.Services
{
    public interface IHttpLogModelCreator
    {
        HttpLogModel LogModel { get; }
        string LogString();
    }

    public class HttpLogModelCreator : IHttpLogModelCreator
    {
        public HttpLogModel LogModel { get; private set; }

        public HttpLogModelCreator()
        {
            LogModel = new HttpLogModel();
        }

        public string LogString()
        {
            var jsonString = JsonConvert.SerializeObject(LogModel, Formatting.Indented);
            return jsonString;
        }
    }

    public interface IHttpLogger
    {
        void Log(IHttpLogModelCreator logCreator);
    }    

    public class HttpLogger : IHttpLogger
    {
        private readonly ILogger<HttpLogger> _logger;

        public HttpLogger(ILogger<HttpLogger> logger)
        {
            _logger = logger;
        }

        public void Log(IHttpLogModelCreator logCreator)
        {
            //_logger.LogTrace(jsonString);
            //_logger.LogDebug(jsonString);
            //_logger.LogInformation(jsonString);
            //_logger.LogWarning(jsonString);
            //_logger.LogError(jsonString);
            _logger.LogCritical(logCreator.LogString());
        }
    } 
}