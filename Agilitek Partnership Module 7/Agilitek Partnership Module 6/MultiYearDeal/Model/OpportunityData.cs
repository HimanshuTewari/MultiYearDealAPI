using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

namespace MultiYearDeal.Model
{
    public class OpportunityData
    {
        public string Id { get; set; }
        public decimal DealValue { get; set; }
        public decimal AutomaticAmount { get; set; }
        public decimal ManualAmount { get; set; }
        public string PricingMode { get; set; }
        public decimal TotalHardCost { get; set; }
        public decimal TotalProductionCost { get; set; }
        public decimal TotalRateCard { get; set; }
        public decimal PercentOfRate { get; set; }
        public decimal PercentOfRateCard { get; set; } //sunny(30-05-25)
        public decimal? BarterAmount { get; set; }//sunny(30-05-25)
        public decimal? TargetAmount { get; set; }//sunny(30-05-25)
        public decimal? CashAmount { get; set; } //sunny(30-05-25)
        public string EscalationType { get; set; }
        public decimal? EscalationValue { get; set; } // Nullable to handle `null` values
        public string SeasonName { get; set; }
        public string StartSeason { get; set; }
    }
}
