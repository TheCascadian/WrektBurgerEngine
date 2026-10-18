"""Names for the pieces a region of the dataset is cut into: peoples and houses, never a direction, a number or the word culture."""
import re, hashlib

POOLS = [
    (r'khoisan', 'tribe', ['Khoikhoi', 'San', 'Nama', 'Korana', 'Griqua', 'Hei//om', 'Kung', 'Naro', 'Cape Khoi', 'Gonaqua', 'Gora', 'Cochoqua', 'Chainouqua', 'Hessequa', 'Attaqua', 'Damaqua', 'Inqua', 'Gamtoos', 'Einiqua', 'Hurutshe', 'Tswana', 'Kalahari San', 'Khomani', 'Taa', 'Ju', 'Hadza', 'Sandawe', 'Kwadi', 'Heikum', 'Kxoe', 'Nharo', 'Gui', 'Gana', 'Tsumkwe', 'Ovambo', 'Herero', 'Damara', 'Topnaar', 'Bergdama', 'Hai', 'Okavango', 'Mbukushu', 'Kwangali', 'Subia', 'Totela', 'Lozi', 'Tonga']),
    (r'bantu', 'people', ['Kongo', 'Luba', 'Lunda', 'Zulu', 'Xhosa', 'Shona', 'Bemba', 'Chewa', 'Yao', 'Makua', 'Swazi', 'Sotho', 'Tswana', 'Venda', 'Tsonga', 'Ndebele', 'Ovimbundu', 'Mbundu', 'Tio', 'Teke', 'Kuba', 'Songye', 'Tetela', 'Mongo', 'Ngala', 'Fang', 'Duala', 'Bamileke', 'Tikar', 'Mbuti', 'Kikuyu', 'Kamba', 'Meru', 'Chagga', 'Haya', 'Ganda', 'Nyoro', 'Soga', 'Gusii', 'Luhya', 'Zigua', 'Swahili', 'Makonde', 'Ngoni', 'Tumbuka', 'Lala', 'Ila', 'Kaonde', 'Lamba', 'Sena', 'Nyanja', 'Mbunda', 'Chokwe', 'Pende', 'Yaka', 'Bakongo', 'Mpongwe', 'Punu', 'Kota', 'Tanga']),
    (r'west african cereal', 'people', ['Mande', 'Soninke', 'Bambara', 'Malinke', 'Dogon', 'Mossi', 'Gurma', 'Senufo', 'Songhai', 'Fulani', 'Wolof', 'Serer', 'Tukulor', 'Susu', 'Mende', 'Temne', 'Kissi', 'Lobi', 'Bobo', 'Gur', 'Dagomba', 'Gonja', 'Akan', 'Ewe', 'Fon', 'Yoruba', 'Igbo', 'Nupe', 'Tiv', 'Jukun', 'Hausa', 'Kanuri', 'Bariba', 'Busa', 'Dendi', 'Zarma', 'Gurunsi', 'Kasena', 'Birifor', 'Wala', 'Sisala', 'Tallensi', 'Konkomba', 'Bassari', 'Diola', 'Balanta', 'Bijago', 'Manjak']),
    (r'manioc|maize|pampas|tupi|guarani|arawak', 'people', ['Tupinamba', 'Guarani', 'Kaingang', 'Xavante', 'Bororo', 'Kayapo', 'Munduruku', 'Tapajo', 'Omagua', 'Arawak', 'Carib', 'Wayana', 'Tukano', 'Baniwa', 'Yanomami', 'Shuar', 'Achuar', 'Kokama', 'Tembe', 'Mura', 'Pano', 'Shipibo', 'Machiguenga', 'Ashaninka', 'Chiquitano', 'Mojo', 'Baure', 'Guarayo', 'Chane', 'Charrua', 'Querandi', 'Tehuelche', 'Puelche', 'Huarpe', 'Diaguita', 'Calchaqui', 'Mapuche', 'Pehuenche', 'Wichi', 'Toba', 'Mocovi', 'Pilaga', 'Guaycuru', 'Payagua', 'Mbaya', 'Terena', 'Karaja', 'Tapuya', 'Aimore', 'Goitaca', 'Potiguar', 'Caete', 'Tabajara', 'Timbira', 'Xingu', 'Waura', 'Kuikuro', 'Palikur', 'Galibi', 'Taino', 'Warao']),
    (r'austro-asiatic|austronesian|malay|proto-thai|burmese|shan|thai', 'people', ['Khmer', 'Mon', 'Cham', 'Viet', 'Muong', 'Katu', 'Bahnar', 'Jarai', 'Rade', 'Stieng', 'Lawa', 'Wa', 'Palaung', 'Khasi', 'Munda', 'Santal', 'Ho', 'Nicobarese', 'Pyu', 'Karen', 'Kayah', 'Tai Dam', 'Lao', 'Ahom', 'Lue', 'Khun', 'Yuan', 'Shan', 'Chin', 'Kachin', 'Naga', 'Bunun', 'Atayal', 'Paiwan', 'Ami', 'Tagalog', 'Visayan', 'Ilocano', 'Bicolano', 'Pampangan', 'Cebuano', 'Ifugao', 'Tausug', 'Maranao', 'Bugis', 'Makassar', 'Minangkabau', 'Batak', 'Acehnese', 'Dayak', 'Iban', 'Kadazan', 'Sasak', 'Sundanese', 'Javanese', 'Balinese', 'Madurese', 'Toraja', 'Ambonese', 'Ternate', 'Tidore', 'Malagasy', 'Merina', 'Betsileo', 'Sakalava']),
    (r'papua', 'people', ['Motu', 'Huli', 'Enga', 'Chimbu', 'Hagen', 'Asmat', 'Dani', 'Marind', 'Kiwai', 'Trobriander', 'Mekeo', 'Toaripi', 'Elema', 'Sepik', 'Abelam', 'Iatmul', 'Baining', 'Tolai', 'Sulka', 'Mendi', 'Duna', 'Foi', 'Kewa', 'Gimi', 'Fore', 'Gahuku', 'Bena', 'Lani', 'Yali', 'Mek', 'Wodani', 'Ekari', 'Kamoro', 'Mimika', 'Biak']),
    (r'siberian', 'people', ['Evenk', 'Yakut', 'Chukchi', 'Koryak', 'Yukaghir', 'Nivkh', 'Nenets', 'Khanty', 'Mansi', 'Selkup', 'Ket', 'Dolgan', 'Nganasan', 'Even', 'Itelmen', 'Ulchi', 'Nanai', 'Udege', 'Orok', 'Tofalar', 'Tuvan', 'Altai', 'Shor', 'Khakas', 'Buryat', 'Dauri', 'Oroqen', 'Evenki', 'Sakha', 'Chuvan', 'Kerek', 'Alutor', 'Negidal', 'Oroch', 'Ainu']),
    (r'saharan|tuareg|berber|nomad', 'tribe', ['Tuareg', 'Tubu', 'Zenata', 'Sanhaja', 'Lamta', 'Gazzula', 'Masmuda', 'Garamantes', 'Gaetuli', 'Mazices', 'Nasamones', 'Mauri', 'Zeghawa', 'Tebu', 'Teda', 'Daza', 'Kel Ahaggar', 'Kel Air', 'Iwellemmeden', 'Kel Adagh', 'Maghrawa', 'Banu Ifran', 'Kutama', 'Ghomara', 'Zanata', 'Haskura', 'Hintata', 'Lamtuna', 'Messufa', 'Godala', 'Regraga', 'Zenaga', 'Tajakant', 'Berabish', 'Kunta', 'Ouled Slimane', 'Chaamba', 'Beni Mzab', 'Ait Atta']),
    (r'turcik|turkic|cuman|kipchak|khanate|proto-altaic|altaic|steppe|central asian', 'khanate', ['Kimek', 'Kangly', 'Karluk', 'Oghuz', 'Pecheneg', 'Kipchak', 'Kyrgyz', 'Uighur', 'Naiman', 'Kerait', 'Merkit', 'Tatar', 'Onogur', 'Khazar', 'Burtas', 'Berendey', 'Torci', 'Uz', 'Chigil', 'Yagma', 'Khalaj', 'Basmil', 'Sirt', 'Tolis', 'Kirghiz', 'Qarluq', 'Turgesh', 'Kumul', 'Kenegez', 'Salar', 'Sart', 'Nogai', 'Kazakh', 'Karakalpak', 'Uzbek', 'Turkmen', 'Yomut', 'Tekke', 'Ersari', 'Saryg', 'Telengit', 'Tele', 'Barlas', 'Jalayir', 'Dughlat', 'Arghun', 'Kunghrat']),
    (r'slav|rus ', 'people', ['Polans', 'Drevlyans', 'Dregovichi', 'Krivichi', 'Vyatichi', 'Radimichs', 'Severians', 'Ulichs', 'Tivertsy', 'Volhynians', 'Buzhans', 'Dulebes', 'Ilmen Slavs', 'Polotsk', 'Smolensk', 'Chernigov', 'Pereyaslavl', 'Ryazan', 'Murom', 'Rostov', 'Suzdal', 'Galich', 'Turov', 'Pinsk', 'Vitebsk', 'Minsk', 'Belz', 'Kholm', 'Terebovl', 'Peremyshl', 'Zvenigorod', 'Vladimir', 'Tver', 'Yaroslavl', 'Kostroma']),
    (r'celt|hallstatt|danes|northmen', 'people', ['Helvetii', 'Boii', 'Vindelici', 'Raeti', 'Noricans', 'Taurisci', 'Scordisci', 'Carnic', 'Sequani', 'Aedui', 'Arverni', 'Lingones', 'Mediomatrici', 'Leuci', 'Tigurini', 'Latobici', 'Varciani', 'Azali', 'Cotini', 'Anartii', 'Osi', 'Bastarnae', 'Taurisc', 'Ambrones', 'Rauraci', 'Tulingi', 'Vangiones', 'Nemetes', 'Triboci', 'Ubii']),
    (r'jomon|ainu', 'people', ['Emishi', 'Kumaso', 'Hayato', 'Ezo', 'Koshi', 'Kibi', 'Izumo', 'Tsukushi', 'Kenu', 'Hitachi', 'Mutsu', 'Dewa', 'Awa', 'Kii', 'Hyuga', 'Satsuma', 'Tsugaru', 'Nambu', 'Ainu']),
    (r'pottery|bell-shaped|milograd|pomeranian|masurian|sambian|brushed', 'people', ['Neuri', 'Budini', 'Galindians', 'Sudovians', 'Yotvingians', 'Aestii', 'Veneti', 'Lugii', 'Rugii', 'Gutones', 'Burgii', 'Semnones', 'Silingi', 'Hasdingi', 'Lemovii', 'Buri', 'Cotini', 'Osi', 'Arsietae', 'Helisii', 'Manimi', 'Naharvali', 'Skirii', 'Bastarnae', 'Peucini', 'Carpi', 'Costoboci', 'Venedi', 'Polans', 'Drevlyans', 'Krivichi', 'Vyatichi', 'Radimichs', 'Dregovichi', 'Severians', 'Volhynians', 'Pomeranians', 'Mazovians', 'Prussians', 'Lithuanians']),
    (r'hopewell|adena|marksville|glades|swift creek|caloosahatchee|woodland|mississipp|laurel|copena|porter|point peninsula|fourche|goodall|saugeen|couture|mill creek|miller', 'people', ['Adena', 'Hopewell', 'Marksville', 'Calusa', 'Tequesta', 'Timucua', 'Apalachee', 'Natchez', 'Caddo', 'Chickasaw', 'Choctaw', 'Creek', 'Cherokee', 'Shawnee', 'Miami', 'Illinois', 'Osage', 'Quapaw', 'Tunica', 'Chitimacha', 'Ofo', 'Biloxi', 'Houma', 'Ais', 'Jaega', 'Mayaimi', 'Pensacola', 'Yamasee', 'Catawba', 'Tuscarora', 'Powhatan', 'Lenape', 'Erie']),
    (r'sixteen', 'state', ['Han Zhao', 'Cheng Han', 'Dai', 'Yan', 'Liang', 'Qin', 'Xia', 'Zhao', 'Wei', 'Ran Wei', 'Murong', 'Tuoba', 'Duan', 'Yuwen', 'Chouchi', 'Juqu', 'Tufa', 'Qifu', 'Helian', 'Feng', 'Di', 'Qiang', 'Xianbei']),
    (r'timurid|emirate|buwayhid', 'state', ['Hamdanid', 'Mirdasid', 'Uqaylid', 'Marwanid', 'Shaddadid', 'Ziyarid', 'Sallarid', 'Rawadid', 'Kakuyid', 'Bavandid', 'Musafirid', 'Ilyasid', 'Saffarid', 'Samanid', 'Banijurid', 'Farighunid', 'Muhtajid', 'Hasanwayhid', 'Annazid', 'Mazyadid', 'Numayrid', 'Mahdid', 'Najahid', 'Sulayhid', 'Zurayid', 'Rassid', 'Yufirid']),
    (r'austro-hungarian', 'state', ['Bohemia', 'Moravia', 'Galicia', 'Carniola', 'Styria', 'Tyrol', 'Carinthia', 'Slovakia', 'Transylvania', 'Croatia', 'Dalmatia', 'Bukovina', 'Silesia', 'Bosnia']),
    (r'mesoamerican|city-states|chiefdom|maya', 'people', ['Zapotec', 'Mixtec', 'Totonac', 'Huastec', 'Tarascan', 'Otomi', 'Mazahua', 'Tlaxcala', 'Cholula', 'Texcoco', 'Tlacopan', 'Xochimilco', 'Chalco', 'Cuauhnahuac', 'Itza', 'Quiche', 'Kaqchikel', 'Mam']),
    (r'hindu|rajput|mahratta|islamic|indian|minor states|mesoamerican|city-states|chiefdom', 'state', ['Anga', 'Vanga', 'Kalinga', 'Utkala', 'Chedi', 'Vatsa', 'Kuru', 'Panchala', 'Malla', 'Vriji', 'Kasi', 'Matsya', 'Surasena', 'Assaka', 'Avanti', 'Gandhara', 'Kamboja', 'Pandya', 'Chera', 'Kadamba', 'Ganga', 'Hoysala', 'Yadava', 'Kakatiya', 'Paramara', 'Chauhan', 'Solanki', 'Gahadavala', 'Chandela', 'Kalachuri', 'Sena', 'Pala', 'Varman', 'Maitraka', 'Gurjara', 'Rashtrakuta', 'Pratihara', 'Sisodia', 'Rathore', 'Kachwaha', 'Bhati', 'Tomara', 'Bundela', 'Holkar', 'Scindia', 'Gaekwad', 'Bhonsle', 'Peshwa']),
    (r'beja|gur|mel|benue|hausa|mossi|borgu|narmada|rift|bantu homeland', 'people', ['Beja', 'Blemmyes', 'Bisharin', 'Hadendoa', 'Amarar', 'Beni Amer', 'Nuba', 'Funj', 'Dinka', 'Nuer', 'Shilluk', 'Anuak', 'Luo', 'Maasai', 'Turkana', 'Pokot', 'Kalenjin', 'Sidamo', 'Oromo', 'Somali', 'Afar', 'Hadiya', 'Gurage', 'Kafa', 'Wolayta', 'Tiv', 'Idoma', 'Igala', 'Jukun', 'Bura', 'Kanuri', 'Mandara', 'Gbaya', 'Banda', 'Zande', 'Mangbetu', 'Alur', 'Lugbara', 'Acholi', 'Lango', 'Teso', 'Karamojong', 'Rendille', 'Gabbra', 'Samburu', 'Dorobo']),
    (r'khmer|cham|lao|tai|lavo|champa|mon|laotian', 'people', ['Khmer', 'Cham', 'Lao', 'Mon', 'Lawa', 'Tai Dam', 'Tai Lue', 'Tai Yuan', 'Phuan', 'Khun', 'Kuy', 'Bru', 'Pacoh', 'Katu', 'Bahnar', 'Jarai', 'Rade', 'Koho', 'Mnong', 'Stieng', 'Chrau', 'Lavo', 'Dvaravati', 'Haripunjaya', 'Sukhothai', 'Phayao', 'Nan', 'Phrae', 'Chiang Saen', 'Lanna', 'Lan Xang', 'Champasak', 'Xieng Khouang', 'Vientiane', 'Luang Prabang']),
    (r'baltic|germanic|thracian|libyan|finnic|tamaulipeco|andean|aymara|maya|mesoamerican|lombard|croatian|sinhalese|kushan|perm|white sea|elbe|ananyino|dyakovo|milograd', 'people', ['Prussians', 'Lithuanians', 'Samogitians', 'Curonians', 'Semigallians', 'Selonians', 'Latgalians', 'Yotvingians', 'Skalvians', 'Nadruvians', 'Sudovians', 'Galindians', 'Goths', 'Vandals', 'Burgundians', 'Lombards', 'Gepids', 'Heruls', 'Suebi', 'Alamanni', 'Thuringii', 'Angles', 'Saxons', 'Frisians', 'Franks', 'Hermunduri', 'Quadi', 'Marsi', 'Bructeri', 'Tencteri', 'Ampsivarii', 'Getae', 'Dacians', 'Triballi', 'Odrysae', 'Bessi', 'Dentheletae', 'Paeonians', 'Agrianes', 'Maedi', 'Thyni', 'Nasamones', 'Psylli', 'Maces', 'Auschisae', 'Cabales', 'Asbystae', 'Giligammae', 'Komi', 'Udmurts', 'Mari', 'Mordvins', 'Sami', 'Veps', 'Votes', 'Izhorians', 'Livonians', 'Quechua', 'Aymara', 'Puquina', 'Uru', 'Colla', 'Lupaca', 'Chanca', 'Huanca', 'Cañari', 'Chimu', 'Moche', 'Nazca', 'Wari', 'Tiwanaku', 'Mixe', 'Zoque', 'Chol', 'Tzeltal', 'Tzotzil', 'Quiche', 'Kaqchikel', 'Mam', 'Itza', 'Yucatec', 'Lacandon', 'Pipil', 'Lenca', 'Huaxtec', 'Sinhala', 'Rohana', 'Ruhuna', 'Pihiti']),
    (r'tibetan', 'people', ['Zhangzhung', 'Sumpa', 'Tangut', 'Qiang', 'Di', 'Ran', 'Mang', 'Baima', 'Yi', 'Naxi']),
    (r'.*', 'tribe', []),
]

COLLECTIVE = re.compile(r'(tribes|nomads|peoples?|farmers|pastoralists|states|kingdoms|city-states|chiefdoms|principalities|clans|republics|confederation)')
SNAP_USED = set()
RENAME = {'Suren Kingdom': 'Indo-Parthian Kingdom', 'Saka Kingdom': 'Western Satraps', 'Satavahanihara': 'Satavahana', 'Mascat': 'Oman', 'Koguryo': 'Goguryeo', 'Porter': 'Houma', 'Eastern Masurian': 'Masurians', 'Western Masurian': 'Pomesanians', 'Tamil South': 'Tamilakam', 'Northmen': 'Norse', 'Irrawaddy and Mainland Southeast Asia': 'Irrawaddy peoples', 'Syracuse and Eastern Sicily': 'Syracuse'}
ANCIENT = [
    (r'hindu|rajput|mahratta|indian|minor states|islamic', 'state', ['Kosala', 'Kuru', 'Panchala', 'Vatsa', 'Avanti', 'Gandhara', 'Kamboja', 'Assaka', 'Chedi', 'Matsya', 'Surasena', 'Kalinga', 'Anga', 'Vanga', 'Pandya', 'Chera', 'Chola', 'Malla', 'Vajji', 'Kasi', 'Videha', 'Sibi', 'Ambashtha', 'Kshudraka', 'Malava', 'Sauvira', 'Madra', 'Yaudheya', 'Andhaka', 'Vidarbha', 'Mulaka', 'Utkala', 'Kekaya', 'Trigarta', 'Kuninda', 'Abhira', 'Satiyaputra', 'Ay', 'Ezhimala', 'Tamraparni', 'Pulinda', 'Ujjayini', 'Dasarna', 'Ikshvaku', 'Pundra', 'Suhma', 'Magadha']),
    (r'mesoamerican|city-states|chiefdom|maya|andean|aymara|tamaulipeco', 'people', ['Zapotec', 'Mixtec', 'Totonac', 'Huastec', 'Olmec', 'Mixe', 'Zoque', 'Otomi', 'Tarascan', 'Mazahua', 'Nahua', 'Chontal', 'Tlapanec', 'Popoluca', 'Chichimec', 'Cuicatec', 'Chinantec', 'Mazatec', 'Triqui', 'Amuzgo', 'Pame', 'Tepehua', 'Yucatec', 'Lacandon', 'Chol', 'Tzeltal', 'Tzotzil', 'Quiche', 'Mam', 'Itza', 'Pipil', 'Lenca', 'Paracas', 'Chavin', 'Cupisnique', 'Salinar', 'Vicus', 'Pukara', 'Chiripa', 'Tiwanaku', 'Puquina', 'Uru', 'Chanca', 'Cañari']),
    (r'arabs', 'tribe', ['Qedar', 'Nabataeans', 'Thamud', 'Lihyan', 'Minaeans', 'Sabaeans', 'Awsan', 'Himyar', 'Dedan', 'Midian', 'Amalekites', 'Tayma', 'Gerrha', 'Katabania', 'Hadramis', 'Dilmun', 'Magan', 'Omanis', 'Yamama', 'Najd', 'Hijaz', 'Asir', 'Najran', 'Ausan', 'Kinda', 'Hadhramaut', 'Mahra', 'Sheba', 'Dumah']),
    (r'saharan|tuareg|berber|libyan|nomad', 'tribe', ['Garamantes', 'Gaetuli', 'Nasamones', 'Mauri', 'Mazices', 'Libu', 'Maxyes', 'Psylli', 'Augilae', 'Lotophagi', 'Atarantes', 'Atlantes', 'Troglodytes', 'Macae', 'Gindanes', 'Machlyes', 'Auses', 'Zaueces', 'Gyzantes', 'Adyrmachidae', 'Giligammae', 'Asbystae', 'Auschisae', 'Cabales', 'Bacales', 'Maces', 'Pharusii', 'Nigretes', 'Perorsi', 'Musulamii', 'Nubae', 'Masaesyli', 'Massyli', 'Mazyes', 'Zeugitani', 'Byzacenes', 'Macurebi', 'Garamantian clans']),
    (r'turcik|turkic|cuman|kipchak|khanate|altaic|steppe|central asian|tibetan|siberian', 'khanate', ['Dingling', 'Jie', 'Qiang', 'Wusun', 'Yuezhi', 'Lin Hu', 'Loufan', 'Dahae', 'Massagetae', 'Issedones', 'Sauromatae', 'Agathyrsi', 'Aorsi', 'Siraces', 'Tochari', 'Sogdians', 'Chorasmians', 'Derbices', 'Apasiacae', 'Tapuri', 'Saka Tigraxauda', 'Saka Haumavarga', 'Argippaei', 'Tyrcae', 'Iyrcae', 'Hunnic clans', 'Sai', 'Yuban', 'Rong', 'Di', 'Gedrosians', 'Paricanii', 'Arimaspi', 'Kangju', 'Yancai', 'Xianbei', 'Wuhuan', 'Shiwei', 'Mohe', 'Jushi', 'Wensu', 'Kucha', 'Yutian', 'Loulan', 'Shule', 'Dayuan', 'Daxia', 'Saka Paradraya', 'Amyrgians', 'Pazyryk', 'Tagar', 'Sargat', 'Gorokhovo', 'Kulai', 'Itkul', 'Baitovo', 'Ust-Polui', 'Karasuk', 'Andronovo', 'Alakul', 'Tashtyk', 'Okunevo', 'Karakol', 'Sintashta', 'Yamnaya', 'Srubna', 'Catacomb', 'Kobyakovo', 'Taksai', 'Zhuwei', 'Gushi', 'Lüfan', 'Linhu']),
    (r'slav|rus |baltic|germanic|finno|finnic|ugric|pottery|bell-shaped|milograd|pomeranian|masurian|sambian|brushed', 'people', ['Neuri', 'Budini', 'Venedi', 'Androphagi', 'Melanchlaeni', 'Callipidae', 'Alazones', 'Gelonians', 'Thyssagetae', 'Iyrcae', 'Argippaei', 'Boruski', 'Aestii', 'Galindians', 'Sudovians', 'Yotvingians', 'Curonians', 'Semigallians', 'Selonians', 'Bastarnae', 'Peucini', 'Sciri', 'Rugii', 'Gutones', 'Lemovii', 'Burgundiones', 'Silingi', 'Hasdingi', 'Kotini', 'Osi', 'Anartes', 'Arsietae', 'Harii', 'Helveconae', 'Manimi', 'Nahanarvali', 'Elisii', 'Buri']),
]
ONSET = ['b', 'd', 'g', 'k', 'l', 'm', 'n', 'r', 's', 't', 'v', 'z', 'h', 'p', 'f', 'y']
VOWEL = ['a', 'e', 'i', 'o', 'u']
CODA = ['', '', 'n', 'r', 's', 'l', 'm', 'k']


STYLES = [
    (r'bantu|west african|ethiopian', ['Ba', 'Wa', 'Ma', 'Aba', 'Ama', 'Va', 'Ba', 'Ova'], ['k', 'l', 'm', 'n', 'ng', 'nd', 'mb', 'nk', 't', 'z', 'ch', 'w', 'b', 's', 'r'], ['a', 'e', 'i', 'o', 'u'], ['', 'a', 'e', 'u', 'o'], 2),
    (r'khoisan', ['', '', 'K', 'G', 'N'], ['k', 'g', 'n', 'x', 'd', 't', 'h', 'm', 'b', 'r', 'l'], ['a', 'e', 'o', 'u', 'i'], ['', '', 'n', 'qua', 'ra', 'm', 'xa'], 2),
    (r'manioc|maize|pampas|tupi|guarani|arawak|andean|aymara', ['', '', '', 'Ta', 'Ya', 'Ka'], ['k', 'p', 't', 'm', 'n', 'r', 'y', 'w', 'ch', 'qu', 'g', 'b', 'c'], ['a', 'i', 'u', 'o', 'e'], ['ua', 'ay', 'ara', 'ano', 'i', 'e', 'o', 'ana'], 2),
    (r'siberian|steppe|turcik|turkic|khanate|altaic|tibetan|slavonic|slavic', ['', '', ''], ['k', 'ch', 't', 'b', 'd', 'g', 'y', 'n', 'm', 'r', 's', 'l', 'z'], ['a', 'o', 'u', 'i', 'e'], ['ak', 'ur', 'an', 'ay', 'ir', 'ut', 'ar', 'ich'], 2),
    (r'hindu|rajput|mahratta|islamic|indian|emirate|minor|kingdom|state', ['', '', ''], ['k', 'v', 'm', 'p', 'd', 'g', 'n', 'r', 's', 't', 'b', 'j', 'sh', 'ch'], ['a', 'i', 'u', 'e', 'o'], ['a', 'ika', 'ana', 'ya', 'ra', 'ala', 'eya', 'ika'], 2),
    (r'sahar|berber|tuareg|nomad', ['', '', ''], ['g', 'm', 'z', 'k', 'n', 't', 'r', 'l', 's', 'b', 'h', 'w'], ['a', 'i', 'u', 'e'], ['ae', 'i', 'es', 'ae', 'ii', 'an', 'in'], 2),
]
DEFAULT_STYLE = ([''], ['b', 'd', 'g', 'k', 'l', 'm', 'n', 'r', 's', 't', 'v', 'z', 'h', 'p', 'f', 'y'], ['a', 'e', 'i', 'o', 'u'], ['', 'n', 'r', 's', 'l', 'm', 'k', 'a'], 2)


def invented(seed, family=''):
    h = int(hashlib.sha1(seed.encode()).hexdigest(), 16)
    low = family.lower()
    st = next(((p, o, v, e, n) for rx, p, o, v, e, n in STYLES if re.search(rx, low)), DEFAULT_STYLE)
    pre, ons, vow, end, syl = st
    out = pre[h % len(pre)]
    h //= 31
    for _ in range(syl + h % 2):
        out += ons[h % len(ons)] + vow[(h // 17) % len(vow)]
        h //= 97
    out += end[h % len(end)]
    return out[:1].upper() + out[1:]


def pool_for(family, year=9999):
    low = family.lower()
    if year < 500:
        for rx, kind, names in ANCIENT:
            if re.search(rx, low):
                return kind, names
    for rx, kind, names in POOLS:
        if re.search(rx, low):
            return kind, names
    return 'tribe', []


ALL_USED = set()
INVENTED = {}
MEMO = {}


def cluster_name(family, ikey, year=9999):
    """A people's name for a cluster; the same cluster key always gets the same name and no two clusters share one."""
    if ikey in MEMO:
        return MEMO[ikey]
    kind, names = pool_for(family, year)
    h = int(hashlib.sha1((family + ikey).encode()).hexdigest(), 16)
    pick = None
    for t in range(len(names)):
        c = names[(h + t) % len(names)]
        if c not in ALL_USED:
            pick = c
            break
    n = 0
    while pick is None:
        c = invented(family + ikey + str(n), family)
        n += 1
        if c not in ALL_USED:
            pick = c
    if pick not in names:
        INVENTED[family] = INVENTED.get(family, 0) + 1
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


def year_of(y):
    """'bc400' -> -400, '1200' -> 1200."""
    y = str(y)
    return -int(y[2:]) if y.startswith('bc') else int(y)


import history_names_extra
history_names_extra.merge(POOLS)


# the name a polity of the dataset had at a date, where the dataset keeps one name for a dynasty's whole span: (snapshot, name) -> name
YEAR_NAMES = {('300', 'Parthian Empire'): 'Sasanian Empire', ('400', 'Parthian Empire'): 'Sasanian Empire', ('bc323', 'Magadha'): 'Nanda Empire', ('bc300', 'Magadha'): 'Maurya Empire'}
