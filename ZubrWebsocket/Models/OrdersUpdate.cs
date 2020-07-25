using System;
using System.Collections.Generic;
using System.Text;

namespace ZubrWebsocket.Models
{
    public class OrdersUpdate
    {
        public int InstrumentId { get; set; }
        public long OrderId { get; set; }
        public long AccountId { get; set; }
        public OrderType OrderType { get; set; }
        public TimeInForce TimeInForce { get; set; }
        public Side Side { get; set; }
        public decimal InitialSize { get; set; }
        public decimal RemainingSize { get; set; }
        public decimal Price { get; set; }
        public string Status { get; set; }
        public DateTime UpdateTime { get; set; }
        public DateTime CreationTime { get; set; }
        public bool IsSnapshot { get; set; }
    }
}
