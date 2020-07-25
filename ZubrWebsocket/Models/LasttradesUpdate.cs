using System;
using System.Collections.Generic;
using System.Text;

namespace ZubrWebsocket.Models
{
    public class LasttradesUpdate
    {
        public long Id { get; set; }
        public int InstrumentId { get; set; }
        public Side Side { get; set; }
        public decimal Price { get; set; }
        public decimal Size { get; set; }
        public DateTime TimeUTC { get; set; }
    }
}
