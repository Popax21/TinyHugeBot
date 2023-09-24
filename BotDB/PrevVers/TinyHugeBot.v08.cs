using ChessChallenge.API;
using static System.AppDomain;

class MyBot : IChessBot {
    //TinyBot_asmBuf either holds the TinyBot IChessBot instance, or the assembly buffer during decoding
    dynamic TinyBot_asmBuf = new byte[8704];
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
            73786.976307732568653M,19807040628566.084401472995583M,31105283551.3167525259001891M,641562523904M,142482664737278754160864M,7737125245.5336267181203469M,73786978493861462528M,4110347786.6897391940011012M,41277563240262130762296328704M,7555786.3725983042899970M,1152921504606846992M,0.0000309237645936M,3647555871.3813200077598212M,9444733000923662516736M,0.0000412316868620M,2997758279.3546677029456025M,1099512111104M,944473.2966838802055477M,113640539676789186605613060M,1136405397032481866098.93458M,253782147137085114486609914.88M,19188552296141429730353110528M,128148110699625849560957030M,67701450782042557441.900638M,294014159497557021822487301.12M,28782648991943840730045240576M,124520835189950760495743046M,1051783725586247621517.64045M,210454198520417610646.23298048M,31877461311153407149993917952M,119685463949226554786644067M,107597865954980.428919799919M,464234456726761390344870574.08M,55088624547603614351900051968M,304651944502443839004344500M,27322107222350963871.7636785M,2537821471370871705715087.8464M,25378214713708511448660991488M,99133651225423872846331998M,337294546512565667927425118M,21678542986558909054515404.81M,73968371851815038432705592833M,333669687502439385779208476M,311908819832376628912521.552M,287834187549025.03676482481409M,30330905196791160740551661569M,39169674335220953191297871.0M,423129515647471950801207.559M,89767559134934287804300935.69M,8667252011756163248938895617M,36631142251080480030870768.4M,3179541130211177276786610.14M,451861856695127222499557378.57M,47661976029749627819107885569M,35784676504672353002697549.4M,43521844180785433371063118.5M,70873578424881718928485748481M,3715251012410718361038662144M,30707158547106395425898524.8M,331251798944670299058.733378M,2971193989865023815.5103477761M,27236059818782448609237475329M,46060588402173198717793103.2M,4666509189459230106822249.30M,8976788968041207229749235201M,15785530022130072971064212481M,35059555287566686808375333.9M,36268471883555937475.6905335M,3744912181654487316.1211065857M,32497286099377874136629064449M,50170991529207758909564547.8M,4497260681602269582219021.30M,10214903736976105.217208707841M,12071804350955098680770795521M,36389336795570193283357123.1M,367519850265514650902.790483M,39305937429944701842414968065M,34973142563738307639365412097M,41104010987033624220080565.2M,43159168279547318235267101.0M,93071235768096041391579392.1M,75207143048498302853440466434M,61172445230367390424288504.8M,62139650450226.3042778202573M,46425117412749357.855369459457M,63446844897585559411227753729M,57666693169704552323796223.5M,533145436160089080337007092M,1240187923201686407324222.210M,76754563375166462444546680322M,62260539342951540726045127.0M,621397113245099147739857380M,66851189450713612175836053762M,311935954271494870524888833M,6504114804923361682321577.01M,615352889972862374453772795M,7830233788061797.5107165492994M,930915419872112254042382337M,6407400739354191477214417.89M,633486094743.180717501383184M,73971181718023047.465924691969M,74899589524615324024501931011M,1178719571621624972854166.435M,12524639912843590987.95950975M,4488023775153134172.1955992323M,47046495870499824781794475523M,1138826993363855145452766.211M,1143662991790781847294772220M,76756834868969609194341.903620M,78304274086905236872050752259M,1206525658690638217675539.402M,121982386114948.3964593210283M,696389582610439735989231.94371M,53855151920334643421304324611M,1188393523826691457426719.752M,11424548591817.25530859439161M,623541343897905022.361638659M,8979603556976454402593509636M,123916606551738580476880.5830M,1310493352968694.673176396734M,58188234846717201694958300.419M,72729038696330.450939479585027M,79212445980539.376163421028298M,79195521554016127393670365208M,6592037320046557929242.4664320M,2785341477208215113928019711M,79202775035155282981448515567M,7919189518238537168284300084.3M,73966690673133803243269255424M,7736941074826033283437953023M,12088649460347069796188114M,27804943361.028676979654605M,7149116477046405132057995.4943M,76442816312744.119754815447551M,79208820125426.180245037776906M,265971981366940988266250.39M,5570730175919551.994663004672M,5570692399149377276237310976M,14506574886552648754331644M,79206402163096.072000351174591M,61904557512850445248233036.7M,54159437532815.796341280080896M,79130237807305.553902853619618M,7913023780730555390.2853619618M,15467544538986014403680862.71M,9593057757305.756713765261311M,79002092002411.783115513921420M,79034732317040557664692.535207M,6034875049338952.0714403375871M,52302414136155.269586355179263M,79115730734357.192554235690901M,79162878490846973488.957095838M,526120124830604877781993.84319M,49517171829940.278706072165887M,79107268511876.277691467694023M,7911573086348721593.5934758834M,649914506563593453.66761449471M,61277554981215.929485009851903M,79084298626153.442538940137368M,7913023780730555393.2919832480M,504456740824926884533475530.23M,50445674082492.688453347553023M,78905373749442.403338377363286M,789222998730599138.21440966368M,671570429088274.01906326284030M,47349893657977.852389102645246M,788425104553442.41004912967408M,7877722838728315.6556587138843M,7458369144642509.6641333587710M,77060223214452.840944623314686M,788630597408722.67942068551326M,78879985347986011340.342951562M,49516307618499034662.815332862M,48587748695242.946788099807230M,788497638995856.86337803189974M,7880986903393812.4852508032766M,779885176807182996957.46555134M,77988886028114.260273624170494M,788920749566747.98941438607035M,7894285017314507329037428290.9M,646811533844427589286.67706623M,60348339632949.753911252547326M,787832726105700.30226227003161M,7881833134866064.1533499145964M,742747211745504.38240040435198M,64680733092816.811187909864958M,788860287042581.80497549819552M,78867896200472863143601.372801M,349704129825879.52766969110526M,46421353623827.403625067371006M,787651382989911.26613362998995M,78743377541995335.668872642251M,6870398155166417.6872386628862M,60966705189610.448298528638718M,788739403499488.34720181714545M,788739402577213068526.62247055M,3744622694990143959207397.3758M,44564514402342.145590608975102M,787881081847086.13586714689253M,787482136695534.11501996179160M,69632422415830237405937.308414M,69632469638198.016215500104190M,788908658832767.28157319200415M,78588631384595.558627978509827M,1544955216487676357157712381M,1235474927735.785162312382718M,785559901292067.61507953180147M,78599511514063.253521481924099M,7736888179003548.8640704850173M,1235479649237.602192320233469M,786055573790914.90850526854630M,78609183234208627491600203310M,173287521093655433.61251770110M,14852843697892.920088432869118M,786611669706760.57637929680376M,78707106262510885239619714566M,61680241696540460318289.0238M,2783140817821.411214710677758M,786176464897145.29235226394180M,7862127299046996006696864927.1M,170192765488168056510.34278398M,5877787855861.324894806346238M,786974348928390.12091454291457M,7799020539301359387793778.2268M,191829562167019018605808770.52M,21349417399807.022530521977852M,78059112449195967.238208355351M,780675737862244.40924536044542M,5256924128779784809147546.363M,70248450346461.711866219221756M,779212961047656132.70528162857M,7800954763427807826487404.0399M,15159849426254982964385157116M,33110102778934.758836769915388M,78040978654135749.515260263449M,78050649820880771444308048897M,34000471253362481670733337.56M,78604734507234.095528775797244M,780325175384363982.42933636167M,77987787928745010835178.912878M,386808046257714555122.44385788M,36204995383418.531539958103804M,78103842889006881.967847701512M,7814736486414257744960139.9832M,464236487113046863155667200.0M,78918606671395.719772997947904M,1209165619.124890382893052M,108800187841.34149195497455M,75823969075566.746144371377407M,72419487574675.316418411497983M,79174969685961.944131968237584M,967173859465128425376970.6M,959390780258424589504845.6704M,11450822582942040559162746368M,32641070916852834844671997M,544011638337.97543532691435M,5880281300529007189.674697215M,72729199258448.176678537530880M,79219700328670.436902188941356M,14506980716048329183133723M,52611790570026258383.96472320M,4642360149340413529117686784M,65282400084182047645237301M,7919914855285588494918615041.1M,6327975545817870.27436032M,1145215428982556.1755351253248M,1813573196863952235003908M,39626170515504870401008141312M,30948501618583547581680844.80M,39626170515411636079672283173M,39055760794746205638293192.704M,34055440338843600949111422979M,78918678095317240.574914855550M,1208909681073185877.880575M,218010924579800053.4212862047M,154889041056894334.43837034527M,618970038593841.973948317738M,6933315681121.7356870652989698M,6820806697932663299151681822M,38517378384.625498379832M,2833759901409250709564715274M,78621281753549628700408809984M,78661385860598329839408120589M,433083537515942.8273528111107M,4680207004567.816439738138627M,97226364310040.01870257586957M,2005618120443303394284364328M,945373264219547839168644096M,27550291263778281122104.813568M,526738114599634341.0558116895M,5085969812587344943423560197M,6810082203648532623376845312M,29829168083900.336908823367166M,872722667.88733953508109M,1258510667686866022.875488266M,286527700540351372139295.13502M,1905272091360234862596679278M,1397876323564790573107712M,14572929278463594605312M,24702772858877832871346.176M,1237940214169770228917.733888M,1861745763105.843721778757653M,73483352450237693165344M,991122446774616088396248039.5M,999543308285.6123975607189251M,3872371933.6104979196805890M,24178539473428457455618.56M,77891854621918.53503062216974M,6166561152915580349426675.43M,3614782294903610.24484476684M,322783525172399646937073042.14M,681964263459253968.2355019809M,26048795822835257178508888595M,68256770827494490872007909.07M,5321894816222175238017712638M,4972727723809099288.452006526M,5986647882403271792452966184M,5987299846898374484.856539665M,528067946392354933546313754.6M,15508171564194664142058146349M,4679160633392910060646970615.1M,3219740181986995212003941556.2M,342494387337634888345039950.7M,970084743205685286267055975.4M,5085969812587344940099656472M,3729616473761747818387409408M,4961630050822864617241379592M,30948504880418678389506051.2M,120180537641317778325209M,818090969239969644.0953864717M,93108557206.2441104573860354M,9911224467746160883962480426M,123794028795.6722460618587907M,13328499607856644662247.816992M,945372467770622021611489539M,3094850377799730304312152593M,2524322427547546434277675281M,945370898063187437549586944M,369285441669189054929740.8M,774926172740147408348.907560M,309485024867.5899501048627458M,71181696511009685084821962.41M,55941157611843393578.34674203M,154863397497070043044.01764881M,18569100987052741.67263236873M,15679912871810167922851233.50M,6263133179702991939.88743176M,3094850622722385248063064443M,18569101171754015048819929.64M,3436016999910418590622541910.9M,1547911957323850.5774850375680M,4387239023049.176701794647664M,133102642357730.47084129715725M,71573368710228855232001M,3060330609164.0618685459M,7798875233875538737791048196M,7254549815461864632148312.27M,345706304797403362286685554.7M,3807003362427732613666701902.9M,21876885894058736413761863.74M,156800075670827355.8693873425M,79228158755670989731055272722M,1237940058812995062858776.618M,3869047144946.6544573951119618M,960612456485081937247154996.2M,309485065961529613469846.6651M,12406015650353080167969796639M,9769160280982280250942423.04M,381609668.1502781227543298M,2554779514645399434439424M,30766217636177351251253505M,309485058583338316826588211.2M,38695164569645457674.671628M,5597345747880817410713841664M,117735682369392151874534.12648M,10037729985726648137909273873M,15474248401406042426954877440M,9598951402037536279716956.174M,5261250147451.801610912942130M,2797454347680385815024248.838M,3094850248671414580016971.282M,5986709385074403273506554481M,15486339749707004295811.827217M,1300691319191075044182137722.4M,31069393559767414164161101.2M,5273337138776078275791814668M,6044629388273873438572800M,1237940232616559382652.391680M,1917375239377.964823096590368M,1237940214169858138271.916403M,11461223380.026563602939924M,15087998529647354382844928M,4815804986178777459283985924M,2953042627336.0099491810312450M,133344668128746239531837.11238M,14552337699278552097.307446279M,328054252081711166871932.16515M,6245310802575.953635029422953M,9920525817556924348311277073M,2171340181331384054904259328M,32278352664735945325786589260M,13334466789137887554243527.422M,1237940287956705807818299137M,23529717905219789295712.206624M,12384302283266745768784243304M,8973705243272222148317806.592M,472467682821148.7703042M,5575565880745811772952281.113M,7442487770342151169185282M,41627734333482641317702922M,4642272985601408366302530048M,197226328655838330098857.8392M,4172012820859727557563514912M,45661578787087345948440265728M,7743274677004385718033387.283M,123976193575655741343433364.66M,590739319950181890302935040.5M,38517959332493579413547M,4330741572314873275601594378M,115299356220438809192.96M,9860597589118356324160257M,34983895368595882998309453322M,635885554831076033405386754M,11825592726416188254230M,1331994468247082357380703795.3M,248392903422554044255.8481M,4343680155031642114831156776M,126211854723345876721546.0904M,5906840239324957191434474262M,986043156900217498.0667704M,2934072557880999097717033472M,13623465525934936954030326784M,4440493249386118708858126882M,280881174866002351879214.4919M,1240462650274986589218.343185M,123915762786185855193729.85603M,44770394536680561311749M,1516968163200306618047339.1121M,18322930825946291333828878M,2805916827411472365147738890M,3143207279580231810508603140M,52683903341699103787520M,4486672.286505299219978M,59081058342620.97836359288845M,12549878560755231143464.25107M,783444484849901.8360689425M,2005619004161092361356182528M,5900819676134829796343022592M,2005621072432669297384577047M,2934075306378411059451659264M,373689380477.7535758780007426M,34983895368595872076207901995M,52660808708524091146355998.72M,78635794378229649536654454027M,433082744284330.3796340686861M,3094850439120742830739.816461M,3734770160566105029656711823M,38069891951912614345823180823M,944704082859889.4460928M,309485035935018033688910263.5M,38078745467457827.877202845794M,23256110714298328495696117778M,133151987023428155804027.68389M,187987966794752762931374109.0M,64076405506563084192.2192659M,5270916573391781399497803304M,2982912125525673654669865.3393M,38069477238578735864057176070M,4282427989750293.6549747326976M,26448757303708245227.61486M,483629371838406348033228.8M,6238057233886796899776954.88M,9052794994425580383896576M,87603587610445244138496M,4363859531063827873482670297.3M,38695144747623898746454031M,1237940288100533963169005824M,1208949043080600597037.098M,1491715380585.6252695343533060M,1475740370321694275.889M,15347691208015.135900400M,1395149097435294583.44656898M,3350433544762.7164393947912195M,9444732965739290452591M,1578612501832893938086094600.7M,350488137404776453632M,309485010253690633170452.480M,2361183.2415447737704963M,4722366485068668470272M,12884901891M,444884720066616459861688321M,309498705188549648.5216849152M,3096587969791693945672156160M,7260343378898146020885078M,726253863686293458.4655881M,495176488215973607423125.38114M,2166461183970099870560165120M,6479843500974885357542444.64M,967151734096602802.1465559M,45184811461586900765030483457M,45807070.1760959720980741M,44875326425247958668824024320M,1423631045545.6818154090536195M,71326623362611360613531667M,82206955820207893787967795M,928813929316736598953426.944M,1417286.4056132020227075M,14173440516884323652608M,67884038457449473310811M,1807721896.8483703213822722M,1938059211.7760115553109507M,2317737476.9999404932826627M,2459408471.4868000916891139M,2952423532.2993800286078467M,773713469033.8696238621820419M,34354998934883252518403459328M,151126278995068785007394836M,1825538308550029704303739.00M,1114225373700625162313097.4720M,5262071598247094431776859904M,20555040953555258627260810M,20555040948.488709046730901M,68396267463357849.195388123136M,29711094576603433145215269888M,7676683566731511002159515.53M,701179355072.335900831187174M,4982924942710359348471660851.2M,2476319260671557137600616704M,33125106103071596831473666.5M,3844449408076542924111872.33M,773732361792843594821596876.8M,928455250824965193136689664M,124525871215541619246235786M,4835703442840568827.215989M,4722402516064794574850M,0.0006597069791499M,7.2057594037962247M,43.2345564227592969M,1949854731851.6388122635438601M,3650389174.70208M,15536455536805590460904923.504M,36033589280174829503188129.024M,3355540504209422469676472735.0M,3602269794036018329396126.8837M,34177912319597567.130447472485M,12143272909165592059552.8769M,3447186055669601637228.0357989M,32267440921618594532801.409633M,313830266826231524058483.55699M,33546979715547745160420.217344M,3139876669721360367488797.1144M,3138250722238127083131.4644589M,3076161481162450084804803.0559M,3444637415022462359761.6976997M,31877435045391372422499.356773M,341780398426573915086105.77509M,33542052996034888840318772585M,26112138442911583587641.091445M,3187743508056478011291109976.2M,31396358200190555733337535589M,31399942123995644242060141927M,3138250722303143510774.1289728M,3444644911779631378911797.4879M,122660800194437234911.372662M,3447299883429805509850.4279141M,32010435736972171437334029422M,22592898990347726822357362023M,32622051003003854783704.353139M,227078033348767.95510800646251M,2259292232641715818101.9805044M,32635478166150355336679155.026M,2489817374854444747217.9571060M,313782376049320704190373840.47M,32589464091296553223905.178736M,30150937913088582.223215683948M,34181722698505316124322001776M,138343873084628956854708564M,31398766697213617981691.749166M,3754802915994971.8476060708398M,313902569025562186601058438.27M,366575272006924.27680585183043M,2150128610709661058057.7215337M,360275100348411414925.01603700M,2537831591466574952532.6819407M,357290727057515854481112.07534M,1407136638416564363367.74912M,27978691648374488563015775.333M,32267783792477089062754.153061M,225929556224207831600025.07635M,33542144861107990203067.622510M,29541790187166653555.723560314M,2568782909007431692276.3243354M,146820429423801036.035159155M,1568075976336191762594792192M,991322964792449076762090113.7M,2485575687909225558419509506M,25109673169508070262.78074376M,6196991530277015065434785288M,8982333229423743508.557201410M,557799813990774945947359719.1M,557194854784432290017837209.8M,15474250860007886216440753.05M,9444879636222588748034M,2485556265141933363647619077M,52624780197244548.52563240709M,18702130587749301765390999.13M,2475970098762911677150068994M,38700698442590654524563459M,528663736858338100586.2182916M,3100900003153812664868536581M,24891830784176214094219225.71M,657684182992450832000684296M,34069041684300836.45326373137M,5262478019727258581847443464M,1557096806225263528.335378690M,248925402495171031039010435.2M,24868407099893412435266.77777M,3414044441531571593768601873M,931961390274571844862.1949192M,4962645396570120618371980800M,217609968009896973388.2513416M,1568076161806538863201030408M,248556107808790045438122625.7M,5270949778312428104658192657M,0.000002055M
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