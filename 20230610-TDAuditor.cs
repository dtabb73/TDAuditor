using System;
using System.IO;
using System.Data;
using System.Text;
using System.Xml;
using System.Globalization;

namespace TDAuditor {

    class Program {

	static void Main(string[] args)
	{
	    // Use periods to separate decimals
	    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
	    Console.WriteLine("TDAuditor: Quality metrics for top-down proteomes");
	    Console.WriteLine("David L. Tabb, for the Laboratory of Julia Chamot-Rooke, Institut Pasteur");
	    Console.WriteLine("beta version 20230817");

	    /*
	      TODO: Would like to have the following:
	      -What is the max resolution seen for MS scans?
	      -What is the max resolution seen for MSn scans?
	    */
	    
	    string CWD = Directory.GetCurrentDirectory();
	    string mzMLPattern = "*.mzML";
	    string msAlignPattern = "*ms2.msalign";
	    string[] mzMLs = Directory.GetFiles(CWD, mzMLPattern);
	    string[] msAligns = Directory.GetFiles(CWD, msAlignPattern);
	    LCMSMSExperiment Raws = new LCMSMSExperiment();
	    LCMSMSExperiment RawsRunner = Raws;
	    string   Basename;

	    Console.WriteLine("\nImporting from mzML files...");
	    foreach (string current in mzMLs)
	    {
		Basename = Path.GetFileNameWithoutExtension(current);
		Console.WriteLine("\tReading mzML {0}",Basename);
		RawsRunner.Next = new LCMSMSExperiment();
		string    FileSpec = Path.Combine(CWD, current);
		XmlReader XMLfile = XmlReader.Create(FileSpec);
		RawsRunner = RawsRunner.Next;
		RawsRunner.SourceFile = Basename;
		RawsRunner.ReadFromMZML(XMLfile);
		RawsRunner.ParseScanNumbers();
	    }
	    // TODO: The following will run into a problem if user has created a conjoint ms2.msalign file in TopPIC
	    Console.WriteLine("\nImporting from msAlign files...");
	    foreach (string current in msAligns)
	    {
		Basename = Path.GetFileNameWithoutExtension(current);
		Console.WriteLine("\tReading msAlign {0}",Basename);
		string SourcemzML = SniffMSAlignForSource(current);
		LCMSMSExperiment CorrespondingRaw = Raws.Find(SourcemzML);
		if (CorrespondingRaw == null) {
		    Console.Error.WriteLine("\tWARNING: {0} could not be matched to an mzML title.",Basename);
		}
		else {
		    CorrespondingRaw.ReadFromMSAlign(current);
		}
	    }
	    /*
	      TODO: we should really check to see if any of the mzMLs
	      lack corresponding msAlign files.
	     */
	    Console.WriteLine("\nSeeking MS/MS scan pairs with many shared masses...");
	    Raws.FindSimilarSpectraWithinRaw();
	    Console.WriteLine("\nGenerating sequence tags...");
	    Raws.GenerateSequenceTags();
	    Console.WriteLine("\nWriting TDAuditor-byRun and TDAuditor-byMSn TSV reports...");
	    Raws.WriteTextQCReport();
	}
	
	static string SniffMSAlignForSource(string PathAndFile)
	{
	    using (StreamReader msAlign = new StreamReader(PathAndFile))
	    {
		string LineBuffer = msAlign.ReadLine();
		while (LineBuffer != null) {
		    if (LineBuffer.StartsWith("FILE_NAME="))
			return LineBuffer.Substring(10,LineBuffer.Length-15);
		    LineBuffer=msAlign.ReadLine();
		}
	    }
	    Console.Error.WriteLine("Could not determine source mzML from {0}.",PathAndFile);
	    Environment.Exit(1);
	    return "";
	}
    }

    class MSMSPeak {
	public double   Mass      = 0;
	public float    Intensity = 0;
	public int      OrigZ     = 0;
	public int      LongestTagStartingHere = 0;
	public MSMSPeak Next      = null;
	public MassGap  AALinks   = null;
    }

    class MassGap
    {
	public MSMSPeak NextPeak = null;
	public double   ExpectedMass = 0;
	public MassGap  Next = null;

	public int DepthTagSearch()
	{
	    if (NextPeak.AALinks == null)
	    {
		return 1;
	    }
	    else
	    {
		MassGap MGRunner = NextPeak.AALinks;
		if (NextPeak.LongestTagStartingHere == 0) {
		    int BestTagLength = 0;
		    while (MGRunner != null)
		    {
			int ThisTagLength = MGRunner.DepthTagSearch();
			if (ThisTagLength > BestTagLength) BestTagLength = ThisTagLength;
			MGRunner = MGRunner.Next;
		    }
		    NextPeak.LongestTagStartingHere = BestTagLength+1;
		}
		return NextPeak.LongestTagStartingHere;
	    }
	}
    }
    
    class SimilarityLink
    {
	public ScanMetrics    Other=null;
	public double         Score=0;
	public SimilarityLink Next=null;
    }
    
    class ScanMetrics {
	public string NativeID="";
	public float  ScanStartTime=0;
	public string mzMLDissociation="";
	//TODO Should I record what dissociation type msAlign reports?
	public int    mzMLPrecursorZ=0;
	public int    msAlignPrecursorZ=0;
	public double msAlignPrecursorMass=0;
	public int    mzMLPeakCount=0;
	public int    msAlignPeakCount=0;
	public double[] PeakMZs;
	public int    Degree=0;
	public int    ComponentNumber=0;
	public bool   Visited=false;
	public float  LongestTag=0;
	public ScanMetrics Next = null;
	public int    ScanNumber;
	public SimilarityLink SimilarScans = new SimilarityLink();
	public  static double FragmentTolerance = 0.02;
	private static double LowMZ = 400;
	private static double HighMZ = 2000;
	public  static int    NumberOfPossibleMasses = (int)(Math.Ceiling((HighMZ-LowMZ)/FragmentTolerance));
	public  static double[] LogFactorials;
	//TODO Allow users to specify alternative mass for Cys
	// Values from https://education.expasy.org/student_projects/isotopident/htdocs/aa-list.html
	public  static double[] AminoAcids = {57.02146,71.03711,87.03203,97.05276,99.06841,101.04768,103.00919,113.08406,114.04293,
					      115.02694,128.05858,128.09496,129.04259,131.04049,137.05891,147.06841,156.10111,
					      163.06333,186.07931};
	public  static string[] AminoAcidSymbols = {"G","A","S","P","V","T","C","L/I","N","D","Q","K","E","M","H","F","R","Y","W"};

	public static void ComputeLogFactorials()
	{
	    /*
	      We will compute MS/MS match log probabilities by the
	      hypergeometric distribution.  Therefore we will need log
	      factorials; this will be our lookup table.
	    */
	    LogFactorials = new double[NumberOfPossibleMasses+1];
	    double Accum = 0;
	    double ThisLog;
	    LogFactorials[0] = Math.Log(1);
	    for (int index = 1; index <= NumberOfPossibleMasses; index++)
	    {
		ThisLog = Math.Log(index);
		Accum += ThisLog;
		//Console.WriteLine("index = {0}, ThisLog = {1}, Accum = {2}",index,ThisLog,Accum);
		LogFactorials[index] = Accum;
	    }
	}

	public static double ComputeLogCombinationCount(int big, int little)
	{
	    // This code computes numbers of combinations on a log scale
	    double LogCombinationCount;
	    LogCombinationCount = LogFactorials[big]-(LogFactorials[little]+LogFactorials[big-little]);
	    return(LogCombinationCount);
	}
	
	public static double log1pexp(double x)
	{
	    // This function prevents failures when probabilities are very close to zero.
	    return x < Math.Log(Double.Epsilon) ? 0 : Math.Log(Math.Exp(x)+1);
	}

	public static double sum_log_prob(double a, double b)
	{
	    // This function adds together log probabilities efficiently.
	    return a>b ? a+log1pexp(b-a):  b+log1pexp(a-b);
	}
    
	public double TestForSimilarity (ScanMetrics Other)
	{
	    // Compare this MS/MS mass list to the other one; how many masses do the lists share?
	    int ThisOffset = 0;
	    int OtherOffset = 0;
	    double MassDiff;
	    double AbsMassDiff;
	    int MatchesSoFar = 0;
	    int CombA;
	    int CombB;
	    int CombC;
	    int CombD;
	    double NegLogProbability;
	    double LPSum;
	    while ((ThisOffset < this.msAlignPeakCount) && (OtherOffset < Other.msAlignPeakCount))
	    {
		MassDiff = this.PeakMZs[ThisOffset] - Other.PeakMZs[OtherOffset];
		AbsMassDiff = Math.Abs(MassDiff);
		if (AbsMassDiff < FragmentTolerance)
		{
		    MatchesSoFar++;
		    ThisOffset++;
		    OtherOffset++;
		}
		else
		{
		    if (MassDiff > 0)
		    {
			OtherOffset++;
		    }
		    else
		    {
			ThisOffset++;
		    }
		}
	    }
	    if (MatchesSoFar > 0)
	    {
		//Compute point hypergeometric probability
		CombA = this.msAlignPeakCount;
		CombB = MatchesSoFar;
		CombC = NumberOfPossibleMasses;
		CombD = Other.msAlignPeakCount;
		LPSum = (ComputeLogCombinationCount(CombA,CombB)+ComputeLogCombinationCount(CombC-CombA,CombD-CombB))-ComputeLogCombinationCount(CombC,CombD);
		int MostMatchesPossible = Math.Min(this.msAlignPeakCount, Other.msAlignPeakCount);
		/*
		  At the moment, LPSum equals the log probability of
		  matching exactly this many peaks.  We will now add
		  the probabilities for cases with more peaks matching
		  than this.
		 */
		for (int MoreMatches = MatchesSoFar +1; MoreMatches <= MostMatchesPossible; MoreMatches++)
		{
		    LPSum = sum_log_prob(LPSum, (ComputeLogCombinationCount(CombA,MoreMatches)+ComputeLogCombinationCount(CombC-CombA,CombD-MoreMatches))-ComputeLogCombinationCount(CombC,CombD));
		}
		NegLogProbability = -LPSum;
		return NegLogProbability;
	    }
	    else
	    {
		return 0;
	    }
	}

	public int ComponentRecurse(int ComponentLabel) {
	    if (Visited)
	    {
		return 0;
	    }
	    else
	    {
		Visited = true;
		ComponentNumber = ComponentLabel;
		int SizeContribution = 1;
		SimilarityLink SLRunner = SimilarScans.Next;
		while (SLRunner != null) {
		    SizeContribution += SLRunner.Other.ComponentRecurse(ComponentLabel);
		    SLRunner = SLRunner.Next;
		}
		return SizeContribution;
	    }
	}
    }

    class LCMSMSExperiment {
	// Fields read directly from file
	public string SourceFile="";
	public string Instrument="";
	public string SerialNumber="";
	public string StartTimeStamp="";
	public float  MaxScanStartTime=0;
	// Computed fields
	public int    mzMLMS1Count=0;
	public int    mzMLMSnCount=0;
	public int    msAlignMSnCount=0;
	// This next count includes only the MSn scans with zero peaks in their deconvolutions
	public int    msAlignMSnCount0=0;
	/*
	  The following metrics characterize the extent to which MSn
	  scans in this experiment share fragment ions.
	*/
	// What fraction of all possible MSn-MSn links were detected in deconvolved mass lists?
	public float  Redundancy=0;
	// What is the largest number of scans found to match fragments with a single MSn?
	public int    HighestDegree=0;
	// What is the largest set of MSn scans found to share fragments with each other?
	public int    LargestComponentSize=0;
	// Which component number is the biggest one?
	public int    LargestComponentIndex=0;
	// How many different components do these MSn separate into?
	public int    ComponentCount=0;
	// The following MSn counts reflect mzML information
	public int    mzMLHCDCount=0;
	public int    mzMLCIDCount=0;
	public int    mzMLETDCount=0;
	public int    mzMLECDCount=0;
	public int    mzMLEThcDCount=0;
	public int    mzMLETciDCount=0;
	// Charge state histograms
	public static int MaxZ=100;
	public static int MaxLength=50;
	public static int MaxPkCount=1000;
	public int[]  mzMLPrecursorZ        = new int[MaxZ+1];
	public int[]  msAlignPrecursorZ     = new int[MaxZ+1];
	public int[]  msAlignPeakCountDistn = new int[MaxPkCount+1];
	public int[]  LongestTagDistn       = new int[MaxLength+1];
	public int    mzMLPrecursorZMin=MaxZ;
	public int    mzMLPrecursorZMax=0;
	public int    msAlignPrecursorZMin=MaxZ;
	public int    msAlignPrecursorZMax=0;
	// Per-scan metrics
	public  ScanMetrics ScansTable = new ScanMetrics();
	private ScanMetrics ScansRunner;
	public  LCMSMSExperiment Next = null;
	private int    LastPeakCount = 0;
	private string LastNativeID = "";
	private bool   ThisScanIsCID = false;
	private bool   ThisScanIsHCD = false;
	private bool   ThisScanIsECD = false;
	private bool   ThisScanIsETD = false;
	private bool   NeedToFinalizeActivation = false;
	
	public void ReadFromMZML(XmlReader Xread)
	{
	    /*
	      Do not think of this code as a general-purpose mzML
	      reader.  It is intended to populate only the fields that
	      TDAuditor cares about.  For example, it entirely ignores
	      the array of m/z values and intensities stored for any
	      spectra.  This is intended to glean only the required
	      fields in a single pass of the file.  It uses only the
	      System.Xml libraries from Microsoft, obviating the need
	      for any add-in libraries (to simplify the build process
	      to something even I can use).
	     */
	    ScansRunner=ScansTable;
	    while (Xread.Read())
	    {
		XmlNodeType ThisNodeType = Xread.NodeType;
		if (ThisNodeType == XmlNodeType.EndElement)
		{
		    if (Xread.Name=="spectrum" && NeedToFinalizeActivation)
		    {
			/*
			  When we reach the end of an MS/MS spectrum,
			  we need to summarize its activation data to
			  a single string.  "EThcD" for example, means
			  that CV terms for both "ETD" and "beam-type
			  CID" were detected for a particular MS/MS.
			*/
			// At present, only ETD can exist in combination with other activation types.
			if (ThisScanIsETD)
			{
			    if (ThisScanIsCID)
			    {
				ScansRunner.mzMLDissociation = "ETciD";
				mzMLETciDCount++;
			    }
			    else
				if (ThisScanIsHCD)
				{
				    ScansRunner.mzMLDissociation = "EThcD";
				    mzMLEThcDCount++;
				}
				else
				{
				    ScansRunner.mzMLDissociation = "ETD";
				    mzMLETDCount++;
				}
			}
			else
			{
			    // I did not intend to write code that favors one activation over another, but this prioritizes CID and HCD over ECD.
			    if (ThisScanIsCID)
			    {
				ScansRunner.mzMLDissociation = "CID";
				mzMLCIDCount++;
			    }
			    else if (ThisScanIsHCD)
			    {
				ScansRunner.mzMLDissociation = "HCD";
				mzMLHCDCount++;
			    }
			    else if (ThisScanIsECD)
			    {
				ScansRunner.mzMLDissociation = "ECD";
				mzMLECDCount++;
			    }
			}
			NeedToFinalizeActivation = false;
		    }
		}
		else if (ThisNodeType == XmlNodeType.Element)
		{
		    if (Xread.Name=="run") {
			/*
			  We directly read relatively little
			  information about the mzML file as a whole.
			  Here we grab the startTimeStamp, and
			  elsewhere we grab the file name root,
			  instrument model, and serial number.
			*/
			StartTimeStamp = Xread.GetAttribute("startTimeStamp");
		    }
		    else if (Xread.Name=="spectrum") {
			/*
			  We only create a new ScanMetrics object if
			  it isn't an MS1 scan.  We need to keep two
			  pieces of information from this new spectrum
			  header in case we do make a new ScanMetrics
			  object.
			*/
			string ThisPeakCount = Xread.GetAttribute("defaultArrayLength");
			LastPeakCount = int.Parse(ThisPeakCount);
			LastNativeID = Xread.GetAttribute("id");
		    }
		    else if (Xread.Name=="cvParam") {
			string Accession = Xread.GetAttribute("accession");
			switch(Accession)
			{
			    /*
			      If you see that instrument model is ever
			      blank for an mzML, there are two likely
			      causes.  The first would be that the
			      mzML converter has not listed the CV
			      term for the instrument type in the
			      mzML-- ProteoWizard _does_ record this
			      information.  The second likely cause is
			      that the CV term relating to your
			      instrument model is missing from this
			      list.  Just add a "case" line for it and
			      recompile.
			     */
			    case "MS:1000557":
			    case "MS:1001910":
			    case "MS:1001911":
			    case "MS:1002416":
			    case "MS:1002523":
			    case "MS:1002732":
			    case "MS:1003029":
				Instrument = Xread.GetAttribute("name");
				break;
			    case "MS:1000529":
				SerialNumber = Xread.GetAttribute("value");
				break;
			    case "MS:1000016":
				string ThisStartTime = Xread.GetAttribute("value");
				// We need the "InvariantCulture" nonsense because some parts of the world separate decimals with commas.
				float  ThisStartTimeFloat = Single.Parse(ThisStartTime, CultureInfo.InvariantCulture);
				ScansRunner.ScanStartTime = ThisStartTimeFloat;
				if (ThisStartTimeFloat > MaxScanStartTime) MaxScanStartTime = ThisStartTimeFloat;
				break;
			    case "MS:1000511":
				string ThisLevel = Xread.GetAttribute("value");
				int    ThisLevelInt = int.Parse(ThisLevel);
				if (ThisLevelInt == 1)
				{
				    // We do very little with MS scans other than count them.
				    mzMLMS1Count++;
				}
				else
				{
				    /*
				      If we detect an MS of level 2 or
				      greater in the mzML, we have
				      work to do.  Each MS/MS is
				      matched by an item in the linked
				      list of ScanMetrics for each
				      LCMSMSExperiment.  We will need
				      to capture some information we
				      already saw (such as the
				      NativeID of this scan) and set
				      up for collection of some
				      additional information in the
				      Activation section.
				     */
				    mzMLMSnCount++;
				    ScansRunner.Next = new ScanMetrics();
				    ScansRunner = ScansRunner.Next;
				    ScansRunner.NativeID = LastNativeID;
				    ScansRunner.mzMLPeakCount = LastPeakCount;
				    ThisScanIsCID = false;
				    ThisScanIsHCD = false;
				    ThisScanIsECD = false;
				    ThisScanIsETD = false;
				    NeedToFinalizeActivation = true;
				}
				break;
			    case "MS:1000041":
				string ThisCharge = Xread.GetAttribute("value");
				int    ThisChargeInt = int.Parse(ThisCharge);
				ScansRunner.mzMLPrecursorZ = ThisChargeInt;
				if (ThisChargeInt < mzMLPrecursorZMin) mzMLPrecursorZMin = ThisChargeInt;
				if (ThisChargeInt > mzMLPrecursorZMax) mzMLPrecursorZMax = ThisChargeInt;
				try {
				    mzMLPrecursorZ[ThisChargeInt]++;
				}
				catch (IndexOutOfRangeException) {
				    Console.Error.WriteLine("Maximum charge of {0} is less than mzML charge {1}.",MaxZ,ThisChargeInt);
				}
				break;
			    case "MS:1000133":
				ThisScanIsCID = true;
				break;
			    case "MS:1000422":
				ThisScanIsHCD = true;
				break;
			    case "MS:1000250":
				ThisScanIsECD = true;
				break;
			    case "MS:1000598":
				ThisScanIsETD = true;
				break;
				/*  TODO: May need to implement IRMPD combinations, too.
			    case "MS:1000262":
				ThisScanIsIRMPD = true;
				break;
				*/
				
			}
		    }
		}
	    }
	}

	public LCMSMSExperiment Find(string Basename)
	{
	    /*
	      We receive an msAlign filename.  We seek the
	      LCMSMSExperiment in this linked list that has the
	      corresponding filename.
	    */
	    LCMSMSExperiment LRunner = this.Next;
	    while (LRunner != null) {
		if (Basename == LRunner.SourceFile)
		    return LRunner;
		LRunner = LRunner.Next;
	    }
	    return null;
	}

	public void ParseScanNumbers() {
	    /*
	      Instrument manufacturers differ in the ways that they
	      report the identities of each MS and MS/MS.  Because
	      TopFD was created for Thermo instruments, though, it
	      expects each spectrum to have a unique scan number for a
	      given RAW file.  To match to TopFD msAlign files, we'll
	      need to extract those from the NativeIDs.
	     */
	    ScanMetrics SRunner = this.ScansTable.Next;
	    while (SRunner != null)
	    {
		// Example of Thermo NativeID: controllerType=0 controllerNumber=1 scan=12
		string[] Tokens = SRunner.NativeID.Split(' ');
		string[] Tokens2 = Tokens[2].Split('=');
		SRunner.ScanNumber = int.Parse(Tokens2[1]);
		SRunner = SRunner.Next;
	    }
	}
	
	public ScanMetrics GoToScan(int Target) {
	    ScanMetrics SRunner = this.ScansTable.Next;
	    while (SRunner != null)
	    {
		if (SRunner.ScanNumber == Target)
		    return SRunner;
		SRunner = SRunner.Next;
	    }
	    return null;
	}
	
	public void ReadFromMSAlign(string PathAndFileName)
	{
	    /*
	      An earlier function to read mzML files is handed a
	      particular XML Reader object that has already been
	      initialized from a particular mzML file.  The MSAlign
	      reader is simpler and more complex for a variety of
	      reasons.  1) Since it looks a lot like MGF, it's an easy
	      text format to parse.  2) Since in some cases multiple
	      msAligns can be produced by topFD for an individual
	      mzML, we may have some added characters in file names,
	      making it tricky to match which mzML corresponds to each
	      msAlign; this is handled by the Find function.  3) We
	      have already imported information for mzMLs, and those
	      data are all in RAM; now we need to add information from
	      each msAlign to the correct LC-MS/MS data structure.
	     */
	    using (StreamReader msAlign = new StreamReader(PathAndFileName))
	    {
		string      LineBuffer = msAlign.ReadLine();
		string []   Tokens;
		ScanMetrics ScanRunner = null;
		MSMSPeak    PeakList = null;
		MSMSPeak    PeakRunner = null;
		int         NumberFromString;
		while (LineBuffer != null) {
		    /*
		      We are particularly interested in the block of
		      lines at the start of each spectrum that
		      contains a variable and a value, separated by an
		      equals symbol.
		    */
		    if (LineBuffer.Contains("="))
		    {
			Tokens = LineBuffer.Split('=');
			switch (Tokens[0])
			{
			    case "SCANS":
				NumberFromString = int.Parse(Tokens[1]);
				ScanRunner = this.GoToScan(NumberFromString);
				if (ScanRunner == null) {
				    Console.Error.WriteLine("Error seeking scan {0} from {1}",NumberFromString,PathAndFileName);
				}
				PeakList = new MSMSPeak();
				PeakRunner = PeakList;
				this.msAlignMSnCount++;
				break;
			    case "PRECURSOR_CHARGE":
				NumberFromString = int.Parse(Tokens[1]);
				ScanRunner.msAlignPrecursorZ = NumberFromString;
				if (NumberFromString < this.msAlignPrecursorZMin) this.msAlignPrecursorZMin = NumberFromString;
				if (NumberFromString > this.msAlignPrecursorZMax) this.msAlignPrecursorZMax = NumberFromString;
				try {
				    this.msAlignPrecursorZ[NumberFromString]++;
				}
				catch (IndexOutOfRangeException) {
				    Console.Error.WriteLine("Maximum charge of {0} is less than msAlign charge {1}.",MaxZ,NumberFromString);
				}
				break;
			    case "PRECURSOR_MASS":
				ScanRunner.msAlignPrecursorMass = double.Parse(Tokens[1], CultureInfo.InvariantCulture);
				break;
			}
		    }
		    else if ((LineBuffer.Length > 0) && char.IsDigit(LineBuffer[0]))
		    {
			//This is a line containing a deconvolved mass, intensity, and original charge, delimited by whitespace
			Tokens = LineBuffer.Split(null);
			PeakRunner.Next = new MSMSPeak();
			/*
			  This new linked list is temporary storage
			  while we're on this scan; we'll dump the
			  mass list to an array in a moment.
			*/
			PeakRunner = PeakRunner.Next;
			PeakRunner.Mass      = double.Parse(Tokens[0], CultureInfo.InvariantCulture);
			PeakRunner.Intensity = float.Parse(Tokens[1], CultureInfo.InvariantCulture);
			PeakRunner.OrigZ     = int.Parse(Tokens[2]);
			ScanRunner.msAlignPeakCount++;
		    }
		    else if (LineBuffer == "END IONS")
		    {
			if (ScanRunner.msAlignPeakCount > MaxPkCount)
			{
			    this.msAlignPeakCountDistn[MaxPkCount]++;
			}
			else
			{
			    this.msAlignPeakCountDistn[ScanRunner.msAlignPeakCount]++;
			}
			if (ScanRunner.msAlignPeakCount == 0) this.msAlignMSnCount0++;
			else
			{
			    // Copy the linked list masses to an array and sort it.
			    ScanRunner.PeakMZs = new double[ScanRunner.msAlignPeakCount];
			    int Offset = 0;
			    PeakRunner = PeakList.Next;
			    while (PeakRunner != null)
			    {
				ScanRunner.PeakMZs[Offset] = PeakRunner.Mass;
				Offset++;
				PeakRunner=PeakRunner.Next;
			    }
			    Array.Sort(ScanRunner.PeakMZs);
			}
		    }
		    LineBuffer = msAlign.ReadLine();
		}
	    }
	}

	public void FindSimilarSpectraWithinRaw()
	{
	    LCMSMSExperiment LCMSMSRunner = this.Next;
	    SimilarityLink   SimilarityRunner;
	    SimilarityLink   SimBuffer;
	    double           ThisMatchScore;
	    int              NonVacantScanCount;
	    int              LinkCount;
	    ScanMetrics.ComputeLogFactorials();
	    // TODO: Employ a ThreadPool to use multi-core processing.
	    while (LCMSMSRunner != null) {
		ScanMetrics SMRunner = LCMSMSRunner.ScansTable.Next;
		NonVacantScanCount=0;
		LinkCount=0;
		while (SMRunner != null) {
		    if (SMRunner.msAlignPeakCount > 0)
		    {
			ScanMetrics OtherScan = SMRunner.Next;
			SimilarityRunner = SMRunner.SimilarScans;
			// NonVacantScanCount should, in the end, be the same as msAlignMSnCount - msAlignMSnCount0
			NonVacantScanCount++;
			while (OtherScan != null)
			{
			    if (OtherScan.msAlignPeakCount > 0)
			    {
				// Test these two scans to determine if they have an improbable amount of deconvolved mass overlap.
				ThisMatchScore = SMRunner.TestForSimilarity(OtherScan);
				/*
				  100 is a very arbitrary threshold...
				  ThisMatchScore is a log probability
				  that the spectra would share this
				  many overlapping peaks _or more_ by
				  random chance.
				*/
				if (ThisMatchScore > 100)
				{
				    //Make a link between these MS/MS scans to reflect their high mass list overlap.
				    SimBuffer=SimilarityRunner.Next;
				    SimilarityRunner.Next = new SimilarityLink();
				    SimilarityRunner = SimilarityRunner.Next;
				    SimilarityRunner.Other = OtherScan;
				    SimilarityRunner.Score = ThisMatchScore;
				    SimilarityRunner.Next = SimBuffer;
				    //Make the reverse link; this is necessary for component detector.
				    SimBuffer = OtherScan.SimilarScans.Next;
				    OtherScan.SimilarScans.Next = new SimilarityLink();
				    OtherScan.SimilarScans.Next.Other = SMRunner;
				    OtherScan.SimilarScans.Next.Score = ThisMatchScore;
				    OtherScan.SimilarScans.Next.Next  = SimBuffer;
				    LinkCount++;
				}
			    }
			    OtherScan = OtherScan.Next;
			}
		    }
		    SMRunner = SMRunner.Next;
		}
		if (LinkCount == 0)
		{
		    LCMSMSRunner.Redundancy = 0;
		}
		else
		{
		    /*
		      The math here is based on the handshake problem:
		      in a party attended by N people, how many
		      distinct handshakes can take place?  The answer
		      is (N(N-1))/2.  If N==1, this would result in a
		      division by zero.
		    */
		    LCMSMSRunner.Redundancy = (float)LinkCount / ((float)NonVacantScanCount * (float)(NonVacantScanCount-1) / 2.0f);
		}
		Console.WriteLine("\tDetected {0}% of possible MSn-MSn similarity in {1}",
				  Math.Round(LCMSMSRunner.Redundancy * 100,2),LCMSMSRunner.SourceFile);

		SMRunner = LCMSMSRunner.ScansTable.Next;
		int MaxDegreeSoFar = 0;
		// First, ask how many spectra each MSn was linked to by similar fragment masses.
		while (SMRunner != null)
		{
		    SimilarityLink   SLRunner=SMRunner.SimilarScans.Next;
		    int ThisDegree = 0;
		    while (SLRunner != null)
		    {
			ThisDegree++;
			SLRunner = SLRunner.Next;
		    }
		    SMRunner.Degree = ThisDegree;
		    if (ThisDegree > MaxDegreeSoFar) MaxDegreeSoFar = ThisDegree;
		    // If an MS/MS contains no masses, don't count it as a component.
		    if (SMRunner.msAlignPeakCount==0) SMRunner.Visited = true;
		    SMRunner = SMRunner.Next;
		}
		LCMSMSRunner.HighestDegree = MaxDegreeSoFar;
		// Next, subdivide the MSn scans into connected components.
		SMRunner = LCMSMSRunner.ScansTable.Next;
		int ComponentCount = 0;
		while (SMRunner != null) {
		    if (!SMRunner.Visited)
		    {
			//This scan is a starting point for an unexplored component.
			ComponentCount++;
			SMRunner.Visited = true;
			SMRunner.ComponentNumber = ComponentCount;
			SimilarityLink SLRunner = SMRunner.SimilarScans.Next;
			int ComponentSize = 1;
			while (SLRunner != null)
			{
			    ComponentSize += SLRunner.Other.ComponentRecurse(ComponentCount);
			    SLRunner = SLRunner.Next;
			}
			if (ComponentSize > LCMSMSRunner.LargestComponentSize)
			{
			    LCMSMSRunner.LargestComponentSize = ComponentSize;
			    LCMSMSRunner.LargestComponentIndex = ComponentCount;
			}
			// Console.WriteLine("RAW\t{0}\tLabel\t{1}\tSize\t{2}",LCMSMSRunner.SourceFile,ComponentCount,ComponentSize);
		    }
		    SMRunner = SMRunner.Next;
		}
		LCMSMSRunner.ComponentCount = ComponentCount;
		//TODO: Only produce the graphical output of the biggest component if the command line indicates that is desired.
		LCMSMSRunner.GraphVizPrintComponent(LCMSMSRunner.LargestComponentIndex);
		// Cleanup memory.
		SMRunner = LCMSMSRunner.ScansTable.Next;
		while (SMRunner != null) {
		    SMRunner.SimilarScans.Next = null;
		    SMRunner = SMRunner.Next;
		}
		//We're all done with this mzML / msAlign pair.  Go to the next one.
		LCMSMSRunner = LCMSMSRunner.Next;
	    }
	}

	public void GraphVizPrintComponent(int TargetComponentNumber)
	{
	    /*
	      The TargetComponentNumber tells us a connected component
	      of MS/MS scans in this RAW file.  Create an input file
	      for GraphViz DOT that can be used to visualize the
	      component.
	     */
	    SimilarityLink SLRunner;
	    ScanMetrics    SMRunner=this.ScansTable.Next;
	    using (StreamWriter DOTFile = new StreamWriter(SourceFile + "-ConnectedComponent.txt"))
	    {
		DOTFile.WriteLine("graph LargestComponent {");
		while (SMRunner != null)
		{
		    if (SMRunner.ComponentNumber == TargetComponentNumber)
		    {
			SLRunner = SMRunner.SimilarScans.Next;
			while (SLRunner != null)
			{
			    /*
			      Because similarity links are stored in
			      both directions, we need to ensure we
			      write only half of the links.
			     */
			    if (SLRunner.Other.ScanNumber > SMRunner.ScanNumber)
				DOTFile.WriteLine(SMRunner.ScanNumber + "--" + SLRunner.Other.ScanNumber);
			    SLRunner = SLRunner.Next;
			}
		    }
		    SMRunner = SMRunner.Next;
		}
		DOTFile.WriteLine("}");
	    }
	}

	public void GenerateSequenceTags()
	{
	    /*
	      What is the longest sequence we can "read" from the
	      fragment masses?  We seek gaps between fragments that
	      match amino acid masses.  Then we use recursion to
	      determine the length of the longest possible tag
	      stringing together those gaps.
	     */
	    // TODO: Employ a ThreadPool to use multi-core processing.
	    // TODO: Why does 20170309_ksn5514_FACS_BC_RP4H_10547771_D1_B_SEP_tech_rep_01 still suck on runtime?
	    LCMSMSExperiment LCMSMSRunner = this.Next;
	    ScanMetrics      SMRunner;
	    MSMSPeak         PeakList;
	    MSMSPeak         PRunner1;
	    MSMSPeak         PRunner2;
	    MassGap          MGBuffer;
	    // What is the mass of the largest amino acid?  Helpfully, AminoAcids is already sorted.
	    double           BiggestAAPlusTol = ScanMetrics.AminoAcids[ScanMetrics.AminoAcids.Length-1] + ScanMetrics.FragmentTolerance;
	    while (LCMSMSRunner != null)
	    {
		int  LongestTagForThisRAW = 0;
		SMRunner = LCMSMSRunner.ScansTable.Next;
		while (SMRunner != null)
		{
		    /*
		      First, convert the array of mass values into a linked list again.
		     */
		    if (SMRunner.msAlignPeakCount>1)
		    {
			int LongestTagSoFar = 0;
			PeakList = new MSMSPeak();
			PRunner1 = PeakList;
			foreach (double ThisPeak in SMRunner.PeakMZs)
			{
			    PRunner1.Next = new MSMSPeak();
			    PRunner1 = PRunner1.Next;
			    PRunner1.Mass = ThisPeak;
			}
			/*
			  Find the gaps corresponding to amino acid masses.
			*/
			//StreamWriter DOTFile = new StreamWriter(LCMSMSRunner.SourceFile + "-" + SMRunner.ScanNumber + "-DeNovo.txt");
			//DOTFile.WriteLine("graph DeNovo {");
			PRunner1 = PeakList.Next;
			while (PRunner1 != null)
			{
			    PRunner2 = PRunner1.Next;
			    while (PRunner2 != null)
			    {
				double MassDiff = PRunner2.Mass - PRunner1.Mass;
				int index = 0;
				foreach (double ThisAA in ScanMetrics.AminoAcids)
				{
				    double MassError = Math.Abs(MassDiff - ThisAA);
				    if (MassError < ScanMetrics.FragmentTolerance)
				    {
					// ThisAA corresponds in mass to the separation between these two peaks
					MGBuffer = PRunner1.AALinks;
					PRunner1.AALinks = new MassGap();
					PRunner1.AALinks.Next = MGBuffer;
					PRunner1.AALinks.NextPeak = PRunner2;
					PRunner1.AALinks.ExpectedMass = ThisAA;
					//DOTFile.WriteLine(Math.Round(PRunner1.Mass,3) + "--" + Math.Round(PRunner2.Mass,3) + " [label=\"" + ScanMetrics.AminoAcidSymbols[index] +"\"]");
				    }
				    index++;
				}
				PRunner2 = PRunner2.Next;
			    }
			    PRunner1 = PRunner1.Next;
			}
			//DOTFile.WriteLine("}");
			//DOTFile.Flush();
			/*
			  Use recursion to seek the longest sequence
			  for which each consecutive fragment exists.
			*/
			PRunner1 = PeakList.Next;
			while (PRunner1 != null)
			{
			    if (PRunner1.LongestTagStartingHere == 0)
			    {
				MGBuffer = PRunner1.AALinks;
				while (MGBuffer != null) {
				    int ThisTagLength = MGBuffer.DepthTagSearch();
				    if (ThisTagLength > LongestTagSoFar)
				    {
					LongestTagSoFar = ThisTagLength;
				    }
				    if (ThisTagLength > LongestTagForThisRAW)
				    {
					LongestTagForThisRAW = ThisTagLength;
				    }
				    MGBuffer = MGBuffer.Next;
				}
			    }
			    else
			    {
				//If LongestTagStartingHere >0, this node has already been visited from one of lower mass and thus this peak cannot start the longest path in the spectrum.
			    }
			    PRunner1 = PRunner1.Next;
			}
			SMRunner.LongestTag=LongestTagSoFar;
			if (LongestTagSoFar > LCMSMSExperiment.MaxLength) {
			    LongestTagSoFar = LCMSMSExperiment.MaxLength;
			}
			LCMSMSRunner.LongestTagDistn[LongestTagSoFar]++;
		    }
		    SMRunner = SMRunner.Next;
		}
		Console.WriteLine("\tInferred sequence tags as long as {0} AAs in {1}",LongestTagForThisRAW,LCMSMSRunner.SourceFile);
		LCMSMSRunner = LCMSMSRunner.Next;
	    }
	}

	public void WriteTextQCReport()
	{
	    /*
	      We have two TSV outputs.  The "byRun" report contains a
	      row for each LC-MS/MS (or mzML) in this directory.  The
	      "byMSn" report contains a row for each MS/MS in each
	      mzML in this directory.
	     */
	    LCMSMSExperiment LCMSMSRunner = this.Next;
	    string delim = "\t";
	    using (StreamWriter TSVbyRun = new StreamWriter("TDAuditor-byRun.tsv"))
	    {
		TSVbyRun.Write("SourceFile\tInstrument\tSerialNumber\tStartTimeStamp\tRTDuration" +
			       "\tMS1Count\tmzMLMSnCount\tmsAlignMSnWithPeaksCount\tmsAlignMSnWithoutPeaksCount\tmsAlignMSnWithPeaksFraction" +
			       "\tRedundancy\tHighestDegree\tLargestComponentSize\tComponentCount" +
			       "\tmzMLHCDCount\tmzMLCIDCount\tmzMLETDCount\tmzMLECDCount\tmzMLEThcDCount\tmzMLETciDCount" +
			       "\tmzMLPreZMin\tmzMLPreZMax\tmsAlignPreZMin\tmsAlignPreZQ1\tmsAlignPreZQ2\tmsAlignPreZQ3\tmsAlignPreZMax" +
			       "\tmsAlignPkCountQ1\tmsAlignPkCountQ2\tmsAlignPkCountQ3\tmsAlignPkCountMax" +
			       "\tTagLengthQ1\tTagLengthQ2\tTagLengthQ3\tTagLengthMax\tblank\t");
		for (int i=0; i<=MaxZ; i++)
		{
		    TSVbyRun.Write("mzMLPreZ={0}\t",i);
		}
		TSVbyRun.Write("blank\t");
		for (int i=0; i<=MaxZ; i++)
		{
		    TSVbyRun.Write("msAlignPreZ={0}\t",i);
		}
		TSVbyRun.Write("blank\t");
		for (int i=0; i<=MaxLength; i++)
		{
		    TSVbyRun.Write("BestTagLength={0}\t",i);
		}
		TSVbyRun.WriteLine();
		while (LCMSMSRunner != null)
		{
		    int PreZSum = 0;
		    foreach(int Count in LCMSMSRunner.msAlignPrecursorZ)
		    {
			PreZSum += Count;
		    }
		    int ZQ1Count = PreZSum / 4;
		    int ZQ2Count = PreZSum / 2;
		    int ZQ3Count = ZQ1Count + ZQ2Count;
		    int ZQuartile1 = 0;
		    int ZQuartile2 = 0;
		    int ZQuartile3 = 0;
		    PreZSum = 0;
		    int CurrentZ = 0;
		    foreach(int Count in LCMSMSRunner.msAlignPrecursorZ)
		    {
			if(PreZSum < ZQ1Count && ZQ1Count <= (PreZSum + Count))
			    ZQuartile1 = CurrentZ;
			if(PreZSum < ZQ2Count && ZQ2Count <= (PreZSum + Count))
			    ZQuartile2 = CurrentZ;
			if(PreZSum < ZQ3Count && ZQ3Count <= (PreZSum + Count))
			    ZQuartile3 = CurrentZ;
			PreZSum += Count;
			CurrentZ++;
		    }
		    //We need to distinguish between MS/MS that yield deconvolved mass lists and those that don't.
		    int MSnCountWithPeaks = (LCMSMSRunner.msAlignMSnCount-LCMSMSRunner.msAlignMSnCount0);
		    float MSnWithPeaksFraction = (float)MSnCountWithPeaks / (float)LCMSMSRunner.msAlignMSnCount;
		    //We want the interquartiles of the best tag lengths.
		    int TagLengthSum = 0;
		    foreach(int Count in LCMSMSRunner.LongestTagDistn)
		    {
			TagLengthSum += Count;
		    }
		    int Q1Count = TagLengthSum / 4;
		    int Q2Count = TagLengthSum / 2;
		    int Q3Count = Q1Count+Q2Count;
		    int LengthQ1 = 0;
		    int LengthQ2 = 0;
		    int LengthQ3 = 0;
		    int LengthMax = 0;
		    TagLengthSum = 0;
		    int CurrentLength = 0;
		    foreach(int Count in LCMSMSRunner.LongestTagDistn)
		    {
			if (Count > 0) LengthMax = CurrentLength;
			if (TagLengthSum < Q1Count && Q1Count <= (TagLengthSum+Count))
			    LengthQ1 = CurrentLength;
			if (TagLengthSum < Q2Count && Q2Count <= (TagLengthSum+Count))
			    LengthQ2 = CurrentLength;
			if (TagLengthSum < Q3Count && Q3Count <= (TagLengthSum+Count))
			    LengthQ3 = CurrentLength;
			TagLengthSum += Count;
			CurrentLength++;
		    }
		    //We want the interquartiles for the numbers of peaks found in the msAlign mass lists
		    int PkCountQ1 = 0;
		    int PkCountQ2 = 0;
		    int PkCountQ3 = 0;
		    int PkCountMax = 0;
		    int PkCountSum = 0;
		    int ThisPkCount;
		    foreach(int Count in LCMSMSRunner.msAlignPeakCountDistn)
		    {
			PkCountSum += Count;
		    }
		    Q1Count = PkCountSum / 4;
		    Q2Count = PkCountSum / 2;
		    Q3Count = Q1Count+Q2Count;
		    PkCountSum = 0;
		    for (int PkCountIndex=0; PkCountIndex <= MaxPkCount; PkCountIndex++)
		    {
			ThisPkCount = LCMSMSRunner.msAlignPeakCountDistn[PkCountIndex];
			if (ThisPkCount > 0) PkCountMax = PkCountIndex;
			if (PkCountSum < Q1Count && Q1Count <= (PkCountSum+ThisPkCount))
			    PkCountQ1 = PkCountIndex;
			if (PkCountSum < Q2Count && Q2Count <= (PkCountSum+ThisPkCount))
			    PkCountQ2 = PkCountIndex;
			if (PkCountSum < Q3Count && Q3Count <= (PkCountSum+ThisPkCount))
			    PkCountQ3 = PkCountIndex;
			PkCountSum += ThisPkCount;
		    }
		    // Actually write the metrics to the byRun file...
		    TSVbyRun.Write(LCMSMSRunner.SourceFile + delim);
		    TSVbyRun.Write(LCMSMSRunner.Instrument + delim);
		    TSVbyRun.Write(LCMSMSRunner.SerialNumber + delim);
		    TSVbyRun.Write(LCMSMSRunner.StartTimeStamp + delim);
		    TSVbyRun.Write(LCMSMSRunner.MaxScanStartTime + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLMS1Count + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLMSnCount + delim);
		    TSVbyRun.Write(MSnCountWithPeaks + delim);
		    TSVbyRun.Write(LCMSMSRunner.msAlignMSnCount0 + delim);
		    TSVbyRun.Write(MSnWithPeaksFraction + delim);
		    TSVbyRun.Write(LCMSMSRunner.Redundancy + delim);
		    TSVbyRun.Write(LCMSMSRunner.HighestDegree + delim);
		    TSVbyRun.Write(LCMSMSRunner.LargestComponentSize + delim);
		    TSVbyRun.Write(LCMSMSRunner.ComponentCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLHCDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLCIDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLETDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLECDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLEThcDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLETciDCount + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLPrecursorZMin + delim);
		    TSVbyRun.Write(LCMSMSRunner.mzMLPrecursorZMax + delim);
		    TSVbyRun.Write(LCMSMSRunner.msAlignPrecursorZMin + delim);
		    TSVbyRun.Write(ZQuartile1 + delim);
		    TSVbyRun.Write(ZQuartile2 + delim);
		    TSVbyRun.Write(ZQuartile3 + delim);
		    TSVbyRun.Write(LCMSMSRunner.msAlignPrecursorZMax + delim);
		    TSVbyRun.Write(PkCountQ1 + delim);
		    TSVbyRun.Write(PkCountQ2 + delim);
		    TSVbyRun.Write(PkCountQ3 + delim);
		    TSVbyRun.Write(PkCountMax + delim);
		    TSVbyRun.Write(LengthQ1 + delim);
		    TSVbyRun.Write(LengthQ2 + delim);
		    TSVbyRun.Write(LengthQ3 + delim);
		    TSVbyRun.Write(LengthMax + delim);
		    TSVbyRun.Write(delim);
		    foreach(int charge in LCMSMSRunner.mzMLPrecursorZ)
		    {
			TSVbyRun.Write(charge + delim);
		    }
		    TSVbyRun.Write(delim);
		    foreach(int charge in LCMSMSRunner.msAlignPrecursorZ)
		    {
			TSVbyRun.Write(charge + delim);
		    }
		    TSVbyRun.Write(delim);
		    foreach(int Length in LCMSMSRunner.LongestTagDistn)
		    {
			TSVbyRun.Write(Length + delim);
		    }
		    TSVbyRun.WriteLine();
		    LCMSMSRunner = LCMSMSRunner.Next;
		}
	    }
	    LCMSMSRunner = this.Next;
	    using (StreamWriter TSVbyScan = new StreamWriter("TDAuditor-byMSn.tsv"))
	    {
		TSVbyScan.WriteLine("SourceFile\tNativeID\tScanStartTime\tmzMLDissociation\tmzMLPrecursorZ\tmsAlignPrecursorZ\tmsAlignPrecursorMass\tmzMLPeakCount\tmsAlignPeakCount\tDegree\tComponentNumber\tLongestTag");
		while (LCMSMSRunner != null) {
		    ScanMetrics SMRunner = LCMSMSRunner.ScansTable.Next;
		    while (SMRunner != null) {
			TSVbyScan.Write(LCMSMSRunner.SourceFile + delim);
			TSVbyScan.Write(SMRunner.NativeID + delim);
			TSVbyScan.Write(SMRunner.ScanStartTime + delim);
			TSVbyScan.Write(SMRunner.mzMLDissociation + delim);
			TSVbyScan.Write(SMRunner.mzMLPrecursorZ + delim);
			TSVbyScan.Write(SMRunner.msAlignPrecursorZ + delim);
			TSVbyScan.Write(SMRunner.msAlignPrecursorMass + delim);
			TSVbyScan.Write(SMRunner.mzMLPeakCount + delim);
			TSVbyScan.Write(SMRunner.msAlignPeakCount + delim);
			TSVbyScan.Write(SMRunner.Degree + delim);
			TSVbyScan.Write(SMRunner.ComponentNumber + delim);
			TSVbyScan.WriteLine(SMRunner.LongestTag);
			SMRunner = SMRunner.Next;
		    }
		    LCMSMSRunner = LCMSMSRunner.Next;
		}
	    }
	}
    }
}
