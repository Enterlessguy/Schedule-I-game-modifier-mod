using System;
using System.Collections.Generic;

namespace ScheduleIControlCenter
{
    internal sealed class SaveDescriptor
    {
        public string OwnerId { get; set; }
        public string SlotName { get; set; }
        public string FolderPath { get; set; }
        public string GameVersion { get; set; }
        public DateTime LastPlayed { get; set; }
        public DateTime LastWriteTime { get; set; }
        public bool ConsoleEnabled { get; set; }
        public bool IsLastLoaded { get; set; }

        public string Key
        {
            get { return OwnerId + "\\" + SlotName; }
        }

        public override string ToString()
        {
            string owner = OwnerId ?? "unknown";
            if (owner.Length > 10)
                owner = owner.Substring(0, 6) + "..." + owner.Substring(owner.Length - 4);

            return string.Format(
                "{0}{1}  •  {2}",
                IsLastLoaded ? "ACTIVE  •  " : string.Empty,
                SlotName,
                owner);
        }
    }

    internal sealed class PropertyState
    {
        public string Code { get; set; }
        public string RelativeFile { get; set; }
        public bool IsOwned { get; set; }

        public override string ToString()
        {
            return string.Format("{0}  [{1}]", Code, IsOwned ? "owned" : "not owned");
        }
    }

    internal sealed class PriceChange
    {
        public string ProductId { get; set; }
        public int BaselinePrice { get; set; }
        public int CurrentPrice { get; set; }
        public int NewPrice { get; set; }
    }

    internal sealed class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string AppliedMode { get; set; }
        public bool ReloadRequired { get; set; }
        public string BackupPath { get; set; }
        public List<PriceChange> PriceChanges { get; set; }
        public string Code { get; set; }
        public long Revision { get; set; }
        public Dictionary<string, object> Data { get; set; }
        public string RawResponse { get; set; }
        public Exception Error { get; set; }

        public OperationResult()
        {
            PriceChanges = new List<PriceChange>();
            Data = new Dictionary<string, object>();
        }

        public static OperationResult Ok(string message)
        {
            return new OperationResult { Success = true, Message = message };
        }

        public static OperationResult Fail(string message)
        {
            return new OperationResult { Success = false, Message = message };
        }

        public static OperationResult Fail(string message, Exception error)
        {
            return new OperationResult { Success = false, Message = message, Error = error };
        }
    }

    internal sealed class MarketProductRow
    {
        public string ProductId { get; set; }
        public string Name { get; set; }
        public string DrugType { get; set; }
        public decimal SellPrice { get; set; }
        public decimal VanillaMarketValue { get; set; }
        public decimal EffectiveMarketValue { get; set; }
        public decimal PlannedMarketValue { get; set; }
        public decimal Factor { get; set; }
        public decimal ValueProposition { get; set; }
        public bool Aligned { get; set; }
    }

    internal sealed class SellPriceProductRow
    {
        public string ProductId { get; set; }
        public string Name { get; set; }
        public string DrugType { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal PlannedPrice { get; set; }
        public decimal FairMarketValue { get; set; }
        public decimal ValueProposition { get; set; }
        public bool Aligned { get; set; }
    }

    internal sealed class CustomerAllowanceRow
    {
        public string CustomerId { get; set; }
        public string Name { get; set; }
        public bool Unlocked { get; set; }
        public decimal OriginalMinWeeklySpend { get; set; }
        public decimal OriginalMaxWeeklySpend { get; set; }
        public decimal CurrentMinWeeklySpend { get; set; }
        public decimal CurrentMaxWeeklySpend { get; set; }
        public decimal PlannedMinWeeklySpend { get; set; }
        public decimal PlannedMaxWeeklySpend { get; set; }
        public decimal AdjustedWeeklySpend { get; set; }
        public decimal OrdersPerWeek { get; set; }
        public decimal AllowancePerOrder { get; set; }
        public decimal HardOfferLimit { get; set; }
        public bool Overridden { get; set; }
    }
}
