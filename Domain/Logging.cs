using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    public enum LoggingType
    {
        Information = 0,
        Debug,
        Warning,
        Error,
        Critical,
        Fatal
    }
    public class Logging
    {
        public string EndpointName { get; set; } = string.Empty;
        public string Input { get; set; } = string.Empty;
        public string CorrelationID { get; set; } = string.Empty;
        public string CallerID { get; set; } = string.Empty;
        public LogEventLevel Type { get; set; } = LogEventLevel.Error;
        public string LogMessage { get; set; } = string.Empty;
        public DateTime LogStartDateTime { get; set; }
        public DateTime LogFinishDateTime { get; set; }
        public string ExceptionStackTrace { get; set; } = string.Empty;
    }
}
