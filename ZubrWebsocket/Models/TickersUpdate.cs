using System;
using System.Collections.Generic;
using System.Text;

namespace ZubrWebsocket.Models
{
    public class TickersUpdate
    {
        public int InstrumentId { get; set; }
        public decimal LastPrice { get; set; }
        public decimal PriceChange24h { get; set; }
        public decimal HighPrice24h { get; set; }
        public decimal LowPrice24h { get; set; }
        public decimal Volume24h { get; set; }
        public decimal markPrice { get; set; }
    }
}
