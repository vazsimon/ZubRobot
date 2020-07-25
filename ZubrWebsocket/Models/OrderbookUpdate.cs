using System;
using System.Collections.Generic;
using System.Text;

namespace ZubrWebsocket.Models
{
    public class OrderbookUpdate
    {
        public int InstrumentId { get; set; }
        public bool IsSnapshot { get; set; }
        public Dictionary<decimal,decimal> Bids { get; set; }
        public Dictionary<decimal,decimal> Asks { get; set; }
    }
}
