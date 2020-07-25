using System;
using System.Collections.Generic;
using System.Text;

namespace ZubrWebsocket
{
    public enum Side
    {
        BUY,
        SELL
    }

    public enum OrderType
    {
        LIMIT,
        POST_ONLY
    }

    public enum TimeInForce
    {
        GTC,
        IOC,
        FOK
    }

    public enum Channel
    {
        orders,
        orderFills,
        lasttrades,
        orderbook,
        tickers,
        instruments,
        positions
    }
}
