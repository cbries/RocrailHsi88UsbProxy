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
        IExchange toEcos
        )
    {
        public static string LineTermination = "\r\n";

        public bool SendToRocrail(string data)
        {
            if (string.IsNullOrEmpty(data)) return true;

            //if (!data.EndsWith(LineTermination))
            //    data = data.Trim() + LineTermination;
            //if (!data.EndsWith(LineTermination + LineTermination))
            //    data += LineTermination;

            toRocrail?.Send(data);

            return true;
        }

        public bool SendToEcos(string data)
        {
            if (string.IsNullOrEmpty(data)) return true;

            //if (!data.EndsWith(LineTermination))
            //    data = data.Trim() + LineTermination;
            //if (!data.EndsWith(LineTermination + LineTermination))
            //    data += LineTermination;

            toEcos?.Send(data);

            return true;
        }
    }
}
