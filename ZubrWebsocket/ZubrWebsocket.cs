using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using ZubrWebsocket.Models;

namespace ZubrWebsocket
{
    /// <summary>
    /// Websocket client to handle orders and market data on Zubr exhange.
    /// </summary>
    public class ZubrWebsocketClient
    {

        private const string wsAddress = @"wss://uat.zubr.io/api/v1/ws";

        public event EventHandler<OrdersUpdate> Orders;
        public event EventHandler<OrderFillsUpdate> OrderFills;
        public event EventHandler<LasttradesUpdate> LastTrades;
        public event EventHandler<PositionsUpdate> Positions;
        public event EventHandler<OrderbookUpdate> Orderbook;
        public event EventHandler<TickersUpdate> Tickers;


        private WebSocket _ws;
        public bool Connected { get { return _ws.IsAlive; } }

        private string _apiKey;
        private string _secret;
        private readonly HMACSHA256 _hmac = new HMACSHA256();
        

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        public void SetCredentials(string apiKey, string apiSecret)
        {
            _apiKey = apiKey; 
            _secret = apiSecret;
            _hmac.Key = StringToByteArray(_secret);
        }


        /// <summary>
        /// Message id for the json-rpc protocol
        /// </summary>
        private int id = 1;

        private List<string> _queuedMessages;


        /// <summary>
        /// APIKey and ApiSecret are required to be able to log in to the websocket interface of ZUBR
        /// </summary>
        /// <param name="apiKey"></param>
        /// <param name="apiSecret"></param>
        public ZubrWebsocketClient(string apiKey, string apiSecret)
        {
            SetCredentials(apiKey, apiSecret);
            _queuedMessages = new List<string>();
            _ws = new WebSocket(wsAddress);
            _ws.Log.Level = LogLevel.Trace;

            _ws.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            _ws.EmitOnPing = true;
 

            _ws.OnMessage += _ws_OnMessage;
            _ws.OnError += _ws_OnError;

            // 15 second ping required to keep connection alive
            Task.Run(() =>
            {
                while (true)
                {
                    if (Connected)
                    {
                        _ws.Ping();
                    }
                    Thread.Sleep(15000);
                }
            });

        }



        private void Send(string msg)
        {
            if (!Connected)
            {
                LoggedIn = false;
                _ws.Connect();
            }
            Console.WriteLine("---------------------------------------Sending------------------------------------------");
            Console.WriteLine(msg);
            Console.WriteLine("---------------------------------------Sending------------------------------------------");
            _ws.Send(msg);
        }


        /// <summary>
        /// Use only for messages requiring login.
        /// Messages are queued until successful login, in a First-come - first served manner
        /// </summary>
        /// <param name="msg"></param>
        private void SendAfterLogin(string msg)
        {
            if (LoggedIn)
            {
                Send(msg);
            }
            else
            {
                _queuedMessages.Add(msg);
            }
        }


        private int loginMessageId = -1;

        public void Login()
        {
            var timestamp = Math.Round((DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds).ToString();
            string preHash = string.Format("key={0};time={1}", _apiKey, timestamp);
            var data = Encoding.UTF8.GetBytes(preHash);
         
            var dataSigned = _hmac.ComputeHash(data);
            StringBuilder sb = new StringBuilder(dataSigned.Length * 2);
            foreach (byte b in dataSigned)
            {
                sb.AppendFormat("{0:x2}", b);
            }
            var signString = sb.ToString();
            
            string msgStrBase = @"{{""method"":9,""params"":{{""data"":{{""method"":""loginSessionByApiToken"",""params"":{{""apiKey"":""{0}"",""time"":{{""seconds"":{1},""nanos"":{2}}},""hmacDigest"":""{3}""}}}}}},""id"":{4}}}";
            
            string msgStr = string.Format(msgStrBase, _apiKey, timestamp, 0, signString, id);
            loginMessageId = id++;
            Send(msgStr);
        }


        private void _ws_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            throw new Exception(e.Message);
        }

        public void PlaceOrder(int instrument_id, decimal price, int size, Side side, OrderType type, TimeInForce timeInForce)
        {
            string msgStrBase = @"{{""method"":9,""params"":{{""data"":{{""method"":""placeOrder"",""params"":{{""instrument"":{0},""price"":{{""mantissa"":{1},""exponent"":{2}}},""size"":{3},""type"":""{4}"",""timeInForce"":""{5}"",""side"":""{6}""}}}}}},""id"":{7}}}";
            string typeStr = Enum.GetName(typeof(OrderType), type);
            string sideStr = Enum.GetName(typeof(Side), side);
            string tifStr = Enum.GetName(typeof(TimeInForce), timeInForce);
            long mantissa = 0;
            int exponent = 0;
            GetMantissaAndExponent(price, ref mantissa, ref exponent);
            string msgStr = string.Format(msgStrBase, instrument_id, mantissa, exponent, size, typeStr, tifStr, sideStr, id++);
            SendAfterLogin(msgStr);
        }


        public void ReplaceOrder(long orderId, decimal price, int size)
        {
            string msgStrBase = @"{{""method"":9,""params"":{{""data"":{{""method"":""replaceOrder"",""params"":{{""orderId"":{0},""price"":{{""mantissa"":{1},""exponent"":{2}}},""size"":{3}}}}}}},""id"":{4}}}";
            long mantissa = 0;
            int exponent = 0;
            GetMantissaAndExponent(price, ref mantissa, ref exponent);
            string msgStr = string.Format(msgStrBase, orderId, mantissa, exponent, size,  id++);
            SendAfterLogin(msgStr);
        }


        public void CancelOrder(long orderId)
        {
            string msgStrBase = @"{{""method"":9,""params"":{{""data"":{{""method"":""cancelOrder"",""params"":{0}}}}},""id"":{1}}}";
            string msgStr = string.Format(msgStrBase, orderId, id++);
            SendAfterLogin(msgStr);
        }


        /// <summary>
        /// Rule - all digits are in mantissa, exponent is the decimal places count
        /// </summary>
        /// <param name="price">original price</param>
        /// <param name="mantissa">REF - directly modifies parameter</param>
        /// <param name="exponent">REF - directly modifies parameter</param>
        private void GetMantissaAndExponent(decimal price, ref long mantissa, ref int exponent)
        {
            StringBuilder sbMantissa = new StringBuilder();
            var priceStr = price.ToString();
            bool decimalFound = false;
            int decimalCount = 0;
            for (int i = 0; i < priceStr.Length; i++)
            {
                if (priceStr[i] != '.')
                {
                    sbMantissa.Append(priceStr[i]);
                    if (decimalFound)
                    {
                        decimalCount++;
                    }
                }
                else
                {
                    decimalFound = true;
                }
            }
            mantissa = long.Parse(sbMantissa.ToString());
            exponent = -1 * decimalCount;
        }

        public bool LoggedIn { get; private set; } = false;

        /// <summary>
        /// Transport level logic - All incoming messages are received in this event subscription
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _ws_OnMessage(object sender, MessageEventArgs e)
        {
            Console.WriteLine("---------------------------------------Received------------------------------------------");
            Console.WriteLine(e.Data);
            Console.WriteLine("---------------------------------------Received------------------------------------------"); ;
            ProcessMessage(e.Data);
        }

        /// <summary>
        /// Api level logic - All incoming websocket messages are handled here
        /// </summary>
        /// <param name="msg"></param>
        private void ProcessMessage(string msg)
        {
            var jt = JToken.Parse(msg);
            if (jt.Value<int>("id") == loginMessageId)
            {
                ProcessLoginResponse(jt);                                
            }

            if (jt["result"] != null && !string.IsNullOrEmpty(jt["result"].Value<string>("channel")))
            {
                var channel = jt["result"].Value<string>("channel");
                if (jt["result"]["data"].Value<string>("tag") == "ok")
                {
                    switch (channel)
                    {
                        case "tickers":
                            ProcessTickerMessage(jt);
                            break;
                        case "lasttrades":
                            ProcessLastTradesMessage(jt);
                            break;
                        case "orderbook":
                            ProcessOrderbookMessage(jt);
                            break;
                        case "orders":
                            ProcessOrdersMessage(jt);
                            break;
                        case "orderFills":
                            ProcessOrderFillsMessage(jt);
                            break;
                        case "positions":
                            ProcessPositionsMessage(jt);
                            break;
                        default:
                            break;
                    }
                }
            }
        }


        /// <summary>
        /// Positions subscription handler
        /// </summary>
        /// <param name="jt"></param>
        private void ProcessPositionsMessage(JToken jt)
        {
            var payload = jt["result"]["data"]["value"]["payload"];
            if (jt["result"]["data"]["value"].Value<string>("type") == "snapshot")
            {
                foreach (var item in payload)
                {
                    var position = item.First();
                    PositionsUpdate t = new PositionsUpdate
                    {
                        InstrumentId = position.Value<int>("instrument"),
                        AccountId = position.Value<long>("accountId"),
                        Size = position.Value<decimal>("size"),
                        CurrentNotionalValue = GetDecimal(position["currentNotionalValue"]),
                        EntryNotionalValue = GetDecimal(position["entryNotionalValue"]),
                        EntryPrice = GetDecimal(position["entryPrice"]),
                        Margin = GetDecimal(position["margin"]),
                        MaxRemovableMargin = GetDecimal(position["maxRemovableMargin"]),
                        RealizedPNL = GetDecimal(position["realizedPnl"]),
                        UnrealizedPNL = GetDecimal(position["unrealizedPnl"]),
                        UpdateTime = GetTimeUTC(position["updateTime"]),
                        IsSnapshot = true
                    };
                    Positions?.Invoke(this, t);
                }
            }
            else
            {
                var position = payload;
                PositionsUpdate t = new PositionsUpdate
                {
                    InstrumentId = position.Value<int>("instrument"),
                    AccountId = position.Value<long>("accountId"),
                    Size = position.Value<decimal>("size"),
                    CurrentNotionalValue = GetDecimal(position["currentNotionalValue"]),
                    EntryNotionalValue = GetDecimal(position["entryNotionalValue"]),
                    EntryPrice = GetDecimal(position["entryPrice"]),
                    Margin = GetDecimal(position["margin"]),
                    MaxRemovableMargin = GetDecimal(position["maxRemovableMargin"]),
                    RealizedPNL = GetDecimal(position["realizedPnl"]),
                    UnrealizedPNL = GetDecimal(position["unrealizedPnl"]),
                    UpdateTime = GetTimeUTC(position["updateTime"]),
                    IsSnapshot = false
                };
                Positions?.Invoke(this, t);
            }
        }


        /// <summary>
        /// Fills subcription handler
        /// </summary>
        /// <param name="jt"></param>
        private void ProcessOrderFillsMessage(JToken jt)
        {
            var payload = jt["result"]["data"]["value"]["payload"];
            if (jt["result"]["data"]["value"].Value<string>("type") == "snapshot")
            {
                foreach (var fill in payload)
                {
                    OrderFillsUpdate t = new OrderFillsUpdate
                    {
                        InstrumentId = fill.Value<int>("instrument"),
                        AccountId = fill.Value<long>("accountId"),
                        OrderId = fill.Value<long>("id"),
                        Side = fill.Value<string>("side") == "BUY" ? Side.BUY : Side.SELL,
                        IsSnapshot = true,
                        InitialQty = fill.Value<decimal>("initialQty"),
                        RemainingQty = fill.Value<decimal>("remainingQty"),
                        TradeQty =  fill.Value<decimal>("tradeQty"),
                        TradePrice = GetDecimal(fill["tradePrice"]),
                         OrderPrice= GetDecimal(fill["orderPrice"]),
                         BrokerFee = GetDecimal(fill["brokerFee"]["amount"]),
                         BrokerFeeCurrency = fill["brokerFee"].Value<string>("currency"),
                         ExchangeFeeCurrency = fill["exchangeFee"].Value<string>("currency"),
                         ExchangeFee = GetDecimal(fill["exchangeFee"]["amount"]),
                         Time = GetTimeUTC(fill["time"]),
                         TradeId = fill.Value<long>("tradeId")
                    };
                    OrderFills?.Invoke(this, t);
                }
            }
            else
            {
                var fill = payload;
                OrderFillsUpdate t = new OrderFillsUpdate
                {
                    InstrumentId = fill.Value<int>("instrument"),
                    AccountId = fill.Value<long>("accountId"),
                    OrderId = fill.Value<long>("id"),
                    Side = fill.Value<string>("side") == "BUY" ? Side.BUY : Side.SELL,
                    IsSnapshot = false,
                    InitialQty = fill.Value<decimal>("initialQty"),
                    RemainingQty = fill.Value<decimal>("remainingQty"),
                    TradeQty = fill.Value<decimal>("tradeQty"),
                    TradePrice = GetDecimal(fill["tradePrice"]),
                    OrderPrice = GetDecimal(fill["orderPrice"]),
                    BrokerFee = GetDecimal(fill["brokerFee"]["amount"]),
                    BrokerFeeCurrency = fill["brokerFee"].Value<string>("currency"),
                    ExchangeFeeCurrency = fill["exchangeFee"].Value<string>("currency"),
                    ExchangeFee = GetDecimal(fill["exchangeFee"]["amount"]),
                    Time = GetTimeUTC(fill["time"]),
                    TradeId = fill.Value<long>("tradeId")
                };
                OrderFills?.Invoke(this, t);
            }
        }

        /// <summary>
        /// Orders subscription handler
        /// </summary>
        /// <param name="jt"></param>
        private void ProcessOrdersMessage(JToken jt)
        {
            var payload = jt["result"]["data"]["value"]["payload"];
            if (jt["result"]["data"]["value"].Value<string>("type") == "snapshot")
            {
                foreach (var item in payload.Children())
                {
                    var order = item.First();
                    OrdersUpdate t = new OrdersUpdate
                    {
                        InstrumentId = order.Value<int>("instrument"),
                        AccountId = order.Value<long>("accountId"),
                        OrderId = order.Value<long>("id"),
                        Status = order.Value<string>("status"),
                        Side = order.Value<string>("side") == "BUY" ? Side.BUY : Side.SELL,
                        OrderType = order.Value<string>("type") == "LIMIT" ? OrderType.LIMIT : OrderType.POST_ONLY,
                        TimeInForce = order.Value<string>("timeInForce") == "GTC" ? TimeInForce.GTC : order.Value<string>("timeInForce") == "IOC" ? TimeInForce.IOC : TimeInForce.FOK,
                        InitialSize = order.Value<decimal>("initialSize"),
                        RemainingSize = order.Value<decimal>("remainingSize"),
                        Price = GetDecimal(order["price"]),
                        CreationTime = GetTimeUTC(order["creationTime"]),
                        UpdateTime = GetTimeUTC(order["updateTime"]),
                        IsSnapshot = true
                    };
                    Orders?.Invoke(this, t);
                }
            }
            else
            {
                var order = payload;
                OrdersUpdate t = new OrdersUpdate
                {
                    InstrumentId = order.Value<int>("instrument"),
                    AccountId = order.Value<long>("accountId"),
                    OrderId = order.Value<long>("id"),
                    Status = order.Value<string>("status"),
                    Side = order.Value<string>("side") == "BUY" ? Side.BUY : Side.SELL,
                    OrderType = order.Value<string>("type") == "LIMIT" ? OrderType.LIMIT : OrderType.POST_ONLY,
                    TimeInForce = order.Value<string>("timeInForce") == "GTC" ? TimeInForce.GTC : order.Value<string>("timeInForce") == "IOC" ? TimeInForce.IOC : TimeInForce.FOK,
                    InitialSize = order.Value<decimal>("initialSize"),
                    RemainingSize = order.Value<decimal>("remainingSize"),
                    Price = GetDecimal(order["price"]),
                    CreationTime = GetTimeUTC(order["creationTime"]),
                    UpdateTime = GetTimeUTC(order["updateTime"]),
                    IsSnapshot = false
                };
                Orders?.Invoke(this, t);
            }
        }

        /// <summary>
        /// Orderbook subscription handler
        /// </summary>
        /// <param name="jt"></param>
        private void ProcessOrderbookMessage(JToken jt)
        {
            var payload = jt["result"]["data"]["value"].Children();
            foreach (var instrument in payload)
            {
                var item = instrument.First();

                OrderbookUpdate t = new OrderbookUpdate
                {
                    InstrumentId = item.Value<int>("instrumentId"),
                    IsSnapshot = item.Value<bool>("isSnapshot"),
                    Asks = TransformOrderBookRaw(item["asks"]),
                    Bids = TransformOrderBookRaw(item["bids"]),
                };

                Orderbook?.Invoke(this, t);
            }
        }

        private Dictionary<decimal, decimal> TransformOrderBookRaw(JToken jTokens)
        {
            Dictionary<decimal, decimal> book = new Dictionary<decimal, decimal>();
            foreach (var jt in jTokens)
            {
                book.Add(GetDecimal(jt["price"]), jt.Value<decimal>("size"));
            }
            return book;
        }


        /// <summary>
        /// Lasttrades subscription handler
        /// </summary>
        /// <param name="jt"></param>
        private void ProcessLastTradesMessage(JToken jt)
        {
            //API first sends state of whe world at subscription as array, later sends individual updates. We have to be able to handle them both in one place
            var payloadAll = jt["result"]["data"]["value"]["payload"].Value<JToken>();
            var payloadArray = payloadAll as JArray;
            if (payloadArray != null)
            {
                foreach (var payload in payloadAll)
                {
                    LasttradesUpdate t = new LasttradesUpdate
                    {
                        Id = payload.Value<long>("id"),
                        InstrumentId = payload.Value<int>("instrumentId"),
                        Price = GetDecimal(payload["price"]),
                        Size = payload.Value<decimal>("size"),
                        Side = payload.Value<string>("side") == "buy" ? Side.BUY : Side.SELL,
                        TimeUTC = GetTimeUTC(payload["time"])
                    };
                    LastTrades?.Invoke(this, t);
                }
            }
            else
            {
                var payload = payloadAll;
                LasttradesUpdate t = new LasttradesUpdate
                {
                    Id = payload.Value<long>("id"),
                    InstrumentId = payload.Value<int>("instrumentId"),
                    Price = GetDecimal(payload["price"]),
                    Size = payload.Value<decimal>("size"),
                    Side = payload.Value<string>("side") == "buy" ? Side.BUY : Side.SELL,
                    TimeUTC = GetTimeUTC(payload["time"])
                };
                LastTrades?.Invoke(this, t);
            }
            
        }

        private DateTime GetTimeUTC(JToken jt)
        {
            return new DateTime(1970, 01, 01, 0, 0, 0, DateTimeKind.Utc).AddSeconds(jt.Value<double>("seconds")).AddMilliseconds(jt.Value<double>("nanos") / 1000000);
        }


        /// <summary>
        /// Ticker stream subscription handler
        /// </summary>
        /// <param name="jt"></param>
        private void ProcessTickerMessage(JToken jt)
        {
            var payload = jt["result"]["data"]["value"].Children();
            foreach (var instrument in payload)
            {
                var item = instrument.First();
                TickersUpdate t = new TickersUpdate
                {
                    InstrumentId = item.Value<int>("id"),
                    LastPrice = GetDecimal(item["lastPrice"]),
                    HighPrice24h = GetDecimal(item["highPrice24h"]),
                    LowPrice24h = GetDecimal(item["lowPrice24h"]),
                    markPrice = GetDecimal(item["markPrice"]),

                    Volume24h = item.Value<decimal>("volume24h"),
                    PriceChange24h = item.Value<decimal>("priceChange24h")
                };
                Tickers?.Invoke(this, t);
            }
        }

        private decimal GetDecimal(JToken jt)
        {
            decimal mantissa = jt.Value<long>("mantissa");
            int exponent = jt.Value<int>("exponent");
            while (exponent != 0)
            {
                if (exponent < 0)
                {
                    mantissa = mantissa / 10;
                    exponent++;
                }
                else
                {
                    mantissa = mantissa * 10;
                    exponent--;
                }
            }
            return mantissa;
        }

        private void ProcessLoginResponse(JToken jt)
        {
            //Response for login request. We have to also check authorization for trading
            LoggedIn = jt["result"].Value<string>("tag") == "ok";
            //Handle the messages enqueued for not being logged in yet
            if (LoggedIn)
            {
                while (_queuedMessages.Count > 0)
                {
                    string msgOut = _queuedMessages[0];
                    _queuedMessages.RemoveAt(0);
                    Send(msgOut);
                }
            }
        }


        /// <summary>
        /// Subscribe to channels provided by ZUBR. Channel updates will come through the corresponding events
        /// </summary>
        /// <param name="channel"></param>
        public void Subscribe(Channel channel)
        {
            string msgStrBase = @"{{""method"":1,""params"":{{""channel"":""{0}""}},""id"":{1}}}";
            string channelStr = Enum.GetName(typeof(Channel), channel);
            string msgStr = string.Format(msgStrBase, channelStr, id++);
            //Ensure we are logged in first if we subscribe to channel with authentication requirement
            Channel[] channelsWithAuth = new Channel[] { Channel.orders, Channel.orderFills, Channel.positions };
            if (channelsWithAuth.Contains(channel))
            {
                SendAfterLogin(msgStr);
            }
            else
            {
                Send(msgStr);
            }
        }

        /// <summary>
        /// Unsubscribe from channel
        /// </summary>
        /// <param name="channel"></param>
        public void Unsubscribe(Channel channel)
        {
            string msgStrBase = @"{{""method"":2,""params"":{{""channel"":""{0}""}},""id"":{1}}}";
            string channelStr = Enum.GetName(typeof(Channel), channel);
            string msgStr = string.Format(msgStrBase, channelStr, id++);
            Send(msgStr);
        }


    }
}
