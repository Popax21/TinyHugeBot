using ChessChallenge.API;
using static System.AppDomain;

class MyBot : IChessBot {
    //TinyBot_asmBuf either holds the TinyBot IChessBot instance, or the assembly buffer during decoding
    dynamic TinyBot_asmBuf = new byte[9216];
    int asmBufOff, scaleParity;
    byte scaleAccum;

    public MyBot() {
        //Decode the assembly
        //The assembly is encoded in a semi-RLE-like format
        //There are two types of tokens:
        // - scaling factors 0-15: regular tokens
        //   - the decimal integer number encodes 12 bytes, which are copied to the assembly buffer
        //   - the decimal scaling factor encodes an additional 4 bits - two regular tokens are paired up, and their scaling factors are combined to form an extra byte
        // - scaling factor 16: skip tokens (can skip up to 255 bytes forward to efficiently encode a stretch of zero bytes)
        //   - the lowest byte of the decimal integer number contains the amount of bytes to skip forward by
        //   - the remaining 11 integer number bytes are copied to the assembly buffer
        //   - skip tokens are invisible to the scalar accumulator; they don't contribute their scale value, nor do they affect parity
        // - the sign bit is unused as a minus sign would require an extra token
        foreach(decimal dec in new[] {
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> BEGIN ENCODED ASSEMBLY <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
            73786.976307732568653M,19807040628566.084401472995583M,31105283551.3167525259001891M,557423098368M,158273077664374130344160M,7737125245.5336267181203469M,73786978493861462528M,4352132950.6126650289423364M,41277563240262130762296328704M,7555786.3725983042899970M,1152921504606846992M,0.0000309237645936M,4051790442.3149616380521988M,9444733003122685772288M,0.0000412316868620M,3324546039.9692471517333657M,1099512153088M,944473.2966838802055477M,326431738453956854441574404M,991319175405429854563.21002M,290919781640315.35772132725248M,29091978164031535772132725248M,123311927817080170671374418M,129356298659891911221.182526M,1733157140020289470.5126563328M,22283336281007940863904473600M,139028111039830691994206303M,96716039386267713989.378141M,2692566337500793911085.1595520M,28782375095264302739373447168M,129356686045458263416111172M,118476464342072736654.491751M,27545053684474989803468779.264M,45495056752884172056101437952M,74956647473182435590144150M,172881317546.359415645667506M,69944594489218467.511715016960M,29091978177506293765365593088M,113640539703248186609893458M,1136405397032481866098.93458M,71192413929524733958768798.72M,16403928636509074447854010881M,37235315547315391562763495.1M,40378622291041060807639063.9M,62049536282407940806156.6977M,6810219170867177267988086273M,35180398063206541391822882.9M,31311770875850314073283.0050M,2909299349148848181156518.4769M,31568878292641922202780905985M,42796461017633910912666857.3M,4521438460091021876498271.00M,2168090419141.800692193269249M,10833741527619139207004496385M,32883641919673228585035406.6M,3094898798293361588566757.38M,321877585885463718363.77633025M,19808254301540275727924006913M,36630900597184930337010506.1M,290146808505.557556693827852M,55722980155712.58965491794432M,11143084867382696331851164161M,35301235304513573292605475.2M,33729731351488855264.3035480M,4023447273589195.3141105631745M,33735141135850376782046896129M,45939743781746332169692802.9M,4630241230459646091132275.71M,136191255076388623442238154.25M,14857164718421857917462477313M,34817744298033165492571378.5M,373565161896777651.482657129M,35901710934753.763711262994945M,45186223446643187137040690177M,48357578819609612910959030.5M,4388455513162545241951439.75M,1485691915370741.3037570413313M,8667582579571765612889858049M,34213220513660760707910899.1M,3493856124811870670560.95601M,3125930828137679708873982.8225M,68708099510146076950172602625M,58512944933149524957947545.9M,523473955818.166128475636211M,62118012405066376.4131958017M,75826051677088479811658177538M,61897835771400259779513589.4M,61051639348637.8802628723149M,5725706914271846.6972729865217M,68089157824774357017677660929M,58392050505866260505047910.8M,563368950587869545800008184M,621335962216706634315588.610M,621128177947307798253666306M,6274417792383492608275911.28M,61535287152949593604181248.1M,7830217731878843026.6621885186M,13929295278470689356085595393M,6298599444677242542180275.17M,61535287153034049411540633.9M,37161152257255884932661944.34M,41785312066414390666923736066M,1079589849553252634858357.743M,1107394387085067.265299907570M,3718131711738.578917587648259M,4956104808742267382300641796M,122224067977582992070528.2961M,123674865660479927.4085188504M,550932383556471.40126829837315M,60663779635250008692044858883M,1169050138863228078373143.544M,11593787138573.15227287815165M,74590420911474882155229.981443M,78923286609215378118295147779M,1218614123673974577259742.177M,127422598420749.9463802487726M,5478395640772872311938.4574211M,61901818846969684817216792835M,1161797672306536851163907.074M,1120692626450067945909781533M,1857364830292848595898176256.4M,14240777887433228329758737156M,1571540847610602394543.8140M,79195521000628.441773796687851M,708720766907225264259061578.23M,4332487906404207578276956159M,79222118051188368417221443541M,792257444228123.68771553886216M,6994372555324906445.4100880383M,64372844261962.365094410064127M,79181014813120.418315061166062M,79188268404926527821088686104M,7118065500423341306.791578880M,8355953596199413712880137216M,4835039203271627398840295M,27805330749.409796121362423M,6808882722993689299616010239M,79228105847956.228777694997504M,79218491439746.843626504454161M,79193104108206675234895167505M,736575126151883457.36193359616M,4951604318326949987500421631M,79115730918849122091053350914M,7911573091882772958.9090058158M,541594375328157.12776819286783M,35280034954656.213820189418239M,78977910442343.590917588385540M,7913748791268519541028768.9502M,2971003202497667580758167.7567M,25067785214161.207019184287231M,79086716459340.298391542235074M,7911814834619043768.7041064872M,625154525194191268193091991.03M,50445631582419.298635676095999M,79118148696681.671380986363817M,79135073436804354478.405320607M,504456599153211932.45590926079M,52921610829533.264749126462463M,79106059881203.442234363871185M,791157304761022127097.87434949M,541594375328157204737.69779455M,54159437532815.712776819286783M,79130237807305.553902853619618M,78863062452559.128472329846690M,92732636593185193518005.4782M,308243014393.674039365123839M,78892074274150.897391596732120M,78918671177137997.413967003288M,43017188521346329726144158718M,14544477891089.990337532190206M,788328393623715.58624668876528M,7875788479941761.1752067497720M,6932297046127715436763221478.2M,73655826713996.505892536621566M,788703137569577.13160694398623M,78911417898913308.146329386652M,51373194065018213481893.003006M,46730928362070.700926001142014M,788860321169018.94068256112379M,788231672917450.84222983634683M,6188203187732675982.109089278M,78917364670059.219378390374655M,789114182678555.08133125750480M,788872392534129475342.67678402M,53539546633982.483815617981694M,48278343965147.427136740450046M,788098687941214.44647783759599M,787167814875604.33495126900432M,6622814869791121600178805.1966M,67156339273843.305235320041214M,788703130744290.26579198377584M,788594333323508387820.49910421M,34351372127664182471233031166M,47659284221190.067627411557886M,787566757444645.96088717246173M,7877601861291894.3834436665028M,6777542735351280552.1371860990M,62823671917152.483729626531070M,788884474597889.95319112728184M,78878775942554710081.087602319M,35589420782531595750858479.870M,46730947253121.922562992826110M,788038241834805.30214181797600M,788134955900425.13767095336672M,739645183475855975958.79080958M,75821537020798.635732214083837M,7859709395756798.8771338124804M,7859709331192969.4327265295875M,76749831491315490332753134.589M,3092403876706.245607327399421M,785983026620331.48453347917305M,78681718672714.047900294249987M,79225791848531226.683032612605M,616759915234.657137021815805M,786043484901636.75742248828471M,786164371027234978288606244.31M,2506592457964487258.2483543806M,16400315969943.901486479967486M,786587492481665.70217581903361M,78672047321514251633878695432M,3092606937168028383810.828286M,1236027444830.504270736267518M,786067658437410.96386633530934M,7862610854617643621145391.8738M,792237895311312967725.40726526M,60964003943595.738266498515707M,779358033806228114.52637707325M,77996249487159183276991052.868M,198019356788438031081.51918332M,33729091690637.395564502443004M,78038560470457941.611187993616M,78074827525616043.051906104290M,4947240780574962234678.857723M,73962232686034.468498063507708M,780010848952784772.97439603760M,780022941346955560539994184.74M,15469400550864416169.785033212M,35895415925042.564272412689148M,78086917743051044.199984659466M,780917532618609360109615175.65M,786049186783740378262701626.84M,924148156362.807415832078587M,78011966500480913.378176334972M,780083399074864139600548.97780M,4022845162588675.3524882872572M,5880139631335878489286918652M,79210029254162513127311278095M,15715500704478539735171070M,278528480873834219404734848.0M,9284441683021450446024732416M,79226953293290343817603055605M,7922211751623729.3883566915561M,2475965080230728768965044.991M,70872199473620.173124412642304M,79196730719646.229778080858142M,483572173026935473733635.6M,139266979414521711.44369007360M,4332842087277127523182307584M,56819310613895222779576339M,19342517974.654512939204587M,3713787063308372270.882233343M,74895580159954.075641482053632M,21760517180.799550968561680M,26596146680444426898440.207M,71800588393700460637947.169536M,23635985975802929713518847M,3094850193330600717317055745M,506272735781985.6818802048M,39614081259514854474644455424M,590724208356.2674686456922115M,78635812794427781769151064838M,77132385876372582411167371.1M,260687667.5507027394495490M,461842910469492194507491840M,7126698117213829000997176320M,71254928643126931024856.57962M,59867329785014581822.46827539M,959789392283599127243371750.9M,21761297917467941995.38206219M,5990341351201641627396278618M,1312049112363905890994961.9970M,618970029298414542081623057M,5818638108483769511710976M,123794006695831113531706675.2M,11806636.783142195699468M,64417659521832441314743688964M,16529003266006283.714560M,4222721074486377234175492M,771353371174351932666349060M,2953526668055.8296512386696194M,298259094666317571836746178.87M,155517682668281958297.56461065M,51663612533221098345924297M,5261245222303099742364905491M,67195572845835.138389368111375M,31626070190614.24668722530063M,34983895368884097985810530305M,3094850340905679284330364.928M,945370730673130577386999294M,3692856879140547848048424M,1237941481809116857877764919.2M,1237941481807182195055250636.8M,5360907035374108910067017320.2M,123794019572.3027344962819587M,592857227573877906.6716740100M,3039254346024.2170289354639874M,5267381145996343053348012856M,786553395336792396464285.41466M,101439366237355774051085M,1589888154183106285.6355576578M,22953875717614584857557475595M,22208439542981504532647809.23M,36928552817117379492577.60M,123794148180906826721.15351570M,1261363161507878070447505430M,303538579121246525060106182.35M,316792143863527453865003.24098M,30948502855634931880784.09480M,133078554436477379119037095.03M,1888946662637.0016645888M,30948505489348272946368353.45M,30747401956956.476304659970M,78957391538656.555590155373056M,2179697975136.320695736860927M,151233283504427503722572M,152518204718075529358079M,151264528656660074987523M,345994573263059206444831078.4M,1264739712197991093992687633M,3227835266473594532578.6589260M,13310486186361329177998851587M,35786093207593761319946.1383M,247853412290.0157501430040066M,8973705243272222147630598186M,35590776147910451763550945.280M,5575565881106099747436888090M,47961755841971646852.887M,78918677506770235509283.368202M,64339113687617716.2340664927M,328066199668191481334679.52251M,3117925852899.286141828830090M,57406992918412227668011M,94537198872920133.0046898984M,16252302104930705608960M,5740699291841222764417.9M,34983895368884113374477942282M,3692853768173524225163.264M,16547526863721606162454M,624173123034308450405758105.4M,36928544214880083199288.49M,12585012981105516135.41187594M,260183597349199674519791621M,1065110870748738701314167296M,433039117532.9330930002362626M,1545022998475704254281023.494M,187748068733152648.5093646504M,71246768766229569215492.33519M,175058499553481814.93482536M,936997974899020593653416.960M,438114728197224729808830911.3M,7157336.8710227731220994M,1873827073032707323362940426M,14907326024581130355434591016M,27250478840738142362.511375106M,1578876009885024229503732.753M,15788760098850242305582.08346M,404391861421090586.1916433811M,2785981836833466956113845916.2M,712428015482087328587039463.6M,3094850285564059423760257.880M,350588638728.605585201168155M,123794030654.7843258541671682M,38000957019869418115367938M,9728245033691846872864661258M,123794019572.2991267993291266M,123794021416977123390378191.0M,7122990929924594843590.329128M,7887999187828741735432218295.2M,4724676828.211487703043M,8490389942960801155056147059M,38695146123192767247419904M,21143683003602751323111427M,39894256899412776459638276M,1905366667602544189987885056M,13623466043226817455316534784M,3094850714955524911481032963M,9913258756307798539587.032623M,2234366507934808572925315924.0M,3762205798434569322893.084179M,433045822989.1574213028350466M,4160589338862260568784897M,78616526441843897856864103169M,2189101129856.832404738539789M,272625053642184.91220864795413M,14511832273916010880828.435M,588142411365.2965496808573187M,1564430290813227786002366472M,5986709346947746289347089174M,49507025511896481.08735499025M,1034721497955.1899958484229890M,1066154606000.5746042017548802M,2953847613.7341847135193602M,1237940306403490597465.491968M,3410370372534.424998848233507M,6814729313762566165680753.686M,13005623967713674494952538635M,4035394387706320156579756802M,328066199668191514320028353.23M,235257768050872445805642082.23M,78918677506751473721972621312M,3097040944249422643981702743.4M,23525778485470.670720031746623M,68293262051467047856441917.76M,618970611528606713232031744M,43884007805413714044593884.16M,133102642352681956.03690095640M,13310264235268084674481885453M,9671642471651910209871.0M,2586562135730870064926792320.4M,217123077292777369687348326.8M,185691026789599.42820698323258M,44814974580446.51425832632320M,404637883304.4499001051186178M,4046696671333041911242230801M,133102642354122005702378.90333M,214277840700936280240559617M,50513780982677791626636231168M,1750585110.8255757957928963M,13879072133571973156373504M,1360060473714107873043683076M,71230586857225855254370.77349M,74428373949187207.06214914M,123794025106321435750596761.6M,123794030640344872149.8668377M,40438719563394951693016856.15M,3965280485028996224.5941103889M,433762584131.6316452265678851M,1550328386977252121101749504.4M,451092296661319004215466265.8M,27337586100253228139746033156M,8416645578364746.349435694342M,47246737421107888.14606M,472467458665.9656391449M,1237940361743773336064625.753M,156235256124947295113281588.2M,18742883675847493706742512.78M,4336494103277123578342932224M,3458765421175963026678351878M,49797639010654071078842542.11M,14701973961953236754435M,12490290412418742353890.5M,6814799921551348527748.420362M,13938914700888.514898845237275M,30948507518490121054419031.28M,1310477505078954.3989559825922M,9914851245205004771486598161M,123794041708.3930976585449219M,13336962088593947066.470760224M,643385115894825674281780258M,1905366668971618729658418.436M,776020101866.5095572733953538M,57408073782331386521643M,156434200837611359.2147120936M,16252302104930706592000M,5740807378233138650165.1M,34983895368884113374511496714M,6110705407402782574575.616M,16548607727632175081494M,655121624016442957278249318.2M,61107071415976699126807.21M,52636917036543561368.49571850M,79086492110506.404173622545926M,2808334734305.051013023139603M,5570730216431666031739147029M,3094850733069001661103.50865M,9109327200176353567496605459M,5284313927231835263231658496M,22343490039147.255804525882630M,43304392298144.21196940250893M,36147822949233353396427163.1M,4330514768799057466129.263660M,3094850494460975051918802959M,9905600354868127954569821.39M,283866173806355389753387701.5M,724027093889528908421949620.2M,5592906834355149767524549465M,52806795685933.71793709928710M,31262831604523728525327401744M,1873920361388306860653086.053M,4481497754589831125717624853M,5280668444774383218523702272M,1239761937598923180986795239.1M,5630725375403288559.616M,181999635314926817674567.85M,46906324578141812566.84896270M,114858810.5345356600314370M,837911207941921551161583155.2M,436424109827712984846.2449683M,38069202506294160071370824196M,380694901456743156093.04989696M,345706304797373904830699930.4M,3807001945717984788263312361.2M,80889919729819851958.0983304M,68317297160728896.01474166798M,57407713499915649487891M,4949276439682.0329661297M,36146882020892452650608666.0M,29829168083900341306871.057918M,25968477756765277181513214M,26103236297720397125003468810M,53601419418244796804895282193M,6191212677453089472050650.83M,8574047.0692111588600578M,104629936623591417322506M,1145215429054677538.2409876859M,5880224631347621982760142155M,5880219908981047543.171457579M,435340834639873489974611738.5M,55707301361614721183044177.97M,972220683170135065779182413.2M,264227333120427661520950641.62M,309484993682981677067.172610M,588021596187816857.1384597087M,6189700196426901390769526.24M,9052794994426679895523584M,92325954093314889352192M,4363738645860564040049020326.1M,38695144747620600211570706M,123794023276030174202357.3504M,3868683515468035879560702361.8M,23635462693506785476870.144M,204260106512363193856791.02208M,14507109835375554391523.914M,6914111193.2812756862137858M,1083197540485.1917150872404739M,108319753947638539997895.72222M,1977802640889660217890534.004M,46484730.4749394457370626M,971464590.6954305348305413M,1844.6768263821130242M,368934881487075934227M,3094850124874760481.44891904M,1416709.9453006982153731M,14167099451907470524672M,897539585090.5153432411177221M,35064106092575752416460810M,464238730355229292848152582M,1858723462861655693716.521216M,3097277436307003628450783232M,12099035137420061973676041M,72606384997.81744812622496M,464232239865573876365.7878784M,2475927329906173748936015872M,7083657810790014648704M,120894426664017786096859.4M,4951774324745395956541841664M,1516480798405.7495554954924291M,8028440.9549238453679619M,1449771986618133113750272M,55361624319378784332M,553627502192657.04016M,2723468086449463118887125.0688M,24758800789382642849474937600M,118474730323078487778263043M,106385472126932170261397507M,928936710845291209997418.496M,70835497.2430509193318403M,4487532642409931426436533478.4M,11371589102.06476795M,80575378395485542809734M,23668500884224445104817664M,2517965815.8750434261591555M,3271655506.5420277520303619M,1744442185.9833047445702147M,773713469033.8657756536935171M,34974748149391141469144746240M,159583557747446242827763724M,18739274386242678168892220.1M,371461824210718812089732.0192M,11142381239748367599179819776M,43525240321423513253380597M,30225580532078210.501050743M,4611338453832330678.4034423809M,3713938192799012577530141440M,79305742221345438468046870.4M,7870129406893.33755468906584M,4335991902622880070.037932288M,5570956851024132734646754305M,35784722614354945125416962.8M,235746382452014000557.522953M,4240036724035113632512615680.0M,2785988466276242597561365760M,1209848283482092830065076M,3094883671670470935807293.44M,615884047709478.50558167408129M,1547425092917746916730750976M,1208935044112591706129006M,0.0001099511652620M,43.2345564227592967M,43.2345564227601929M,2011770623339.4853798196983049M,3804321138.17088M,15536455536805590460904923.504M,36033589280174829503188129.024M,3355540504209422469676472735.0M,3602269794036018329396126.8837M,34177912319597567.130447472485M,12143272909165592059552.8769M,3447186055669601637228.0357989M,32267440921618594532801.409633M,313830266826231524058483.55699M,33546979715547745160420.217344M,3139876669721360367488797.1144M,3138250722238127083131.4644589M,3076161481162450084804803.0559M,3444637415022462359761.6976997M,31877435045391372422499.356773M,341780398426573915086105.77509M,33542052996034888840318772585M,26112138442911583587641.091445M,3187743508056478011291109976.2M,3016202544044858187553.3837413M,360361201448608336749510657.01M,26760032606229903205854.442101M,3140116484905731219375819710.0M,313987191602746948819545.88416M,3444644941358389612944813.6047M,3262700197782319267073.2838262M,34170748378952460834104632.165M,3201041621960170474792.7039593M,12982444761469072636438.1288M,31877468027012803044733.184084M,313987666972136124411822.32436M,310711520810712260478129.27597M,29541790187163572685524005743M,13350796813538043558051.5145M,354158124592220575635921.56024M,26306747406510776904024.548707M,318774775832949.88141070610276M,34472998834298055098504.279141M,326220528472504.32255307641700M,1382965207356333408170364.03M,1383438730995.97218747216995M,35403909529263665263016.114515M,36309097313169683731.196179299M,33551849195523291695037.379694M,313510754087475458422250.75058M,34480318409826817143959.875437M,326366216061036.21515465588851M,22405538898162455176553.918063M,360204237168713991424671.37644M,30158260183278301584883.533568M,34185320827884913166.836984172M,1407608035591088148783.43284M,34131604886095200089946747.503M,3015840305223622418209.6589929M,36039785567034223346.955151104M,23886472953404811397956.199755M,55804063060678991123161.04557M,2806016015621572037493916421M,991322964792449076762090216.1M,2485585132786306451426316546M,18605415610600934342.33096200M,2475880115464823827799214109M,943042530426256773200.611604M,1859365800326307444641176838M,343942234757280169930.4310275M,3414011237247025342203437062M,248918829797558554749.5803668M,1864201596270248036212409096M,16115793029642894524.20827144M,2417889186872982719496736M,2485556265141933363647619077M,32044601024516168264129050.7M,187021313256190647137730638.1M,23045169190688602066059808M,387046448298187679.47079684M,9285802036866454928443324689M,4043908850257718846549463317M,4043894793909675114596009741M,52867081856383778982.28318984M,32044600989078576087282509.7M,3421293661971893690952910.848M,2806016238054638077860121360M,218219153271523084428221160.1M,928596702628412999112591624M,528667053618950558.5788684033M,13942621905926342334770905353M,526249690919179921647.0622749M,2485589301533167310922126605M,24916765421760064462.99285512M,2259552515393288M
//>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>  END ENCODED ASSEMBLY  <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
        }) {
            //Get the bits of the decimal
            var bits = decimal.GetBits(dec);

            //Skip forward if the highest scalar bit is set
            dynamic idx = bits[3] >> 16; //16 for skip tokens, <16 otherwise
            if(idx == 16) asmBufOff += (byte) bits[0];
            else {
                //Accumulate two 4 bit scales, then add to the buffer
                //Note that for even parity tokens, the byte we write here is immediately overwritten again 
                scaleAccum <<= 4;
                TinyBot_asmBuf[asmBufOff++] = scaleAccum |= idx;
                asmBufOff -= scaleParity ^= 1;
            }

            //Add the 88/96 bits of the integer number to the buffer
            idx >>= 4; //1 for skip tokens, 0 otherwise
            while(idx < 12) TinyBot_asmBuf[asmBufOff++] = (byte) (bits[idx / 4] >> idx++ * 8);
        }

        //Load the tiny bot from the assembly
        //We can't just load it and be done with it, because the byte[] overload doesn't add the assembly to the regular load path
        //As such load it whenever any assembly fails to load >:)
        System.ResolveEventHandler asmResolveCB = (_, _) => CurrentDomain.Load(TinyBot_asmBuf); //We can't use a dynamic variable for the callback because we need it to be a delegate type
        CurrentDomain.AssemblyResolve += asmResolveCB;
        TinyBot_asmBuf = CurrentDomain.CreateInstanceAndUnwrap(ToString(), "e");
        CurrentDomain.AssemblyResolve -= asmResolveCB;
    }

    public Move Think(Board board, Timer timer) => TinyBot_asmBuf.Think(board, timer);
}