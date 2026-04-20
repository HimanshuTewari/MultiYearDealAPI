using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiYearDeal.Model
{
    public class InventoryData
    {
        public string ProductId { get; set; }
        public string ProductName { get; set; }
        public string ProductFamily { get; set; }
        public string ProductSubFamily { get; set; }
        public string Division { get; set; }
        public bool IsPassthroughCost { get; set; }
        public string RateType { get; set; } // "Individual" or "Event Schedule"
        public decimal Rate { get; set; }
        public string RateId { get; set; }
        public bool LockRate { get; set; }
        public decimal HardCost { get; set; }
        public bool LockHardCost { get; set; }
        public decimal ProductionCost { get; set; }
        public bool LockProductionCost { get; set; }
        public int QuantityAvailable { get; set; }
        public int QtyUnits { get; set; }
        public int QtyEvents { get; set; }
        public bool IsPackage { get; set; }
        public string Description { get; set; }
        public List<InventoryData> PackageComponents { get; set; }
    }
}
