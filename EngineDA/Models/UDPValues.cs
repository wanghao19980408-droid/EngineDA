using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EngineDA.Models
{
    public class UDPValues
    {
        public bool[]? DI_value { get; set; }
        public bool[]? DQ_value { get; set; }
        public short[]? AQ_value { get; set; }
        public bool[]? PLC_status { get; set; }
        public short[]? AI_value { get; set; }
        public short[]? HIGH_AI_value { get; set; }
    }
}
