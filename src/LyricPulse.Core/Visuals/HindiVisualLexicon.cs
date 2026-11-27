namespace LyricPulse.Core.Visuals;

/// <summary>
/// Curated offline Hindi (Devanagari, pre-normalized without nukta/chandrabindu) and Romanized Hinglish
/// lyric vocabulary mapped directly to TTF-verified <c>StarterLexicon.tsv</c> concept IDs so Hindi songs
/// receive the same animated vector icons, color emojis, and track mood visuals as English songs.
/// </summary>
internal static class HindiVisualLexicon
{
    private static readonly (string ConceptId, string[] Aliases)[] ConceptAliases =
    [
        // --- Love, Heart, Romance & Soul ---
        ("heart", [
            "dil", "dilwala", "dilse", "pyar", "pyaar", "ishq", "ishk", "mohabbat", "muhabbat",
            "sanam", "mehboob", "mehbooba", "dilbar", "jaan", "jaana", "jaaneman", "piya", "saajan",
            "sajan", "chahat", "prem", "aashiq", "aashiqui", "ashiq", "ashiqui", "shiddat",
            "wafa", "wafaa", "chahun", "chaahun", "kurbaan", "qurbaan", "fida", "fidaa",
            "दिल", "प्यार", "इश्क", "मोहब्बत", "मुहब्बत", "सनम", "महबूब", "महबूबा", "दिलबर",
            "जान", "जाना", "पिया", "साजन", "चाहत", "प्रेम", "आशिक", "आशिकी", "शिद्दत",
            "वफा", "चाहूं", "कुर्बान", "फिदा"
        ]),
        ("heart_sparkle", [
            "deewana", "diwana", "deewani", "diwani", "deewangi", "diwangi", "dildar", "dildaar",
            "hasina", "haseena", "shauq", "khwahish", "khwaish", "tamanna",
            "hasrat", "hasratein", "arzoo", "aarzoo", "rubaru", "roobaroo", "nazdeek", "kareeb",
            "दीवाना", "दीवानी", "दीवानगी", "दिलदार", "हसीना", "ख्वाहिश", "तमन्ना",
            "हसरत", "हसरतें", "आरजू", "रूबरू", "नजदीक", "करीब"
        ]),
        ("heartbeat_pulse", [
            "dhadkan", "dhadkane", "dhadkanein", "dhadke", "dhadakta", "dhadak", "seena", "seene",
            "mehsoos", "mahsoos", "spandan",
            "धड़कन", "धड़कनें", "धड़के", "धड़क", "सीना", "सीने",
            "महसूस", "स्पंदन"
        ]),
        ("broken_heart", [
            "dard", "zakhm", "jakhm", "zakhmi", "jakhmi", "toota", "toote", "toot", "tuta", "bikhra", "bikhre",
            "bewafa", "bewafai", "judai", "judaai", "juda", "tadap", "tadapta", "tadapna", "ghayal",
            "dhokha", "dhoka", "ruswa", "bikhar", "chot", "marham",
            "chheen", "chheena", "chheenega", "adhoora", "adhoori", "kasoor", "qasoor", "fariyaad",
            "दर्द", "जख्म", "जख्मी", "टूटा", "टूटे", "बिखरा", "बिखरे", "बेवफा", "बेवफाई",
            "जुदाई", "जुदा", "तड़प", "घायल", "धोखा", "चोट", "मरहम",
            "छीन", "छीनेगा", "अधूरा", "अधूरी", "कसूर", "फरियाद"
        ]),
        ("kiss", [
            "honth", "hoth", "hothon", "lab", "labon", "chumma", "baahon", "bahon", "baho", "aagosh",
            "लिप", "होंठ", "होंठों", "लब", "लबों", "बांहों", "आगोश"
        ]),
        ("cry", [
            "aansu", "ansu", "aansoo", "ansoo", "aasu", "ashq", "ashk", "rona", "roye", "rota", "roti",
            "gham", "gam", "udaas", "udas", "udaasi", "tanha", "tannha", "tanhai", "tanhaai",
            "akela", "akele", "akeli", "dukh", "dukhi", "soona", "suna", "viraan", "veeran", "mayoos",
            "आंसू", "अश्क", "रोना", "रोये", "रोता", "रोती", "गम", "उदास", "उदासी",
            "तन्हा", "तन्हाई", "अकेला", "अकेले", "दुख", "दुखी", "सूना", "वीरान"
        ]),
        ("smile", [
            "khushi", "khush", "khushiyan", "khushian", "muskaan", "muskan", "muskurahat", "muskura",
            "sukhi", "khilkhila", "pyara", "pyari", "meetha", "meethi",
            "खुशी", "खुश", "खुशियां", "मुस्कान", "मुस्कुराहट", "मुस्कुरा", "प्यारा", "प्यारी", "मीठा"
        ]),
        ("laugh", [
            "hansi", "hasi", "hasna", "hansta", "masti", "mast", "maza", "majaa", "sharaarat", "shararat",
            "हंसी", "हंसना", "मस्ती", "मस्त", "मजा", "शरारत"
        ]),
        ("anger", [
            "gussa", "krodh", "nafrat", "dushman", "dushmani", "cheen", "cheekh", "chillana", "shor",
            "गुस्सा", "क्रोध", "नफरत", "दुश्मन", "चीख", "शोर"
        ]),
        ("fear", [
            "darr", "dar", "darta", "khauf", "khatra", "ghabra", "kaamp", "bechain", "bechaini",
            "डर", "डरता", "खौफ", "खतरा", "बेचैन", "बेचैनी"
        ]),
        ("hope", [
            "ummeed", "umeed", "aasha", "asha", "vishwas", "bharosa", "yakeen", "yaqeen", "armaan", "arman",
            "dua", "duaa", "duayein", "mannat", "prarthana", "waada", "wada", "kasam", "qasam",
            "उम्मीद", "आशा", "विश्वास", "भरोसा", "यकीन", "अरमान", "दुआ", "दुआएं", "मन्नत", "वादा", "कसम"
        ]),
        ("peace", [
            "sukoon", "sukun", "shanti", "chain", "aaram", "aram", "khamoshi", "khamosh", "chup", "sannata",
            "fikar", "fikr", "befikar", "befikri", "tasalli",
            "सुकून", "शांति", "चैन", "आराम", "खामोशी", "खामोश", "चुप", "सन्नाटा",
            "फिकर", "फिक्र", "बेफिकर", "बेफिक्री", "तसल्ली"
        ]),
        ("soul", [
            "rooh", "ruh", "aatma", "atma", "mann", "man", "yaad", "yaadein", "yadein", "yaadon",
            "ehsaas", "ehsas", "jazbaat", "jazbat", "khayal", "khayaal", "soch",
            "रूह", "आत्मा", "मन", "याद", "यादें", "यादों", "एहसास", "जज्बात", "ख्याल", "सोच"
        ]),
        ("life", [
            "zindagi", "jindagi", "zindgi", "jeevan", "jivan", "jeena", "jina", "zinda", "jinda",
            "saans", "sans", "saansein", "sansein", "saanson",
            "जिंदगी", "जीवन", "जीना", "जिंदा", "सांस", "सांसें", "सांसों"
        ]),
        ("angel", [
            "jannat", "firdaus", "swarg", "pari", "farishta", "khuda", "rab", "rabba", "maula",
            "bhagwan", "ishwar", "allah", "ibadat", "rehmat", "barkat", "noorani",
            "sajda", "sajde", "karam", "meherbaan",
            "जन्नत", "स्वर्ग", "परी", "फरिश्ता", "खुदा", "रब", "रब्बा", "मौला", "भगवान", "ईश्वर", "इबादत",
            "सजदा", "सजदे", "नूरानी", "करम", "मेहरबान"
        ]),
        ("devil", [
            "shaitan", "shaitaan", "raakshas", "paap", "gunah", "gunaah", "zaalim", "zalim", "sitam",
            "शैतान", "पाप", "गुनाह", "जालिम", "सितम"
        ]),
        ("ghost", [
            "saya", "saaya", "parchhai", "parchai", "bhoot", "khali", "khaali", "ojhal", "gayab",
            "साया", "परछाई", "भूत", "खाली", "ओझल", "गायब"
        ]),
        ("pride", [
            "jeet", "jit", "jeetna", "vijay", "garv", "shaan", "shan", "himmat", "honsla", "hausla",
            "bahadur", "veer", "taqat", "takat", "shakti", "junoon", "josh",
            "जीत", "विजय", "गर्व", "शान", "हिम्मत", "हौसला", "बहादुर", "वीर", "ताकत", "शक्ति", "जुनून", "जोश"
        ]),
        ("freedom", [
            "aazaad", "azad", "azaad", "aazadi", "azadi", "azaadi", "mukti", "behad", "bepanaah", "bepanah",
            "आजाद", "आजादी", "मुक्ति", "बेहद", "बेपनाह"
        ]),

        // --- Night, Stars, Cosmos, Sky & Weather ---
        ("star", [
            "taara", "tara", "taare", "tare", "sitara", "sitare", "sitaare", "taaron", "kehkasha",
            "तारा", "तारे", "सितारा", "सितारे", "तारों", "कहकशां"
        ]),
        ("night", [
            "raat", "rat", "raatein", "ratein", "raaton", "chaand", "chand", "chaandni", "chandni",
            "chandrama", "mahtab", "shab", "shaam", "sham", "saanjh", "sanjh", "nishaa", "nisha",
            "रात", "रातें", "रातों", "चांद", "चांदनी", "शब", "शाम", "सांझ", "निशा"
        ]),
        ("moon_eclipse", [
            "andhera", "andhere", "andheri", "kaala", "kaali", "kala", "kali", "siyah", "zulmat", "grahan",
            "अंधेरा", "अंधेरे", "अंधेरी", "काला", "काली", "स्याह", "ग्रहण"
        ]),
        ("sun", [
            "suraj", "sooraj", "surya", "subah", "subha", "savera", "sawera", "bhor", "dhoop", "dhup",
            "garmi", "sunehra", "sunehri", "din", "dopahar",
            "सूरज", "सूर्य", "सुबह", "सवेरा", "भोर", "धूप", "गर्मी", "सुनहरा", "सुनहरी", "दिन"
        ]),
        ("cloud", [
            "aasmaan", "asman", "asmaan", "aakash", "akash", "falak", "gagan", "ambar", "nabh",
            "baadal", "badal", "badra", "ghata", "dhund", "kohra", "dhuaan", "dhuan",
            "आसमान", "आकाश", "फलक", "गगन", "अंबर", "बादल", "बदरा", "घटा", "धुंध", "कोहरा", "धुआं"
        ]),
        ("rain", [
            "baarish", "barish", "baarishon", "barsaat", "barsat", "barse", "baras", "saawan", "sawan",
            "rimjhim", "bheega", "bheegi", "bheeg", "bhiga", "bhigi", "bheege", "mausam",
            "बारिश", "बरसात", "बरसे", "सावन", "रिमझिम", "भीगा", "भीगी", "भीगे", "मौसम"
        ]),
        ("storm", [
            "toofan", "tufan", "toofani", "aandhi", "andhi", "bawaal", "bawal", "qayamat", "kayamat", "zalzala",
            "तूफान", "तूफानी", "आंधी", "बवाल", "कयामत", "जलजला"
        ]),
        ("lightning", [
            "bijli", "bijuriya", "karant", "jhatka", "tezi", "raftaar", "raftar",
            "बिजली", "बिजुरिया", "झटका", "रफ्तार"
        ]),
        ("snow", [
            "barf", "barfi", "sardi", "thanda", "thandi", "thand", "sheetal", "him", "os",
            "बर्फ", "सर्दी", "ठंडा", "ठंडी", "ठंड", "शीतल", "ओस"
        ]),
        ("wind", [
            "hawa", "hawaa", "hawayein", "hawayen", "pawan", "pavan", "vaayu", "vayu", "jhonka", "jhonke",
            "हवा", "हवाएं", "पवन", "वायु", "झोंका", "झोंके"
        ]),
        ("water", [
            "paani", "pani", "jal", "neer", "boond", "boondein", "bunde", "behta", "behti", "doob", "dooba", "duba",
            "पानी", "जल", "नीर", "बूंद", "बूंदें", "बहता", "बहती", "डूब", "डूबा"
        ]),
        ("ocean", [
            "samundar", "samandar", "sagar", "saagar", "dariya", "darya", "lehar", "lehrein", "lahar",
            "kinara", "kinaara", "sahil", "saahil", "gehra", "gehrai", "gehraai",
            "समंदर", "समुंदर", "सागर", "दरिया", "लहर", "लहरें", "किनारा", "साहिल", "गहरा", "गहराई"
        ]),
        ("river", [
            "nadi", "nadiya", "jheel", "jhil", "jharna", "ganga", "yamuna", "talab", "ghat", "vaadi", "wadi",
            "नदी", "नदिया", "झील", "झरना", "गंगा", "यमुना", "तालाब", "घाट", "वादी"
        ]),

        // --- Fire, Light, Dreams & Color ---
        ("fire", [
            "aag", "ag", "shola", "shole", "jwala", "jala", "jale", "jalna", "jalta", "jalti", "sulag", "raakh", "rakh",
            "आग", "शोला", "शोले", "ज्वाला", "जला", "जले", "जलना", "जलता", "सुलग", "राख"
        ]),
        ("fire_spark", [
            "chingari", "chingaari", "patakha", "pataakha", "chamak", "chamke", "chamakta", "jagmag",
            "चिंगारी", "पटाखा", "चमक", "चमके", "चमकता", "जगमग"
        ]),
        ("light", [
            "roshni", "raushni", "ujala", "ujaala", "ujale", "noor", "nur", "kiran", "kirnein", "prakash", "jyoti",
            "रोशनी", "उजाला", "उजाले", "नूर", "किरण", "किरणें", "प्रकाश", "ज्योति"
        ]),
        ("candle", [
            "diya", "diyaa", "diye", "chirag", "chiraag", "shama", "mombatti", "deepak", "diwali", "deepavali",
            "दीया", "दिया", "दिये", "चिराग", "शमा", "मोमबत्ती", "दीपक", "दिवाली"
        ]),
        ("dream", [
            "sapna", "sapne", "sapno", "sapnon", "khwaab", "khwab", "khwabon", "khwaabon", "armaan",
            "सपना", "सपने", "सपनों", "ख्वाब", "ख्वाबों"
        ]),
        ("magic_wand", [
            "jaadu", "jadu", "jadugar", "jaadugar", "tilism", "karishma", "chamatkar",
            "जादू", "जादूगर", "तिलिस्म", "करिश्मा", "चमत्कार"
        ]),
        ("rainbow", [
            "rang", "rangeen", "rangin", "rangrez", "holi", "indradhanush", "gulaal", "gulal", "laal", "neela", "peela", "hara",
            "रंग", "रंगीन", "रंगरेज", "होली", "इंद्रधनुष", "गुलाल", "लाल", "नीला", "पीला", "हरा"
        ]),

        // --- Nature, Flowers, Mountains & Animals ---
        ("flower", [
            "phool", "phul", "fool", "gulab", "gulaab", "kamal", "kali", "kaliyan", "khushboo", "khushbu",
            "mahak", "mehak", "chaman", "baag", "bagicha", "gulshan", "gul", "bahar", "bahaar", "mehndi", "mehendi",
            "फूल", "गुलाब", "कमल", "कली", "कलियां", "खुशबू", "महक", "चमन", "बाग", "बगीचा", "गुलशन", "बहार", "मेहंदी"
        ]),
        ("mountain", [
            "pahaad", "pahad", "pahaadon", "parvat", "shikhar", "choti", "patthar", "pathar", "chattan", "ooncha", "uncha",
            "पहाड़", "पहाड़ों", "पर्वत", "शिखर", "पत्थर", "चट्टान", "ऊंचा"
        ]),
        ("forest", [
            "jungle", "jangal", "ped", "vriksh", "shaakh", "daali", "bagh", "van", "chhaon", "chaav", "chhaya",
            "जंगल", "पेड़", "वृक्ष", "शाख", "डाली", "वन", "छांव", "छाया"
        ]),
        ("leaf", [
            "patta", "patte", "patti", "patjhad", "hariyali", "साख", "पत्ता", "पत्ते", "पतझड़", "हरियाली"
        ]),
        ("earth", [
            "duniya", "dunya", "jahaan", "jahan", "zameen", "jameen", "dharti", "mitti", "maati", "ret", "dhool", "kaaynat", "kainaat",
            "दुनिया", "जहां", "जमीन", "धरती", "मिट्टी", "माटी", "रेत", "धूल", "कायनात"
        ]),
        ("animal", [
            "parinda", "parinde", "panchi", "panchhi", "titli", "titliyan", "mor", "sher", "sherni", "hiran", "koyal", "bulbul", "hans",
            "परिंदा", "परिंदे", "पक्षी", "पंछी", "तितली", "तितलियां", "मोर", "शेर", "हिरण", "कोयल", "बुलबुल"
        ]),
        ("wing_angel", [
            "pankh", "parwaaz", "parwaz", "udaan", "udan", "udna", "udta", "udti", "udd",
            "पंख", "परवाज", "उड़ान", "उड़ना", "उड़ता", "उड़ती"
        ]),

        // --- Music, Instruments, Dance & Poetry ---
        ("music", [
            "geet", "gaana", "gana", "gaane", "sangeet", "dhun", "raag", "sur", "sargam", "tarana", "naghma", "nagma", "saaz",
            "गीत", "गाना", "गाने", "संगीत", "धुन", "राग", "सुर", "सरगम", "तराना", "नगमा", "साज"
        ]),
        ("sing", [
            "aawaz", "awaz", "awaaz", "gaaye", "gaata", "gaati", "gunguna", "mehfil", "sadaa",
            "आवाज", "गाये", "गाता", "गाती", "गुनगुना", "सदा"
        ]),
        ("rhythm", [
            "dhol", "dholak", "tabla", "taal", "tal", "thap", "damru", "nagaada", "nagada",
            "ढोल", "ढोलक", "तबला", "ताल", "डमरू", "नगाड़ा"
        ]),
        ("guitar", [
            "sitar", "sitaar", "bansuri", "baansuri", "murli", "veena", "sarangi", "shehnai", "ektaara", "iktara",
            "सितार", "बांसुरी", "मुरली", "वीणा", "सारंगी", "शहनाई", "इकतारा"
        ]),
        ("bell", [
            "ghungroo", "ghungru", "payal", "paayal", "choodi", "chudi", "choodiyan", "kangan", "ghanti", "jhanak", "khanak",
            "घुंघरू", "पायल", "चूड़ी", "चूड़ियां", "कंगन", "घंटी", "झनक", "खनक"
        ]),
        ("dance", [
            "naach", "nach", "naache", "nache", "nachle", "jhoom", "jhum", "jhoome", "thumka",
            "bhangra", "garba", "raas", "jashn", "utsav", "tyohaar",
            "नाच", "नाचे", "झूम", "झूमे", "ठुमका", "भांगड़ा", "गरबा", "रास", "जश्न", "उत्सव", "त्योहार"
        ]),
        ("letter", [
            "khat", "chitti", "chitthi", "shayar", "shayari", "ghazal", "gazal", "nazm", "kavita",
            "kahaani", "kahani", "kissa", "qissa", "alfaaz", "alfaz", "lafz", "laafz", "shabd", "kalam", "syahi",
            "खत", "चिट्ठी", "शायर", "शायरी", "गजल", "नज्म", "कविता", "कहानी", "किस्सा", "अल्फाज", "लफ्ज", "शब्द", "कलम", "स्याही"
        ]),
        ("books", [
            "kitaab", "kitab", "kitaabein", "panna", "panne", "daastan", "dastaan", "afsana", "afsaana",
            "किताब", "किताबें", "पन्ना", "पन्ने", "दास्तान", "अफसाना"
        ]),

        // --- Journey, Places, Time, Royalty & Everyday Poetic Motifs ---
        ("time", [
            "waqt", "wakt", "samay", "pal", "lamha", "lamhe", "lamhon", "ghadi", "sadi", "sadiyan", "kal", "aaj", "hamesha", "umr", "umar",
            "वक्त", "समय", "पल", "लम्हा", "लम्हे", "लम्हों", "घड़ी", "सदी", "सदियां", "कल", "आज", "हमेशा", "उम्र"
        ]),
        ("hourglass", [
            "intezaar", "intezar", "intzaar", "thehar", "ruk", "ruka", "dheere", "dhire", "aahista", "ahista",
            "इंतजार", "ठहर", "रुक", "धीरे", "आहिस्ता"
        ]),
        ("hourglass_end", [
            "alvida", "aakhri", "akhri", "khatam", "khatm", "ant", "bichhad", "bichhadna",
            "अलविदा", "आखिरी", "खत्म", "अंत", "बिछड़ना"
        ]),
        ("train", [
            "safar", "musafir", "musaafir", "raasta", "rasta", "raaste", "raah", "raahein", "manzil", "manzilein",
            "karwaan", "karwan", "rail", "patri", "doori", "duri", "faasle", "fasle",
            "सफर", "मुसाफिर", "रास्ता", "रास्ते", "राह", "राहें", "मंजिल", "कारवां", "दूरी", "फासले"
        ]),
        ("car", [
            "gaadi", "gadi", "sadak", "sarak", "chala", "chalna", "daud", "bhaag", "bhag",
            "गाड़ी", "सड़क", "दौड़", "भाग"
        ]),
        ("ship", [
            "kashti", "naav", "nav", "majhdhaar", "majhdhar", "patwar", "langar",
            "कश्ती", "नाव", "मझधार", "पतवार"
        ]),
        ("city", [
            "sheher", "shahar", "shehar", "nagar", "gali", "galiyan", "bazaar", "bazar", "mohalla", "gaon",
            "शहर", "नगर", "गली", "गलियां", "बाजार", "मोहल्ला", "गांव"
        ]),
        ("home", [
            "ghar", "makaan", "makan", "aangan", "angan", "chhat", "mahal", "haveli", "deewar", "diwar", "aashiyana", "ashiyana",
            "घर", "मकान", "आंगन", "छत", "महल", "हवेली", "दीवार", "आशियाना"
        ]),
        ("door", [
            "darwaza", "darwaaza", "dwar", "khidki", "jharokha", "parda", "chaukhat", "dehleez", "dehliz",
            "दरवाजा", "द्वार", "खिड़की", "झरोखा", "पर्दा", "चौखट", "दहलीज"
        ]),
        ("eye", [
            "aankh", "ankh", "aankhein", "ankhein", "aankhon", "ankhon", "naina", "nain", "naino",
            "nazar", "nazrein", "nazron", "palkein", "palkon", "kajal", "kaajal", "surma", "deedar", "didar",
            "आंख", "आंखें", "आंखों", "नैना", "नैन", "नैनों", "नजर", "नजरें", "नजरों", "पलकें", "काजल", "सुरमा", "दीदार"
        ]),
        ("hand", [
            "haath", "hath", "haathon", "hathon", "ungli", "hatheli", "baanh", "chehra", "chehre", "gaal", "zulf", "zulfein", "baal",
            "हाथ", "हाथों", "हथेली", "चेहरा", "चेहरे", "गाल", "जुल्फ", "जुल्फें"
        ]),
        ("footprints", [
            "kadam", "qadam", "nishaan", "nishan", "padchinh", "chalein", "chalta",
            "कदम", "निशान", "चलें", "चलता"
        ]),
        ("crown", [
            "raja", "rani", "raani", "badshah", "baadshah", "shehzada", "shehzadi", "rajkumar", "rajkumari",
            "taaj", "taj", "mukut", "sultan", "sultaan", "nawab", "malika", "begum",
            "राजा", "रानी", "बादशाह", "शहजादा", "शहजादी", "राजकुमार", "ताज", "मुकुट", "सुल्तान", "नवाब", "मलिका"
        ]),
        ("diamond", [
            "heera", "hira", "moti", "sona", "chaandi", "chandi", "jawahar", "ratan", "gajra", "jhumka", "jhumke", "baliya", "baliyan",
            "हीरा", "मोती", "सोना", "चांदी", "रत्न", "झुमका", "झुमके", "बालियां"
        ]),
        ("diamond_ring", [
            "shaadi", "shadi", "byaah", "dulhan", "dulha", "sindoor", "mangalsutra", "phere", "angoothi", "chhalla",
            "शादी", "ब्याह", "दुल्हन", "दूल्हा", "सिंदूर", "फेरे", "अंगूठी", "छल्ला"
        ]),
        ("money", [
            "paisa", "paise", "daulat", "rupiya", "ameer", "gareeb", "shohrat", "sikka",
            "पैसा", "पैसे", "दौलत", "अमीर", "गरीब", "शोहरत", "सिक्का"
        ]),
        ("handshake", [
            "dost", "dosti", "yaar", "yaari", "yari", "humsafar", "saath", "saathi", "sathi", "bhai", "behen", "parivar", "rishta", "rishte",
            "दोस्त", "दोस्ती", "यार", "यारी", "हमसफर", "साथ", "साथी", "भाई", "बहन", "परिवार", "रिश्ता", "रिश्ते"
        ]),
        ("wine", [
            "sharab", "shraab", "jaam", "jam", "maykhana", "maikhana", "saqi", "saaqi", "nasha", "nashe", "madira", "paimana", "peena", "zeher", "zahar", "amrit", "shahad",
            "शराब", "जाम", "मयखाना", "साकी", "नशा", "नशे", "मदिरा", "पैमाना", "जहर", "अमृत", "शहद"
        ]),
        ("coffee", [
            "chai", "chaay", "neend", "nind", "soya", "soye", "jaag", "jaagna", "bistar", "takiya",
            "चाय", "नींद", "सोया", "जाग", "जागना", "बिस्तर", "तकिया"
        ]),
        ("mirror", [
            "sheesha", "shisha", "aina", "aaina", "darpan", "aks", "tasveer", "tasvir", "chehra", "naqaab", "naqab",
            "शीशा", "आईना", "दर्पण", "अक्स", "तस्वीर", "नकाब"
        ]),
        ("key", [
            "raaz", "raz", "tala", "taala", "chaabi", "chabi", "pinjra", "qaid", "kaid", "zanjeer", "janjir", "jhoot", "jhooth", "sach", "sachai",
            "राज", "ताला", "चाबी", "पिंजरा", "कैद", "जंजीर", "झूठ", "सच", "सच्चाई"
        ]),
        ("weapon", [
            "talwar", "talwaar", "teer", "khanjar", "bandook", "goli", "jung", "jang", "ladai", "yuddh", "yoddha", "sipahi",
            "तलवार", "तीर", "खंजर", "बंदूक", "गोली", "जंग", "लड़ाई", "युद्ध", "योद्धा", "सिपाही"
        ]),
        ("skull", [
            "maut", "mrityu", "kafan", "qabr", "kabr", "marna", "mara",
            "मौत", "मृत्यु", "कफन", "कब्र", "मरना"
        ]),
        ("crystal_ball", [
            "taqdeer", "takdeer", "kismat", "qismat", "naseeb", "nasib", "bhagya", "mukaddar", "muqaddar",
            "तकदीर", "किस्मत", "नसीब", "भाग्य", "मुकद्दर"
        ]),
        ("gift", [
            "tohfa", "tohfaa", "uphaar", "inaam", "saugat", "saugaat",
            "तोहफा", "उपहार", "इनाम", "सौगात"
        ]),
        ("rose_petal", [
            "dupatta", "chunari", "chunri", "resham", "reshmi", "makhmal", "saari", "saree", "lehenga", "kurta", "itr", "attar",
            "दुपट्टा", "चुनरी", "रेशम", "रेशमी", "मखमल", "साड़ी", "लहंगा", "कुर्ता", "इत्र"
        ]),
        ("hand_wave", [
            "namaste", "salaam", "salam", "swagat", "mulaqat", "mulakaat", "milna", "mile",
            "नमस्ते", "सलाम", "स्वागत", "मुलाकात", "मिलना"
        ])
    ];

    public static void RegisterAliases(
        IReadOnlyDictionary<string, StarterLexiconEntry> conceptsById,
        IDictionary<string, StarterLexiconEntry> wordMap)
    {
        Span<char> normBuf = stackalloc char[48];
        foreach (var (conceptId, aliases) in ConceptAliases)
        {
            if (!conceptsById.TryGetValue(conceptId, out var entry))
            {
                continue;
            }

            foreach (var rawAlias in aliases)
            {
                if (LyricTextNormalizationData.TryNormalizeToken(rawAlias, normBuf, out var len))
                {
                    var key = normBuf[..len].ToString();
                    wordMap.TryAdd(key, entry);
                }
            }
        }
    }
}
