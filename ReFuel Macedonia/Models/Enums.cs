namespace ReFuel_Macedonia.Models;

public enum OrderType
{
    Standard = 0,
    Emergency = 1,
    Scheduled = 2,
    Contract = 3
}

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Assigned = 2,
    OnTheWay = 3,
    Arrived = 7,
    Delivered = 4,
    Cancelled = 5,
    Failed = 6
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    BankTransfer = 2,
    LoyaltyPoints = 3,
    ContractInvoice = 4
}

public enum DiscountType
{
    Percentage = 0,
    FixedAmount = 1,
    FreeDelivery = 2,
    PerLitre = 3
}

public enum LoyaltyTier
{
    Basic = 0,
    Silver = 1,
    Gold = 2,
    Platinum = 3
}

public enum PriceSource
{
    RegulatoryCommission = 0,
    CompanyWebsite = 1,
    UserReport = 2,
    Manual = 3,
    Estimated = 4
}

public enum ReportStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum StationSource
{
    OpenStreetMap = 0,
    Seed = 1,
    Manual = 2,
    CompanyVerified = 3
}

public enum NotificationKind
{
    Info = 0,
    PriceDrop = 1,
    OrderUpdate = 2,
    Promotion = 3,
    Contract = 4,
    Weather = 5
}

public enum AssetKind
{
    Truck = 0,
    Bus = 1,
    ConstructionMachine = 2,
    Boat = 3,
    Generator = 4,
    StorageTank = 5,
    AgriculturalMachine = 6
}

public enum ContractStatus
{
    Draft = 0,
    Active = 1,
    Suspended = 2,
    Expired = 3,
    Terminated = 4
}

public enum DeliveryFrequency
{
    Nightly = 0,
    Weekly = 1,
    Biweekly = 2,
    Monthly = 3,
    OnDemand = 4
}

public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Paid = 2,
    Overdue = 3,
    Cancelled = 4
}

public enum RoadStatus
{
    Normal = 0,
    Caution = 1,
    Restricted = 2,
    Closed = 3
}

public enum CustomerType
{
    Individual = 0,
    SmallBusiness = 1,
    Construction = 2,
    Fleet = 3,
    Enterprise = 4
}

public enum DeliveryPriority
{
    Emergency = 0,
    Scheduled = 1,
    ContractFleet = 2,
    Standard = 3
}

public enum TelemetrySource
{
    Simulator = 0,
    Device = 1,
    Manual = 2
}

public enum AnomalyKind
{
    VolumeVariance = 0,
    MeterMismatch = 1,
    DispensedOffSite = 2,
    OutsideDeliveryWindow = 3,
    ConsumptionSpike = 4,
    InventoryDrift = 5
}

public enum AnomalySeverity
{
    Information = 0,
    Attention = 1,
    Urgent = 2
}

public enum AnomalyStatus
{
    Open = 0,
    UnderReview = 1,
    Explained = 2,
    Dismissed = 3
}

public enum OfferStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Declined = 3,
    Expired = 4
}

public enum ScheduleKind
{
    OneTime = 0,
    Recurring = 1,
    OvernightFleet = 2,
    SensorTriggered = 3
}
