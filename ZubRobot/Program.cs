using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        private static Dictionary<int, long> orderIdMappingsBuy = new Dictionary<int, long>();
        private static Dictionary<int, long> orderIdMappingsSell = new Dictionary<int, long>();
        public static bool HasActiveOrdersBuy { get { return orderIdMappingsBuy.Count(X => X.Value > -1) > 0 ; } }
        public static bool HasActiveOrdersSell { get { return orderIdMappingsSell.Count(X => X.Value > -1) > 0; } }
        public static bool HasPendingOrdersBuy { get { return orderIdMappingsBuy.Count(X => X.Value == -1) > 0; } }
        public static bool HasPendingOrdersSell { get { return orderIdMappingsSell.Count(X => X.Value == -1) > 0; } }



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
            ws.OrderConfirmations += WS_OrderConfirmationsUpdate;

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


            AppDomain.CurrentDomain.ProcessExit += new EventHandler(CurrentDomain_ProcessExit);
            Console.CancelKeyPress += new ConsoleCancelEventHandler(CurrentDomain_ProcessExit);

            InitOrders();


            while (true)
            {
                //do everything on other threads, we just can't let the main thread exit
                Thread.Sleep(100);
            }
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            Console.WriteLine("EXITING");
            var ordersToCancel = orderIdMappingsBuy.Where(X => X.Value > -1);
            foreach (var order in ordersToCancel)
            {
                ws.CancelOrder(order.Value);
                orderIdMappingsBuy.Remove(order.Key);
            }
            ordersToCancel = orderIdMappingsSell.Where(X => X.Value > -1);
            foreach (var order in ordersToCancel)
            {
                ws.CancelOrder(order.Value);
                orderIdMappingsSell.Remove(order.Key);
            }
        }

        private static void WS_OrderConfirmationsUpdate(object sender, ZubrWebsocket.Models.OrderConfirmationUpdate e)
        {
            lock (orderUpdateLock)
            {
                if (e.OK)
                {
                    if (orderIdMappingsBuy.ContainsKey(e.ClOrderId))
                    {
                        orderIdMappingsBuy[e.ClOrderId] = e.OrderId;
                    }
                    else if (orderIdMappingsSell.ContainsKey(e.ClOrderId))
                    {
                        orderIdMappingsSell[e.ClOrderId] = e.OrderId;
                    }
                    else
                    {
                        throw new Exception("ERROR - Order was created outside of the system");
                    }
                }
                else
                {
                    //Remove reference for order, it was not created right
                    if (orderIdMappingsBuy.ContainsKey(e.ClOrderId))
                    {
                        orderIdMappingsBuy.Remove(e.ClOrderId);
                    }
                    else if (orderIdMappingsSell.ContainsKey(e.ClOrderId))
                    {
                        orderIdMappingsSell.Remove(e.ClOrderId);
                    }
                }
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
                orderIdMappingsBuy.Add(ws.PlaceOrder(instrumentCode, buyPrice, volume, Side.BUY, OrderType.LIMIT, TimeInForce.GTC), -1);
                lastBuyPrice = buyPrice;
                orderIdMappingsSell.Add(ws.PlaceOrder(instrumentCode, sellPrice, volume, Side.SELL, OrderType.LIMIT, TimeInForce.GTC), -1);
                
                lastSellPrice = sellPrice;
                if (logLogic)
                {
                    Console.WriteLine(string.Format("Initial orders sent out at {0} - {1}, volume:{2}", lastBuyPrice, lastSellPrice, volume));
                }
            }            
        }

        private static bool buyReplaceNeeded = false;
        private static bool sellReplaceNeeded = false;


        /// <summary>
        /// Noting the orderId of active orders for amendments and instructing for order replace if needed because of partial fill
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Ws_OrdersUpdate(object sender, ZubrWebsocket.Models.OrdersUpdate e)
        {
            lock (orderUpdateLock)
            {
                if (!e.IsSnapshot && e.InstrumentId == instrumentCode)
                {

                    if (e.Status == "CANCELLED")
                    {
                        if (e.Side == Side.BUY)
                        {
                            var index = orderIdMappingsBuy.Where(X => X.Value == e.OrderId).FirstOrDefault().Key;
                            orderIdMappingsBuy.Remove(index);
                        }
                        else
                        {
                            var index = orderIdMappingsSell.Where(X => X.Value == e.OrderId).FirstOrDefault().Key;
                            orderIdMappingsSell.Remove(index);
                        }
                    }
                    else if(e.Status == "FILLED")
                    {
                        if (e.Side == Side.BUY)
                        {
                            var index = orderIdMappingsBuy.Where(X => X.Value == e.OrderId).FirstOrDefault().Key;
                            orderIdMappingsBuy.Remove(index);
                            buyReplaceNeeded = true;
                        }
                        else
                        {
                            var index = orderIdMappingsSell.Where(X => X.Value == e.OrderId).FirstOrDefault().Key;
                            orderIdMappingsSell.Remove(index);
                            sellReplaceNeeded = true;
                        }
                    }
                    else if(e.Status == "PARTIALLY_FILLED")
                    {
                        if (e.Side == Side.BUY)
                        {
                            buyReplaceNeeded = true;
                        }
                        else
                        {
                            sellReplaceNeeded = true;
                        }
                    }
                    if (logLogic)
                    {
                        Console.WriteLine(string.Format("OrderUpdate received - {0}", e.Status));
                    }
                }
                else
                {
                    orderSnapshotReceived = true;
                }
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
            lock (orderUpdateLock)
            {
                if (e.InstrumentId == instrumentCode && !e.IsSnapshot)
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
                        Console.WriteLine(string.Format("Fill received, position : {0}", position));
                    }
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

        private static object orderUpdateLock = new object();
        /// <summary>
        /// We re-post the orders if one of the conditions are met
        /// - previous order got filled (signalled by buyOrderId = 0 or sellOrderId = 0) , posting new one
        /// - previous order got partially filled (signalled by buyReplaceNeeded or sellReplaceNeeded) - replacing the order with a fresh one
        /// - optimal buy/sell price changed - replacing with fresh one
        /// </summary>
        private static void RefreshOpenOrders()
        {
            lock (orderUpdateLock)
            {

                //only one thread can handle the update to prevent double order placement

                decimal buyPrice = tickRnd(((bestBid + bestAsk) / 2) - interest - shift * position);
                decimal sellPrice = tickRnd(((bestBid + bestAsk) / 2) + interest - shift * position);
                if (maxPosition > position)
                {
                    //We are in the limit with the buys, buy order can go out
                    if ((buyPrice != lastBuyPrice || buyReplaceNeeded) && !HasPendingOrdersBuy)
                    {
                        buyReplaceNeeded = false;
                        //cancelling old, not using replace to avoid race conditions with late fills
                        //cancelling all that not cancelled yet and received ack for from exhange (to circumvent late acknowledgements if that happens)
                        var ordersToCancel = orderIdMappingsBuy.Where(X => X.Value > -1);
                        foreach (var order in ordersToCancel)
                        {
                            ws.CancelOrder(order.Value);
                            orderIdMappingsBuy.Remove(order.Key);
                        }
                        //Ensuring no other order placements happen until acknowledgement, adding it to mapping with -1                         
                        orderIdMappingsBuy.Add(ws.PlaceOrder(instrumentCode, buyPrice, volume, Side.BUY, OrderType.LIMIT, TimeInForce.GTC), -1);
                        lastBuyPrice = buyPrice;
                        if (logLogic)
                        {
                            Console.WriteLine(string.Format("Order replaced -- buy at {0}, volume:{1}", lastBuyPrice, volume));
                        }
                    }                    
                }
                else
                {
                    //Cancel all outstanding buys, if any
                    var ordersToCancel = orderIdMappingsBuy.Where(X => X.Value > -1);
                    foreach (var order in ordersToCancel)
                    {
                        ws.CancelOrder(order.Value);
                        orderIdMappingsBuy.Remove(order.Key);
                    }
                    if (logLogic)
                    {
                        Console.WriteLine("MAx position reached -- buy disabled, orders cancelled");
                    }
                }

                if (-1 * maxPosition < position)
                {
                    //we still can sell some more to reach the limit
                    if ((sellPrice != lastSellPrice || sellReplaceNeeded) && !HasPendingOrdersSell)
                    {
                        sellReplaceNeeded = false;
                        var ordersToCancel = orderIdMappingsSell.Where(X => X.Value > -1);
                        foreach (var order in ordersToCancel)
                        {
                            ws.CancelOrder(order.Value);
                            orderIdMappingsSell.Remove(order.Key);
                        }
                        //Ensuring no other order placements happen until acknowledgement, adding it to mapping with -1                         
                        orderIdMappingsSell.Add(ws.PlaceOrder(instrumentCode, sellPrice, volume, Side.SELL, OrderType.LIMIT, TimeInForce.GTC), -1);
                        lastSellPrice = sellPrice;
                        if (logLogic)
                        {
                            Console.WriteLine(string.Format("Order replaced -- sell at {0}, volume:{1}", lastSellPrice, volume));
                        }
                    }
                }
                else
                {
                    //cancel all sells
                    var ordersToCancel = orderIdMappingsSell.Where(X => X.Value > -1);
                    foreach (var order in ordersToCancel)
                    {
                        ws.CancelOrder(order.Value);
                        orderIdMappingsSell.Remove(order.Key);
                    }
                    if (logLogic)
                    {
                        Console.WriteLine("MAx position reached -- sell disabled, orders cancelled");
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
            if (e == null)
            {
                positionInitialised = true;
            }
            else
            {
                if (e.InstrumentId == instrumentCode)
                {
                    position = e.Size;
                    positionInitialised = true;
                    if (logLogic)
                    {
                        Console.WriteLine(string.Format("Initial position read from exhange : {0}", position));
                    }
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
