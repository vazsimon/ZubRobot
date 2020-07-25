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
            //ws.PlaceOrder(1, 10000.5, 1, Side.SELL, OrderType.LIMIT, TimeInForce.GTC);

            //ws.Subscribe(Channel.orderbook);
            //ws.Orderbook += Ws_Orderbook;

            ws.Subscribe(Channel.positions);
            ws.Positions += Ws_Positions;

            //ws.Subscribe(Channel.orderFills);
            //ws.OrderFills += Ws_OrderFills;

            //ws.Subscribe(Channel.orders);
            //ws.Orders += Ws_Orders;


            //ws.Subscribe(Channel.tickers);
            //ws.Tickers += Ws_Tickers;

            //ws.Subscribe(Channel.lasttrades);
            //ws.LastTrades += Ws_LastTrades;


            //long orderId = 4512734125362872;
            //ws.ReplaceOrder(orderId, 9999, 1);

            //ws.CancelOrder(orderId);

            while (true)
            {
                Thread.Sleep(1000);
            }
        }

        private static void Ws_Positions(object sender, ZubrWebsocket.Models.PositionsUpdate e)
        {
            Console.WriteLine("-----------------------------Positions update----------------------");
            Console.WriteLine(e.InstrumentId.ToString());
            Console.WriteLine(e.Size.ToString());
        }

        private static void Ws_OrderFills(object sender, ZubrWebsocket.Models.OrderFillsUpdate e)
        {
            Console.WriteLine("-----------------------------Fills update----------------------");
            Console.WriteLine(e.InstrumentId.ToString());
        }

        private static void Ws_Orders(object sender, ZubrWebsocket.Models.OrdersUpdate e)
        {
            Console.WriteLine("-----------------------------Orders update----------------------");
            Console.WriteLine(e.InstrumentId.ToString());
        }

        private static void Ws_Orderbook(object sender, ZubrWebsocket.Models.OrderbookUpdate e)
        {
            Console.WriteLine("-----------------------------Orderbook update----------------------");
            Console.WriteLine(e.InstrumentId.ToString());
        }

        private static void Ws_LastTrades(object sender, ZubrWebsocket.Models.LasttradesUpdate e)
        {
            Console.WriteLine("-----------------------------Last Trades Update----------------------");
            Console.WriteLine(e.InstrumentId.ToString());
        }

        private static void Ws_Tickers(object sender, ZubrWebsocket.Models.TickersUpdate e)
        {
            Console.WriteLine("-----------------------------tickersUpdate----------------------");
            Console.WriteLine(e.InstrumentId.ToString());
        }
    }
}
