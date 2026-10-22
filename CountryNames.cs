namespace Ets2Gps;

/// <summary>English names for the ISO alpha-2 codes the map uses for border flags.</summary>
public static class CountryNames
{
    static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ad"] = "Andorra", ["ae"] = "United Arab Emirates", ["al"] = "Albania", ["am"] = "Armenia", ["at"] = "Austria", ["ax"] = "Åland Islands",
        ["az"] = "Azerbaijan", ["ba"] = "Bosnia and Herzegovina", ["be"] = "Belgium", ["bg"] = "Bulgaria", ["bh"] = "Bahrain", ["by"] = "Belarus",
        ["ch"] = "Switzerland", ["cy"] = "Cyprus", ["cz"] = "Czechia", ["de"] = "Germany", ["dk"] = "Denmark", ["dz"] = "Algeria", ["ee"] = "Estonia",
        ["eg"] = "Egypt", ["es"] = "Spain", ["fi"] = "Finland", ["fo"] = "Faroe Islands", ["fr"] = "France", ["gb"] = "United Kingdom", ["ge"] = "Georgia",
        ["gg"] = "Guernsey", ["gi"] = "Gibraltar", ["gl"] = "Greenland", ["gr"] = "Greece", ["hr"] = "Croatia", ["hu"] = "Hungary", ["ie"] = "Ireland",
        ["il"] = "Israel", ["im"] = "Isle of Man", ["iq"] = "Iraq", ["ir"] = "Iran", ["is"] = "Iceland", ["it"] = "Italy", ["je"] = "Jersey",
        ["jo"] = "Jordan", ["kw"] = "Kuwait", ["kz"] = "Kazakhstan", ["lb"] = "Lebanon", ["li"] = "Liechtenstein", ["lt"] = "Lithuania",
        ["lu"] = "Luxembourg", ["lv"] = "Latvia", ["ly"] = "Libya", ["ma"] = "Morocco", ["mc"] = "Monaco", ["md"] = "Moldova", ["me"] = "Montenegro",
        ["mk"] = "North Macedonia", ["mr"] = "Mauritania", ["mt"] = "Malta", ["nl"] = "Netherlands", ["no"] = "Norway", ["om"] = "Oman", ["pl"] = "Poland",
        ["ps"] = "Palestine", ["pt"] = "Portugal", ["qa"] = "Qatar", ["ro"] = "Romania", ["rs"] = "Serbia", ["ru"] = "Russia", ["sa"] = "Saudi Arabia",
        ["se"] = "Sweden", ["si"] = "Slovenia", ["sj"] = "Svalbard", ["sk"] = "Slovakia", ["sy"] = "Syria", ["tm"] = "Turkmenistan", ["tn"] = "Tunisia",
        ["tr"] = "Türkiye", ["ua"] = "Ukraine", ["uz"] = "Uzbekistan", ["xk"] = "Kosovo", ["ye"] = "Yemen",
    };

    public static string Of(string cc) => Names.TryGetValue(cc, out var n) ? n : cc.ToUpperInvariant();
}
