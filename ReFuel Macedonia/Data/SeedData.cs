using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Data;

public static class SeedData
{
    public record BrandSeed(string Name, string NameMk, string Slug, string Color, string? Website, int Share, bool DeliveryPartner);

    public static readonly BrandSeed[] Brands =
    {
        new("Makpetrol",        "Макпетрол",        "makpetrol",       "#C8102E", "https://www.makpetrol.com.mk", 128, true),
        new("Lukoil Macedonia", "Лукоил Македонија","lukoil",          "#E4002B", "https://www.lukoil.mk",         40, true),
        new("OKTA",             "ОКТА",             "okta",            "#0033A0", "https://www.okta.com.mk",       28, true),
        new("Dadi Oil",         "Дади Оил",         "dadi-oil",        "#F59E0B", null,                            26, true),
        new("Supertrejd",       "Супертрејд",       "supertrejd",      "#7C3AED", null,                            22, true),
        new("Crna Reka Petrol", "Црна Река Петрол", "crna-reka-petrol","#0EA5E9", null,                            18, true),
        new("Detoil",           "Детоил",           "detoil",          "#DB2777", null,                            14, false),
        new("Ompetrol",         "Омпетрол",         "ompetrol",        "#059669", null,                            13, true),
        new("Pucko Petrol",     "Пуцко Петрол",     "pucko-petrol",    "#16A34A", null,                            12, true),
        new("Javor Sped",       "Јавор Шпед",       "javor-sped",      "#65A30D", null,                            10, false),
        new("Euro Petrol",      "Еуро Петрол",      "euro-petrol",     "#EA580C", null,                             9, false),
        new("Nafta Komerc",     "Нафта Комерц",     "nafta-komerc",    "#475569", null,                             8, false),
        new("Sardich MB",       "Сардич МБ",        "sardich-mb",      "#9333EA", null,                             7, false),
        new("Vero Petrol",      "Веро Петрол",      "vero-petrol",     "#0891B2", null,                             6, false)
    };

    public record FuelSeed(string Code, string Mk, string En, string Sq, string Unit, string Color, bool Regulated, bool Deliverable, int Sort);

    public static readonly FuelSeed[] Fuels =
    {
        new("EUROSUPER_95", "Еуросупер БС-95",  "Eurosuper BS-95",  "Eurosuper BS-95",  "L",  "#16A34A", true,  true,  1),
        new("EUROSUPER_98", "Еуросупер БС-98",  "Eurosuper BS-98",  "Eurosuper BS-98",  "L",  "#0EA5E9", true,  true,  2),
        new("EURODIESEL",   "Еуродизел БС (Д-Е V)", "Eurodiesel BS (D-E V)", "Eurodizel BS (D-E V)", "L", "#F59E0B", true, true, 3),
        new("LPG",          "Течен нафтен гас (ТНГ)", "LPG (Autogas)", "Gaz i lëngshëm (LPG)", "L", "#7C3AED", false, false, 4),
        new("CNG",          "Компримиран природен гас", "CNG", "CNG", "kg", "#0891B2", false, false, 5),
        new("EL_1",         "Екстра лесно масло ЕЛ-1", "Extra light heating oil EL-1", "Vaj ngrohjeje ekstra i lehtë EL-1", "L", "#DC2626", true, true, 6),
        new("MAZUT_M1",     "Мазут М-1 НС",     "Mazut M-1 NS",     "Mazut M-1 NS",     "kg", "#4B5563", true,  false, 7)
    };

    public record PriceAnchor(DateTime Date, decimal Bs95, decimal Bs98, decimal Diesel, decimal El1, decimal Mazut);

    public static readonly PriceAnchor[] PriceAnchors =
    {
        new(new DateTime(2026, 5, 27), 87.50m, 89.50m, 92.00m, 94.00m, 51.144m),
        new(new DateTime(2026, 6,  8), 86.00m, 88.00m, 87.00m, 89.00m, 47.297m),
        new(new DateTime(2026, 8,  5), 91.00m, 93.00m, 99.50m, 98.50m, 46.419m),
        new(new DateTime(2026, 8, 11), 87.00m, 89.00m, 91.50m, 92.50m, 43.538m)
    };

    public const decimal LpgStartPrice = 47.00m;
    public const decimal CngStartPrice = 81.50m;

    public const int HistoryMonths = 42;

    public record CitySeed(string Name, string Municipality, double Lat, double Lon, int Weight, bool Motorway);

    public static readonly CitySeed[] Cities =
    {
        new("Скопје",            "Скопје",            41.9981, 21.4254, 68, true),
        new("Аеродром",          "Скопје",            41.9825, 21.4586,  8, false),
        new("Кисела Вода",       "Скопје",            41.9711, 21.4364,  8, false),
        new("Гази Баба",         "Скопје",            42.0083, 21.4692,  8, false),
        new("Чаир",              "Скопје",            42.0119, 21.4319,  6, false),
        new("Ѓорче Петров",      "Скопје",            42.0044, 21.3536,  6, false),
        new("Карпош",            "Скопје",            42.0022, 21.3894,  7, false),
        new("Бутел",             "Скопје",            42.0300, 21.4400,  5, false),
        new("Сарај",             "Скопје",            42.0000, 21.3167,  4, false),
        new("Илинден",           "Илинден",           41.9744, 21.5744,  5, true),
        new("Петровец",          "Петровец",          41.9333, 21.6167,  4, true),
        new("Битола",            "Битола",            41.0314, 21.3347, 20, false),
        new("Куманово",          "Куманово",          42.1322, 21.7144, 18, true),
        new("Прилеп",            "Прилеп",            41.3444, 21.5528, 14, false),
        new("Тетово",            "Тетово",            42.0106, 20.9714, 16, true),
        new("Велес",             "Велес",             41.7156, 21.7756, 12, true),
        new("Охрид",             "Охрид",             41.1231, 20.8016, 12, false),
        new("Гостивар",          "Гостивар",          41.7964, 20.9083, 12, true),
        new("Штип",              "Штип",              41.7458, 22.1958, 11, false),
        new("Струмица",          "Струмица",          41.4375, 22.6431, 11, false),
        new("Кавадарци",         "Кавадарци",         41.4331, 22.0119,  8, false),
        new("Кочани",            "Кочани",            41.9167, 22.4128,  7, false),
        new("Кичево",            "Кичево",            41.5142, 20.9628,  7, true),
        new("Струга",            "Струга",            41.1775, 20.6781,  7, false),
        new("Радовиш",           "Радовиш",           41.6383, 22.4644,  5, false),
        new("Гевгелија",         "Гевгелија",         41.1394, 22.5019,  7, true),
        new("Дебар",             "Дебар",             41.5247, 20.5272,  4, false),
        new("Крива Паланка",     "Крива Паланка",     42.2011, 22.3319,  4, false),
        new("Свети Николе",      "Свети Николе",      41.8664, 21.9439,  4, false),
        new("Неготино",          "Неготино",          41.4844, 22.0894,  6, true),
        new("Делчево",           "Делчево",           41.9678, 22.7742,  3, false),
        new("Виница",            "Виница",            41.8828, 22.5089,  3, false),
        new("Ресен",             "Ресен",             41.0894, 21.0119,  3, false),
        new("Берово",            "Берово",            41.7078, 22.8578,  3, false),
        new("Пробиштип",         "Пробиштип",         41.9958, 22.1789,  3, false),
        new("Македонски Брод",   "Македонски Брод",   41.5136, 21.2153,  2, false),
        new("Демир Хисар",       "Демир Хисар",       41.2200, 21.2036,  2, false),
        new("Крушево",           "Крушево",           41.3689, 21.2489,  2, false),
        new("Валандово",         "Валандово",         41.3172, 22.5606,  2, false),
        new("Богданци",          "Богданци",          41.2033, 22.5761,  2, false),
        new("Стар Дојран",       "Дојран",            41.1861, 22.7189,  2, false),
        new("Кратово",           "Кратово",           42.0781, 22.1817,  2, false),
        new("Демир Капија",      "Демир Капија",      41.4092, 22.2453,  3, true),
        new("Градско",           "Градско",           41.5747, 21.9425,  3, true),
        new("Теарце",            "Теарце",            42.0736, 21.0489,  3, false),
        new("Боговиње",          "Боговиње",          41.9231, 20.9139,  3, false),
        new("Желино",            "Желино",            41.9531, 21.0128,  2, false),
        new("Липково",           "Липково",           42.1583, 21.5833,  2, false),
        new("Студеничани",       "Студеничани",       41.9128, 21.4406,  2, false),
        new("Ново Село",         "Ново Село",         41.4103, 22.8783,  2, false),
        new("Македонска Каменица","Македонска Каменица",42.0261,22.5906, 2, false),
        new("Пехчево",           "Пехчево",           41.7622, 22.8900,  2, false),
        new("Ростуше",           "Маврово и Ростуше", 41.6142, 20.6042,  2, false),
        new("Вевчани",           "Вевчани",           41.2400, 20.5906,  1, false),
        new("Центар Жупа",       "Центар Жупа",       41.4744, 20.6000,  1, false),
        new("Могила",            "Могила",            41.1053, 21.3831,  1, false),
        new("Новаци",            "Новаци",            41.0431, 21.4581,  1, false),
        new("Росоман",           "Росоман",           41.5069, 21.9469,  1, false),
        new("Чашка",             "Чашка",             41.6497, 21.6461,  1, false),
        new("Зрновци",           "Зрновци",           41.8544, 22.4442,  1, false),
        new("Чучер Сандево",     "Чучер Сандево",     42.1128, 21.3900,  1, false)
    };

    public static readonly string[] Streets =
    {
        "бул. Партизански одреди", "ул. Македонија", "бул. Илинден", "ул. Гоце Делчев",
        "бул. Кузман Јосифовски-Питу", "ул. Јане Сандански", "бул. Александар Македонски",
        "ул. Никола Карев", "ул. Маршал Тито", "бул. Србија", "ул. Климент Охридски",
        "ул. Први Мај", "магистрален пат А1", "магистрален пат А2", "магистрален пат А3",
        "ул. Дескал Тоска", "ул. Борис Кидрич", "бул. Јане Сандански", "ул. Владо Тасевски"
    };

    public record FleetSeed(
        string Code, string Driver, string Phone, double Capacity,
        double Lat, double Lon, string City, string Truck, string Plate,
        double Rating, int Deliveries, int Years, string Photo,
        bool Boats, bool Construction);

    public static readonly FleetSeed[] Fleet =
    {
        new("RF-TRK-01", "Александар Стојановски", "+389 70 111 001", 1500, 41.9981, 21.4254, "Скопје",
            "MAN TGM 18.290 ADR", "SK-4821-AB", 4.9, 1284, 11, "/img/drivers/driver-1.svg", false, true),
        new("RF-TRK-02", "Бесник Ајдини",          "+389 70 111 002", 1200, 42.0106, 20.9714, "Тетово",
            "Mercedes Atego 1523", "TE-1177-CD", 4.8, 963, 8, "/img/drivers/driver-2.svg", false, true),
        new("RF-TRK-03", "Марија Петровска",       "+389 70 111 003", 1000, 41.0314, 21.3347, "Битола",
            "Iveco Eurocargo 140", "BT-2043-EF", 4.9, 741, 6, "/img/drivers/driver-3.svg", false, false),
        new("RF-TRK-04", "Дејан Ристов",           "+389 70 111 004", 1000, 41.7458, 22.1958, "Штип",
            "Volvo FL 240 ADR", "ST-3390-GH", 4.7, 655, 9, "/img/drivers/driver-4.svg", false, true),
        new("RF-VAN-05", "Ана Николовска",         "+389 70 111 005",  600, 41.1231, 20.8016, "Охрид",
            "Renault Master Tank", "OH-8812-IJ", 5.0, 402, 4, "/img/drivers/driver-5.svg", true, false),
        new("RF-TRK-06", "Игор Трајковски",        "+389 70 111 006", 1800, 41.4375, 22.6431, "Струмица",
            "Scania P280 ADR", "SR-5561-KL", 4.8, 1109, 13, "/img/drivers/driver-6.svg", false, true),
        new("RF-TRK-07", "Фатон Реџепи",           "+389 70 111 007", 1400, 41.7964, 20.9083, "Гостивар",
            "DAF LF 260 ADR", "GV-6634-MN", 4.9, 878, 7, "/img/drivers/driver-7.svg", false, true),
        new("RF-BRG-08", "Кристина Ангелова",      "+389 70 111 008",  900, 41.1394, 22.5019, "Гевгелија",
            "Iveco Daily Bunker", "GE-9905-OP", 4.9, 517, 5, "/img/drivers/driver-8.svg", true, false)
    };

    public record BorderSeed(string Name, string Country, double Lat, double Lon, int Entry, int Exit);

    public static readonly BorderSeed[] Borders =
    {
        new("Табановце", "Србија", 42.1908, 21.7581, 65, 30),
        new("Богородица", "Грција", 41.1250, 22.5342, 30, 30),
        new("Меџитлија", "Грција", 40.9525, 21.3086, 15, 10),
        new("Ќафасан", "Албанија", 41.1128, 20.5031, 20, 15),
        new("Свети Наум", "Албанија", 40.9139, 20.7472, 25, 20),
        new("Блаце", "Косово", 42.1642, 21.3306, 40, 25),
        new("Јажинце", "Косово", 42.1783, 21.0367, 15, 10),
        new("Деве Баир", "Бугарија", 42.2317, 22.4406, 20, 15),
        new("Ново Село", "Бугарија", 41.4183, 22.9506, 30, 20),
        new("Пелинце", "Србија", 42.2483, 21.8931, 55, 30)
    };

    public record RoadSeed(string Road, string Region, RoadStatus Status, string Description, double Lat, double Lon);

    public static readonly RoadSeed[] Roads =
    {
        new("А1 Табановце – Гевгелија", "Вардарска", RoadStatus.Normal,
            "Прооден по сув коловоз, нормална фреквенција на возила.", 41.6000, 21.9000),
        new("А2 Скопје – Тетово – Гостивар", "Полог", RoadStatus.Caution,
            "Зголемена фреквенција, се препорачува зголемено внимание кај Групчин.", 42.0000, 21.1000),
        new("А3 Штип – Кочани", "Источна", RoadStatus.Normal,
            "Прооден, без ограничувања.", 41.8300, 22.3000),
        new("Р1102 Тетово – Попова Шапка", "Полог", RoadStatus.Restricted,
            "Задолжителна зимска опрема во зимски услови, кривини со ограничена видливост.", 42.0100, 20.8300),
        new("Р1201 Гостивар – Маврово – Дебар", "Полог", RoadStatus.Caution,
            "Одрони на делови од трасата, движење со намалена брзина.", 41.6500, 20.7500),
        new("Тунел Јасен (Скопје – Македонски Брод)", "Скопски", RoadStatus.Restricted,
            "Наизменично пуштање на сообраќај во определени временски интервали.", 41.7000, 21.2500),
        new("Р2246 Крива Паланка – Деве Баир", "Североисточна", RoadStatus.Normal,
            "Прооден, зголемено внимание поради товарни возила.", 42.2100, 22.4000),
        new("Р1105 Битола – Ресен – Охрид", "Пелагонија", RoadStatus.Caution,
            "Превој Буково: чести магли во утринските часови.", 41.0800, 21.1500),
        new("Р1109 Демир Капија – Смоквица", "Вардарска", RoadStatus.Normal,
            "Прооден, нормални услови за возење.", 41.3500, 22.3500),
        new("Р2432 Струмица – Ново Село", "Југоисточна", RoadStatus.Normal,
            "Прооден, редовно одржување.", 41.4200, 22.7800)
    };

    public record BusinessSeed(
        string Company, string Tax, string Address, string City, string Contact,
        string Email, string Phone, string Industry, string Contract, string Title,
        string FuelCode, double MonthlyLitres, decimal DiscountPerLitre, int Months,
        DeliveryFrequency Frequency, double Lat, double Lon);

    // DiscountPerLitre is the customer's total discount off the pump price ("фиксен попуст по литар
    // врз основа на цената на РКЕ"). ReFuel keeps spread − discount: 4.00 − 2.00 = 2.00 ден/л on the
    // 120 000 L bus fleet, 4.00 − 1.80 = 2.20 on the truck fleet, 4.00 − 0.70 = 3.30 on the marina.
    // Never more than spread − the tier's floor.
    public static readonly BusinessSeed[] Businesses =
    {
        new("Транс Логистик Скопје ДОО", "MK4030998123456", "ул. Индустриска бр. 12", "Скопје",
            "Горан Митревски", "goran@translogistik.mk", "+389 2 3099 100", "Логистика и транспорт",
            "RF-C-2026-001", "Ноќно полнење на возен парк - 42 камиони", "EURODIESEL",
            96000, 1.80m, 12, DeliveryFrequency.Nightly, 41.9744, 21.5744),

        new("Градба Инженеринг АД", "MK4057997654321", "бул. Партизански одреди бр. 88", "Скопје",
            "Билјана Ефремова", "b.efremova@gradba.mk", "+389 2 3112 480", "Градежништво",
            "RF-C-2026-002", "Снабдување на градилишта и тешка механизација", "EURODIESEL",
            54000, 1.30m, 12, DeliveryFrequency.Weekly, 41.9333, 21.6167),

        new("Охрид Марина Турс ДООЕЛ", "MK4020996112233", "Кеј Македонија бр. 3", "Охрид",
            "Никола Спасески", "info@ohridmarina.mk", "+389 46 260 700", "Наутика и туризам",
            "RF-C-2026-003", "Полнење на бродови и чамци во марина Охрид", "EUROSUPER_95",
            18000, 0.70m, 12, DeliveryFrequency.Weekly, 41.1231, 20.8016),

        new("Агро Пелагонија ДОО", "MK4002996778899", "ул. Новачки пат бб", "Битола",
            "Стојан Крстевски", "kontakt@agropelagonija.mk", "+389 47 220 330", "Земјоделство",
            "RF-C-2026-004", "Сезонско снабдување на земјоделска механизација", "EURODIESEL",
            32000, 0.90m, 12, DeliveryFrequency.Biweekly, 41.0314, 21.3347),

        new("Скопје Транспорт АД", "MK4043994556677", "бул. Никола Карев бб", "Скопје",
            "Ирена Јовановска", "fleet@skopjetransport.mk", "+389 2 2460 190", "Јавен превоз",
            "RF-C-2026-005", "Ноќно полнење на автобуски возен парк", "EURODIESEL",
            120000, 2.00m, 12, DeliveryFrequency.Nightly, 42.0083, 21.4692)
    };

    public record AssetSeed(string Contract, string Name, AssetKind Kind, string Identifier, double Capacity, double Lat, double Lon);

    public static readonly AssetSeed[] Assets =
    {
        new("RF-C-2026-001", "Влекач Volvo FH 460 #1", AssetKind.Truck, "SK-1201-TL", 600, 41.9744, 21.5744),
        new("RF-C-2026-001", "Влекач Volvo FH 460 #2", AssetKind.Truck, "SK-1202-TL", 600, 41.9744, 21.5744),
        new("RF-C-2026-001", "Влекач Scania R450 #3", AssetKind.Truck, "SK-1203-TL", 700, 41.9744, 21.5744),
        new("RF-C-2026-001", "Влекач MAN TGX #4", AssetKind.Truck, "SK-1204-TL", 650, 41.9744, 21.5744),
        new("RF-C-2026-001", "Резервоар на депо", AssetKind.StorageTank, "TANK-SK-01", 15000, 41.9744, 21.5744),

        new("RF-C-2026-002", "Багер Caterpillar 320", AssetKind.ConstructionMachine, "GR-EX-001", 400, 41.9333, 21.6167),
        new("RF-C-2026-002", "Ровокопач JCB 3CX", AssetKind.ConstructionMachine, "GR-EX-002", 160, 41.9333, 21.6167),
        new("RF-C-2026-002", "Кран Liebherr LTM 1050", AssetKind.ConstructionMachine, "GR-CR-003", 550, 41.9333, 21.6167),
        new("RF-C-2026-002", "Дизел агрегат 250 kVA", AssetKind.Generator, "GR-GN-004", 400, 41.9333, 21.6167),

        new("RF-C-2026-003", "Брод „Билјана“", AssetKind.Boat, "OH-B-101", 900, 41.1231, 20.8016),
        new("RF-C-2026-003", "Брод „Македонија“", AssetKind.Boat, "OH-B-102", 1200, 41.1231, 20.8016),
        new("RF-C-2026-003", "Глисер „Јасмин“", AssetKind.Boat, "OH-B-103", 300, 41.1231, 20.8016),

        new("RF-C-2026-004", "Трактор John Deere 6120M", AssetKind.AgriculturalMachine, "BT-AG-201", 250, 41.0314, 21.3347),
        new("RF-C-2026-004", "Комбајн Claas Tucano", AssetKind.AgriculturalMachine, "BT-AG-202", 600, 41.0314, 21.3347),

        new("RF-C-2026-005", "Автобус MAN Lion's City #17", AssetKind.Bus, "SK-9017-BS", 300, 42.0083, 21.4692),
        new("RF-C-2026-005", "Автобус MAN Lion's City #18", AssetKind.Bus, "SK-9018-BS", 300, 42.0083, 21.4692),
        new("RF-C-2026-005", "Автобус Otokar Kent #21", AssetKind.Bus, "SK-9021-BS", 280, 42.0083, 21.4692)
    };

    // ------------------------------------------------------------------ pricing model v2
    // "Pump price, delivered." ReFuel buys at the regulator's wholesale price (pump − spread) and
    // quotes every customer at or below the pump price. The spread is the fuel margin; the tiers
    // decide how much of it is handed back as a discount. Delivery is priced separately and
    // costed as a share of a route, not as a taxi ride per order.

    /// <summary>ден/л below the pump price at which ReFuel buys. Placeholder — verify against the current ERC decision.</summary>
    public const decimal ProcurementSpreadPerLitre = 4.00m;

    public const int AverageStopsPerRoute = 6;
    public const double InterStopKm = 3;
    public const int ServiceMinutesPerStop = 10;
    public const double ContractMinDropLitres = 300;

    public record TierSeed(
        string Code, string Mk, string En, string Sq, CustomerType Type,
        decimal Margin, decimal MinMargin, decimal MinFee, decimal PerKm, decimal FreeThreshold, bool FreeScheduledOnly,
        int ScheduledDiscount, decimal Emergency,
        double MinLitres, double MaxLitres, bool Negotiated, int Sort,
        string MarketingMk, string MarketingEn, string MarketingSq);

    // Margin = ReFuel's margin above wholesale. Customer pays pump − (spread − margin) − bands.
    //   Individual      4.00 → pays the pump price       (+ delivery)
    //   Small business  3.50 → pump − 0.50
    //   Construction    3.00 → pump − 1.00
    //   Fleet           2.50 → pump − 1.50
    //   Enterprise      2.00 → pump − 2.00 (negotiated)
    public static readonly TierSeed[] Tiers =
    {
        new("INDIVIDUAL", "Физичко лице", "Individual", "Person fizik", CustomerType.Individual,
            4.00m, 3.00m, 150m, 18m, 6200m, true, 40, 350m, 10, 120, false, 1,
            "Иста цена како на пумпа — доставено.", "Pump price. Delivered.", "Çmimi i pompës — i dorëzuar."),

        new("SMALL_BUSINESS", "Мал бизнис", "Small business", "Biznes i vogël", CustomerType.SmallBusiness,
            3.50m, 2.50m, 140m, 16m, 12000m, true, 40, 300m, 40, 1000, false, 2,
            "Помалку застанувања, повеќе работа.", "Fewer stops, more work done.", "Më pak ndalesa, më shumë punë."),

        new("CONSTRUCTION", "Градежништво", "Construction", "Ndërtimtari", CustomerType.Construction,
            3.00m, 2.00m, 250m, 15m, 0m, false, 25, 400m, 200, 6000, false, 3,
            "Механизацијата останува на градилиштето.", "Your machines stay on site.", "Makineritë mbeten në kantier."),

        new("FLEET", "Возен парк", "Fleet", "Flotë", CustomerType.Fleet,
            2.50m, 1.50m, 0m, 12m, 0m, false, 0, 400m, 300, 14000, false, 4,
            "Ноќно полнење, подготвен возен парк наутро.", "Refuelled overnight, rolling at dawn.", "Furnizim gjatë natës, gati në mëngjes."),

        new("ENTERPRISE", "Договорна цена", "Negotiated contract", "Kontratë e negociuar", CustomerType.Enterprise,
            2.00m, 1.00m, 0m, 10m, 0m, false, 0, 400m, 500, 30000, true, 5,
            "Цена договорена по мерка на вашиот обем.", "A price negotiated around your volume.", "Çmim i negociuar sipas vëllimit tuaj.")
    };

    public record BandSeed(
        string? TierCode, double From, double? To, decimal Discount,
        string Mk, string En, string Sq);

    // Volume discounts are a slice of the tier's margin, never a multiple of it. A band with a tier
    // code applies to that tier only; the tier's floor (MinMargin) is what it can never cross.
    public static readonly BandSeed[] Bands =
    {
        // Small business — margin 3.50, floor 2.50 → up to 1.00 of room
        new("SMALL_BUSINESS", 100, 500, 0.10m,
            "Количински попуст 100–500 л", "Volume discount 100–500 L", "Zbritje sasie 100–500 L"),
        new("SMALL_BUSINESS", 500, 2000, 0.20m,
            "Количински попуст 500–2 000 л", "Volume discount 500–2,000 L", "Zbritje sasie 500–2 000 L"),
        new("SMALL_BUSINESS", 2000, null, 0.30m,
            "Количински попуст над 2 000 л", "Volume discount above 2,000 L", "Zbritje sasie mbi 2 000 L"),

        // Construction — margin 3.00, floor 2.00
        new("CONSTRUCTION", 500, 2000, 0.10m,
            "Количински попуст 500–2 000 л", "Volume discount 500–2,000 L", "Zbritje sasie 500–2 000 L"),
        new("CONSTRUCTION", 2000, 10000, 0.20m,
            "Количински попуст 2 000–10 000 л", "Volume discount 2,000–10,000 L", "Zbritje sasie 2 000–10 000 L"),
        new("CONSTRUCTION", 10000, null, 0.30m,
            "Количински попуст над 10 000 л", "Volume discount above 10,000 L", "Zbritje sasie mbi 10 000 L"),

        // Fleet — margin 2.50, floor 1.50
        new("FLEET", 2000, 10000, 0.10m,
            "Попуст за возен парк 2 000–10 000 л", "Fleet volume discount 2,000–10,000 L", "Zbritje flote 2 000–10 000 L"),
        new("FLEET", 10000, 50000, 0.20m,
            "Попуст за возен парк 10 000–50 000 л", "Fleet volume discount 10,000–50,000 L", "Zbritje flote 10 000–50 000 L"),
        new("FLEET", 50000, null, 0.30m,
            "Попуст за возен парк над 50 000 л", "Fleet volume discount above 50,000 L", "Zbritje flote mbi 50 000 L"),

        // Enterprise — margin 2.00, floor 1.00; most of the discount is negotiated in the contract itself
        new("ENTERPRISE", 10000, 50000, 0.10m,
            "Договорен попуст 10 000–50 000 л", "Negotiated discount 10,000–50,000 L", "Zbritje e negociuar 10 000–50 000 L"),
        new("ENTERPRISE", 50000, null, 0.20m,
            "Договорен попуст над 50 000 л", "Negotiated discount above 50,000 L", "Zbritje e negociuar mbi 50 000 L")
    };

    public record ZoneSeed(
        string Name, string Cities, double Lat, double Lon, double Radius,
        decimal BaseFee, decimal PerKm, double MinLitres, double Speed, string Color, int Sort);

    public static readonly ZoneSeed[] Zones =
    {
        new("Скопски регион", "Скопје, Илинден, Петровец, Сопиште, Арачиново",
            41.9981, 21.4254, 28, 150m, 16m, 10, 42, "#FF6A00", 1),
        new("Полог", "Тетово, Гостивар, Боговиње, Желино, Врапчиште",
            41.9050, 20.9400, 32, 170m, 18m, 10, 46, "#FF8A1F", 2),
        new("Пелагонија", "Битола, Прилеп, Ресен, Демир Хисар, Крушево",
            41.1200, 21.3300, 45, 190m, 18m, 15, 52, "#F0590A", 3),
        new("Југозапад", "Охрид, Струга, Дебар, Кичево, Македонски Брод",
            41.1231, 20.8016, 42, 200m, 19m, 15, 44, "#FFB03A", 4),
        new("Исток", "Штип, Кочани, Виница, Пробиштип, Свети Николе, Делчево",
            41.7458, 22.1958, 45, 190m, 18m, 15, 50, "#C6CDD8", 5),
        new("Југоисток", "Струмица, Гевгелија, Радовиш, Валандово, Богданци",
            41.4375, 22.6431, 40, 195m, 18m, 15, 48, "#8A93A3", 6),
        new("Североисток", "Куманово, Крива Паланка, Кратово, Липково",
            42.1322, 21.7144, 38, 180m, 17m, 10, 50, "#4A5262", 7),
        new("Вардар", "Велес, Кавадарци, Неготино, Гевгелија, Демир Капија",
            41.7150, 21.7750, 40, 185m, 17m, 10, 52, "#FFC24A", 8)
    };

    public record CompartmentSeed(string TruckCode, int Number, double Capacity, string FuelCode, double Fill);

    public static readonly CompartmentSeed[] Compartments =
    {
        new("RF-TRK-01", 1, 800, "EURODIESEL", 0.82),
        new("RF-TRK-01", 2, 500, "EUROSUPER_95", 0.64),
        new("RF-TRK-01", 3, 200, "EUROSUPER_98", 0.40),

        new("RF-TRK-02", 1, 700, "EURODIESEL", 0.75),
        new("RF-TRK-02", 2, 500, "EUROSUPER_95", 0.58),

        new("RF-TRK-03", 1, 600, "EURODIESEL", 0.66),
        new("RF-TRK-03", 2, 400, "EUROSUPER_95", 0.52),

        new("RF-TRK-04", 1, 700, "EURODIESEL", 0.88),
        new("RF-TRK-04", 2, 300, "EUROSUPER_95", 0.35),

        new("RF-VAN-05", 1, 400, "EUROSUPER_95", 0.70),
        new("RF-VAN-05", 2, 200, "EURODIESEL", 0.55),

        new("RF-TRK-06", 1, 1000, "EURODIESEL", 0.91),
        new("RF-TRK-06", 2, 500, "EUROSUPER_95", 0.47),
        new("RF-TRK-06", 3, 300, "EL_1", 0.30),

        new("RF-TRK-07", 1, 900, "EURODIESEL", 0.78),
        new("RF-TRK-07", 2, 500, "EUROSUPER_95", 0.61),

        new("RF-BRG-08", 1, 600, "EUROSUPER_95", 0.72),
        new("RF-BRG-08", 2, 300, "EURODIESEL", 0.44)
    };

    public record SiteSeed(
        string Company, string Name, string Address, string City,
        double Lat, double Lon, double WeeklyLitres,
        string Contact, string Phone, int AccessFrom, int AccessTo, string Note);

    public static readonly SiteSeed[] Sites =
    {
        new("Градба Инженеринг АД", "Градилиште Аеродром - станбен комплекс Б3",
            "ул. Јане Сандански бб", "Скопје", 41.9840, 21.4610, 3800,
            "Дарко Стефановски", "+389 71 220 411", 6, 19,
            "Влез од јужната порта, потребна е најава ден однапред."),

        new("Градба Инженеринг АД", "Обиколница Скопје - делница 4",
            "автопат А1, км 12", "Скопје", 42.0290, 21.5480, 5200,
            "Ѓорѓи Најдовски", "+389 71 220 412", 5, 20,
            "Ноќно полнење дозволено, механизацијата останува на трасата."),

        new("Градба Инженеринг АД", "Хидроцентрала Црн Дрим - подготвителни работи",
            "регионален пат Р1201", "Дебар", 41.5250, 20.5280, 2600,
            "Емил Костоски", "+389 71 220 413", 7, 17,
            "Тежок пристап во зимски услови, потребен е погон на сите тркала.")
    };

    public record ScheduleSeed(
        string Company, string Title, ScheduleKind Kind, DeliveryFrequency Frequency,
        string Days, int WindowStart, int WindowEnd, double Litres, string FuelCode,
        string Address, string City, double Lat, double Lon, double? TriggerPercent);

    public static readonly ScheduleSeed[] Schedules =
    {
        new("Транс Логистик Скопје ДОО", "Ноќно полнење на влекачи - депо Индустриска",
            ScheduleKind.OvernightFleet, DeliveryFrequency.Nightly, "1,2,3,4,5", 22, 2, 2400, "EURODIESEL",
            "ул. Индустриска бр. 12", "Скопје", 41.9744, 21.5744, null),

        new("Градба Инженеринг АД", "Понеделник / среда / петок - градилишта",
            ScheduleKind.Recurring, DeliveryFrequency.Weekly, "1,3,5", 6, 10, 500, "EURODIESEL",
            "ул. Јане Сандански бб", "Скопје", 41.9840, 21.4610, null),

        new("Скопје Транспорт АД", "Ноќно полнење на автобуски парк",
            ScheduleKind.OvernightFleet, DeliveryFrequency.Nightly, "0,1,2,3,4,5,6", 23, 4, 3600, "EURODIESEL",
            "бул. Никола Карев бб", "Скопје", 42.0083, 21.4692, null),

        new("Агро Пелагонија ДОО", "Сензорско полнење на резервоар - под 25 %",
            ScheduleKind.SensorTriggered, DeliveryFrequency.OnDemand, "1,4", 7, 15, 1800, "EURODIESEL",
            "ул. Новачки пат бб", "Битола", 41.0314, 21.3347, 25),

        new("Охрид Марина Турс ДООЕЛ", "Неделно полнење на пловни објекти",
            ScheduleKind.Recurring, DeliveryFrequency.Weekly, "4", 8, 12, 900, "EUROSUPER_95",
            "Кеј Македонија бр. 3", "Охрид", 41.1231, 20.8016, null)
    };
}
