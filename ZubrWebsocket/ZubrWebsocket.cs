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

       

        public ZubrWebsocketClient(string apiKey, string apiSecret)
        {
            SetCredentials(apiKey, apiSecret);
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
                loggedIn = false;
                _ws.Connect();
            }
            Console.WriteLine("---------------------------------------Sending------------------------------------------");
            Console.WriteLine(msg);
            Console.WriteLine("---------------------------------------Sending------------------------------------------");
            _ws.Send(msg);
        }

        DateTime nextPingTime = DateTime.MaxValue;

        public void Login()
        {
            var timestamp = Math.Round((DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds).ToString();
            string preHash = string.Format("key={0};time={1}", _apiKey, timestamp);
            //string preHash = "key=3ochCJvpuZV4NKKRN6G71u;time=1595423007";
            //string hash = "97bbcdd364fac2f60488e48801ce7f19e5c45f28773b58c8e9741ab65aa0bf26";
            var data = Encoding.UTF8.GetBytes(preHash);
         
            var dataSigned = _hmac.ComputeHash(data);
            StringBuilder sb = new StringBuilder(dataSigned.Length * 2);
            foreach (byte b in dataSigned)
            {
                sb.AppendFormat("{0:x2}", b);
            }
            var signString = sb.ToString();

            //var signString = System.Convert.ToBase64String(dataSigned);
            
            string msgStrBase = @"{{""method"":9,""params"":{{""data"":{{""method"":""loginSessionByApiToken"",""params"":{{""apiKey"":""{0}"",""time"":{{""seconds"":{1},""nanos"":{2}}},""hmacDigest"":""{3}""}}}}}},""id"":{4}}}";
            
            string msgStr = string.Format(msgStrBase, _apiKey, timestamp, 0, signString, id++);
            Send(msgStr);
        }


        private void _ws_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            throw new Exception(e.Message);
        }



        public bool loggedIn { get; set; } = false;

        private void _ws_OnMessage(object sender, MessageEventArgs e)
        {
            nextPingTime = DateTime.Now.AddSeconds(15);
            Console.WriteLine("---------------------------------------Received------------------------------------------");
            Console.WriteLine(e.Data);
            Console.WriteLine("---------------------------------------Received------------------------------------------"); ;                
        }

        public void Connect()
        {
            _ws.Connect();
        }
    }
}
