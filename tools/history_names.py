"""Names for the pieces a region of the dataset is cut into: peoples and houses, never a direction, a number or the word culture."""
import re, hashlib

POOLS = [
    (r'khoisan', 'tribe', ['Khoikhoi', 'San', 'Nama', 'Korana', 'Griqua', 'Hei//om', 'Kung', 'Naro', 'Cape Khoi', 'Gonaqua', 'Gora', 'Cochoqua', 'Chainouqua', 'Hessequa', 'Attaqua', 'Damaqua', 'Inqua', 'Gamtoos', 'Einiqua', 'Hurutshe', 'Tswana', 'Kalahari San', 'Khomani', 'Taa', 'Ju', 'Hadza', 'Sandawe', 'Kwadi', 'Heikum', 'Kxoe', 'Nharo', 'Gui', 'Gana', 'Tsumkwe', 'Ovambo', 'Herero', 'Damara', 'Topnaar', 'Bergdama', 'Hai', 'Okavango', 'Mbukushu', 'Kwangali', 'Subia', 'Totela', 'Lozi', 'Tonga']),
    (r'bantu', 'people', ['Kongo', 'Luba', 'Lunda', 'Zulu', 'Xhosa', 'Shona', 'Bemba', 'Chewa', 'Yao', 'Makua', 'Swazi', 'Sotho', 'Tswana', 'Venda', 'Tsonga', 'Ndebele', 'Ovimbundu', 'Mbundu', 'Tio', 'Teke', 'Kuba', 'Songye', 'Tetela', 'Mongo', 'Ngala', 'Fang', 'Duala', 'Bamileke', 'Tikar', 'Mbuti', 'Kikuyu', 'Kamba', 'Meru', 'Chagga', 'Haya', 'Ganda', 'Nyoro', 'Soga', 'Gusii', 'Luhya', 'Zigua', 'Swahili', 'Makonde', 'Ngoni', 'Tumbuka', 'Lala', 'Ila', 'Tonga', 'Kaonde', 'Lamba', 'Sena', 'Nyanja', 'Mbunda', 'Chokwe', 'Pende', 'Yaka', 'Bakongo', 'Mpongwe', 'Punu', 'Kota', 'Tanga']),
    (r'west african cereal', 'people', ['Mande', 'Soninke', 'Bambara', 'Malinke', 'Dogon', 'Mossi', 'Gurma', 'Senufo', 'Songhai', 'Fulani', 'Wolof', 'Serer', 'Tukulor', 'Susu', 'Mende', 'Temne', 'Kissi', 'Lobi', 'Bobo', 'Gur', 'Dagomba', 'Gonja', 'Akan', 'Ewe', 'Fon', 'Yoruba', 'Igbo', 'Nupe', 'Tiv', 'Jukun', 'Hausa', 'Kanuri', 'Bariba', 'Busa', 'Dendi', 'Zarma', 'Gurunsi', 'Kasena', 'Birifor', 'Wala', 'Sisala', 'Tallensi', 'Konkomba', 'Bassari', 'Diola', 'Balanta', 'Bijago', 'Manjak']),
    (r'manioc|maize|pampas|tupi|guarani|arawak', 'people', ['Tupinamba', 'Guarani', 'Kaingang', 'Xavante', 'Bororo', 'Kayapo', 'Munduruku', 'Tapajo', 'Omagua', 'Arawak', 'Carib', 'Wayana', 'Tukano', 'Baniwa', 'Yanomami', 'Shuar', 'Achuar', 'Kokama', 'Tembe', 'Mura', 'Pano', 'Shipibo', 'Machiguenga', 'Ashaninka', 'Chiquitano', 'Mojo', 'Baure', 'Guarayo', 'Chane', 'Charrua', 'Querandi', 'Tehuelche', 'Puelche', 'Huarpe', 'Diaguita', 'Calchaqui', 'Mapuche', 'Pehuenche', 'Wichi', 'Toba', 'Mocovi', 'Pilaga', 'Guaycuru', 'Payagua', 'Mbaya', 'Terena', 'Karaja', 'Tapuya', 'Aimore', 'Goitaca', 'Potiguar', 'Caete', 'Tabajara', 'Timbira', 'Xingu', 'Waura', 'Kuikuro', 'Palikur', 'Galibi', 'Taino', 'Warao']),
    (r'austro-asiatic|austronesian|malay|proto-thai|burmese|shan|thai', 'people', ['Khmer', 'Mon', 'Cham', 'Viet', 'Muong', 'Katu', 'Bahnar', 'Jarai', 'Rade', 'Stieng', 'Lawa', 'Wa', 'Palaung', 'Khasi', 'Munda', 'Santal', 'Ho', 'Nicobarese', 'Pyu', 'Karen', 'Kayah', 'Tai Dam', 'Lao', 'Ahom', 'Lue', 'Khun', 'Yuan', 'Shan', 'Chin', 'Kachin', 'Naga', 'Bunun', 'Atayal', 'Paiwan', 'Ami', 'Tagalog', 'Visayan', 'Ilocano', 'Bicolano', 'Pampangan', 'Cebuano', 'Ifugao', 'Tausug', 'Maranao', 'Bugis', 'Makassar', 'Minangkabau', 'Batak', 'Acehnese', 'Dayak', 'Iban', 'Kadazan', 'Sasak', 'Sundanese', 'Javanese', 'Balinese', 'Madurese', 'Toraja', 'Ambonese', 'Ternate', 'Tidore', 'Malagasy', 'Merina', 'Betsileo', 'Sakalava']),
    (r'papua', 'people', ['Motu', 'Huli', 'Enga', 'Chimbu', 'Hagen', 'Asmat', 'Dani', 'Marind', 'Kiwai', 'Trobriander', 'Mekeo', 'Toaripi', 'Elema', 'Sepik', 'Abelam', 'Iatmul', 'Baining', 'Tolai', 'Sulka', 'Mendi', 'Duna', 'Foi', 'Kewa', 'Gimi', 'Fore', 'Gahuku', 'Bena', 'Lani', 'Yali', 'Mek', 'Wodani', 'Ekari', 'Kamoro', 'Mimika', 'Biak']),
    (r'siberian', 'people', ['Evenk', 'Yakut', 'Chukchi', 'Koryak', 'Yukaghir', 'Nivkh', 'Nenets', 'Khanty', 'Mansi', 'Selkup', 'Ket', 'Dolgan', 'Nganasan', 'Even', 'Itelmen', 'Ulchi', 'Nanai', 'Udege', 'Orok', 'Tofalar', 'Tuvan', 'Altai', 'Shor', 'Khakas', 'Buryat', 'Dauri', 'Oroqen', 'Evenki', 'Sakha', 'Chuvan', 'Kerek', 'Alutor', 'Negidal', 'Oroch', 'Ainu']),
    (r'saharan|tuareg|berber|nomad', 'tribe', ['Tuareg', 'Tubu', 'Zenata', 'Sanhaja', 'Lamta', 'Gazzula', 'Masmuda', 'Garamantes', 'Gaetuli', 'Mazices', 'Nasamones', 'Mauri', 'Zeghawa', 'Tebu', 'Teda', 'Daza', 'Kel Ahaggar', 'Kel Air', 'Iwellemmeden', 'Kel Adagh', 'Maghrawa', 'Banu Ifran', 'Kutama', 'Ghomara', 'Zanata', 'Haskura', 'Hintata', 'Lamtuna', 'Messufa', 'Godala', 'Regraga', 'Zenaga', 'Tajakant', 'Berabish', 'Kunta', 'Ouled Slimane', 'Chaamba', 'Beni Mzab', 'Ait Atta']),
    (r'turcik|turkic|cuman|kipchak|khanate|proto-altaic|altaic|steppe|central asian', 'khanate', ['Kimek', 'Kangly', 'Karluk', 'Oghuz', 'Pecheneg', 'Kipchak', 'Kyrgyz', 'Uighur', 'Naiman', 'Kerait', 'Merkit', 'Tatar', 'Onogur', 'Khazar', 'Burtas', 'Berendey', 'Torci', 'Uz', 'Chigil', 'Yagma', 'Khalaj', 'Basmil', 'Sirt', 'Tolis', 'Kirghiz', 'Qarluq', 'Turgesh', 'Kumul', 'Kenegez', 'Salar', 'Sart', 'Nogai', 'Kazakh', 'Karakalpak', 'Uzbek', 'Turkmen', 'Yomut', 'Tekke', 'Ersari', 'Saryg', 'Telengit', 'Tele', 'Barlas', 'Jalayir', 'Dughlat', 'Arghun', 'Kunghrat']),
    (r'slav|rus ', 'people', ['Polans', 'Drevlyans', 'Dregovichi', 'Krivichi', 'Vyatichi', 'Radimichs', 'Severians', 'Ulichs', 'Tivertsy', 'Volhynians', 'Buzhans', 'Dulebes', 'Ilmen Slavs', 'Polotsk', 'Smolensk', 'Chernigov', 'Pereyaslavl', 'Ryazan', 'Murom', 'Rostov', 'Suzdal', 'Galich', 'Turov', 'Pinsk', 'Vitebsk', 'Minsk', 'Belz', 'Kholm', 'Terebovl', 'Peremyshl', 'Zvenigorod', 'Vladimir', 'Tver', 'Yaroslavl', 'Kostroma']),
    (r'celt|hallstatt|danes|northmen', 'people', ['Helvetii', 'Boii', 'Vindelici', 'Raeti', 'Noricans', 'Taurisci', 'Scordisci', 'Carnic', 'Sequani', 'Aedui', 'Arverni', 'Lingones', 'Mediomatrici', 'Leuci', 'Tigurini', 'Latobici', 'Varciani', 'Azali', 'Cotini', 'Anartii', 'Osi', 'Bastarnae', 'Taurisc', 'Ambrones', 'Rauraci', 'Tulingi', 'Vangiones', 'Nemetes', 'Triboci', 'Ubii']),
    (r'hopewell|adena|marksville|glades|swift creek|caloosahatchee|woodland|mississipp|jomon|pottery|beaker|bell', 'people', ['Adena', 'Hopewell', 'Marksville', 'Calusa', 'Tequesta', 'Timucua', 'Apalachee', 'Natchez', 'Caddo', 'Chickasaw', 'Choctaw', 'Creek', 'Cherokee', 'Shawnee', 'Miami', 'Illinois', 'Osage', 'Quapaw', 'Tunica', 'Chitimacha', 'Ofo', 'Biloxi', 'Houma', 'Ais', 'Jaega', 'Mayaimi', 'Pensacola', 'Yamasee', 'Catawba', 'Tuscarora', 'Powhatan', 'Lenape', 'Erie']),
    (r'sixteen', 'state', ['Han Zhao', 'Cheng Han', 'Dai', 'Yan', 'Liang', 'Qin', 'Xia', 'Zhao', 'Wei', 'Ran Wei', 'Murong', 'Tuoba', 'Duan', 'Yuwen', 'Chouchi', 'Juqu', 'Tufa', 'Qifu', 'Helian', 'Feng', 'Di', 'Qiang', 'Xianbei']),
    (r'timurid|emirate|buwayhid', 'state', ['Hamdanid', 'Mirdasid', 'Uqaylid', 'Marwanid', 'Shaddadid', 'Ziyarid', 'Sallarid', 'Rawadid', 'Kakuyid', 'Bavandid', 'Musafirid', 'Ilyasid', 'Saffarid', 'Samanid', 'Banijurid', 'Farighunid', 'Muhtajid', 'Hasanwayhid', 'Annazid', 'Mazyadid', 'Numayrid', 'Mahdid', 'Najahid', 'Sulayhid', 'Zurayid', 'Rassid', 'Yufirid']),
    (r'austro-hungarian', 'state', ['Bohemia', 'Moravia', 'Galicia', 'Carniola', 'Styria', 'Tyrol', 'Carinthia', 'Slovakia', 'Transylvania', 'Croatia', 'Dalmatia', 'Bukovina', 'Silesia', 'Bosnia']),
    (r'hindu|rajput|mahratta|islamic|indian|minor states|mesoamerican|city-states|chiefdom', 'state', ['Anga', 'Vanga', 'Kalinga', 'Utkala', 'Chedi', 'Vatsa', 'Kuru', 'Panchala', 'Malla', 'Vriji', 'Kasi', 'Matsya', 'Surasena', 'Assaka', 'Avanti', 'Gandhara', 'Kamboja', 'Pandya', 'Chera', 'Kadamba', 'Ganga', 'Hoysala', 'Yadava', 'Kakatiya', 'Paramara', 'Chauhan', 'Solanki', 'Gahadavala', 'Chandela', 'Kalachuri', 'Sena', 'Pala', 'Varman', 'Maitraka', 'Gurjara', 'Rashtrakuta', 'Pratihara', 'Sisodia', 'Rathore', 'Kachwaha', 'Bhati', 'Tomara', 'Bundela', 'Holkar', 'Scindia', 'Gaekwad', 'Bhonsle', 'Peshwa', 'Zapotec', 'Mixtec', 'Totonac', 'Huastec', 'Tarascan', 'Otomi', 'Mazahua', 'Tlaxcala', 'Cholula', 'Texcoco', 'Tlacopan', 'Xochimilco', 'Chalco', 'Cuauhnahuac']),
    (r'beja|gur|mel|benue|hausa|mossi|borgu|narmada|rift|bantu homeland', 'people', ['Beja', 'Blemmyes', 'Bisharin', 'Hadendoa', 'Amarar', 'Beni Amer', 'Nuba', 'Funj', 'Dinka', 'Nuer', 'Shilluk', 'Anuak', 'Luo', 'Maasai', 'Turkana', 'Pokot', 'Kalenjin', 'Sidamo', 'Oromo', 'Somali', 'Afar', 'Hadiya', 'Gurage', 'Kafa', 'Wolayta', 'Tiv', 'Idoma', 'Igala', 'Jukun', 'Bura', 'Kanuri', 'Mandara', 'Gbaya', 'Banda', 'Zande', 'Mangbetu', 'Alur', 'Lugbara', 'Acholi', 'Lango', 'Teso', 'Karamojong', 'Rendille', 'Gabbra', 'Samburu', 'Dorobo']),
    (r'khmer|cham|lao|tai|lavo|champa|mon|laotian', 'people', ['Khmer', 'Cham', 'Lao', 'Mon', 'Lawa', 'Tai Dam', 'Tai Lue', 'Tai Yuan', 'Phuan', 'Khun', 'Kuy', 'Bru', 'Pacoh', 'Katu', 'Bahnar', 'Jarai', 'Rade', 'Koho', 'Mnong', 'Stieng', 'Chrau', 'Lavo', 'Dvaravati', 'Haripunjaya', 'Sukhothai', 'Phayao', 'Nan', 'Phrae', 'Chiang Saen', 'Lanna', 'Lan Xang', 'Champasak', 'Xieng Khouang', 'Vientiane', 'Luang Prabang']),
    (r'baltic|germanic|thracian|libyan|finnic|tamaulipeco|andean|aymara|maya|mesoamerican|lombard|croatian|sinhalese|kushan|perm|white sea|elbe|ananyino|dyakovo|milograd', 'people', ['Prussians', 'Lithuanians', 'Samogitians', 'Curonians', 'Semigallians', 'Selonians', 'Latgalians', 'Yotvingians', 'Skalvians', 'Nadruvians', 'Sudovians', 'Galindians', 'Goths', 'Vandals', 'Burgundians', 'Lombards', 'Gepids', 'Heruls', 'Suebi', 'Alamanni', 'Thuringii', 'Angles', 'Saxons', 'Frisians', 'Franks', 'Hermunduri', 'Quadi', 'Marsi', 'Bructeri', 'Tencteri', 'Ampsivarii', 'Getae', 'Dacians', 'Triballi', 'Odrysae', 'Bessi', 'Dentheletae', 'Paeonians', 'Agrianes', 'Maedi', 'Thyni', 'Nasamones', 'Psylli', 'Maces', 'Auschisae', 'Cabales', 'Asbystae', 'Giligammae', 'Komi', 'Udmurts', 'Mari', 'Mordvins', 'Sami', 'Veps', 'Votes', 'Izhorians', 'Livonians', 'Quechua', 'Aymara', 'Puquina', 'Uru', 'Colla', 'Lupaca', 'Chanca', 'Huanca', 'Cañari', 'Chimu', 'Moche', 'Nazca', 'Wari', 'Tiwanaku', 'Mixe', 'Zoque', 'Chol', 'Tzeltal', 'Tzotzil', 'Quiche', 'Kaqchikel', 'Mam', 'Itza', 'Yucatec', 'Lacandon', 'Pipil', 'Lenca', 'Huaxtec', 'Sinhala', 'Rohana', 'Ruhuna', 'Pihiti']),
    (r'.*', 'tribe', []),
]

COLLECTIVE = re.compile(r'(tribes|nomads|peoples?|farmers|pastoralists|states|kingdoms|city-states|chiefdoms|principalities|clans|republics|confederation)')
SNAP_USED = set()
RENAME = {'Eastern Masurian': 'Masurians', 'Western Masurian': 'Pomesanians', 'Tamil South': 'Tamilakam', 'Northmen': 'Norse', 'Irrawaddy and Mainland Southeast Asia': 'Irrawaddy peoples', 'Syracuse and Eastern Sicily': 'Syracuse'}
ONSET = ['b', 'd', 'g', 'k', 'l', 'm', 'n', 'r', 's', 't', 'v', 'z', 'h', 'th', 'kr', 'br', 'dr', 'sh']
VOWEL = ['a', 'e', 'i', 'o', 'u', 'ai', 'ou']
CODA = ['', '', 'n', 'r', 's', 'l', 'th', 'k']


def invented(seed):
    h = int(hashlib.sha1(seed.encode()).hexdigest(), 16)
    out = ''
    for _ in range(3):
        out += ONSET[h % len(ONSET)] + VOWEL[(h // 7) % len(VOWEL)]
        h //= 53
    return (out + CODA[h % len(CODA)]).capitalize()


def pool_for(family):
    low = family.lower()
    for rx, kind, names in POOLS:
        if re.search(rx, low):
            return kind, names
    return 'tribe', []


ALL_USED = set()
MEMO = {}


def cluster_name(family, ikey):
    """A people's name for a cluster; the same cluster key always gets the same name and no two clusters share one."""
    if ikey in MEMO:
        return MEMO[ikey]
    kind, names = pool_for(family)
    h = int(hashlib.sha1((family + ikey).encode()).hexdigest(), 16)
    pick = None
    for t in range(len(names)):
        c = names[(h + t) % len(names)]
        if c not in ALL_USED:
            pick = c
            break
    n = 0
    while pick is None:
        c = invented(family + ikey + str(n))
        n += 1
        if c not in ALL_USED:
            pick = c
    ALL_USED.add(pick)
    MEMO[ikey] = pick
    return pick


def tidy(name):
    n = re.sub(r'\s+(tribes|nomads|peoples|people|tribe|farmers|pastoralists)$', '', name, flags=re.I)
    n = {'Hu and the Ordos': 'Hu and Ordos', 'Elbe Germanic': 'Hermunduri', 'Libyan and Berber': 'Libu and Mazyes', 'Lavo and the Chao Phraya': 'Lavo', 'Perm and the Finnic': 'Perm Finns', 'Bantu Homeland': 'Mongo', 'Benue': 'Tiv', 'Gur': 'Gurunsi', 'Mel': 'Temne', 'Narmada': 'Bhil', 'Empty Quarter': 'Rub al Khali Bedouin', 'Thracian': 'Thracians', 'Bantu Homeland Peoples': 'Mongo', 'Libyan and Berber Tribes': 'Libu and Mazyes', 'Tai': 'Tai Lue', 'Lao': 'Lao Lum', 'White Sea': 'Sami'}.get(n, n)
    return n or name


def clean(name):
    """The name of the dataset without 'culture', numbers and place words in brackets."""
    n = name.strip()
    n = re.sub(r'\s*\([^)]*\)\s*$', '', n) or n
    n = re.sub(r'\s+cultures?\b', '', n, flags=re.I)
    n = re.sub(r'\s+\d+$', '', n)
    n = n.strip() or name
    return RENAME.get(n, n)
