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

namespace ZubrWebsocket
{
    public class ZubrWebsocketClient
    {

        private const string wsAddress = @"wss://uat.zubr.io/api/v1/ws";

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

        private int id = 1;

        private List<string> _queuedMessages;

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



        public void Send(string msg)
        {
            if (!Connected)
            {
                _loggedIn = false;
                _ws.Connect();
            }
            Console.WriteLine("---------------------------------------Sending------------------------------------------");
            Console.WriteLine(msg);
            Console.WriteLine("---------------------------------------Sending------------------------------------------");
            _ws.Send(msg);
        }

        public void SendAfterLogin(string msg)
        {
            if (loggedIn)
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

        public void PlaceOrder(int instrument_id, double price, int size, Side side, OrderType type, TimeInForce timeInForce)
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


        public void ReplaceOrder(long orderId, double price, int size)
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

        

        private void GetMantissaAndExponent(double price, ref long mantissa, ref int exponent)
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

        private bool _loggedIn = false;
        public bool loggedIn { get { return _loggedIn; } } 

        private void _ws_OnMessage(object sender, MessageEventArgs e)
        {
            Console.WriteLine("---------------------------------------Received------------------------------------------");
            Console.WriteLine(e.Data);
            Console.WriteLine("---------------------------------------Received------------------------------------------"); ;
            ProcessMessage(e.Data);
        }

        private void ProcessMessage(string msg)
        {
            var jt = JToken.Parse(msg);
            if (jt.Value<int>("id") == loginMessageId)
            {
                _loggedIn = jt["result"].Value<string>("tag") == "ok";
                if (_loggedIn)
                {
                    while (_queuedMessages.Count > 0)
                    {
                        string msgOut = _queuedMessages[0];
                        _queuedMessages.RemoveAt(0);
                        Send(msgOut);
                    }
                }                
            }
        }
    }
}
