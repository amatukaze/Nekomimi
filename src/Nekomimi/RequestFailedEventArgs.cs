using System;

namespace Sakuno.Nekomimi
{
    public sealed class RequestFailedEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public TimeSpan? RetryDueTime { get; set; }

        internal RequestFailedEventArgs(Exception exception)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }
    }
}
