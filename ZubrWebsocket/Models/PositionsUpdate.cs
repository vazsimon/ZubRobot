using System;
using System.Collections.Generic;
using System.Text;

namespace ZubrWebsocket.Models
{
    public class PositionsUpdate
    {
        public int InstrumentId { get; set; }
        public long AccountId { get; set; }
        public decimal Size { get; set; }
        public decimal UnrealizedPNL { get; set; }
        public decimal RealizedPNL { get; set; }
        public decimal Margin { get; set; }
        public decimal MaxRemovableMargin { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal EntryNotionalValue { get; set; }
        public decimal CurrentNotionalValue { get; set; }
        public DateTime UpdateTime { get; set; }
        public bool IsSnapshot { get; set; }
    }
}
