using System;

namespace MiniIPC
{
    public class RemoteException : Exception
    {
        public RemoteException(string message, string remoteStackTrace) : base(message)
        {
            RemoteStackTrace = remoteStackTrace;
        }

        public string RemoteStackTrace { get; }
    }
}