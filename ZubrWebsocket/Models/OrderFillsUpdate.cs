using System;
using System.Collections.Generic;
using System.Text;

namespace ZubrWebsocket.Models
{
    public class OrderFillsUpdate
    {
        public long OrderId { get; set; }
        public long AccountId { get; set; }
        public long TradeId { get; set; }
        public int InstrumentId { get; set; }
        public decimal OrderPrice { get; set; }
        public decimal TradePrice { get; set; }
        public decimal InitialQty { get; set; }
        public decimal RemainingQty { get; set; }
        public decimal TradeQty { get; set; }
        public decimal ExchangeFee { get; set; }
        public string ExchangeFeeCurrency { get; set; }
        public decimal BrokerFee { get; set; }
        public string BrokerFeeCurrency { get; set; }
        public DateTime Time { get; set; }
        public Side Side { get; set; }
        public bool IsSnapshot { get; set; }
    }
}
