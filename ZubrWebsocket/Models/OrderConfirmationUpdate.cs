using System;
using System.Collections.Generic;
using System.Text;

namespace ZubrWebsocket.Models
{
    public class OrderConfirmationUpdate
    {
        public int ClOrderId { get; set; }
        public long OrderId { get; set; }
        public bool OK { get; set; }
    }
}
