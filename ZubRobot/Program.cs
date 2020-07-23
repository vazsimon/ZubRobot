using System;
using System.Threading;
using System.Threading.Tasks;
using ZubrWebsocket;

namespace ZubRobot
{
    class Program
    {
        static void Main(string[] args)
        {

            var ws = new ZubrWebsocketClient("3ochCJvpuZV4NKKRN6G71u", "00ea2a26932165e473445effdc2a740549245a4ea2e30fd841721fb40a67b9f2");
            ws.Login();
            

            while (true)
            {
                Thread.Sleep(1000);
            }
        }
    }
}
