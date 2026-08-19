namespace ReFuel_Macedonia.Infrastructure;

/// <summary>
/// Company identity shown in the site chrome (utility bar, footer) and on
/// printed documents (invoices, delivery proofs). Bound from the "Company"
/// section of appsettings.json so nothing about the legal entity is hardcoded
/// in the views. Values below are placeholders for the demo company.
/// </summary>
public class CompanyOptions
{
    public const string SectionName = "Company";

    public string Name { get; set; } = "ReFuel Macedonia";
    public string LegalName { get; set; } = "ReFuel Macedonia ДООЕЛ Скопје";
    public string Street { get; set; } = "ул. Партизански одреди бб";
    public string PostalCode { get; set; } = "1000";
    public string City { get; set; } = "Скопје";
    public string Phone { get; set; } = "+389 2 3 120 120";
    public string Email { get; set; } = "info@refuel.mk";
    public string SupportHours { get; set; } = "24/7";

    /// <summary>ЕМБС — company registration number (Central Registry).</summary>
    public string RegistrationNumber { get; set; } = "7123456";

    /// <summary>ЕДБ — tax identification number.</summary>
    public string TaxNumber { get; set; } = "MK4057026500123";

    public string BankAccount { get; set; } = "300000000000000";

    public string Address => $"{Street}, {PostalCode} {City}";

    /// <summary>Phone number in a form that works inside a tel: link.</summary>
    public string PhoneHref => "tel:" + new string(Phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
}
