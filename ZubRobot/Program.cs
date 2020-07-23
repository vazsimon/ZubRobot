using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZubrWebsocket;

namespace ZubRobot
{
    class Program
    {
        static void Main(string[] args)
        {
            string configStr = File.ReadAllText("config.json");
            JToken config = JToken.Parse(configStr);

            var ws = new ZubrWebsocketClient(config.Value<string>("apiKey"), config.Value<string>("apiSecret"));
            ws.Login();
            ws.PlaceOrder(1, 10000.5, 1, Side.SELL, OrderType.LIMIT, TimeInForce.GTC);

            //long orderId = 4512734125362872;
            //ws.ReplaceOrder(orderId, 9999, 1);

            //ws.CancelOrder(orderId);

            while (true)
            {
                Thread.Sleep(1000);
            }
        }
    }
}
