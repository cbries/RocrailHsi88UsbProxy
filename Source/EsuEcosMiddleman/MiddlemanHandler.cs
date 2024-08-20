// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

namespace EsuEcosMiddleman
{
    public interface IExchange
    {
        public void Send(string data);
    }

    internal class MiddlemanHandler(
        IExchange toRocrail,
        IExchange toEcos,
        IExchange toWs
        )
    {
        public bool SendToRocrail(string data)
        {
            if (string.IsNullOrEmpty(data)) return true;
            toRocrail?.Send(data);
            return true;
        }

        public bool SendToEcos(string data)
        {
            if (string.IsNullOrEmpty(data)) return true;
            toEcos?.Send(data);
            return true;
        }

        public bool SendToWs(string data)
        {
            if (string.IsNullOrEmpty(data)) return true;
            toWs?.Send(data);
            return true;
        }
    }
}
