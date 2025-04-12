using System;
using System.Linq;

namespace LogStatTool.Contracts
{
    public class HashLinesDataflowConfiguration
    {
        public int BoundedCapacity { get; set; } = 10000;
        public int Parallelism { get; set; } = 8;

    }
}
