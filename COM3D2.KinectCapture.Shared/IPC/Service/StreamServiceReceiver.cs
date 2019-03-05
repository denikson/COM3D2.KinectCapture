using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MessagePack;

namespace MiniIPC.Service
{
    public class StreamServiceReceiver<T> : IDisposable
    {
        static readonly byte[] Zero = {0x00, 0x00, 0x00, 0x00};
        readonly Dictionary<string, int> commandsMap;
        readonly InvokeMethodDelegate invoke;
        object instance;

        public StreamServiceReceiver(T serviceImplementor, Stream stream)
        {
            Stream = stream;
            invoke = ProxyFactory.CreateReceiverProxy(serviceImplementor, out instance, out commandsMap);
        }

        public Stream Stream { get; }

        public void Dispose()
        {
            instance = null;
            Stream?.Dispose();
        }

        public void ProcessMessage()
        {
            var sizeBuffer = new byte[4];

            Stream.Read(sizeBuffer, 0, sizeBuffer.Length);
            var methodNameLength = BitConverter.ToInt32(sizeBuffer, 0);

            var stringNameBuffer = new byte[methodNameLength];
            Stream.Read(stringNameBuffer, 0, stringNameBuffer.Length);
            var methodName = Encoding.UTF8.GetString(stringNameBuffer, 0, stringNameBuffer.Length);

            Stream.Read(sizeBuffer, 0, sizeBuffer.Length);
            var dataSize = BitConverter.ToInt32(sizeBuffer, 0);

            var data = new byte[dataSize];
            if (dataSize != 0)
                Stream.Read(data, 0, data.Length);

            try
            {
                if (!commandsMap.TryGetValue(methodName, out var commandIndex))
                    throw new NotImplementedException($"Method {methodName} is not implemented by the service!");

                var result = invoke(commandIndex, data, 0);

                if (result == null)
                {
                    Stream.Write(Zero, 0, Zero.Length);
                    return;
                }

                Stream.Write(BitConverter.GetBytes(result.Length), 0, 4);
                Stream.Write(result, 0, result.Length);
            }
            catch (Exception e)
            {
                WriteError(e);
            }
        }

        void WriteError(Exception e)
        {
            var err = MessagePackSerializer.Serialize(new Error {Message = e.Message, StackTrace = e.StackTrace});
            Stream.Write(BitConverter.GetBytes(-err.Length), 0, 4);
            Stream.Write(err, 0, err.Length);
        }
    }
}