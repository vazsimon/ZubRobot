using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZubrWebsocket;

namespace ZubRobot
{
    class Program
    {
        private static bool positionInitialised = false;
        private static bool orderSnapshotReceived = false;
        private static decimal position;
        private static int instrumentCode;
        private static int volume;
        private static decimal interest;
        private static decimal shift;
        private static decimal maxPosition;
        private static bool logTransport = false;
        private static bool logLogic = false;


        private static decimal lastPrice = -1;
        private static decimal bestBid = -1;
        private static decimal bestAsk = -1;
        private static decimal minTickSize;

        /// <summary>
        /// Reference price for previous calculations. If different from current optimal price, we need to replace the order
        /// </summary>
        private static decimal lastBuyPrice = -1;
        /// <summary>
        /// Reference price for previous calculations. If different from current optimal price, we need to replace the order
        /// </summary>
        private static decimal lastSellPrice = -1;

        private static ZubrWebsocketClient ws;



        static void Main(string[] args)
        {
            string configStr = File.ReadAllText("config.json");
            JToken config = JToken.Parse(configStr);
            logLogic = config.Value<bool>("LogLogicalEvents");
            logTransport = config.Value<bool>("LogTransport");




            ws = new ZubrWebsocketClient(config.Value<string>("apiKey"), config.Value<string>("apiSecret"), config.Value<string>("apiEndpoint"));
            ws.LogTransport = logTransport;
            ws.Login();
            while (!ws.LoggedIn)
            {
                Thread.Sleep(100);
            }
            if (!CheckRequiredPermissions(ws))
            {
                throw new Exception("Api key's permission level not suitable for trading");
            }


            ///Setting instrument from config
            ///
            instrumentCode = config.Value<int>("InstrumentCode");

            volume = config.Value<int>("QuoteSize");
            interest = config.Value<decimal>("Interest");
            shift = config.Value<decimal>("Shift");
            maxPosition = config.Value<decimal>("MaxPosition");
            minTickSize = config.Value<decimal>("MinTickSize");

            ///Initial position read
            ///

            //check if configured position size should be used for calculations, otherwise 
            if (config.Value<bool>("InitialPositionFromConfig"))
            {
                position = config.Value<decimal>("InitialPosition");
                positionInitialised = true;
                if (logLogic)
                {
                    Console.WriteLine("Initial position read from config");
                }
            }
            else
            {
                //subscribe to positions
                //get SOW position
                //Unsubscribe -- using execution reports for position calculations going forward
                ws.Positions += Ws_PositionsInit;
                ws.Subscribe(Channel.positions);
                while (!positionInitialised)
                {
                    Thread.Sleep(100);
                }
                ws.Unsubscribe(Channel.positions);
                ws.Positions -= Ws_PositionsInit;
                if (logLogic)
                {
                    Console.WriteLine("Initial position stream unsubscribed");
                }
            }



            ///Setting up eventhandlers to act upon price change and fills

            ws.Tickers += Ws_TickersUpdate;
            ws.OrderFills += Ws_OrderFillsUpdate;
            ws.Orderbook += Ws_OrderbookUpdate;
            ws.Orders += Ws_OrdersUpdate;

            ws.Subscribe(Channel.tickers);
            ws.Subscribe(Channel.orderFills);
            ws.Subscribe(Channel.orderbook);
            ws.Subscribe(Channel.orders);
            if (logLogic)
            {
                Console.WriteLine("Streams subscribed");
            }


            while (asks.Count == 0 || bids.Count == 0)
            {
                //We have to have the orderbook info first to be able to place any trade
                Thread.Sleep(100);
            }
            if (logLogic)
            {
                Console.WriteLine("Orderbook received");
            }
            while (!orderSnapshotReceived)
            {
                //We only want to work with the new orders from now on
                Thread.Sleep(100);
            }
            if (logLogic)
            {
                Console.WriteLine("Order snapshots received");
            }

            InitOrders();


            while (true)
            {
                //do everything on other threads, we just can't let the main thread exit
                Thread.Sleep(100);
            }
        }

        private static decimal tickRnd(decimal d)
        {
            var mod = d % minTickSize;
            if (mod >= minTickSize / 2)
            {
                return d - mod + minTickSize;
            }
            else
            {
                return d - mod;
            }
        }


        /// <summary>
        /// Initial submission of orders
        /// </summary>
        private static void InitOrders()
        {
            if (maxPosition - Math.Abs(position) > 0 )
            {
                decimal buyPrice = tickRnd( ((bestBid + bestAsk) / 2) - interest - shift * position);
                decimal sellPrice = tickRnd( ((bestBid + bestAsk) / 2) + interest - shift * position);
                ws.PlaceOrder(instrumentCode, buyPrice, volume, Side.BUY, OrderType.LIMIT, TimeInForce.GTC);
                lastBuyPrice = buyPrice;
                ws.PlaceOrder(instrumentCode, sellPrice, volume, Side.SELL, OrderType.LIMIT, TimeInForce.GTC);
                lastSellPrice = sellPrice;
                if (logLogic)
                {
                    Console.WriteLine(string.Format("Initial orders sent out at {0} - {1}, volume:{2}", lastBuyPrice, lastSellPrice, volume));
                }
            }            
        }

        private static long buyOrderId = -1;
        private static bool buyReplaceNeeded = false;
        private static long sellOrderId = -1;
        private static bool sellReplaceNeeded = false;


        /// <summary>
        /// Noting the orderId of active orders for amendments and instructing for order replace if needed because of partial fill
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Ws_OrdersUpdate(object sender, ZubrWebsocket.Models.OrdersUpdate e)
        {
            if (!e.IsSnapshot && e.InstrumentId == instrumentCode)
            {
                if (e.Status == "FILLED" || e.Status == "CANCELLED")
                {
                    if (e.Side == Side.BUY)
                    {
                        buyOrderId = 0;
                    }
                    else
                    {
                        sellOrderId = 0;
                    }
                }
                else if (e.Status == "PARTIALLY_FILLED")
                {
                    if (e.Side == Side.BUY)
                    {
                        buyOrderId = e.OrderId;
                        buyReplaceNeeded = true;
                    }
                    else
                    {
                        sellOrderId = e.OrderId;
                        sellReplaceNeeded = true;
                    }
                }
                else if (e.Status == "NEW")
                {
                    if (e.Side == Side.BUY)
                    {
                        buyOrderId = e.OrderId;
                    }
                    else
                    {
                        sellOrderId = e.OrderId;
                    }
                }
                if (logLogic)
                {
                    Console.WriteLine("OrderUpdate received");
                }
            }
            else
            {
                orderSnapshotReceived = true;
            }
        }

        private static Dictionary<decimal, decimal> asks = new Dictionary<decimal, decimal>();
        private static Dictionary<decimal, decimal> bids = new Dictionary<decimal, decimal>();


        /// <summary>
        /// Keeping track of the orderbook to find best bid/best ask for calculations
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Ws_OrderbookUpdate(object sender, ZubrWebsocket.Models.OrderbookUpdate e)
        {
            if (e.InstrumentId == instrumentCode)
            {
                if (e.IsSnapshot)
                {
                    //Initial SOW push, full orderbook on subscription
                    asks = e.Asks;
                    bids = e.Bids;
                }
                else
                {
                    //orderbook update
                    foreach (var item in e.Asks)
                    {
                        if (item.Value > 0)
                        {
                            asks[item.Key] = item.Value;
                        }
                        else
                        {
                            asks.Remove(item.Key);
                        }
                    }                   

                    foreach (var item in e.Bids)
                    {
                        if (item.Value > 0)
                        {
                            bids[item.Key] = item.Value;
                        }
                        else
                        {
                            bids.Remove(item.Key);
                        }
                    }
                    
                }
                bestAsk = asks.Keys.Min();
                bestBid = bids.Keys.Max();
            }            
        }


        /// <summary>
        /// Position calculation on execution report
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Ws_OrderFillsUpdate(object sender, ZubrWebsocket.Models.OrderFillsUpdate e)
        {
            if (e.InstrumentId == instrumentCode)
            {
                if (e.Side == Side.BUY)
                {
                    position += e.TradeQty;
                }
                else
                {
                    position -= e.TradeQty;
                }
                if (logLogic)
                {
                    Console.WriteLine(string.Format("Fill received, position : {0}",position));
                }
            }
        }


        /// <summary>
        /// We use price change to update the orders. The price change is coming down on this event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Ws_TickersUpdate(object sender, ZubrWebsocket.Models.TickersUpdate e)
        {
            if (e.InstrumentId == instrumentCode)
            {                
                if (e.LastPrice != lastPrice)
                {
                    if (logLogic)
                    {
                        Console.WriteLine("Ticker received with new price");
                    }
                    lastPrice = e.LastPrice;
                    RefreshOpenOrders();
                }
            };
        }


        /// <summary>
        /// We re-post the orders if one of the conditions are met
        /// - previous order got filled (signalled by buyOrderId = 0 or sellOrderId = 0) , posting new one
        /// - previous order got partially filled (signalled by buyReplaceNeeded or sellReplaceNeeded) - replacing the order with a fresh one
        /// - optimal buy/sell price changed - replacing with fresh one
        /// </summary>
        private static void RefreshOpenOrders()
        {
            if (maxPosition - Math.Abs(position) > 0)
            {
                decimal buyPrice = tickRnd( ((bestBid + bestAsk) / 2) - interest - shift * position);
                decimal sellPrice = tickRnd( ((bestBid + bestAsk) / 2) + interest - shift * position);
                if ((buyPrice != lastBuyPrice || buyReplaceNeeded) && buyOrderId > -1)
                {
                    buyReplaceNeeded = false;
                    if (buyOrderId > 0)
                    {
                        ws.ReplaceOrder(buyOrderId, buyPrice, volume);
                    }
                    else
                    {
                        ws.PlaceOrder(instrumentCode, buyPrice, volume, Side.BUY, OrderType.LIMIT, TimeInForce.GTC);
                    }
                    lastBuyPrice = buyPrice;
                    if (logLogic)
                    {
                        Console.WriteLine(string.Format("Order replaced -- buy at {0}, volume:{1}", lastBuyPrice,  volume));
                    }
                }
                if ((sellPrice != lastSellPrice || sellReplaceNeeded) && sellOrderId > -1)
                {
                    sellReplaceNeeded = false;
                    if (sellOrderId > 0)
                    {
                        ws.ReplaceOrder(sellOrderId, sellPrice, volume);
                    }
                    else
                    {
                        ws.PlaceOrder(instrumentCode, sellPrice, volume, Side.SELL, OrderType.LIMIT, TimeInForce.GTC);
                    }
                    lastSellPrice = sellPrice;
                    if (logLogic)
                    {
                        Console.WriteLine(string.Format("Order replaced -- sell at {0}, volume:{1}", lastSellPrice, volume));
                    }
                }                
            }            
        }

        
        /// <summary>
        /// Initial call to query the position from the exchange if configured so
        /// in normal operations, we use execution reports, we unsubscribe from this after the init
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Ws_PositionsInit(object sender, ZubrWebsocket.Models.PositionsUpdate e)
        {            
            if (e.InstrumentId == instrumentCode)
            {
                position = e.Size;
                positionInitialised = true;
                if (logLogic)
                {
                    Console.WriteLine("Initial position read from exhange");
                }
            }
        }



        private static bool CheckRequiredPermissions(ZubrWebsocketClient ws)
        {
            List<string> requiredPermissions = new List<string>();
            requiredPermissions.Add("GET_ACCOUNT_DATA");
            requiredPermissions.Add("MARKET_DATA");
            requiredPermissions.Add("ORDER_DATA");
            requiredPermissions.Add("NEW_ORDER");
            requiredPermissions.Add("CANCEL_ORDER");
            bool ok = true;
            foreach (var permission in requiredPermissions)
            {
                if (!ws.Permissions.Contains(permission))
                {
                    ok = false;
                    break;
                }
            }
            return ok;
        }
       
    }
}
