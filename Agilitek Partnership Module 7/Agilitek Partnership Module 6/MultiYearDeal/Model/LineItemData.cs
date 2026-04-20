using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiYearDeal.Model
{
    public class LineItemData
    {
        public ProductData Product2 { get; set; }
        public List<RateData> rates { get; set; }
        public List<OpportunityLineItemData> items { get; set; }
        public bool IsPackage { get; set; }
        public bool IsPackageComponent { get; set; }
        public List<LineItemData> PackageComponents { get; set; }
    }

    public class ProductData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Division { get; set; }
        public string ProductFamily { get; set; }
        public string ProductSubFamily { get; set; }
        public bool IsPassthroughCost { get; set; }
        public bool IsPackage { get; set; }
    }

    public class RateData
    {
        public string RateType { get; set; }
        public decimal Rate { get; set; }
        public decimal HardCost { get; set; }
        public decimal ProductionCost { get; set; }
        public bool LockHardCost { get; set; }
        public bool LockRate { get; set; }
        public bool LockProductionCost { get; set; }
        public string Season { get; set; }
        public string SeasonName { get; set; }
        public bool UnlimitedQuantity { get; set; }
        public string Product { get; set; }
    }

    public class OpportunityLineItemData
    {
        public string Id { get; set; }
        public string Opportunity { get; set; }
        public decimal TotalValue { get; set; }
        public int QtyEvents { get; set; }
        public int QtyUnits { get; set; }
        public decimal HardCost { get; set; }
        public decimal TotalHardCost { get; set; }
        public decimal ProductionCost { get; set; }
        public decimal TotalProductionCost { get; set; }
        public bool LockProductionCost { get; set; }
        public string Product2 { get; set; }
        public decimal Rate { get; set; }
        public int QuantityAvailable { get; set; }
        public string RateType { get; set; }
        public bool IsActive { get; set; }
        public bool LockHardCost { get; set; }
        public bool LockRate { get; set; }
        public string RateName { get; set; }
        public string Division { get; set; }
        public decimal Unscheduled { get; set; }
        public decimal QuantityScheduled { get; set; }
        public bool IsManualPriceOverride { get; set; }
        public string LegalDefinitionProduct { get; set; }
        public string LegalDefinitionInventoryBySeason { get; set; }
        public string LegalDefinition { get; set; }
        public bool NotAvailable { get; set; }
        public bool OverwriteLegalDefinition { get; set; }
        public string Description { get; set; }
        public int QuantityPitched { get; set; }
        public int QuantitySold { get; set; }
        public int QuantityTotal { get; set; }
        public string PackageLineItemId { get; set; }
    }
}
